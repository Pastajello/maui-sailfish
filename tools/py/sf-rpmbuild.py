#!/usr/bin/env python3
"""rpmbuild -bb fallback for build hosts without the rpm toolchain.

Microsoft.Maui.SailfishOS.targets packages the published sample with

    rpmbuild -bb --target <triple> --buildroot <dir> \\
             --define '_topdir <dir>' --define '_rpmdir <dir>' \\
             --define '_rpmfilename %{NAME}-%{VERSION}-%{RELEASE}.%{ARCH}.rpm' <spec>

On hosts where rpm cannot be installed (no package-manager access / no sudo)
this script writes a byte-compatible RPM with nothing but the Python standard
library plus an xz/lzma compressor, so `dotnet publish -p:CreateSailfishRpm`
and tools/sf deploy keep working. Layout follows a real Sailfish package
(verified against releases.jolla.com RPMs with tools/py/sf-rpm2cpio.py):

    lead(96) + signature header (+ zero pad to 8) + main header + cpio payload

Details that must match rpm's reader: the immutable-region trailer stores
-(nindex * 16) as a signed int32 and sits at the end of the data store, index
entries are sorted by tag, INT16/32/64 values are aligned to their size inside
the store, file digests are sha256 (FILEDIGESTALGO=8), the payload is announced
as cpio/xz and symlinks record the target length as their size.

Supported spec features: preamble tags (Name/Version/Release/Summary/License/
Vendor/Group/Packager/URL/AutoReqProv/BuildArch), %description, the scriptlets
%pre/%post/%preun/%postun, %prep/%build/%install (run through /bin/sh with
RPM_BUILD_ROOT exported) and %files with plain paths, directories (recursive),
%dir, %attr(...), %config, %doc and globs - everything the generated Sailfish
spec uses. Subpackages and unknown directives fail loudly instead of silently
producing a package that would not install.
"""

import glob as globmod
import hashlib
import lzma
import os
import re
import shlex
import shutil
import socket
import stat
import struct
import subprocess
import sys
import tempfile
import time

HEADER_MAGIC = b"\x8e\xad\xe8\x01\x00\x00\x00\x00"
LEAD_MAGIC = b"\xed\xab\xee\xdb"
LEAD_SIZE = 96

T_INT16 = 3
T_INT32 = 4
T_INT64 = 5
T_STRING = 6
T_BIN = 7
T_STRING_ARRAY = 8
T_I18N = 9
_TYPE_ALIGN = {T_INT16: 2, T_INT32: 4, T_INT64: 8}

TAG_SIGREGION = 62
TAG_MAINREGION = 63
TAG_NAME = 1000
TAG_VERSION = 1001
TAG_RELEASE = 1002
TAG_SUMMARY = 1004
TAG_DESCRIPTION = 1005
TAG_BUILDTIME = 1006
TAG_BUILDHOST = 1007
TAG_SIZE = 1009
TAG_VENDOR = 1011
TAG_LICENSE = 1014
TAG_PACKAGER = 1015
TAG_GROUP = 1016
TAG_URL = 1020
TAG_OS = 1021
TAG_ARCH = 1022
TAG_PREIN = 1023
TAG_POSTIN = 1024
TAG_PREUN = 1025
TAG_POSTUN = 1026
TAG_FILESIZES = 1028
TAG_FILEMODES = 1030
TAG_FILERDEVS = 1033
TAG_FILEMTIMES = 1034
TAG_FILEDIGESTS = 1035
TAG_FILELINKTOS = 1036
TAG_FILEFLAGS = 1037
TAG_FILEUSERNAME = 1039
TAG_FILEGROUPNAME = 1040
TAG_FILEVERIFYFLAGS = 1045
TAG_REQUIREFLAGS = 1048
TAG_REQUIRENAME = 1049
TAG_REQUIREVERSION = 1050
TAG_RPMVERSION = 1064
TAG_PREINPROG = 1085
TAG_POSTINPROG = 1086
TAG_PREUNPROG = 1087
TAG_POSTUNPROG = 1088
TAG_FILEDEVICES = 1095
TAG_FILEINODES = 1096
TAG_FILELANGS = 1097
TAG_DIRINDEXES = 1116
TAG_BASENAMES = 1117
TAG_DIRNAMES = 1118
TAG_PAYLOADFORMAT = 1124
TAG_PAYLOADCOMPRESSOR = 1125
TAG_PAYLOADFLAGS = 1126
TAG_FILEDIGESTALGO = 5011
TAG_PAYLOADDIGEST = 5092
TAG_PAYLOADDIGESTTYPE = 5093

SIG_SHA1 = 269
SIG_SHA256 = 273
SIG_SIZE = 1000
SIG_MD5 = 1004
SIG_PAYLOADSIZE = 1007

RPMFILE_CONFIG = 1 << 0
RPMFILE_DOC = 1 << 1

S_IFREG = 0o100000
S_IFDIR = 0o040000
S_IFLNK = 0o120000

XZ_LEVEL = 2
RPM_VERSION_STAMP = "4.20.0"


def die(msg):
    sys.stderr.write("sf-rpmbuild: ERROR: %s\n" % msg)
    sys.exit(1)


def warn(msg):
    sys.stderr.write("sf-rpmbuild: warning: %s\n" % msg)


def _enc(s):
    return s.encode("utf-8") if isinstance(s, str) else bytes(s)


class Header(object):
    """An RPM header: tag-sorted index entries + a type-aligned data store."""

    def __init__(self, region_tag):
        self.region_tag = region_tag
        self._entries = {}

    def put(self, tag, typ, value):
        if tag in self._entries:
            die("duplicate header tag %d" % tag)
        self._entries[tag] = (typ, value)

    def string(self, tag, s):
        self.put(tag, T_STRING, [s])

    def i18n(self, tag, s):
        self.put(tag, T_I18N, [s])

    def array(self, tag, items):
        self.put(tag, T_STRING_ARRAY, list(items))

    def int16(self, tag, items):
        self.put(tag, T_INT16, list(items))

    def int32(self, tag, items):
        self.put(tag, T_INT32, list(items))

    def int64(self, tag, items):
        self.put(tag, T_INT64, list(items))

    def serialize(self):
        store = bytearray()
        index = []
        for tag in sorted(self._entries):
            typ, value = self._entries[tag]
            align = _TYPE_ALIGN.get(typ, 1)
            if align > 1 and len(store) % align:
                store.extend(b"\0" * (align - len(store) % align))
            offset = len(store)
            if typ in (T_STRING, T_I18N, T_STRING_ARRAY):
                data = b"".join(_enc(v) + b"\0" for v in value)
                count = len(value)
            elif typ == T_INT16:
                data = struct.pack(">%dH" % len(value),
                                   *[v & 0xFFFF for v in value])
                count = len(value)
            elif typ == T_INT32:
                data = struct.pack(">%dI" % len(value),
                                   *[v & 0xFFFFFFFF for v in value])
                count = len(value)
            elif typ == T_INT64:
                data = struct.pack(">%dQ" % len(value),
                                   *[v & 0xFFFFFFFFFFFFFFFF for v in value])
                count = len(value)
            elif typ == T_BIN:
                data = bytes(value)
                count = len(data)
            else:
                die("unsupported header value type %d (tag %d)" % (typ, tag))
                return b""
            index.append((tag, typ, offset, count))
            store.extend(data)

        # the immutable region covers every entry; its 16-byte trailer sits at
        # the end of the store and holds -(nindex * 16) as a signed int32
        nindex = len(index) + 1
        trailer_offset = len(store)
        store.extend(struct.pack(">IIiI", self.region_tag, T_BIN,
                                 -(nindex * 16), 16))
        full_index = [(self.region_tag, T_BIN, trailer_offset, 16)] + index
        body = b"".join(struct.pack(">IIII", *e) for e in full_index)
        return (HEADER_MAGIC + struct.pack(">II", nindex, len(store))
                + body + bytes(store))


def make_lead(nvr):
    """The 96-byte lead: modern rpm only checks the magic, but write it right."""
    name = _enc(nvr)[:LEAD_SIZE - 30]
    lead = bytearray(LEAD_SIZE)
    lead[0:4] = LEAD_MAGIC
    lead[4] = 3                           # major
    lead[5] = 0                           # minor
    struct.pack_into(">H", lead, 6, 0)    # type: binary package
    struct.pack_into(">H", lead, 8, 0)    # archnum (ignored by rpm >= 4.x)
    lead[10:10 + len(name)] = name
    struct.pack_into(">H", lead, 76, 1)   # os: linux
    struct.pack_into(">H", lead, 78, 5)   # signature type: header signatures
    return bytes(lead)


def cpio_header(ino, mode, uid, gid, nlink, mtime, filesize, namesize):
    fields = (ino, mode, uid, gid, nlink, mtime, filesize,
              0, 0, 0, 0, namesize, 0)     # devmajor/minor, rdev*, check
    return b"070701" + b"".join(b"%08X" % v for v in fields)


class CpioWriter(object):
    """newc (070701) archive written straight into a compressing sink."""

    def __init__(self, sink):
        self.sink = sink
        self.size = 0
        self._ino = 0

    def _write(self, data):
        self.sink.write(data)
        self.size += len(data)

    def _pad(self, count):
        pad = (-count) % 4
        if pad:
            self._write(b"\0" * pad)

    def _ino_next(self):
        self._ino += 1
        return self._ino

    def _name(self, path):
        raw = _enc(path) + b"\0"
        return raw, len(raw)

    def directory(self, path, mode, mtime):
        raw, namesize = self._name(path)
        self._write(cpio_header(self._ino_next(), S_IFDIR | (mode & 0o7777),
                                0, 0, 2, mtime, 0, namesize))
        self._write(raw)
        self._pad(110 + namesize)

    def symlink(self, path, target, mode, mtime):
        raw, namesize = self._name(path)
        data = _enc(target)
        self._write(cpio_header(self._ino_next(), S_IFLNK | (mode & 0o7777),
                                0, 0, 1, mtime, len(data), namesize))
        self._write(raw)
        self._pad(110 + namesize)
        self._write(data)
        self._pad(len(data))

    def file_begin(self, path, mode, mtime, filesize):
        raw, namesize = self._name(path)
        self._write(cpio_header(self._ino_next(), S_IFREG | (mode & 0o7777),
                                0, 0, 1, mtime, filesize, namesize))
        self._write(raw)
        self._pad(110 + namesize)

    def file_body(self, path, filesize):
        with open(path, "rb") as f:
            shutil.copyfileobj(f, self.sink, 1024 * 256)
        self.size += filesize
        self._pad(filesize)

    def trailer(self):
        raw, namesize = self._name("TRAILER!!!")
        self._write(cpio_header(0, 0, 0, 0, 1, 0, 0, namesize))
        self._write(raw)
        self._pad(110 + namesize)


class _XzSink(object):
    """write()-able sink piping into an external xz process (multi-threaded)."""

    def __init__(self, proc, out):
        self.proc = proc
        self.out = out

    def write(self, data):
        self.proc.stdin.write(data)

    def close(self):
        self.proc.stdin.close()
        rc = self.proc.wait()
        self.out.close()
        if rc != 0:
            die("xz failed compressing the payload (rc=%d)" % rc)


class _LzmaSink(object):
    """write()-able sink using the stdlib lzma compressor."""

    def __init__(self, out, comp):
        self.out = out
        self.comp = comp

    def write(self, data):
        self.out.write(self.comp.compress(data))

    def close(self):
        self.out.write(self.comp.flush())
        self.out.close()


def open_payload(payload_path):
    """Return (sink, compressor, flags) for the cpio payload."""
    out = open(payload_path, "wb")
    xz = shutil.which("xz")
    if xz:
        proc = subprocess.Popen([xz, "-T0", "-%d" % XZ_LEVEL, "-c"],
                                stdin=subprocess.PIPE, stdout=out)
        return _XzSink(proc, out), "xz", str(XZ_LEVEL)
    return (_LzmaSink(out, lzma.LZMACompressor(format=lzma.FORMAT_XZ,
                                               preset=XZ_LEVEL)),
            "xz", str(XZ_LEVEL))


def expand_macros(text, macros):
    """Expand %{name} / %name against the macro table (case-insensitive).

    `%%` is rpm's escaped percent; it becomes a literal `%` per pass and the
    result is re-expanded on the next one, exactly like rpmbuild does - which
    matters because MSBuild hands us `_rpmfilename %%{NAME}-%%{VERSION}-...`.
    """
    def sub_braced(m):
        key = m.group(1).lstrip("?")
        val = macros.get(key.lower())
        return val if val is not None else ""

    def sub_bare(m):
        val = macros.get(m.group(1).lower())
        return val if val is not None else m.group(0)

    prev = None
    out = text
    for _ in range(8):                    # macros may expand to macros
        if out == prev:
            break
        prev = out
        out = out.replace("%%", "\x01")   # protect the escaped percent
        out = re.sub(r"%\{([?]?[A-Za-z_][A-Za-z0-9_]*)\}", sub_braced, out)
        out = re.sub(r"%([A-Za-z_][A-Za-z0-9_]*)(?![A-Za-z0-9_{(])", sub_bare, out)
        out = out.replace("\x01", "%")
    return out


SECTION_RE = re.compile(
    r"^%(description|files|package|prep|build|install|pre|post|preun|postun"
    r"|pretrans|posttrans|changelog|clean|check)\b(.*)$", re.I)


def parse_spec(spec_path):
    """Split a spec into (preamble lines, {section: lines}, {macro: value})."""
    try:
        with open(spec_path, encoding="utf-8") as f:
            text = f.read()
    except OSError as exc:
        die("cannot read the spec file: %s" % exc)
    sections = {"preamble": []}
    macros = {}
    current = "preamble"
    for raw in text.splitlines():
        line = raw.rstrip()
        if line.startswith("%%"):                     # escaped literal %
            sections[current].append(line[1:])
            continue
        if line.startswith("%"):
            m = re.match(r"^%(?:define|global)\s+(\S+)\s*(.*)$", line)
            if m:
                macros[m.group(1).lower()] = m.group(2).strip()
                continue
            m = SECTION_RE.match(line)
            if m:
                name = m.group(1).lower()
                if name == "package":
                    die("subpackages (%package) are not supported by sf-rpmbuild.py")
                current = name
                sections.setdefault(current, [])
                sections.setdefault(current + "_args", []).append(m.group(2).strip())
                continue
        sections[current].append(line)
    return sections, macros


# RPMSENSE comparison bits of a dependency
_SENSE = {"<": 0x02, ">": 0x04, "=": 0x08, "<=": 0x0A, ">=": 0x0C}


def parse_requires(value):
    """'a, b >= 1.2 c' -> [(name, flags, version)] (rpm's dependency syntax)."""
    tokens = value.replace(",", " ").split()
    deps = []
    i = 0
    while i < len(tokens):
        name, flags, ver = tokens[i], 0, ""
        if i + 1 < len(tokens) and tokens[i + 1] in _SENSE:
            if i + 2 >= len(tokens):
                die("dependency %r lacks a version" % value)
            flags, ver = _SENSE[tokens[i + 1]], tokens[i + 2]
            i += 3
        else:
            i += 1
        deps.append((name, flags, ver))
    return deps


def parse_preamble(lines, macros):
    tags = {"requires": []}
    for line in lines:
        s = line.strip()
        if not s or s.startswith("#"):
            continue
        m = re.match(r"^([A-Za-z]\w*)(\([^)]*\))?\s*:\s*(.*)$", s)
        if not m:
            warn("ignoring unparseable spec preamble line: %r" % s)
            continue
        key = m.group(1).lower()
        value = expand_macros(m.group(3).strip(), macros)
        if key == "requires":          # repeatable; Requires(post) etc. too
            tags["requires"].extend(parse_requires(value))
            continue
        tags[key] = value
    return tags


def parse_files(lines, macros):
    entries = []
    for line in lines:
        s = line.strip()
        if not s or s.startswith("#"):
            continue
        s = expand_macros(s, macros).strip()
        # %defattr(-,root,root,-): this writer stamps root:root on every entry
        # anyway (the host rpmbuild needs it — on macOS it would record wheel)
        if re.match(r"^%defattr\(", s):
            continue
        flags = 0
        mode_override = None
        dir_only = False
        while s.startswith("%"):
            if s.startswith("%dir"):
                dir_only = True
                s = s[len("%dir"):].strip()
                continue
            m = re.match(r"^%attr\(([^)]*)\)\s*(.*)$", s)
            if m:
                perm = m.group(1).split(",")[0].strip()
                if perm not in ("", "-"):
                    try:
                        mode_override = int(perm, 8)
                    except ValueError:
                        die("bad %attr permission %r in %r" % (perm, s))
                s = m.group(2).strip()
                continue
            m = re.match(r"^%config(?:\([^)]*\))?\s*(.*)$", s)
            if m:
                flags |= RPMFILE_CONFIG
                s = m.group(1).strip()
                continue
            if s.startswith("%doc"):
                flags |= RPMFILE_DOC
                s = s[len("%doc"):].strip()
                continue
            m = re.match(r"^%(?:license|verify)(?:\([^)]*\))?\s*(.*)$", s)
            if m:
                s = m.group(1).strip()
                continue
            die("unsupported %files directive in %r" % s)
        if not s:
            die("empty %files entry")
        entries.append({"path": s, "flags": flags, "mode": mode_override,
                        "dir_only": dir_only})
    return entries


def collect_files(buildroot, entries):
    """Resolve %files against the buildroot -> flat, parent-first file list."""
    items = {}
    pending = []

    def record(rel, flags, mode_override, recurse):
        rel = rel.strip("/")
        if not rel or rel in items:
            return
        ap = os.path.join(buildroot, rel)
        try:
            st = os.lstat(ap)
        except OSError:
            die("%files entry missing from the buildroot: /%s" % rel)
        mode = st.st_mode
        if mode_override is not None:
            mode = (mode & ~0o7777) | (mode_override & 0o7777)
        mtime = int(st.st_mtime)
        if stat.S_ISLNK(st.st_mode):
            target = os.readlink(ap)
            items[rel] = {"kind": "link", "cpio": rel, "mode": mode,
                          "mtime": mtime, "size": len(_enc(target)),
                          "target": target, "flags": flags, "path": ap}
        elif stat.S_ISDIR(st.st_mode):
            items[rel] = {"kind": "dir", "cpio": rel, "mode": mode,
                          "mtime": mtime, "size": 0, "target": "",
                          "flags": flags, "path": ap}
            if recurse:
                pending.append(rel)
        elif stat.S_ISREG(st.st_mode):
            items[rel] = {"kind": "file", "cpio": rel, "mode": mode,
                          "mtime": mtime, "size": st.st_size, "target": "",
                          "flags": flags, "path": ap}
        else:
            die("unsupported file type for /%s (mode %o): only regular files, "
                "directories and symlinks can be packaged" % (rel, st.st_mode))

    for e in entries:
        path = e["path"]
        if any(c in path for c in "*?["):
            matches = sorted(globmod.glob(os.path.join(buildroot, path.lstrip("/"))))
            if not matches:
                die("%files glob matched nothing in the buildroot: %s" % path)
            for m in matches:
                record(os.path.relpath(m, buildroot).replace(os.sep, "/"),
                       e["flags"], e["mode"], not e["dir_only"])
        else:
            record(path, e["flags"], e["mode"], not e["dir_only"])

    while pending:
        rel = pending.pop()
        ap = os.path.join(buildroot, rel)
        try:
            names = sorted(os.listdir(ap))
        except OSError as exc:
            die("cannot list buildroot directory /%s: %s" % (rel, exc))
        for name in names:
            record(rel + "/" + name, 0, None, True)

    if not items:
        die("the %files list resolved to nothing (buildroot: %s)" % buildroot)
    return [items[k] for k in sorted(items)]


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def run_scriptlet(name, lines, topdir, buildroot):
    body = "\n".join(lines).strip()
    if not body:
        return
    builddir = os.path.join(topdir, "BUILD")
    os.makedirs(builddir, exist_ok=True)
    env = dict(os.environ)
    env["RPM_BUILD_ROOT"] = buildroot
    env["RPM_BUILD_DIR"] = builddir
    sh = shutil.which("sh") or "/bin/sh"
    sys.stderr.write("sf-rpmbuild: running the %%%s scriptlet with %s\n" % (name, sh))
    if subprocess.call([sh, "-e", "-c", body], cwd=builddir, env=env) != 0:
        die("the %%%s scriptlet failed" % name)


def parse_args(argv):
    mode = None
    target = None
    buildroot = None
    defines = []
    spec = None
    i = 0
    while i < len(argv):
        arg = argv[i]
        if arg in ("-bb", "-ba", "-bp", "-bc", "-bi"):
            mode = arg
            i += 1
        elif arg in ("--target", "--buildroot"):
            if i + 1 >= len(argv):
                die("%s needs an argument" % arg)
            if arg == "--target":
                target = argv[i + 1]
            else:
                buildroot = argv[i + 1]
            i += 2
        elif arg in ("-D", "--define"):
            if i + 1 >= len(argv):
                die("%s needs an argument" % arg)
            defines.append(argv[i + 1])
            i += 2
        elif arg.startswith("--define="):
            defines.append(arg.split("=", 1)[1])
            i += 1
        elif arg.startswith("-D") and len(arg) > 2:
            defines.append(arg[2:])
            i += 1
        elif arg in ("--nodeps", "--nosignature", "--short-circuit", "--nocheck",
                     "--noclean", "--nobuild", "--rmspec", "--rmsource"):
            i += 1
        elif arg in ("-h", "--help"):
            sys.stdout.write(__doc__)
            sys.exit(0)
        elif arg.startswith("-"):
            warn("ignoring unsupported rpmbuild option %r" % arg)
            i += 1
        elif spec is None:
            spec = arg
            i += 1
        else:
            warn("ignoring extra argument %r" % arg)
            i += 1
    if mode != "-bb":
        die("only 'rpmbuild -bb' is supported (got %r)" % mode)
    if not spec:
        die("no spec file given")
    return spec, target, buildroot, defines


def build_rpm(spec, target, buildroot_cli, defines):
    sections, spec_macros = parse_spec(spec)
    macros = {}
    macros.update(spec_macros)
    for d in defines:                       # --define '_topdir /path'
        parts = d.split(None, 1)
        if len(parts) != 2:
            die("--define expects 'name value', got %r" % d)
        macros[parts[0].lower()] = parts[1]

    preamble = parse_preamble(sections.get("preamble", []), macros)
    name = preamble.get("name")
    version = preamble.get("version")
    release = preamble.get("release", "0")
    if not name or not version:
        die("the spec must define Name and Version (got %r/%r)" % (name, version))

    if target:
        arch = target.split("-")[0]
    elif preamble.get("buildarch"):
        arch = preamble["buildarch"]
    else:
        arch = os.uname().machine

    buildroot = buildroot_cli or preamble.get("buildroot")
    if not buildroot:
        die("no buildroot (--buildroot or a BuildRoot: tag is required)")
    buildroot = os.path.abspath(buildroot)
    if not os.path.isdir(buildroot):
        die("the buildroot does not exist: %s" % buildroot)

    topdir = os.path.abspath(macros.get("_topdir", os.path.dirname(os.path.abspath(spec))))
    rpmdir = os.path.abspath(macros.get("_rpmdir", os.path.join(topdir, "RPMS")))
    for key, val in (("name", name), ("version", version), ("release", release),
                     ("arch", arch), ("_arch", arch), ("_topdir", topdir),
                     ("_rpmdir", rpmdir), ("_builddir", os.path.join(topdir, "BUILD")),
                     ("_buildrootdir", os.path.join(topdir, "BUILDROOT")),
                     ("_sourcedir", os.path.join(topdir, "SOURCES")),
                     ("_specdir", os.path.join(topdir, "SPECS")),
                     ("_srcrpmdir", os.path.join(topdir, "SRPMS")),
                     ("_target", target or arch), ("_target_cpu", arch),
                     ("_target_os", "linux"), ("buildroot", buildroot)):
        macros.setdefault(key, val)
    macros.setdefault("_rpmfilename",
                      "%{ARCH}/%{NAME}-%{VERSION}-%{RELEASE}.%{ARCH}.rpm")

    for sect in ("prep", "build", "install"):
        if sections.get(sect):
            run_scriptlet(sect, sections[sect], topdir, buildroot)

    entries = parse_files(sections.get("files", []), macros)
    items = collect_files(buildroot, entries)

    payload_tmp = tempfile.NamedTemporaryFile(prefix="sf-rpmbuild-payload-",
                                              suffix=".cpio.xz", delete=False)
    payload_path = payload_tmp.name
    payload_tmp.close()
    try:
        sink, compressor, cflags = open_payload(payload_path)
        writer = CpioWriter(sink)
        digests = []
        for it in items:
            if it["kind"] == "dir":
                writer.directory(it["cpio"], it["mode"], it["mtime"])
                digests.append("")
            elif it["kind"] == "link":
                writer.symlink(it["cpio"], it["target"], it["mode"], it["mtime"])
                digests.append("")
            else:
                writer.file_begin(it["cpio"], it["mode"], it["mtime"], it["size"])
                writer.file_body(it["path"], it["size"])
                digests.append(sha256_file(it["path"]))
        writer.trailer()
        sink.close()
        cpio_size = writer.size
        payload_size = os.path.getsize(payload_path)

        main = Header(TAG_MAINREGION)
        main.string(TAG_NAME, name)
        main.string(TAG_VERSION, version)
        main.string(TAG_RELEASE, release)
        main.i18n(TAG_SUMMARY, preamble.get("summary", name))
        desc = "\n".join(sections.get("description", [])).strip() or preamble.get("summary", name)
        main.i18n(TAG_DESCRIPTION, desc)
        main.int32(TAG_BUILDTIME, [int(time.time())])
        main.string(TAG_BUILDHOST, socket.gethostname())
        total = sum(it["size"] for it in items)
        if total > 0xFFFFFFFF:
            main.int64(TAG_SIZE, [total])
        else:
            main.int32(TAG_SIZE, [total])
        if preamble.get("vendor"):
            main.string(TAG_VENDOR, preamble["vendor"])
        if preamble.get("license"):
            main.string(TAG_LICENSE, preamble["license"])
        if preamble.get("packager"):
            main.string(TAG_PACKAGER, preamble["packager"])
        main.i18n(TAG_GROUP, preamble.get("group", "Unspecified"))
        if preamble.get("url"):
            main.string(TAG_URL, preamble["url"])
        main.string(TAG_OS, "linux")
        main.string(TAG_ARCH, arch)
        main.string(TAG_RPMVERSION, RPM_VERSION_STAMP)
        main.string(TAG_PAYLOADFORMAT, "cpio")
        main.string(TAG_PAYLOADCOMPRESSOR, compressor)
        main.string(TAG_PAYLOADFLAGS, cflags)
        main.int32(TAG_FILEDIGESTALGO, [8])          # 8 = sha256
        if preamble["requires"]:
            main.int32(TAG_REQUIREFLAGS, [f for _, f, _ in preamble["requires"]])
            main.array(TAG_REQUIRENAME, [n for n, _, _ in preamble["requires"]])
            main.array(TAG_REQUIREVERSION, [v for _, _, v in preamble["requires"]])

        scriptlets = (("pre", TAG_PREIN, TAG_PREINPROG),
                      ("post", TAG_POSTIN, TAG_POSTINPROG),
                      ("preun", TAG_PREUN, TAG_PREUNPROG),
                      ("postun", TAG_POSTUN, TAG_POSTUNPROG))
        for sect, body_tag, prog_tag in scriptlets:
            body = "\n".join(sections.get(sect, [])).strip()
            if body:
                main.string(body_tag, body)
                main.string(prog_tag, "/bin/sh")

        dirnames, dirindex = [], {}
        dirindexes = []
        basenames, sizes, modes, rdevs, mtimes, links, flags, devices = [], [], [], [], [], [], [], []
        inodes, langs = [], []
        for n, it in enumerate(items, start=1):
            path = "/" + it["cpio"]
            head, tail = os.path.split(path)
            dirname = head + "/"
            if dirname not in dirindex:
                dirindex[dirname] = len(dirnames)
                dirnames.append(dirname)
            dirindexes.append(dirindex[dirname])
            basenames.append(tail)
            sizes.append(it["size"])
            modes.append(it["mode"] & 0xFFFF)
            rdevs.append(0)
            mtimes.append(it["mtime"])
            links.append(it["target"])
            flags.append(it["flags"])
            devices.append(1)
            inodes.append(n)
            langs.append("")
        main.array(TAG_BASENAMES, basenames)
        main.array(TAG_DIRNAMES, dirnames)
        main.int32(TAG_DIRINDEXES, dirindexes)
        main.int32(TAG_FILESIZES, sizes)
        main.int16(TAG_FILEMODES, modes)
        main.int16(TAG_FILERDEVS, rdevs)
        main.int32(TAG_FILEMTIMES, mtimes)
        main.array(TAG_FILEDIGESTS, digests)
        main.array(TAG_FILELINKTOS, links)
        main.int32(TAG_FILEFLAGS, flags)
        main.array(TAG_FILEUSERNAME, ["root"] * len(items))
        main.array(TAG_FILEGROUPNAME, ["root"] * len(items))
        main.int32(TAG_FILEVERIFYFLAGS, [-1] * len(items))
        main.int32(TAG_FILEDEVICES, devices)
        main.int32(TAG_FILEINODES, inodes)
        main.array(TAG_FILELANGS, langs)

        # rpm >= 4.14 also verifies a digest of the *compressed* payload that is
        # stored in the main header (type 8 = sha256).
        payload_sha256 = hashlib.sha256()
        with open(payload_path, "rb") as f:
            for chunk in iter(lambda: f.read(1024 * 1024), b""):
                payload_sha256.update(chunk)
        main.array(TAG_PAYLOADDIGEST, [payload_sha256.hexdigest()])
        main.int32(TAG_PAYLOADDIGESTTYPE, [8])

        main_bytes = main.serialize()

        # Signature header digests (semantics verified against real Sailfish
        # packages and against what the device's rpm 4.16.1.3 computes):
        #   SHA1/SHA256 (269/273) cover the MAIN HEADER ONLY
        #   MD5 (1004) covers main header + compressed payload
        #   SIZE (1000) is len(main header) + len(compressed payload)
        sig = Header(TAG_SIGREGION)
        md5 = hashlib.md5()
        md5.update(main_bytes)
        with open(payload_path, "rb") as f:
            for chunk in iter(lambda: f.read(1024 * 1024), b""):
                md5.update(chunk)
        sig.int32(SIG_SIZE, [len(main_bytes) + payload_size])
        sig.put(SIG_MD5, T_BIN, md5.digest())
        sig.int32(SIG_PAYLOADSIZE, [cpio_size])
        sig.string(SIG_SHA1, hashlib.sha1(main_bytes).hexdigest())
        sig.string(SIG_SHA256, hashlib.sha256(main_bytes).hexdigest())
        sig_bytes = sig.serialize()

        out_name = expand_macros(macros["_rpmfilename"], macros)
        out_path = os.path.join(rpmdir, out_name)
        out_dir = os.path.dirname(out_path)
        if out_dir:
            os.makedirs(out_dir, exist_ok=True)
        with open(out_path, "wb") as out:
            out.write(make_lead("%s-%s-%s" % (name, version, release)))
            out.write(sig_bytes)
            pad = (-len(sig_bytes)) % 8
            if pad:
                out.write(b"\0" * pad)
            out.write(main_bytes)
            with open(payload_path, "rb") as f:
                shutil.copyfileobj(f, out, 1024 * 256)
        sys.stdout.write("Wrote: %s\n" % out_path)
        sys.stderr.write(
            "sf-rpmbuild: %d files, payload %d -> %d bytes (%s), header %d bytes\n"
            % (len(items), cpio_size, payload_size, compressor, len(main_bytes)))
    finally:
        try:
            os.unlink(payload_path)
        except OSError:
            pass
    return 0


def main(argv):
    spec, target, buildroot, defines = parse_args(argv[1:])
    return build_rpm(spec, target, buildroot, defines)


if __name__ == "__main__":
    sys.exit(main(sys.argv))
