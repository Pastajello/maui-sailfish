#!/usr/bin/env python3
"""Page-load timing from a showcase recording (tools/sf record --keep DIR).

Reads either recording kind:
  DIR/screen.mp4 + screen.mp4.frames.tsv   compositor recorder (default): frame
      times are CLOCK_MONOTONIC arrival times, steps use the log's mono=
  DIR/frames/<ms>.png                      in-app recorder (--app): frame names
      are ms since recording start, steps use the log's t=

For every navigation step of the tour ("open …", "<page> #n", "back …") it
lines the frames up with the step time from the app log and reports, relative
to the tap:
  first    the first rendered frame after the tap
  move     the first frame that clearly differs from the tap-time picture
           (the transition / new content visibly started)
  settled  the last frame that still changed the picture noticeably (> 1.0
           mean abs difference on a 90x198 thumbnail) before the next step
  stall    the longest gap between rendered frames inside [tap, settled] —
           the GUI thread was busy (no frames are rendered while it is)
plus the NAV-TIMELINE and PAINT-SLOW lines the app logged for that step.

Usage: tools/sf page-load DIR [--max-settled MS]   (exit 1 when a page settles later)
Needs Pillow, numpy (and ffmpeg for screen.mp4).
"""
import os
import re
import subprocess
import sys

import numpy as np
from PIL import Image


def load_video(path):
    """Decoded frames (90x198 RGB, float) keyed by their arrival time (mono ms)."""
    rows = [l.split("\t") for l in open(path + ".frames.tsv").read().splitlines()[1:] if l]
    times = [int(r[2]) for r in rows if r[4] == "1"]
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", path, "-fps_mode", "passthrough", "-vf", "scale=90:198",
                          "-f", "rawvideo", "-pix_fmt", "rgb24", "-"], check=True, capture_output=True).stdout
    n = len(raw) // (90 * 198 * 3)
    arr = np.frombuffer(raw[: n * 90 * 198 * 3], dtype=np.uint8).reshape(n, 198, 90, 3).astype(np.float32)
    if n != len(times):
        print(f"note: {n} decoded frames vs {len(times)} in the .tsv - using the first {min(n, len(times))}")
    imgs = {}
    for t, a in zip(times, arr):
        imgs[t] = a
    return imgs, sorted(imgs)


def main(argv):
    if len(argv) < 2:
        sys.exit(__doc__)
    root = argv[1]
    limit = int(argv[argv.index("--max-settled") + 1]) if "--max-settled" in argv else None
    video = os.path.join(root, "screen.mp4")
    if os.path.exists(video):
        imgs, times = load_video(video)
        clock = "mono"
    else:
        fdir = os.path.join(root, "frames")
        frames = sorted((int(f.rsplit(".", 1)[0]), f) for f in os.listdir(fdir) if f[0].isdigit())

        def load(name):
            a = np.asarray(Image.open(os.path.join(fdir, name)).convert("RGBA").resize((90, 198))).astype(np.float32)
            return a[..., :3] * (a[..., 3:4] / 255.0)

        imgs = {t: load(f) for t, f in frames}
        times = [t for t, _ in frames]
        clock = "t"

    steps, notes = [], {}
    current = None
    for line in open(os.path.join(root, "app.log"), errors="ignore"):
        m = re.search(r"SHOWCASE step \d+/\d+ t=(\d+)(?: mono=(\d+))?: (.*) \(page", line)
        if m:
            when = m.group(2) if clock == "mono" else m.group(1)
            if when is None:
                sys.exit("app.log has no mono= step times (old build) - re-deploy the sample")
            current = (int(when), m.group(3))
            steps.append(current)
            continue
        m = re.search(r"(NAV-TIMELINE .*|PAINT-SLOW .*)$", line)
        if m and current:
            notes.setdefault(current, []).append(m.group(1))

    worst = 0
    print(f"{'step':26s} {'first':>6s} {'move':>6s} {'settled':>8s} {'stall':>6s}  (ms after the tap)")
    for i, step in enumerate(steps):
        t, name = step
        if not re.search(r"^(open |back|.* #\d+$)", name):
            continue
        t_next = steps[i + 1][0] if i + 1 < len(steps) else t + 3000
        seq = [x for x in times if t < x < t_next]
        before = [x for x in times if x <= t]
        last = imgs[before[-1]] if before else None
        changes = []
        for x in seq:
            diff = float(np.abs(imgs[x] - last).mean()) if last is not None else 0.0
            changes.append((x - t, diff))
            last = imgs[x]
        first = changes[0][0] if changes else -1
        # move: the first frame that differs clearly from the tap-time picture
        # (the transition visibly started; a pressed highlight stays below 5)
        base = imgs[before[-1]] if before else None
        move = next((x - t for x in seq if base is not None and float(np.abs(imgs[x] - base).mean()) > 5.0), -1)
        big = [ms for ms, d in changes if d > 1.0]
        settled = big[-1] if big else first
        points = [0] + [ms for ms, _ in changes if ms <= settled]
        stall = max((b - a for a, b in zip(points, points[1:])), default=0)
        if not name.startswith("back"):
            worst = max(worst, settled)
        req = ""
        for note in notes.get(step, []):
            m = re.search(r"request@mono=(\d+)", note)
            if m and clock == "mono":
                req = f"  request +{int(m.group(1)) - t} ms"
                break
        print(f"{name:26s} {first:6d} {move:6d} {settled:8d} {stall:6d}{req}")
        for note in notes.get(step, []):
            if note.startswith("NAV-TIMELINE") or "border-canvas" in note or int(re.search(r"(\d+) ms$", note).group(1)) >= 40:
                print(f"{'':28s}{note}")
    if limit is not None and worst > limit:
        print(f"FAIL: a page settled {worst} ms after its tap (> {limit} ms)")
        return 1
    return 0


if __name__ == "__main__":
    if len(sys.argv) < 2 or sys.argv[1] in ("-h", "--help"):
        print(__doc__)
        sys.exit(0 if len(sys.argv) > 1 else 2)
    sys.exit(main(sys.argv))
