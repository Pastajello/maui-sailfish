#!/usr/bin/env python3
"""S61: every managed assembly in a publish folder must be listed in its deps.json.

The host builds the app's trusted assembly list from deps.json: a .dll the publish copied but deps.json leaves out
fails with FileNotFoundException at its first use (an incremental trimmed publish kept a new facade without an entry).
A composite ReadyToRun image (<name>.r2r.dll, PublishReadyToRunComposite) is not an assembly the host loads: the
runtime finds it through its component assemblies, so deps.json does not list it.

Usage: sf-depscheck.py <publish-dir> <app-name>   (exit 1 and the missing names when any is unlisted)
"""
import json
import os
import sys


def main(pub, app):
    with open(os.path.join(pub, app + ".deps.json"), encoding="utf-8") as f:
        deps = json.load(f)
    listed = set()
    for target in deps.get("targets", {}).values():
        for lib in target.values():
            for kind in ("runtime", "native", "resources"):
                for path in lib.get(kind, {}) or {}:
                    listed.add(os.path.basename(path))
    dlls = sorted(n for n in os.listdir(pub) if n.endswith(".dll") and not n.endswith(".r2r.dll"))
    missing = [n for n in dlls if n not in listed]
    if missing:
        print("not in %s.deps.json: %s" % (app, ", ".join(missing)))
        return 1
    print("%d assemblies, all in %s.deps.json" % (len(dlls), app))
    return 0


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    sys.exit(main(sys.argv[1], sys.argv[2]))
