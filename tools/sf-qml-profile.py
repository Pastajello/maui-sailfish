#!/usr/bin/env python3
"""Minimal QML profiler client for Qt 5.6 on the phone, plus a per-function report (docs/profiling.md).

The app must run with MAUI_SAILFISH_QML_PROFILER=<port> (shim debug server on 127.0.0.1; needs
qt5-qtdeclarative-plugin-qmlinspector on the phone), reached through ssh -L. Qt 5.6's own qmlprofiler
CLI on the phone never connects in attach mode, so this speaks the protocol itself: the
QDeclarativeDebugServer hello, then the CanvasFrameRate (profiler) service.

  sf-qml-profile.py record HOST PORT SECONDS OUT.json   record until SECONDS pass or the app quits
  sf-qml-profile.py report OUT.json [TOP] [FROM_MS TO_MS]  self/inclusive ms per JS function, binding, signal
                                                          (optionally only ranges starting in the window)
  sf-qml-profile.py timeline OUT.json [BUCKET_MS]        self ms per range type per bucket (ms from the first range)
"""
import collections
import json
import socket
import struct
import sys
import time

RANGE_TYPES = {0: 'Painting', 1: 'Compiling', 2: 'Creating', 3: 'Binding', 4: 'HandlingSignal', 5: 'Javascript'}
# message types: 0 Event, 1 RangeStart, 2 RangeData, 3 RangeLocation, 4 RangeEnd, 5 Complete,
# 6 PixmapCacheEvent, 7 SceneGraphFrame, 8 MemoryAllocation, 9 DebugMessage


def qstr(s):
    b = s.encode('utf-16-be')
    return struct.pack('>I', len(b)) + b


def qint(i):
    return struct.pack('>i', i)


def qba(b):
    return struct.pack('>I', len(b)) + b


class Reader:
    def __init__(self, b):
        self.b, self.p = b, 0

    def end(self):
        return self.p >= len(self.b)

    def take(self, n):
        v = self.b[self.p:self.p + n]
        self.p += n
        return v

    def i32(self):
        return struct.unpack('>i', self.take(4))[0]

    def u32(self):
        return struct.unpack('>I', self.take(4))[0]

    def i64(self):
        return struct.unpack('>q', self.take(8))[0]

    def qstr(self):
        n = self.u32()
        return None if n == 0xFFFFFFFF else self.take(n).decode('utf-16-be', 'replace')

    def qba(self):
        n = self.u32()
        return b'' if n == 0xFFFFFFFF else self.take(n)


def record(host, port, seconds, out):
    sock = socket.create_connection((host, port), timeout=5)
    # QPacketProtocol frames: little-endian int32 length including itself; payload is QDataStream (big-endian).
    send = lambda payload: sock.sendall(struct.pack('<i', len(payload) + 4) + payload)
    buf, pos = bytearray(), 0

    def packets(timeout):
        nonlocal buf, pos
        sock.settimeout(timeout)
        try:
            chunk = sock.recv(1 << 20)
            if not chunk:
                return None
            buf += chunk
        except socket.timeout:
            pass
        found = []
        while len(buf) - pos >= 4:
            n = struct.unpack_from('<i', buf, pos)[0]
            if len(buf) - pos < n:
                break
            found.append(bytes(buf[pos + 4:pos + n]))
            pos += n
        if pos > (1 << 22):
            del buf[:pos]
            pos = 0
        return found

    # hello: server id, op 0, protocol 1, the services we talk to, max data stream version (Qt_5_6 = 17)
    send(qstr('QDeclarativeDebugServer') + qint(0) + qint(1) +
         struct.pack('>I', 1) + qstr('CanvasFrameRate') + qint(17))
    messages, deadline, stop_at, closed, services = [], time.time() + seconds, None, False, None
    while True:
        got = packets(0.5)
        if got is None:
            closed = True
            break
        for p in got:
            r = Reader(p)
            name = r.qstr()
            if name == 'QDeclarativeDebugClient' and r.i32() == 0:
                r.i32()
                services = [r.qstr() for _ in range(r.u32())]
                print('connected, services:', services, flush=True)
                # enable, all engines, all features, flush every 500 ms
                send(qstr('CanvasFrameRate') + qba(b'\x01' + qint(-1) + struct.pack('>QI', 0xFFFFFFFFFFFFFFFF, 500)))
            elif name == 'CanvasFrameRate':
                while not r.end():
                    messages.append(r.qba())
        if services is not None and stop_at is None and time.time() > deadline:
            send(qstr('CanvasFrameRate') + qba(b'\x00' + qint(-1)))
            stop_at = time.time()
        if stop_at and time.time() - stop_at > 6:
            break
    events = []
    for m in messages:
        r = Reader(m)
        try:
            e = {'t': r.i64(), 'm': r.i32(), 'd': r.i32()}
            if e['m'] == 2:
                e['s'] = r.qstr()
            elif e['m'] == 3:
                e['s'], e['line'], e['col'] = r.qstr(), r.i32(), r.i32()
            events.append(e)
        except struct.error:
            pass
    json.dump({'closedByApp': closed, 'services': services, 'events': events}, open(out, 'w'))
    print(f'{len(messages)} messages, app quit: {closed} -> {out}', flush=True)


def ranges(events):
    """(start_ns, end_ns, type, name, location, self_ms) of every completed range."""
    stacks = collections.defaultdict(list)
    out = []
    for e in events:
        m, d, t = e.get('m'), e.get('d'), e.get('t')
        if m not in (1, 2, 3, 4) or t is None:
            continue
        stack = stacks[d]
        if m == 1:
            stack.append({'t': t, 'loc': '', 'name': '', 'child': 0.0})
        elif m == 3 and stack:
            stack[-1]['loc'] = f"{(e.get('s') or '<eval>').split('/')[-1]}:{e.get('line')}"
        elif m == 2 and stack:
            stack[-1]['name'] = e.get('s') or ''
        elif m == 4 and stack:
            frame = stack.pop()
            ms = (t - frame['t']) / 1e6
            out.append((frame['t'], t, RANGE_TYPES.get(d, str(d)), frame['name'][:60] or '(anon)', frame['loc'], ms - frame['child']))
            if stack:
                stack[-1]['child'] += ms
    return out


def timeline(path, bucket_ms):
    rs = ranges(json.load(open(path))['events'])
    if not rs:
        return
    t0 = min(r[0] for r in rs)
    buckets = collections.defaultdict(collections.Counter)
    for start, _, kind, _, _, self_ms in rs:
        buckets[int((start - t0) / 1e6 // bucket_ms)][kind] += self_ms
    kinds = ['Javascript', 'Creating', 'Binding', 'HandlingSignal', 'Compiling']
    print(f"{'ms':>8}" + ''.join(f'{k[:10]:>12}' for k in kinds))
    for b in sorted(buckets):
        row = buckets[b]
        if sum(row.values()) < 1:
            continue
        print(f'{b * bucket_ms:8.0f}' + ''.join(f'{row[k]:12.1f}' for k in kinds))


def report(path, top, window=None):
    events = json.load(open(path))['events']
    if window is not None:
        rs = ranges(events)
        t0 = min(r[0] for r in rs)
        lo, hi = t0 + window[0] * 1e6, t0 + window[1] * 1e6
        by = collections.Counter()
        count = collections.Counter()
        for start, _, kind, name, loc, self_ms in rs:
            if lo <= start < hi:
                by[(kind, name, loc)] += self_ms
                count[(kind, name, loc)] += 1
        types = collections.Counter()
        for k, v in by.items():
            types[k[0]] += v
        print(f"window {window[0]:.0f}..{window[1]:.0f} ms; self ms by type: " + ', '.join(f'{k} {v:.0f}' for k, v in types.most_common()))
        for k, v in by.most_common(top):
            print(f'{k[0]:15}{v:9.1f}{count[k]:8}  {k[1]} @ {k[2]}')
        return
    stacks = collections.defaultdict(list)
    total, self_ms, count = collections.Counter(), collections.Counter(), collections.Counter()
    first = last = None
    for e in events:
        m, d, t = e.get('m'), e.get('d'), e.get('t')
        if m not in (1, 2, 3, 4) or t is None:
            continue
        first = t if first is None else min(first, t)
        last = t if last is None else max(last, t)
        stack = stacks[d]
        if m == 1:
            stack.append({'t': t, 'loc': '', 'name': '', 'child': 0.0})
        elif m == 3 and stack:
            stack[-1]['loc'] = f"{(e.get('s') or '<eval>').split('/')[-1]}:{e.get('line')}"
        elif m == 2 and stack:
            stack[-1]['name'] = e.get('s') or ''
        elif m == 4 and stack:
            frame = stack.pop()
            ms = (t - frame['t']) / 1e6
            key = (RANGE_TYPES.get(d, str(d)), frame['name'][:60] or '(anon)', frame['loc'])
            total[key] += ms
            self_ms[key] += ms - frame['child']
            count[key] += 1
            if stack:
                stack[-1]['child'] += ms
    by_type = collections.Counter()
    for k, v in self_ms.items():
        by_type[k[0]] += v
    print(f"span {(last - first) / 1e6 if first else 0:.0f} ms; self ms by type: "
          + ', '.join(f'{k} {v:.0f}' for k, v in by_type.most_common()))
    print(f"{'type':15}{'self ms':>9}{'incl ms':>9}{'count':>8}  name @ location")
    for k, v in self_ms.most_common(top):
        print(f'{k[0]:15}{v:9.1f}{total[k]:9.1f}{count[k]:8}  {k[1]} @ {k[2]}')


if __name__ == '__main__':
    if len(sys.argv) >= 6 and sys.argv[1] == 'record':
        record(sys.argv[2], int(sys.argv[3]), float(sys.argv[4]), sys.argv[5])
    elif len(sys.argv) >= 3 and sys.argv[1] == 'report':
        window = (float(sys.argv[4]), float(sys.argv[5])) if len(sys.argv) > 5 else None
        report(sys.argv[2], int(sys.argv[3]) if len(sys.argv) > 3 else 30, window)
    elif len(sys.argv) >= 3 and sys.argv[1] == 'timeline':
        timeline(sys.argv[2], float(sys.argv[3]) if len(sys.argv) > 3 else 250)
    else:
        sys.exit(__doc__)
