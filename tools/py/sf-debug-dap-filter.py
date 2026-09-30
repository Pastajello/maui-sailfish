#!/usr/bin/env python3
"""DAP wire filter between VS Code and the on-device vsdbg.

VS Code attaches SHA384/SHA512 checksums to breakpoint requests, but vsdbg only
supports MD5/SHA1/SHA256 and rejects the whole setBreakpoints request
(ErrorCode 3001, "Incorrect breakpoint request format."); neither side has a
setting for it. This filter strips the unsupported checksums and relays
everything else byte-exact.

Fail-open: any frame it cannot parse is forwarded unchanged, so a bug degrades
to "no filtering", never "no debugging". Spawned by tools/sf-debug-pipe.sh.
"""

import json
import os
import re
import subprocess
import sys
import threading

ALLOWED = frozenset(("MD5", "SHA1", "SHA256"))
BP_COMMANDS = frozenset((
    "setBreakpoints",
    "setFunctionBreakpoints",
    "setInstructionBreakpoints",
))

CONTENT_LENGTH = re.compile(rb"(?im)^content-length:[ \t]*(\d+)")


def read_frame(stream):
    """Return (headers, body) or None on EOF. body may be None if no length."""
    headers = b""
    while not headers.endswith(b"\r\n\r\n"):
        c = stream.read(1)
        if not c:
            # EOF mid-header: pass the tail through instead of swallowing it,
            # so a non-DAP byte stream (a crash message, a stray banner) still
            # reaches the other end.
            return (headers, None) if headers else None
        headers += c
        if len(headers) > 16384:  # not a DAP header at all; bail out
            return headers, None
    m = CONTENT_LENGTH.search(headers)
    if not m:
        return headers, None
    n = int(m.group(1))
    body = b""
    while len(body) < n:
        chunk = stream.read(n - len(body))
        if not chunk:
            return headers, body
        body += chunk
    return headers, body


def strip_checksums(body):
    try:
        msg = json.loads(body.decode("utf-8"))
    except Exception:
        return body
    if not isinstance(msg, dict) or msg.get("type") != "request":
        return body
    if msg.get("command") not in BP_COMMANDS:
        return body
    args = msg.get("arguments")
    if not isinstance(args, dict):
        return body
    src = args.get("source")
    if not isinstance(src, dict):
        return body
    checksums = src.get("checksums")
    if not isinstance(checksums, list):
        return body
    kept = [c for c in checksums
            if isinstance(c, dict) and c.get("algorithm") in ALLOWED]
    if kept:
        src["checksums"] = kept
    else:
        src.pop("checksums", None)
    return json.dumps(msg).encode("utf-8")


def frame(body):
    return b"Content-Length: %d\r\n\r\n" % len(body) + body


def relay_in(child_stdin):
    """VS Code -> adapter, rewriting breakpoint checksums."""
    src = sys.stdin.buffer
    dst = child_stdin
    try:
        while True:
            got = read_frame(src)
            if got is None:
                break
            headers, body = got
            if body is None:
                dst.write(headers)
                continue
            new = strip_checksums(body)
            dst.write(frame(new) if new is not body else headers + body)
            dst.flush()
    except Exception:
        pass
    finally:
        try:
            dst.close()
        except Exception:
            pass


def relay_out(child_stdout):
    """Adapter -> VS Code, untouched."""
    src = child_stdout
    dst = sys.stdout.buffer
    try:
        while True:
            got = read_frame(src)
            if got is None:
                break
            headers, body = got
            dst.write(headers if body is None else headers + body)
            dst.flush()
    except Exception:
        pass


def main():
    ssh_opts = os.environ.get("SF_SSH_OPTS", "").split()
    target = os.environ.get("SF_SSH_TARGET", "")
    remote = sys.argv[1:]
    if not target or not remote:
        sys.stderr.write("sf-debug-dap-filter: SF_SSH_TARGET and a remote "
                         "command are required\n")
        return 2
    child = subprocess.Popen(
        ["ssh"] + ssh_opts + [target] + remote,
        stdin=subprocess.PIPE, stdout=subprocess.PIPE,
        stderr=sys.stderr)

    t_in = threading.Thread(target=relay_in, args=(child.stdin,), daemon=True)
    t_out = threading.Thread(target=relay_out, args=(child.stdout,),
                             daemon=True)
    t_in.start()
    t_out.start()
    t_out.join()
    try:
        child.stdin.close()
    except Exception:
        pass
    return child.wait()


if __name__ == "__main__":
    sys.exit(main())
