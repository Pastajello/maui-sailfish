/*
 * sf-screenrec — screen recorder for Sailfish OS (runs ON the phone).
 *
 * Frames come straight from the compositor: lipstick's own Wayland recorder
 * extension (lipstick_recorder_manager, the one lipstick2vnc uses) copies
 * every repainted frame into a shm buffer we hand it — no VNC, no network,
 * no "Screenshot captured" banner.
 *
 * Encoding (--encoder):
 *   v4l2  (default when available) the SoC's hardware H.264 encoder through
 *         V4L2 mem2mem (MediaTek mtk-vcodec-enc on the Jolla phones). It takes
 *         32-bit RGB directly, so a frame costs one copy from the compositor's
 *         buffer into the encoder's — no colour conversion on the CPU. The
 *         encoder is limited to 2560x1440, so the portrait frame is stored
 *         rotated (landscape) and the MP4 says "rotate 90°" — players show it
 *         upright. gst-droid's droidvenc can't be used: it only accepts
 *         camera metadata buffers.
 *   soft  software MPEG-4 (libav) through GStreamer — fallback, slower.
 * The elementary stream goes through GStreamer into an MP4 (h264parse ->
 * mp4mux), with --audio also PulseAudio (pulsesrc -> AAC).
 *
 * Timestamps: the compositor only sends a frame when it repaints (nothing on
 * a static screen; after 40 ms of silence one repaint is requested so the
 * settled screen is always recorded), so the frame rate varies; every frame is stamped with the
 * compositor's own frame time anchored to the pipeline clock — never a frame
 * counter — so the video plays at real speed and stays in sync with audio.
 *
 * Privileges: lipstick lets only processes in the "privileged" group bind the
 * recorder, and the encoder node is media:system 0660. Started as root
 * (devel-su) it opens the encoder, then drops to the owner of
 * XDG_RUNTIME_DIR (default /run/user/100000) with the privileged group — the
 * Wayland and PulseAudio sessions are that user's. Without root:
 *     devel-su -p sf-screenrec …        (privileged group; soft encoder only)
 *
 * Usage: sf-screenrec -o OUT.mp4 [--encoder v4l2|soft] [--audio] [--fps N]
 *                     [--bitrate BPS] [--max-seconds N] [--stop-file PATH]
 *                     [--ready-file PATH] [--dump FILE]
 *        sf-screenrec --inspect ELEMENT | --list [PATTERN]   (GStreamer)
 * Stop with SIGINT/SIGTERM, --max-seconds or --stop-file. Next to OUT.mp4 it
 * writes OUT.frames.tsv: frame, compositor ms, arrival CLOCK_MONOTONIC ms,
 * pts ms, encoded (0 = dropped) — for lining frames up with app logs.
 *
 * Built by tools/sf-screenrec-build.sh (zig cc, sysroot from sf-sysroot.sh).
 */
#define _GNU_SOURCE
#include <dirent.h>
#include <errno.h>
#include <fcntl.h>
#include <grp.h>
#include <poll.h>
#include <pwd.h>
#include <signal.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/ioctl.h>
#include <sys/mman.h>
#include <sys/stat.h>
#include <sys/syscall.h>
#include <time.h>
#include <unistd.h>

#include <linux/videodev2.h>

#include <gst/app/gstappsrc.h>
#include <gst/gst.h>
#include <wayland-client.h>

/* ---- lipstick-recorder protocol (lipstick/protocol/lipstick-recorder.xml) --
 * Written out by hand (no wayland-scanner on the build host); the message
 * names and signatures match the tables compiled into liblipstick-qt5. */
extern const struct wl_interface lipstick_recorder_interface;

static const struct wl_interface *rec_types_none[] = { NULL, NULL, NULL, NULL };
static const struct wl_interface *rec_types_buffer[] = { &wl_buffer_interface };
static const struct wl_interface *rec_types_frame[] = { &wl_buffer_interface, NULL, NULL };
static const struct wl_interface *rec_types_failed[] = { NULL, &wl_buffer_interface };
static const struct wl_interface *mgr_types_create[] = { &lipstick_recorder_interface, &wl_output_interface };

static const struct wl_message recorder_requests[] = {
	{ "destroy", "", rec_types_none },
	{ "record_frame", "o", rec_types_buffer },
	{ "repaint", "", rec_types_none },
};
static const struct wl_message recorder_events[] = {
	{ "setup", "iiii", rec_types_none },
	{ "frame", "oui", rec_types_frame },
	{ "failed", "io", rec_types_failed },
	{ "cancelled", "o", rec_types_buffer },
};
const struct wl_interface lipstick_recorder_interface = {
	"lipstick_recorder", 1, 3, recorder_requests, 4, recorder_events,
};
static const struct wl_message manager_requests[] = {
	{ "create_recorder", "no", mgr_types_create },
};
static const struct wl_interface lipstick_recorder_manager_interface = {
	"lipstick_recorder_manager", 1, 1, manager_requests, 0, NULL,
};

struct recorder_listener {
	void (*setup)(void *data, struct wl_proxy *rec, int32_t width, int32_t height, int32_t stride, int32_t format);
	void (*frame)(void *data, struct wl_proxy *rec, struct wl_buffer *buffer, uint32_t time, int32_t transform);
	void (*failed)(void *data, struct wl_proxy *rec, int32_t result, struct wl_buffer *buffer);
	void (*cancelled)(void *data, struct wl_proxy *rec, struct wl_buffer *buffer);
};
#define RECORDER_TRANSFORM_Y_INVERTED 2

/* ---- state ----------------------------------------------------------------- */
#define NBUF 3
#define NOUT 8
#define NCAP 8

struct shmbuf {
	struct wl_buffer *wl;
	void *data;
	int busy;          /* handed to the compositor, frame not back yet */
};

struct v4l2buf {
	void *p;
	size_t len;
	int queued;
};

enum { ENC_V4L2, ENC_SOFT };

static struct {
	/* wayland */
	struct wl_display *display;
	struct wl_shm *shm;
	struct wl_output *output;
	struct wl_proxy *manager;
	struct wl_proxy *recorder;
	int width, height, stride, format;
	struct shmbuf bufs[NBUF];
	int ready;

	/* gstreamer */
	GstElement *pipeline;
	GstAppSrc *appsrc;
	GstClockTime pts0;       /* running time of the first frame */
	int64_t comp0;           /* its compositor time (ms, unwrapped) */
	uint32_t last_comp;
	int64_t comp_wrap;
	GstClockTime last_pts;

	/* encoder */
	int encoder;
	int vfd;                 /* V4L2 mem2mem encoder */
	int ew, eh, ebpl;        /* encoded (landscape) size, input bytes per line */
	struct v4l2buf out[NOUT], cap[NCAP];
	int nout, ncap;
	int got_last;
	GByteArray *header;      /* SPS/PPS sent alone — prepended to the next frame */
	long encoded;

	FILE *tsv;
	const char *dump;
	const char *ready_file;  /* touched once the first frame is in */
	long frames, dropped;
	int64_t copy_us;         /* time spent rotating frames into the encoder */
	int64_t enc_us;          /* queue -> encoded latency, summed */
	int64_t queued_at[NOUT];
	double fps_cap;
	int64_t next_slot_ms;    /* --fps: earliest time for the next request */
	int pending_request;
	int64_t last_frame_ms;   /* arrival of the newest frame */
	int settle_repaint;      /* 0 armed, 1 asked for since that frame, 2 got it */
} S = { .vfd = -1 };

static volatile sig_atomic_t g_stop;
static void on_signal(int sig) { (void)sig; g_stop = 1; }

static int64_t mono_us(void)
{
	struct timespec ts;
	clock_gettime(CLOCK_MONOTONIC, &ts);
	return (int64_t)ts.tv_sec * 1000000 + ts.tv_nsec / 1000;
}

static int64_t mono_ms(void)
{
	struct timespec ts;
	clock_gettime(CLOCK_MONOTONIC, &ts);
	return (int64_t)ts.tv_sec * 1000 + ts.tv_nsec / 1000000;
}

/* lipstick announces WL_SHM_FORMAT_RGBA8888 but fills the buffer straight
 * from glReadPixels(GL_RGBA): bytes R,G,B,A (checked with --dump). */
static const char *gst_format_for(int shm_format)
{
	switch ((uint32_t)shm_format) {
	case WL_SHM_FORMAT_RGBA8888: return "RGBA";
	case WL_SHM_FORMAT_ABGR8888: return "RGBA";
	case WL_SHM_FORMAT_XBGR8888: return "RGBx";
	case WL_SHM_FORMAT_ARGB8888: return "BGRA";
	case WL_SHM_FORMAT_XRGB8888: return "BGRx";
	default: return NULL;
	}
}
static int source_is_rgba(void)
{
	const char *f = gst_format_for(S.format);
	return f && f[0] == 'R';
}

/* ---- V4L2 mem2mem H.264 encoder -------------------------------------------- */
static int xioctl(int fd, unsigned long req, void *arg)
{
	int r;
	do r = ioctl(fd, req, arg); while (r < 0 && errno == EINTR);
	return r;
}

/* The first video4linux node whose driver encodes to H.264. */
static int v4l2_find_and_open(char *path, size_t len)
{
	DIR *d = opendir("/dev");
	if (!d)
		return -1;
	struct dirent *e;
	int found = -1;
	while (found < 0 && (e = readdir(d))) {
		if (strncmp(e->d_name, "video", 5) || e->d_name[5] < '0' || e->d_name[5] > '9')
			continue;
		snprintf(path, len, "/dev/%s", e->d_name);
		int fd = open(path, O_RDWR | O_NONBLOCK);
		if (fd < 0)
			continue;
		struct v4l2_capability cap = { 0 };
		if (xioctl(fd, VIDIOC_QUERYCAP, &cap) == 0 && (cap.device_caps & V4L2_CAP_VIDEO_M2M_MPLANE)) {
			for (int i = 0;; i++) {
				struct v4l2_fmtdesc f = { .index = (uint32_t)i, .type = V4L2_BUF_TYPE_VIDEO_CAPTURE_MPLANE };
				if (xioctl(fd, VIDIOC_ENUM_FMT, &f) < 0)
					break;
				if (f.pixelformat == V4L2_PIX_FMT_H264) {
					found = fd;
					break;
				}
			}
		}
		if (found < 0)
			close(fd);
	}
	closedir(d);
	return found;
}

static void v4l2_ctrl(int id, int value, const char *name)
{
	struct v4l2_control c = { .id = (uint32_t)id, .value = value };
	if (xioctl(S.vfd, VIDIOC_S_CTRL, &c) < 0)
		fprintf(stderr, "sf-screenrec: encoder ignores %s (%s)\n", name, strerror(errno));
}

static int v4l2_setup(int bitrate)
{
	/* stored rotated: the portrait frame becomes landscape (<= 2560x1440) */
	S.ew = S.height;
	S.eh = S.width;

	struct v4l2_format cf = { .type = V4L2_BUF_TYPE_VIDEO_CAPTURE_MPLANE };
	cf.fmt.pix_mp.pixelformat = V4L2_PIX_FMT_H264;
	cf.fmt.pix_mp.width = (uint32_t)S.ew;
	cf.fmt.pix_mp.height = (uint32_t)S.eh;
	cf.fmt.pix_mp.num_planes = 1;
	cf.fmt.pix_mp.plane_fmt[0].sizeimage = 4 * 1024 * 1024;
	if (xioctl(S.vfd, VIDIOC_S_FMT, &cf) < 0) {
		fprintf(stderr, "sf-screenrec: encoder: S_FMT H264 %dx%d: %s\n", S.ew, S.eh, strerror(errno));
		return -1;
	}
	struct v4l2_format of = { .type = V4L2_BUF_TYPE_VIDEO_OUTPUT_MPLANE };
	of.fmt.pix_mp.pixelformat = V4L2_PIX_FMT_ABGR32;   /* 'AR24': bytes B,G,R,A */
	of.fmt.pix_mp.width = (uint32_t)S.ew;
	of.fmt.pix_mp.height = (uint32_t)S.eh;
	of.fmt.pix_mp.num_planes = 1;
	of.fmt.pix_mp.plane_fmt[0].bytesperline = (uint32_t)S.ew * 4;
	if (xioctl(S.vfd, VIDIOC_S_FMT, &of) < 0) {
		fprintf(stderr, "sf-screenrec: encoder: S_FMT AR24 %dx%d: %s\n", S.ew, S.eh, strerror(errno));
		return -1;
	}
	if (of.fmt.pix_mp.pixelformat != V4L2_PIX_FMT_ABGR32 || (int)of.fmt.pix_mp.width != S.ew) {
		fprintf(stderr, "sf-screenrec: encoder: wanted AR24 %dx%d, got %.4s %ux%u\n", S.ew, S.eh,
			(char *)&of.fmt.pix_mp.pixelformat, of.fmt.pix_mp.width, of.fmt.pix_mp.height);
		return -1;
	}
	S.ebpl = (int)of.fmt.pix_mp.plane_fmt[0].bytesperline;
	fprintf(stderr, "sf-screenrec: encoder input AR24 %dx%d bpl %d size %u, output H.264\n",
		S.ew, S.eh, S.ebpl, of.fmt.pix_mp.plane_fmt[0].sizeimage);

	/* mtk-vcodec-enc paces itself to this rate: at 60 it managed ~39 fps of
	 * 2272x1032 and dropped the rest; above the 90 Hz panel it keeps up
	 * (~70 fps captured, 18 ms latency, nothing dropped) */
	struct v4l2_streamparm parm = { .type = V4L2_BUF_TYPE_VIDEO_OUTPUT_MPLANE };
	parm.parm.output.timeperframe.numerator = 1;
	parm.parm.output.timeperframe.denominator = 120;
	xioctl(S.vfd, VIDIOC_S_PARM, &parm);
	v4l2_ctrl(V4L2_CID_MPEG_VIDEO_BITRATE, bitrate, "bitrate");
	v4l2_ctrl(V4L2_CID_MPEG_VIDEO_GOP_SIZE, 60, "gop size");
	v4l2_ctrl(V4L2_CID_MPEG_VIDEO_H264_PROFILE, V4L2_MPEG_VIDEO_H264_PROFILE_HIGH, "high profile");
	v4l2_ctrl(V4L2_CID_MPEG_VIDEO_B_FRAMES, 0, "b-frames 0");

	for (int t = 0; t < 2; t++) {
		int type = t == 0 ? V4L2_BUF_TYPE_VIDEO_OUTPUT_MPLANE : V4L2_BUF_TYPE_VIDEO_CAPTURE_MPLANE;
		struct v4l2buf *arr = t == 0 ? S.out : S.cap;
		struct v4l2_requestbuffers rb = { .count = t == 0 ? NOUT : NCAP, .type = (uint32_t)type, .memory = V4L2_MEMORY_MMAP };
		if (xioctl(S.vfd, VIDIOC_REQBUFS, &rb) < 0 || rb.count == 0) {
			fprintf(stderr, "sf-screenrec: encoder: REQBUFS: %s\n", strerror(errno));
			return -1;
		}
		int n = (int)rb.count > (t == 0 ? NOUT : NCAP) ? (t == 0 ? NOUT : NCAP) : (int)rb.count;
		for (int i = 0; i < n; i++) {
			struct v4l2_plane planes[VIDEO_MAX_PLANES] = { 0 };
			struct v4l2_buffer b = { .index = (uint32_t)i, .type = (uint32_t)type, .memory = V4L2_MEMORY_MMAP,
				.length = VIDEO_MAX_PLANES, .m.planes = planes };
			if (xioctl(S.vfd, VIDIOC_QUERYBUF, &b) < 0) {
				fprintf(stderr, "sf-screenrec: encoder: QUERYBUF: %s\n", strerror(errno));
				return -1;
			}
			arr[i].len = planes[0].length;
			arr[i].p = mmap(NULL, planes[0].length, PROT_READ | PROT_WRITE, MAP_SHARED, S.vfd, planes[0].m.mem_offset);
			if (arr[i].p == MAP_FAILED) {
				perror("sf-screenrec: encoder mmap");
				return -1;
			}
			if (t == 1) {
				b.m.planes[0].bytesused = 0;
				if (xioctl(S.vfd, VIDIOC_QBUF, &b) < 0) {
					fprintf(stderr, "sf-screenrec: encoder: QBUF capture: %s\n", strerror(errno));
					return -1;
				}
				arr[i].queued = 1;
			}
		}
		if (t == 0) S.nout = n; else S.ncap = n;
	}
	int type = V4L2_BUF_TYPE_VIDEO_OUTPUT_MPLANE;
	int type2 = V4L2_BUF_TYPE_VIDEO_CAPTURE_MPLANE;
	if (xioctl(S.vfd, VIDIOC_STREAMON, &type) < 0 || xioctl(S.vfd, VIDIOC_STREAMON, &type2) < 0) {
		fprintf(stderr, "sf-screenrec: encoder: STREAMON: %s\n", strerror(errno));
		return -1;
	}
	S.header = g_byte_array_new();
	return 0;
}

/* portrait RGBA (bytes R,G,B,A; maybe bottom-up) -> landscape BGRA rotated
 * 90° counter-clockwise: dst(x, y) = src(W-1-y, x). 32x32 tiles keep both
 * sides in cache. */
static void rotate_into(uint8_t *dst, int dbpl, const uint8_t *src, int sstride, int w, int h, int inverted, int swap_rb)
{
	const int T = 32;
	for (int y0 = 0; y0 < w; y0 += T)
		for (int x0 = 0; x0 < h; x0 += T) {
			int y1 = y0 + T < w ? y0 + T : w, x1 = x0 + T < h ? x0 + T : h;
			for (int y = y0; y < y1; y++) {
				uint32_t *d = (uint32_t *)(dst + (size_t)y * dbpl);
				int sx = w - 1 - y;
				for (int x = x0; x < x1; x++) {
					int sy = inverted ? h - 1 - x : x;
					uint32_t v = *(const uint32_t *)(src + (size_t)sy * sstride + (size_t)sx * 4);
					if (swap_rb)
						v = (v & 0xFF00FF00u) | ((v >> 16) & 0xFFu) | ((v & 0xFFu) << 16);
					d[x] = v;
				}
			}
		}
}

static void v4l2_reclaim_output(void)
{
	for (;;) {
		struct v4l2_plane planes[VIDEO_MAX_PLANES] = { 0 };
		struct v4l2_buffer b = { .type = V4L2_BUF_TYPE_VIDEO_OUTPUT_MPLANE, .memory = V4L2_MEMORY_MMAP,
			.length = VIDEO_MAX_PLANES, .m.planes = planes };
		if (xioctl(S.vfd, VIDIOC_DQBUF, &b) < 0)
			return;
		if (b.index < (uint32_t)S.nout) {
			S.out[b.index].queued = 0;
			S.enc_us += mono_us() - S.queued_at[b.index];
		}
	}
}

static int has_vcl(const uint8_t *p, size_t n)
{
	for (size_t i = 0; i + 3 < n; i++)
		if (p[i] == 0 && p[i + 1] == 0 && p[i + 2] == 1) {
			int t = p[i + 3] & 0x1f;
			if (t >= 1 && t <= 5)
				return 1;
			i += 2;
		}
	return 0;
}

static void v4l2_collect(void)
{
	for (;;) {
		struct v4l2_plane planes[VIDEO_MAX_PLANES] = { 0 };
		struct v4l2_buffer b = { .type = V4L2_BUF_TYPE_VIDEO_CAPTURE_MPLANE, .memory = V4L2_MEMORY_MMAP,
			.length = VIDEO_MAX_PLANES, .m.planes = planes };
		if (xioctl(S.vfd, VIDIOC_DQBUF, &b) < 0)
			return;
		size_t n = planes[0].bytesused - planes[0].data_offset;
		const uint8_t *p = (const uint8_t *)S.cap[b.index].p + planes[0].data_offset;
		if (n > 0) {
			if (!has_vcl(p, n)) {
				g_byte_array_append(S.header, p, (guint)n);   /* SPS/PPS alone */
			} else {
				GstBuffer *gb = gst_buffer_new_allocate(NULL, S.header->len + n, NULL);
				gst_buffer_fill(gb, 0, S.header->data, S.header->len);
				gst_buffer_fill(gb, S.header->len, p, n);
				g_byte_array_set_size(S.header, 0);
				GST_BUFFER_PTS(gb) = (GstClockTime)b.timestamp.tv_sec * GST_SECOND + (GstClockTime)b.timestamp.tv_usec * GST_USECOND;
				GST_BUFFER_DTS(gb) = GST_BUFFER_PTS(gb);
				if (!(b.flags & V4L2_BUF_FLAG_KEYFRAME))
					GST_BUFFER_FLAG_SET(gb, GST_BUFFER_FLAG_DELTA_UNIT);
				gst_app_src_push_buffer(S.appsrc, gb);
				S.encoded++;
			}
		}
		if (b.flags & V4L2_BUF_FLAG_LAST) {
			S.got_last = 1;
			S.cap[b.index].queued = 0;
			continue;
		}
		planes[0].bytesused = 0;
		if (xioctl(S.vfd, VIDIOC_QBUF, &b) == 0)
			S.cap[b.index].queued = 1;
	}
}

/* 1 = queued, 0 = the encoder is busy (frame dropped) */
static int v4l2_encode(const uint8_t *src, int inverted, GstClockTime pts)
{
	v4l2_reclaim_output();
	int i;
	for (i = 0; i < S.nout && S.out[i].queued; i++)
		;
	if (i == S.nout)
		return 0;
	rotate_into(S.out[i].p, S.ebpl, src, S.stride, S.width, S.height, inverted, source_is_rgba());
	struct v4l2_plane planes[VIDEO_MAX_PLANES] = { 0 };
	planes[0].bytesused = (uint32_t)(S.ebpl * S.eh);
	planes[0].length = (uint32_t)S.out[i].len;
	struct v4l2_buffer b = { .index = (uint32_t)i, .type = V4L2_BUF_TYPE_VIDEO_OUTPUT_MPLANE, .memory = V4L2_MEMORY_MMAP,
		.length = 1, .m.planes = planes, .flags = V4L2_BUF_FLAG_TIMESTAMP_COPY };
	b.timestamp.tv_sec = (time_t)(pts / GST_SECOND);
	b.timestamp.tv_usec = (suseconds_t)((pts % GST_SECOND) / GST_USECOND);
	if (xioctl(S.vfd, VIDIOC_QBUF, &b) < 0) {
		fprintf(stderr, "sf-screenrec: encoder: QBUF: %s\n", strerror(errno));
		return 0;
	}
	S.out[i].queued = 1;
	S.queued_at[i] = mono_us();
	return 1;
}

static void v4l2_drain(void)
{
	struct v4l2_encoder_cmd cmd = { .cmd = V4L2_ENC_CMD_STOP };
	if (xioctl(S.vfd, VIDIOC_ENCODER_CMD, &cmd) < 0) {
		fprintf(stderr, "sf-screenrec: encoder: ENC_CMD_STOP: %s (dropping the tail)\n", strerror(errno));
		v4l2_collect();
		return;
	}
	int64_t until = mono_ms() + 3000;
	while (!S.got_last && mono_ms() < until) {
		struct pollfd pfd = { S.vfd, POLLIN, 0 };
		poll(&pfd, 1, 50);
		v4l2_collect();
	}
}

/* ---- wayland --------------------------------------------------------------- */
static void registry_global(void *data, struct wl_registry *reg, uint32_t name, const char *iface, uint32_t version)
{
	(void)data; (void)version;
	if (!strcmp(iface, "wl_shm"))
		S.shm = wl_registry_bind(reg, name, &wl_shm_interface, 1);
	else if (!strcmp(iface, "wl_output") && !S.output)
		S.output = wl_registry_bind(reg, name, &wl_output_interface, 1);
	else if (!strcmp(iface, "lipstick_recorder_manager"))
		S.manager = wl_registry_bind(reg, name, &lipstick_recorder_manager_interface, 1);
}
static void registry_remove(void *data, struct wl_registry *reg, uint32_t name) { (void)data; (void)reg; (void)name; }
static const struct wl_registry_listener registry_listener = { registry_global, registry_remove };

static void request_frame(void)
{
	for (int i = 0; i < NBUF; i++) {
		if (!S.bufs[i].busy) {
			S.bufs[i].busy = 1;
			wl_proxy_marshal_flags(S.recorder, 1, NULL, wl_proxy_get_version(S.recorder), 0, S.bufs[i].wl);
			wl_display_flush(S.display);   /* now, not after this frame's copy */
			S.pending_request = 0;
			return;
		}
	}
	S.pending_request = 1;   /* all buffers out; ask again when one returns */
}

static void schedule_request(void)
{
	if (S.fps_cap > 0) {
		int64_t now = mono_ms();
		if (now < S.next_slot_ms) {
			S.pending_request = 1;   /* the main loop asks at next_slot_ms */
			return;
		}
		S.next_slot_ms = now + (int64_t)(1000.0 / S.fps_cap);
	}
	request_frame();
}

static struct shmbuf *find_buf(struct wl_buffer *b)
{
	for (int i = 0; i < NBUF; i++)
		if (S.bufs[i].wl == b)
			return &S.bufs[i];
	return NULL;
}

static void rec_setup(void *data, struct wl_proxy *rec, int32_t w, int32_t h, int32_t stride, int32_t format)
{
	(void)data; (void)rec;
	S.width = w; S.height = h; S.stride = stride; S.format = format;
	fprintf(stderr, "sf-screenrec: compositor frame %dx%d stride %d shm format 0x%x\n", w, h, stride, format);
	int size = stride * h;
	int fd = (int)syscall(SYS_memfd_create, "sf-screenrec", 0);
	if (fd < 0 || ftruncate(fd, (off_t)size * NBUF) < 0) {
		perror("sf-screenrec: memfd");
		exit(3);
	}
	uint8_t *map = mmap(NULL, (size_t)size * NBUF, PROT_READ | PROT_WRITE, MAP_SHARED, fd, 0);
	if (map == MAP_FAILED) {
		perror("sf-screenrec: mmap");
		exit(3);
	}
	struct wl_shm_pool *pool = wl_shm_create_pool(S.shm, fd, size * NBUF);
	for (int i = 0; i < NBUF; i++) {
		/* wl_shm only takes the formats it advertises (not the RGBA8888 the
		 * recorder announces); the recorder just fills the bytes */
		S.bufs[i].wl = wl_shm_pool_create_buffer(pool, i * size, w, h, stride, WL_SHM_FORMAT_ARGB8888);
		S.bufs[i].data = map + (size_t)i * size;
		S.bufs[i].busy = 0;
	}
	wl_shm_pool_destroy(pool);
	close(fd);
	S.ready = 1;
}

static GstClockTime frame_pts(int64_t comp)
{
	GstClock *clock = gst_element_get_clock(S.pipeline);
	GstClockTime now_rt = 0;
	if (clock) {
		now_rt = gst_clock_get_time(clock) - gst_element_get_base_time(S.pipeline);
		gst_object_unref(clock);
	}
	GstClockTime pts;
	if (S.frames == 0) {
		S.pts0 = now_rt;
		S.comp0 = comp;
		pts = now_rt;
	} else {
		pts = S.pts0 + (GstClockTime)(comp - S.comp0) * GST_MSECOND;
		if (pts <= S.last_pts)   /* never go backwards (clock mismatch) */
			pts = S.last_pts + GST_MSECOND;
	}
	S.last_pts = pts;
	return pts;
}

static void rec_frame(void *data, struct wl_proxy *rec, struct wl_buffer *buffer, uint32_t time, int32_t transform)
{
	(void)data; (void)rec;
	int64_t arrival = mono_ms();
	struct shmbuf *sb = find_buf(buffer);
	if (!sb)
		return;
	S.last_frame_ms = arrival;
	/* the frame our settle repaint asked for must not arm another one (that
	 * loop would repaint the static screen at 25 fps forever) */
	S.settle_repaint = S.settle_repaint == 1 ? 2 : 0;

	/* compositor time: uint32 ms — unwrap, then anchor to the pipeline clock */
	if (S.frames > 0 && time < S.last_comp && S.last_comp - time > 0x80000000u)
		S.comp_wrap += 0x100000000LL;
	S.last_comp = time;
	int64_t comp = S.comp_wrap + time;
	GstClockTime pts = frame_pts(comp);
	/* ask for the next frame NOW, into another buffer: asking only after
	 * this one is processed would miss every other compositor frame */
	schedule_request();
	int inverted = transform == RECORDER_TRANSFORM_Y_INVERTED;

	if (S.dump && S.frames == 0) {   /* one raw frame, for checking the byte order */
		FILE *f = fopen(S.dump, "wb");
		if (f) {
			fprintf(f, "SFRAW %d %d %d %d %d\n", S.width, S.height, S.stride, S.format, inverted);
			fwrite(sb->data, 1, (size_t)S.stride * S.height, f);
			fclose(f);
		}
	}

	int ok;
	if (S.encoder == ENC_V4L2) {
		int64_t t0 = mono_us();
		ok = v4l2_encode(sb->data, inverted, pts);
		S.copy_us += mono_us() - t0;
		sb->busy = 0;
		if (S.pending_request)
			schedule_request();
		v4l2_collect();
	} else {
		gsize size = (gsize)S.stride * S.height;
		GstBuffer *gb = gst_buffer_new_allocate(NULL, size, NULL);
		GstMapInfo m;
		gst_buffer_map(gb, &m, GST_MAP_WRITE);
		if (inverted) {
			for (int y = 0; y < S.height; y++)
				memcpy(m.data + (size_t)y * S.stride, (uint8_t *)sb->data + (size_t)(S.height - 1 - y) * S.stride, (size_t)S.stride);
		} else {
			memcpy(m.data, sb->data, size);
		}
		gst_buffer_unmap(gb, &m);
		sb->busy = 0;
		if (S.pending_request)
			schedule_request();
		GST_BUFFER_PTS(gb) = pts;
		ok = gst_app_src_get_current_level_bytes(S.appsrc) <= (guint64)size * 4;   /* else drop, don't stall */
		if (ok)
			gst_app_src_push_buffer(S.appsrc, gb);
		else
			gst_buffer_unref(gb);
	}
	if (!ok)
		S.dropped++;
	if (S.frames == 0 && S.ready_file) {
		FILE *f = fopen(S.ready_file, "w");
		if (f) {
			fprintf(f, "%lld\n", (long long)arrival);
			fclose(f);
		}
	}
	if (S.tsv)
		fprintf(S.tsv, "%ld\t%lld\t%lld\t%lld\t%d\n", S.frames, (long long)comp, (long long)arrival,
			(long long)(pts / GST_MSECOND), ok);
	S.frames++;
}

static void rec_failed(void *data, struct wl_proxy *rec, int32_t result, struct wl_buffer *buffer)
{
	(void)data; (void)rec;
	fprintf(stderr, "sf-screenrec: compositor failed to record a frame (result %d)\n", result);
	struct shmbuf *sb = find_buf(buffer);
	if (sb)
		sb->busy = 0;
	schedule_request();
}

static void rec_cancelled(void *data, struct wl_proxy *rec, struct wl_buffer *buffer)
{
	(void)data; (void)rec;
	struct shmbuf *sb = find_buf(buffer);
	if (sb)
		sb->busy = 0;
	schedule_request();
}

static const struct recorder_listener recorder_listener = { rec_setup, rec_frame, rec_failed, rec_cancelled };

/* ---- gst helpers ----------------------------------------------------------- */
static int inspect(const char *name)
{
	GstElementFactory *f = gst_element_factory_find(name);
	if (!f) {
		printf("no element '%s'\n", name);
		return 1;
	}
	printf("%s: %s\n", name, gst_element_factory_get_metadata(f, GST_ELEMENT_METADATA_LONGNAME));
	for (const GList *l = gst_element_factory_get_static_pad_templates(f); l; l = l->next) {
		GstStaticPadTemplate *t = l->data;
		GstCaps *caps = gst_static_caps_get(&t->static_caps);
		gchar *s = gst_caps_to_string(caps);
		printf("  pad %s (%s): %s\n", t->name_template, t->direction == GST_PAD_SRC ? "src" : "sink", s);
		g_free(s);
		gst_caps_unref(caps);
	}
	GstElement *e = gst_element_factory_create(f, NULL);
	if (e) {
		guint n;
		GParamSpec **props = g_object_class_list_properties(G_OBJECT_GET_CLASS(e), &n);
		for (guint i = 0; i < n; i++)
			printf("  prop %s (%s): %s\n", props[i]->name, g_type_name(props[i]->value_type), g_param_spec_get_blurb(props[i]));
		g_free(props);
		gst_object_unref(e);
	}
	gst_object_unref(f);
	return 0;
}

static int check_bus(int wait_eos)
{
	GstBus *bus = gst_element_get_bus(S.pipeline);
	int rc = 0;
	for (;;) {
		GstMessage *msg = wait_eos
			? gst_bus_timed_pop_filtered(bus, 15 * GST_SECOND, GST_MESSAGE_EOS | GST_MESSAGE_ERROR)
			: gst_bus_pop_filtered(bus, GST_MESSAGE_ERROR | GST_MESSAGE_WARNING);
		if (!msg) {
			if (wait_eos) {
				fprintf(stderr, "sf-screenrec: no EOS within 15 s\n");
				rc = 1;
			}
			break;
		}
		if (GST_MESSAGE_TYPE(msg) == GST_MESSAGE_ERROR || GST_MESSAGE_TYPE(msg) == GST_MESSAGE_WARNING) {
			GError *err = NULL;
			gchar *dbg = NULL;
			if (GST_MESSAGE_TYPE(msg) == GST_MESSAGE_ERROR)
				gst_message_parse_error(msg, &err, &dbg);
			else
				gst_message_parse_warning(msg, &err, &dbg);
			fprintf(stderr, "sf-screenrec: gst %s from %s: %s (%s)\n",
				GST_MESSAGE_TYPE(msg) == GST_MESSAGE_ERROR ? "ERROR" : "warning",
				GST_OBJECT_NAME(msg->src), err ? err->message : "?", dbg ? dbg : "");
			if (GST_MESSAGE_TYPE(msg) == GST_MESSAGE_ERROR)
				rc = 2;
			g_clear_error(&err);
			g_free(dbg);
		}
		int eos = GST_MESSAGE_TYPE(msg) == GST_MESSAGE_EOS;
		gst_message_unref(msg);
		if (rc == 2 || eos)
			break;
	}
	gst_object_unref(bus);
	return rc;
}

/* root -> the session user (owner of XDG_RUNTIME_DIR) with the privileged group */
static int drop_privileges(void)
{
	const char *rt = getenv("XDG_RUNTIME_DIR");
	if (!rt || !*rt || !strcmp(rt, "/run/user/0"))
		rt = "/run/user/100000";
	struct stat st;
	if (stat(rt, &st) < 0) {
		fprintf(stderr, "sf-screenrec: no session at %s\n", rt);
		return -1;
	}
	struct passwd *pw = getpwuid(st.st_uid);
	struct group *gr = getgrnam("privileged");
	if (!pw || !gr) {
		fprintf(stderr, "sf-screenrec: cannot resolve the session user / privileged group\n");
		return -1;
	}
	setenv("XDG_RUNTIME_DIR", rt, 1);
	if (!getenv("WAYLAND_DISPLAY"))
		setenv("WAYLAND_DISPLAY", "../../display/wayland-0", 1);
	setenv("HOME", pw->pw_dir, 1);
	setenv("USER", pw->pw_name, 1);
	char bus[256];
	snprintf(bus, sizeof bus, "unix:path=%s/dbus/user_bus_socket", rt);
	setenv("DBUS_SESSION_BUS_ADDRESS", bus, 1);
	if (initgroups(pw->pw_name, gr->gr_gid) < 0 || setgid(gr->gr_gid) < 0 || setuid(pw->pw_uid) < 0) {
		perror("sf-screenrec: drop privileges");
		return -1;
	}
	return 0;
}

/* ---- main ------------------------------------------------------------------ */
int main(int argc, char **argv)
{
	const char *out = NULL, *stop_file = NULL, *want = NULL;
	int audio = 0, max_seconds = 0, bitrate = 20000000;
	for (int i = 1; i < argc; i++) {
		const char *a = argv[i];
		const char *v = i + 1 < argc ? argv[i + 1] : NULL;
		if (!strcmp(a, "--inspect") && v) { gst_init(NULL, NULL); return inspect(v); }
		else if (!strcmp(a, "--list")) {   /* element names containing the pattern */
			gst_init(NULL, NULL);
			GList *fl = gst_registry_get_feature_list(gst_registry_get(), GST_TYPE_ELEMENT_FACTORY);
			for (GList *l = fl; l; l = l->next)
				if (!v || strstr(GST_OBJECT_NAME(l->data), v))
					printf("%s\n", GST_OBJECT_NAME(l->data));
			gst_plugin_feature_list_free(fl);
			return 0;
		}
		else if (!strcmp(a, "-o") && v) { out = v; i++; }
		else if (!strcmp(a, "--encoder") && v) { want = v; i++; }
		else if (!strcmp(a, "--audio")) audio = 1;
		else if (!strcmp(a, "--fps") && v) { S.fps_cap = atof(v); i++; }
		else if (!strcmp(a, "--bitrate") && v) { bitrate = atoi(v); i++; }
		else if (!strcmp(a, "--max-seconds") && v) { max_seconds = atoi(v); i++; }
		else if (!strcmp(a, "--stop-file") && v) { stop_file = v; i++; }
		else if (!strcmp(a, "--dump") && v) { S.dump = v; i++; }
		else if (!strcmp(a, "--ready-file") && v) { S.ready_file = v; i++; }
		else { fprintf(stderr, "sf-screenrec: bad argument '%s' (see the header of sf_screenrec.c)\n", a); return 64; }
	}
	if (!out) {
		fprintf(stderr, "usage: sf-screenrec -o OUT.mp4 [--encoder v4l2|soft] [--audio] [--fps N] [--bitrate BPS]\n"
			"                    [--max-seconds N] [--stop-file PATH] [--ready-file PATH] [--dump FILE]\n"
			"       sf-screenrec --inspect ELEMENT | --list [PATTERN]\n");
		return 64;
	}

	/* the hardware encoder needs root to open; everything else runs as the user */
	S.encoder = want && !strcmp(want, "soft") ? ENC_SOFT : ENC_V4L2;
	char vpath[64] = "";
	if (S.encoder == ENC_V4L2) {
		S.vfd = v4l2_find_and_open(vpath, sizeof vpath);
		if (S.vfd < 0) {
			if (want) {
				fprintf(stderr, "sf-screenrec: no usable V4L2 H.264 encoder (run as root: the node is media:system 0660)\n");
				return 5;
			}
			fprintf(stderr, "sf-screenrec: no V4L2 H.264 encoder (not root?) - software MPEG-4\n");
			S.encoder = ENC_SOFT;
		}
	}
	if (getuid() == 0 && drop_privileges() < 0)
		return 4;
	gst_init(NULL, NULL);

	S.display = wl_display_connect(NULL);
	if (!S.display) {
		fprintf(stderr, "sf-screenrec: cannot connect to the compositor (WAYLAND_DISPLAY=%s XDG_RUNTIME_DIR=%s)\n",
			getenv("WAYLAND_DISPLAY") ? getenv("WAYLAND_DISPLAY") : "", getenv("XDG_RUNTIME_DIR") ? getenv("XDG_RUNTIME_DIR") : "");
		return 3;
	}
	struct wl_registry *reg = wl_display_get_registry(S.display);
	wl_registry_add_listener(reg, &registry_listener, NULL);
	wl_display_roundtrip(S.display);
	if (!S.manager || !S.shm || !S.output) {
		fprintf(stderr, "sf-screenrec: compositor has no lipstick_recorder_manager/wl_shm/wl_output\n");
		return 3;
	}
	S.recorder = wl_proxy_marshal_flags(S.manager, 0, &lipstick_recorder_interface, 1, 0, NULL, S.output);
	wl_proxy_add_listener(S.recorder, (void (**)(void))&recorder_listener, NULL);
	if (wl_display_roundtrip(S.display) < 0 || !S.ready) {
		/* lipstick answers a non-privileged client with a protocol error */
		fprintf(stderr, "sf-screenrec: the compositor refused the recorder (error %d) - run it as root "
			"or with 'devel-su -p' (lipstick wants the privileged group)\n", wl_display_get_error(S.display));
		return 4;
	}
	const char *fmt = gst_format_for(S.format);
	if (!fmt) {
		fprintf(stderr, "sf-screenrec: unsupported shm format 0x%x\n", S.format);
		return 5;
	}
	if (S.encoder == ENC_V4L2 && v4l2_setup(bitrate) < 0) {
		if (want)
			return 5;
		fprintf(stderr, "sf-screenrec: %s would not set up - software MPEG-4\n", vpath);
		close(S.vfd);
		S.vfd = -1;
		S.encoder = ENC_SOFT;
	}

	gchar *video;
	if (S.encoder == ENC_V4L2)
		video = g_strdup_printf(
			"appsrc name=v format=time is-live=true do-timestamp=false "
			"caps=\"video/x-h264,stream-format=byte-stream,alignment=au,width=%d,height=%d,framerate=0/1\" "
			"! h264parse ! taginject tags=\"image-orientation=rotate-90\" ! queue",
			S.ew, S.eh);
	else
		video = g_strdup_printf(
			"appsrc name=v format=time is-live=true do-timestamp=false "
			"caps=\"video/x-raw,format=%s,width=%d,height=%d,framerate=0/1\" "
			"! queue max-size-buffers=4 max-size-bytes=0 max-size-time=0 "
			"! videoconvertscale n-threads=4 ! video/x-raw,format=I420,width=%d,height=%d "
			"! avenc_mpeg4 bitrate=%d max-bframes=0 ! mpeg4videoparse ! queue",
			fmt, S.width, S.height, S.width & ~15, S.height & ~15, bitrate);
	gchar *desc = g_strdup_printf("%s ! mp4mux name=mux ! filesink location=\"%s\" %s", video, out,
		audio ? "pulsesrc ! audioconvert ! audioresample ! avenc_aac ! aacparse ! queue ! mux." : "");
	fprintf(stderr, "sf-screenrec: %s encoder%s%s; pipeline %s\n", S.encoder == ENC_V4L2 ? "hardware H.264" : "software MPEG-4",
		S.encoder == ENC_V4L2 ? " " : "", vpath, desc);
	GError *err = NULL;
	S.pipeline = gst_parse_launch(desc, &err);
	if (!S.pipeline || err) {
		fprintf(stderr, "sf-screenrec: pipeline: %s\n", err ? err->message : "?");
		return 6;
	}
	g_free(desc);
	g_free(video);
	S.appsrc = GST_APP_SRC(gst_bin_get_by_name(GST_BIN(S.pipeline), "v"));
	if (gst_element_set_state(S.pipeline, GST_STATE_PLAYING) == GST_STATE_CHANGE_FAILURE) {
		check_bus(0);
		fprintf(stderr, "sf-screenrec: the pipeline did not start\n");
		return 6;
	}
	gchar *tsv = g_strdup_printf("%s.frames.tsv", out);
	S.tsv = fopen(tsv, "w");
	if (S.tsv)
		fprintf(S.tsv, "frame\tcompositor_ms\tarrival_mono_ms\tpts_ms\tencoded\n");
	g_free(tsv);

	signal(SIGINT, on_signal);
	signal(SIGTERM, on_signal);
	signal(SIGHUP, on_signal);
	int64_t start = mono_ms();
	fprintf(stderr, "sf-screenrec: recording to %s (mono %lld)\n", out, (long long)start);
	request_frame();
	wl_proxy_marshal_flags(S.recorder, 2, NULL, 1, 0);   /* repaint: a first frame right away */

	int rc = 0;
	while (!g_stop) {
		if (max_seconds > 0 && mono_ms() - start >= (int64_t)max_seconds * 1000)
			break;
		if (stop_file && access(stop_file, F_OK) == 0)
			break;
		if (S.pending_request && (S.fps_cap <= 0 || mono_ms() >= S.next_slot_ms)) {
			S.next_slot_ms = 0;
			schedule_request();
		}
		/* The compositor records the frame AFTER a request arrives; the one it
		 * painted while our next request was still in flight is lost — and
		 * when that was the last change (a page finishing its paint), the
		 * video would never show the settled screen. 40 ms of silence ->
		 * ask for one repaint, which captures exactly the current state. */
		if (!S.settle_repaint && S.last_frame_ms && mono_ms() - S.last_frame_ms >= 40) {
			wl_proxy_marshal_flags(S.recorder, 2, NULL, 1, 0);
			S.settle_repaint = 1;
		}
		wl_display_flush(S.display);
		struct pollfd pfd[2] = { { wl_display_get_fd(S.display), POLLIN, 0 }, { S.vfd, POLLIN, 0 } };
		int timeout = S.pending_request && S.fps_cap > 0 ? 5 : !S.settle_repaint ? 10 : 100;
		poll(pfd, S.vfd >= 0 ? 2 : 1, timeout);
		if (pfd[0].revents & POLLIN) {
			if (wl_display_dispatch(S.display) < 0) {
				fprintf(stderr, "sf-screenrec: lost the compositor (error %d)\n", wl_display_get_error(S.display));
				rc = 7;
				break;
			}
		} else {
			wl_display_dispatch_pending(S.display);
		}
		if (S.vfd >= 0)
			v4l2_collect();
		if (check_bus(0) == 2) {
			rc = 8;
			break;
		}
	}

	int64_t span = mono_ms() - start;
	wl_proxy_marshal_flags(S.recorder, 0, NULL, 1, WL_MARSHAL_FLAG_DESTROY);
	wl_display_flush(S.display);
	if (S.encoder == ENC_V4L2)
		v4l2_drain();
	gst_app_src_end_of_stream(S.appsrc);
	if (audio)
		gst_element_send_event(S.pipeline, gst_event_new_eos());
	if (check_bus(1) && !rc)
		rc = 9;
	gst_element_set_state(S.pipeline, GST_STATE_NULL);
	if (S.tsv)
		fclose(S.tsv);
	fprintf(stderr, "sf-screenrec: %ld frames in %.1f s (%.1f fps avg), %ld dropped, %ld encoded, %.1f ms/frame copy, %.1f ms encoder latency -> %s\n",
		S.frames, span / 1000.0, span > 0 ? S.frames * 1000.0 / span : 0.0, S.dropped,
		S.encoder == ENC_V4L2 ? S.encoded : S.frames - S.dropped,
		S.frames ? S.copy_us / 1000.0 / S.frames : 0.0, S.encoded ? S.enc_us / 1000.0 / S.encoded : 0.0, out);
	wl_display_disconnect(S.display);
	return rc;
}
