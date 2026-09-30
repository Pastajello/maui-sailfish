"""RPM file layout shared by tools/sf-rpm2cpio.py and tools/sf-rpmquery.py.

Stdlib only; the scripts import it from their own directory.
"""

import struct
import sys

LEAD_SIZE = 96
LEAD_MAGIC = b"\xed\xab\xee\xdb"
HEADER_MAGIC = b"\x8e\xad\xe8\x01\x00\x00\x00\x00"

T_INT16, T_INT32, T_INT64 = 3, 4, 5
T_STRING, T_BIN, T_STRING_ARRAY, T_I18N = 6, 7, 8, 9

# Error prefix; each script sets its own name.
PROG = "sf-rpm"


def die(msg):
    sys.stderr.write("%s: ERROR: %s\n" % (PROG, msg))
    sys.exit(1)


def read_exact(f, n, what):
    buf = f.read(n)
    if len(buf) != n:
        die("truncated RPM: %s (wanted %d bytes, got %d)" % (what, n, len(buf)))
    return buf


def read_header(f, what):
    """One header structure -> (nindex, index bytes, store bytes)."""
    magic = read_exact(f, 8, "%s header magic" % what)
    if magic != HEADER_MAGIC:
        die("bad %s header magic: %r" % (what, magic))
    nindex, hsize = struct.unpack(">II", read_exact(f, 8, "%s sizes" % what))
    idx = read_exact(f, nindex * 16, "%s index" % what)
    store = read_exact(f, hsize, "%s store" % what)
    return nindex, idx, store


def read_main_header(f, path):
    """Check the lead, skip the signature header; returns the main header, f at the payload."""
    lead = read_exact(f, LEAD_SIZE, "lead")
    if lead[:4] != LEAD_MAGIC:
        die("not an RPM file (bad lead magic): %s" % path)
    read_header(f, "signature")
    # the signature header is padded so the main header is 8-byte aligned
    pad = (-f.tell()) % 8
    if pad:
        read_exact(f, pad, "signature padding")
    return read_header(f, "main")
