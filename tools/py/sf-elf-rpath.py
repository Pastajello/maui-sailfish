#!/usr/bin/env python3
"""F5: rewrites DT_RUNPATH to DT_RPATH in an ELF's dynamic section (in place).

zig's linker ignores --disable-new-dtags and always emits DT_RUNPATH, but the
Harbour validator only accepts an rpath read back as "Library rpath:" (the
DT_RPATH tag). Same string, same semantics for a PIE executable without
LD_LIBRARY_PATH overrides — only the tag changes.

Usage: sf-elf-rpath.py <elf>
"""
import struct
import sys

DT_RPATH = 15
DT_RUNPATH = 29
PT_DYNAMIC = 2


def main(path):
    with open(path, "r+b") as f:
        data = bytearray(f.read())
        if data[:4] != b"\x7fELF":
            sys.exit("not an ELF file: %s" % path)
        is64 = data[4] == 2
        end = "<" if data[5] == 1 else ">"
        if is64:
            phoff, = struct.unpack_from(end + "Q", data, 0x20)
            phentsize, phnum = struct.unpack_from(end + "HH", data, 0x36)
        else:
            phoff, = struct.unpack_from(end + "I", data, 0x1C)
            phentsize, phnum = struct.unpack_from(end + "HH", data, 0x2A)
        changed = 0
        for i in range(phnum):
            ph = phoff + i * phentsize
            p_type, = struct.unpack_from(end + "I", data, ph)
            if p_type != PT_DYNAMIC:
                continue
            if is64:
                offset, = struct.unpack_from(end + "Q", data, ph + 8)
                size, = struct.unpack_from(end + "Q", data, ph + 32)
                entry, fmt = 16, "q"
            else:
                offset, = struct.unpack_from(end + "I", data, ph + 4)
                size, = struct.unpack_from(end + "I", data, ph + 16)
                entry, fmt = 8, "i"
            for off in range(offset, offset + size, entry):
                tag, = struct.unpack_from(end + fmt, data, off)
                if tag == 0:
                    break
                if tag == DT_RUNPATH:
                    struct.pack_into(end + fmt, data, off, DT_RPATH)
                    changed += 1
        if not changed:
            sys.exit("no DT_RUNPATH entry in %s" % path)
        f.seek(0)
        f.write(data)
    print("    DT_RUNPATH -> DT_RPATH (%d entr%s)" % (changed, "y" if changed == 1 else "ies"))


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    main(sys.argv[1])
