#!/usr/bin/env python3
"""rpm2cpio fallback for build hosts without the rpm toolchain.

`tools/sf-sysroot.sh` extracts the Qt5/Sailfish devel RPMs with
`rpm2cpio <file> | cpio -idmu`. On hosts where `rpm` cannot be installed
(no package-manager access / no sudo) this script provides the same stdout
contract using only the Python standard library: it skips the RPM lead and
the two headers, then streams the decompressed payload.

Usage: sf-rpm2cpio.py <file.rpm>   (writes the cpio payload to stdout)

Supported payload compressions: gzip, xz, lzma (alone), bzip2. A zstd payload
is delegated to a `zstd`/`unzstd` binary when present (the stdlib has no zstd
decoder before Python 3.14).
"""

import bz2
import gzip
import lzma
import os
import shutil
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.realpath(__file__)))
import sf_rpm  # noqa: E402  (shared RPM layout, next to this script)
from sf_rpm import die  # noqa: E402

sf_rpm.PROG = "sf-rpm2cpio"
CHUNK = 1024 * 256


def stream_payload(f, out):
    """Decompress the payload at the current offset into `out`."""
    magic = f.read(6)
    if len(magic) < 2:
        die("RPM has no payload after the headers")
    f.seek(-len(magic), 1)

    if magic[:2] == b"\x1f\x8b":
        shutil.copyfileobj(gzip.GzipFile(fileobj=f), out, CHUNK)
    elif magic[:6] == b"\xfd7zXZ\x00":
        shutil.copyfileobj(lzma.LZMAFile(f, "rb"), out, CHUNK)
    elif magic[:1] == b"\x5d":  # lzma alone
        shutil.copyfileobj(
            lzma.LZMAFile(f, "rb", format=lzma.FORMAT_ALONE), out, CHUNK
        )
    elif magic[:3] == b"BZh":
        shutil.copyfileobj(bz2.BZ2File(f), out, CHUNK)
    elif magic[:4] == b"\x28\xb5\x2f\xfd":  # zstd
        for tool in ("zstd", "unzstd"):
            exe = shutil.which(tool)
            if exe:
                proc = subprocess.Popen(
                    [exe, "-dc"], stdin=subprocess.PIPE, stdout=out, stderr=sys.stderr
                )
                shutil.copyfileobj(f, proc.stdin, CHUNK)
                proc.stdin.close()
                if proc.wait() != 0:
                    die("%s failed on the payload" % tool)
                return
        die(
            "payload is zstd-compressed and no zstd decoder is available "
            "(install zstd, or use python >= 3.14)"
        )
    else:
        die("unknown payload compression: %r" % (magic[:4],))


def main(argv):
    if len(argv) != 2 or argv[1] in ("-", ""):
        die("usage: sf-rpm2cpio.py <file.rpm>")
    path = argv[1]
    try:
        with open(path, "rb") as f:
            sf_rpm.read_main_header(f, path)
            out = getattr(sys.stdout, "buffer", sys.stdout)
            stream_payload(f, out)
            out.flush()
    except OSError as exc:
        die("%s" % exc)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))