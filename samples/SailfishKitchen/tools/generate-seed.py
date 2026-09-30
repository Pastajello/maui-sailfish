#!/usr/bin/env python3
"""Generates SailfishKitchen's binary resources and the offline seed catalog.

Two jobs, both dependency-free:
  1. meal_placeholder.png — a pure-stdlib PNG (zlib + struct) used as the tile
     art while a remote thumb is in flight, and permanently in offline mode.
  2. Resources/Seed/*.json — recorded TheMealDB responses, named with exactly the
     keys HttpResponseCache.MakeKey produces, so priming the cache is a copy.

Usage: python3 tools_gen_seed.py
"""
import json
import os
import struct
import subprocess
import sys
import time
import urllib.parse
import zlib

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
APP = os.path.join(REPO, "samples", "SailfishKitchen")
IMAGES = os.path.join(APP, "Resources", "Images")
SEED = os.path.join(APP, "Resources", "Seed")
API = "https://www.themealdb.com/api/json/v1/1"
UA = {"User-Agent": "SailfishKitchen-seed/1.0"}

# Letters the offline seed covers; search.php returns full records, so these render recipe screens offline.
SEED_LETTERS = ["a", "b", "c"]


def write_png(path, size, pixel_fn):
    """Minimal RGBA PNG writer — no PIL needed."""
    raw = bytearray()
    for y in range(size):
        raw.append(0)  # filter type: None
        for x in range(size):
            raw.extend(pixel_fn(x, y, size))

    def chunk(tag, data):
        return (struct.pack(">I", len(data)) + tag + data
                + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

    png = (b"\x89PNG\r\n\x1a\n"
           + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
           + chunk(b"IEND", b""))

    with open(path, "wb") as fh:
        fh.write(png)
    print(f"  wrote {path} ({size}x{size}, {len(png)} bytes)")


def placeholder_pixel(x, y, size):
    """Dark tile with a centred plate ring and a fork/knife hint."""
    cx = cy = size / 2.0
    dx, dy = x - cx, y - cy
    dist = (dx * dx + dy * dy) ** 0.5
    r_outer, r_inner = size * 0.30, size * 0.255

    # base: #232932 with a faint vertical gradient so tiles are not dead flat
    base = (0x23, 0x29, 0x32)
    shade = 1.0 + (0.5 - y / size) * 0.10
    rgb = [min(255, int(c * shade)) for c in base]

    if r_inner <= dist <= r_outer:
        rgb = [0x3A, 0x42, 0x4E]           # plate ring
    elif dist < r_inner:
        rgb = [0x2A, 0x31, 0x3B]           # plate well
        # fork (left) and knife (right): two thin vertical bars with a handle
        half = size * 0.012
        for bx, wide in ((cx - size * 0.075, half), (cx + size * 0.075, half * 0.8)):
            if abs(x - bx) <= wide and cy - size * 0.13 <= y <= cy + size * 0.13:
                rgb = [0x5C, 0x68, 0x78]
        # fork tines
        if y <= cy - size * 0.05:
            for tine in range(3):
                tx = cx - size * 0.075 + (tine - 1) * size * 0.018
                if abs(x - tx) <= size * 0.004 and cy - size * 0.13 <= y <= cy - size * 0.05:
                    rgb = [0x5C, 0x68, 0x78]

    return bytes(rgb) + b"\xff"


def cache_key(endpoint, query=None):
    """Mirrors HttpResponseCache.MakeKey exactly."""
    raw = endpoint if not query else f"{endpoint}?{query}"
    safe = "".join(c if (c.isascii() and c.isalnum()) or c in ".=-" else "_" for c in raw)
    return safe + ".json"


def fetch(endpoint, query=None):
    """Fetches one recorded response.

    Shells out to curl rather than using urllib: a python.org macOS install ships
    without a CA bundle, so urllib raises CERTIFICATE_VERIFY_FAILED against
    TheMealDB's Let's Encrypt chain while the system curl (and the device) validate
    it fine. curl is a prerequisite of the device tooling in ../tools anyway.
    """
    url = f"{API}/{endpoint}" + (f"?{query}" if query else "")
    proc = subprocess.run(
        ["curl", "-sS", "--fail", "--max-time", "40",
         "-H", f"User-Agent: {UA['User-Agent']}", url],
        capture_output=True, text=True, check=True)
    body = proc.stdout
    json.loads(body)  # fail loudly on a non-JSON body
    return body


def q(key, value):
    """Builds a query string the way MealDbClient.BuildUri does.

    Uri.EscapeDataString and quote(safe="") agree on everything TheMealDB puts in
    a category name or a letter, and the escaped form is what Uri.Query — and
    therefore HttpResponseCache.MakeKey — sees. Getting this wrong would prime the
    cache under a key no request ever looks up.
    """
    return f"{key}={urllib.parse.quote(value, safe='')}"


def politeness():
    """The free tier is rate-limited per identity; do not hammer it."""
    time.sleep(0.35)


def main():
    os.makedirs(IMAGES, exist_ok=True)
    os.makedirs(SEED, exist_ok=True)

    print("placeholder art:")
    write_png(os.path.join(IMAGES, "meal_placeholder.png"), 512, placeholder_pixel)

    print("offline seed:")
    endpoints = [("categories.php", None), ("list.php", q("a", "list")), ("random.php", None)]
    for letter in SEED_LETTERS:
        endpoints.append(("search.php", q("f", letter)))

    total = 0
    for endpoint, query in endpoints:
        body = fetch(endpoint, query)
        key = cache_key(endpoint, query)
        path = os.path.join(SEED, key)
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(body)
        total += len(body)
        print(f"  {key:38s} {len(body):>9,} bytes")
        politeness()

    # Per-category filter rows keep the home -> category drill-down usable offline.
    categories = json.loads(fetch("categories.php"))["categories"]
    for category in categories:
        name = category["strCategory"]
        query = q("c", name)
        politeness()
        body = fetch("filter.php", query)
        key = cache_key("filter.php", query)
        with open(os.path.join(SEED, key), "w", encoding="utf-8") as fh:
            fh.write(body)
        total += len(body)
        print(f"  {key:38s} {len(body):>9,} bytes")

    print(f"seed total: {total:,} bytes across {len(os.listdir(SEED))} files")
    return 0


if __name__ == "__main__":
    sys.exit(main())
