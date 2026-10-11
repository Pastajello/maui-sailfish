#!/usr/bin/env python3
"""Harbour (Jolla Store) pre-check of an app RPM against Jolla's own rule lists.

The Jolla Store runs sdk-harbour-rpmvalidator (rpmvalidation.sh) on every upload. That script needs a Linux host with
rpm, objdump and readelf; this one checks the same lists from any build host, Python standard library only:

  - the package name (harbour-...), the files' locations (/usr/bin/NAME, /usr/share/NAME/..., the .desktop file and
    the hicolor icons only), no debug symbols directory;
  - every "Requires:" against allowed_requires.conf;
  - every QML "import" in the payload against allowed_qmlimports.conf (and the disallowed patterns), except modules
    the app ships itself;
  - every ELF file's DT_NEEDED against allowed_libraries.conf, except libraries shipped in /usr/share/NAME/lib;
  - the .desktop file's X-Sailjail Permissions against allowed_permissions.conf;
  - the launcher icons (86, 108, 128, 172).

Usage: sf-harbour-check.py <app.rpm> <rules-dir>     (rules-dir: a clone of sailfishos/sdk-harbour-rpmvalidator)
Exit code 0 when no error was found; warnings do not fail.
"""

import fnmatch
import os
import re
import struct
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.realpath(__file__))
ICON_SIZES = ("86x86", "108x108", "128x128", "172x172")


def read_list(rules, name):
    path = os.path.join(rules, name)
    if not os.path.exists(path):
        return []
    out = []
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line and not line.startswith("#"):
                out.append(line)
    return out


def rpm_query(rpm, fmt):
    return subprocess.run([sys.executable, "-I", os.path.join(HERE, "sf-rpmquery.py"), "-qp", "--queryformat", fmt, rpm],
                          check=True, capture_output=True, text=True).stdout


def extract(rpm, dest):
    payload = subprocess.run([sys.executable, "-I", os.path.join(HERE, "sf-rpm2cpio.py"), rpm], check=True,
                             capture_output=True).stdout
    subprocess.run(["cpio", "-idm", "--quiet"], cwd=dest, input=payload, check=True)


def elf_needed(path):
    """DT_NEEDED entries of a 64- or 32-bit little-endian ELF file; None when it is no ELF."""
    with open(path, "rb") as f:
        data = f.read()
    if data[:4] != b"\x7fELF":
        return None
    is64 = data[4] == 2
    if data[5] != 1:
        return []   # big endian: not a Sailfish target
    if is64:
        e_shoff, = struct.unpack_from("<Q", data, 0x28)
        e_shentsize, e_shnum = struct.unpack_from("<HH", data, 0x3A)
    else:
        e_shoff, = struct.unpack_from("<I", data, 0x20)
        e_shentsize, e_shnum = struct.unpack_from("<HH", data, 0x2E)
    sections = []
    for i in range(e_shnum):
        off = e_shoff + i * e_shentsize
        if is64:
            name, typ, _flags, _addr, offset, size, link = struct.unpack_from("<IIQQQQI", data, off)
        else:
            name, typ, _flags, _addr, offset, size, link = struct.unpack_from("<IIIIIII", data, off)
        sections.append((typ, offset, size, link))
    needed = []
    for typ, offset, size, link in sections:
        if typ != 6:   # SHT_DYNAMIC
            continue
        str_off = sections[link][1]
        entry = 16 if is64 else 8
        for pos in range(offset, offset + size, entry):
            tag, val = struct.unpack_from("<qQ" if is64 else "<iI", data, pos)
            if tag == 0:
                break
            if tag == 1:   # DT_NEEDED
                end = data.index(b"\0", str_off + val)
                needed.append(data[str_off + val:end].decode())
    return needed


def lib_allowed(lib, allowed):
    for entry in allowed:
        name = re.split(r"[!(]", entry, maxsplit=1)[0]
        if lib == name or fnmatch.fnmatch(lib, name):
            return True
    return False


def require_allowed(req, allowed):
    for entry in allowed:
        if req == entry:
            return True
        base = re.split(r"\(", entry, maxsplit=1)[0]
        if entry.endswith("(*)") and req.split("(")[0] == base:
            return True
        if "(!(" in entry and req.split("(")[0] == base and "PRIVATE" not in req:
            return True
    return False


def main(argv):
    if len(argv) != 3:
        print(__doc__.strip().splitlines()[-3])
        return 2
    rpm, rules = argv[1], argv[2]
    errors, warnings = [], []
    name = rpm_query(rpm, "%{NAME}").strip()
    requires = [r for r in rpm_query(rpm, "[%{REQUIRENAME}\n]").splitlines() if r and not r.startswith("rpmlib(")]
    if not name.startswith("harbour-"):
        errors.append(f"package name '{name}' must start with harbour-")

    allowed_requires = read_list(rules, "allowed_requires.conf")
    for req in requires:
        if not require_allowed(req, allowed_requires):
            errors.append(f"Requires: {req} is not allowed")

    with tempfile.TemporaryDirectory() as root:
        extract(rpm, root)
        share = os.path.join(root, "usr", "share", name)
        desktop = os.path.join(root, "usr", "share", "applications", f"{name}.desktop")
        launcher = os.path.join(root, "usr", "bin", name)

        # Locations.
        allowed_paths = [f"usr/bin/{name}", f"usr/share/applications/{name}.desktop"] + \
            [f"usr/share/icons/hicolor/{s}/apps/{name}.png" for s in ICON_SIZES]
        for dirpath, _dirs, files in os.walk(root):
            for f in files:
                rel = os.path.relpath(os.path.join(dirpath, f), root)
                if rel.startswith(f"usr/share/{name}/") or rel in allowed_paths:
                    continue
                errors.append(f"/{rel}: installation not allowed in this location")
        for debug in (os.path.join(root, "usr/lib/debug"), os.path.join(root, "usr/src/debug")):
            if os.path.isdir(debug):
                errors.append(f"/{os.path.relpath(debug, root)}: debug symbols must not be included")

        # Launcher, desktop, icons.
        if not os.path.exists(launcher) or os.path.islink(launcher):
            errors.append(f"/usr/bin/{name} must be an executable ELF file (not a symlink)")
        elif elf_needed(launcher) is None:
            errors.append(f"/usr/bin/{name} is not an ELF file")
        for size in ICON_SIZES:
            if not os.path.exists(os.path.join(root, f"usr/share/icons/hicolor/{size}/apps/{name}.png")):
                errors.append(f"missing launcher icon {size}")
        if not os.path.exists(desktop):
            errors.append(f"missing /usr/share/applications/{name}.desktop")
        else:
            allowed_perms = set(read_list(rules, "allowed_permissions.conf"))
            text = open(desktop, encoding="utf-8").read()
            if "[X-Sailjail]" not in text:
                errors.append(".desktop has no [X-Sailjail] section (Harbour apps run sandboxed)")
            for m in re.finditer(r"^Permissions=(.*)$", text, re.M):
                for perm in filter(None, (p.strip() for p in m.group(1).split(";"))):
                    if perm not in allowed_perms:
                        errors.append(f"X-Sailjail permission '{perm}' is not allowed")
            if re.search(r"^Sandboxing=Disabled", text, re.M):
                errors.append(".desktop disables the sandbox")
            # The [X-Sailjail] keys: only the allowed ones, a valid organization, ExecDBus naming the package.
            section = re.search(r"^\[X-Sailjail\]\s*$(.*?)(?=^\[|\Z)", text, re.M | re.S)
            allowed_keys = set(read_list(rules, "allowed_sailjailkeys.conf"))
            disallowed_orgs = set(read_list(rules, "disallowed_orgnames.conf"))
            for line in (section.group(1).splitlines() if section else []):
                if not line.strip() or line.startswith("#") or "=" not in line:
                    continue
                key, value = line.split("=", 1)
                if key not in allowed_keys:
                    errors.append(f"X-Sailjail key '{key}' is not allowed")
                elif key == "OrganizationName" and (not re.fullmatch(r"[0-9a-z._-]+", value)
                                                    or re.search(r"(^|[.])[0-9]", value) or value in disallowed_orgs):
                    errors.append(f"X-Sailjail OrganizationName '{value}' is not allowed")
                elif key == "ApplicationName" and not re.fullmatch(r"[A-Za-z_-][A-Za-z0-9_-]*", value):
                    errors.append(f"X-Sailjail ApplicationName '{value}' has illegal characters")
                elif key == "ExecDBus" and not re.fullmatch(re.escape(name) + r"(\s+[A-Za-z_-][A-Za-z0-9_-]*)?", value):
                    errors.append(f"X-Sailjail ExecDBus '{value}' must name the package")

        # QML imports.
        allowed_qml = set(read_list(rules, "allowed_qmlimports.conf"))
        deprecated_qml = set(read_list(rules, "deprecated_qmlimports.conf"))
        disallowed_patterns = read_list(rules, "disallowed_qmlimport_patterns.conf")
        own_modules = set()
        for dirpath, _dirs, files in os.walk(share):
            if "qmldir" in files:
                for line in open(os.path.join(dirpath, "qmldir"), encoding="utf-8", errors="replace"):
                    if line.startswith("module "):
                        own_modules.add(line.split()[1])
        seen = {}
        for dirpath, _dirs, files in os.walk(share):
            for f in files:
                if not f.endswith((".qml", ".js")):
                    continue
                path = os.path.join(dirpath, f)
                for line in open(path, encoding="utf-8", errors="replace"):
                    m = re.match(r"\s*import\s+([A-Za-z][\w.]*)\s+(\d+\.\d+)", line)
                    if m:
                        seen.setdefault(f"{m.group(1)} {m.group(2)}", os.path.relpath(path, root))
        # A system namespace (Qt*, Sailfish.*, Nemo.*, ...) must be on the allowed list; anything else must be the
        # app's own module.
        for imp, where in sorted(seen.items()):
            module = imp.split()[0]
            if imp in allowed_qml or module in own_modules:
                continue
            if imp in deprecated_qml:
                warnings.append(f"QML import '{imp}' is deprecated ({where})")
            elif any(fnmatch.fnmatch(module, p) for p in disallowed_patterns):
                errors.append(f"QML import '{imp}' is not allowed ({where})")
            else:
                errors.append(f"QML import '{imp}' is neither allowed nor shipped by the app ({where})")

        # Libraries.
        allowed_libs = read_list(rules, "allowed_libraries.conf")
        own_libs = set()
        own_lib_dir = os.path.join(share, "lib")
        for dirpath, _dirs, files in os.walk(share):
            for f in files:
                if ".so" in f:
                    own_libs.add(f)
        misplaced = set()
        for dirpath, _dirs, files in os.walk(root):
            for f in files:
                path = os.path.join(dirpath, f)
                if os.path.islink(path):
                    continue
                needed = elf_needed(path)
                if needed is None:
                    continue
                if ".so" in f and os.path.dirname(path) != own_lib_dir and path.startswith(share):
                    misplaced.add(os.path.relpath(path, root))
                for lib in needed:
                    if lib in own_libs or lib_allowed(lib, allowed_libs):
                        continue
                    errors.append(f"{os.path.relpath(path, root)} links {lib}, which is neither allowed nor shipped")
        for path in sorted(misplaced):
            warnings.append(f"/{path}: a shared library outside /usr/share/{name}/lib (the validator expects them there)")

    for w in warnings:
        print(f"WARNING {w}")
    for e in errors:
        print(f"ERROR   {e}")
    print(f"{name}: {len(errors)} error(s), {len(warnings)} warning(s)")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
