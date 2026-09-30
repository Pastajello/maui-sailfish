#!/usr/bin/env python3
"""`rpm -qp --qf` fallback for build hosts without the rpm toolchain.

`tools/sf verify` reads the freshly built package with

    rpm -qp --qf '%{NAME}' FILE.rpm
    rpm -qp --qf '[ %{FILENAMES} %{FILEDIGESTS}\n]' FILE.rpm

which only needs the header, not the rpm library. This script answers exactly
that contract with the Python standard library (it reads the same header layout
that tools/py/sf-rpmbuild.py writes), so verification keeps working on hosts where
rpm cannot be installed (no package manager / no sudo).

Usage: sf-rpmquery.py -qp [--qf|--queryformat FORMAT] FILE.rpm

Supported: scalar tags, the file-list array tags (FILENAMES/FILEDIGESTS/
FILEMODES/FILESIZES/FILEMTIMES/FILELINKTOS/FILEFLAGS/FILEVERIFYFLAGS and the
DIRNAMES/DIRINDEXES/BASENAMES trio behind FILENAMES), the `:perms` and `:octal`
format modifiers and one level of `[ ... ]` array iteration - including the
empty digests that rpm prints for directories and symlinks.
"""

import os
import re
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.realpath(__file__)))
import sf_rpm  # noqa: E402  (shared RPM layout, next to this script)
from sf_rpm import (  # noqa: E402
    T_BIN, T_I18N, T_INT16, T_INT32, T_INT64, T_STRING, T_STRING_ARRAY, die,
)

sf_rpm.PROG = "sf-rpmquery"

TAGS = {
    "NAME": 1000, "VERSION": 1001, "RELEASE": 1002, "EPOCH": 1003,
    "SUMMARY": 1004, "DESCRIPTION": 1005, "BUILDTIME": 1006,
    "BUILDHOST": 1007, "SIZE": 1009, "VENDOR": 1011, "LICENSE": 1014,
    "PACKAGER": 1015, "GROUP": 1016, "URL": 1020, "OS": 1021, "ARCH": 1022,
    "FILESIZES": 1028, "FILEMODES": 1030, "FILERDEVS": 1033,
    "FILEMTIMES": 1034, "FILETIMES": 1034, "FILEDIGESTS": 1035,
    "FILELINKTOS": 1036, "FILEFLAGS": 1037, "FILEUSERNAME": 1039,
    "FILEGROUPNAME": 1040, "SOURCERPM": 1044, "FILEVERIFYFLAGS": 1045,
    "REQUIREFLAGS": 1048, "REQUIRENAME": 1049, "REQUIREVERSION": 1050,
    "RPMVERSION": 1064, "DIRINDEXES": 1116, "BASENAMES": 1117,
    "DIRNAMES": 1118, "PAYLOADFORMAT": 1124, "PAYLOADCOMPRESSOR": 1125,
    "PAYLOADFLAGS": 1126, "FILEDIGESTALGO": 5011,
}
# %{FILENAMES} is what the queries actually use; it is derived, not a tag.
DERIVED = ("FILENAMES",)


def _strings(store, off, cnt):
    out = []
    p = off
    for _ in range(cnt):
        end = store.index(b"\0", p)
        out.append(store[p:end].decode("utf-8", "replace"))
        p = end + 1
    return out


def parse_tags(header):
    """(nindex, index, store) from sf_rpm.read_header -> {tag: (type, values)}."""
    nindex, idx, store = header
    tags = {}
    for i in range(nindex):
        tag, typ, off, cnt = struct.unpack(">IIII", idx[i * 16:(i + 1) * 16])
        if typ == T_STRING:
            vals = _strings(store, off, 1)
        elif typ in (T_STRING_ARRAY, T_I18N):
            vals = _strings(store, off, cnt)
        elif typ == T_INT16:
            vals = list(struct.unpack(">%dH" % cnt, store[off:off + 2 * cnt]))
        elif typ == T_INT32:
            vals = list(struct.unpack(">%di" % cnt, store[off:off + 4 * cnt]))
        elif typ == T_INT64:
            vals = list(struct.unpack(">%dq" % cnt, store[off:off + 8 * cnt]))
        elif typ == T_BIN:
            vals = [store[off:off + cnt]]
        else:
            continue                      # unknown type: skip, rpm does too
        tags[tag] = (typ, vals)
    return tags


def file_names(tags):
    """%{FILENAMES} is derived: DIRNAMES[DIRINDEXES[i]] + BASENAMES[i]."""
    basenames = tags.get(TAGS["BASENAMES"], (None, []))[1]
    dirnames = tags.get(TAGS["DIRNAMES"], (None, []))[1]
    dirindexes = tags.get(TAGS["DIRINDEXES"], (None, []))[1]
    if not basenames:
        old = tags.get(1027)              # RPMTAG_OLDFILENAMES (pre-4.x pkgs)
        return list(old[1]) if old else []
    out = []
    for i, base in enumerate(basenames):
        if i < len(dirindexes) and 0 <= dirindexes[i] < len(dirnames):
            out.append(dirnames[dirindexes[i]] + base)
        else:
            out.append("/" + base)
    return out


def perms(mode):
    kind = {0o040000: "d", 0o120000: "l", 0o100000: "-"}.get(mode & 0o170000, "?")
    bits = ""
    for shift in (6, 3, 0):
        m = (mode >> shift) & 7
        bits += ("r" if m & 4 else "-") + ("w" if m & 2 else "-") + ("x" if m & 1 else "-")
    return kind + bits


def tag_value(tags, name, index):
    if name == "FILENAMES":
        vals = file_names(tags)
    else:
        num = TAGS.get(name)
        if num is None:
            die("unsupported query tag %%{%s} - add it to TAGS in tools/py/sf-rpmquery.py"
                % name)
        entry = tags.get(num)
        if entry is None or not entry[1]:
            return "(none)"
        vals = entry[1]
    if not vals:
        return "(none)"
    if index is None:
        if len(vals) == 1:
            v = vals[0]
        else:
            return " ".join(x.hex() if isinstance(x, bytes) else str(x) for x in vals)
    else:
        if index >= len(vals):
            return "(none)"
        v = vals[index]
    if isinstance(v, bytes):
        return v.hex()
    return str(v)


TOKEN_RE = re.compile(r"%\{([A-Za-z_][A-Za-z0-9_]*)(?::([a-z]+))?\}")


def expand(fmt, tags, index=None):
    def sub(m):
        name = m.group(1).upper()
        mod = m.group(2)
        raw = tag_value(tags, name, index)
        if mod == "perms":
            return raw if raw == "(none)" else perms(int(raw))
        if mod == "octal":
            return raw if raw == "(none)" else "%o" % int(raw)
        if mod:
            die("unsupported format modifier ':%s' in %%{%s}"
                % (mod, m.group(1)))
        return raw
    return TOKEN_RE.sub(sub, fmt)


def format_query(fmt, tags):
    fmt = (fmt.replace("\\\\", "\x00").replace("\\n", "\n")
              .replace("\\t", "\t").replace("\x00", "\\"))
    start = fmt.find("[")
    end = fmt.rfind("]")
    if start == -1 or end < start:
        sys.stdout.write(expand(fmt, tags))
        return
    body = fmt[start + 1:end]
    out = [expand(fmt[:start], tags)]
    # like rpm: iterate as many times as the arrays named in the brackets hold
    count = 0
    for m in TOKEN_RE.finditer(body):
        name = m.group(1).upper()
        if name == "FILENAMES":
            count = max(count, len(file_names(tags)))
        elif name in TAGS and TAGS[name] in tags:
            count = max(count, len(tags[TAGS[name]][1]))
    for i in range(count):
        out.append(expand(body, tags, i))
    out.append(expand(fmt[end + 1:], tags))
    sys.stdout.write("".join(out))


def main(argv):
    args = argv[1:]
    fmt = None
    path = None
    query_file = False
    i = 0
    while i < len(args):
        a = args[i]
        if a in ("--qf", "--queryformat"):
            i += 1
            if i >= len(args):
                die("%s needs an argument" % a)
            fmt = args[i]
        elif a.startswith("--qf=") or a.startswith("--queryformat="):
            fmt = a.split("=", 1)[1]
        elif a in ("-qp", "-pq", "-p"):
            query_file = True
        elif a in ("-q", "--query"):
            pass
        elif a in ("--dump", "-l", "--list", "-i", "--info", "--provides",
                   "--requires", "--scripts", "--changelog", "-V"):
            die("sf-rpmquery.py implements only 'rpm -qp --qf ...' (got %s)"
                % a)
        elif a.startswith("-"):
            die("unsupported rpm option %r (sf-rpmquery.py handles 'rpm -qp --qf')" % a)
        elif path is None:
            path = a
        else:
            die("unexpected extra argument %r" % a)
        i += 1
    if fmt is None:
        fmt = "%{NAME}-%{VERSION}-%{RELEASE}.%{ARCH}"
    if not path:
        die("no package file given")
    if not query_file:
        die("only '-qp' (query a package FILE) is supported - installed-package "
            "queries keep running through the real rpm on the device")
    with open(path, "rb") as f:
        tags = parse_tags(sf_rpm.read_main_header(f, path))
    format_query(fmt, tags)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
