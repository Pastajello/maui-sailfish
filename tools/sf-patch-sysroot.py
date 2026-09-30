#!/usr/bin/env python3
"""Patches the Qt 5.6 sysroot for a modern clang (zig c++ / clang >= 19).

Qt 5.6 qtypetraits.h computes is_unsigned as `(T(0) < T(-1))`. For
enumeration types, converting `-1` to an out-of-range enum stopped being a
constant expression in newer clang ("integer value -1 is outside the valid
range"), so the whole Qt header set fails with a cascade of errors. GCC and
older clang accepted it, which is why Jolla built Qt 5.6 without trouble.

The patch keeps the semantics: for enums it compares the underlying type
(underlying_type), for integral types it leaves the original expression.
Idempotent.

Usage: sf-patch-sysroot.py <sysroot-dir>
"""
import sys
import pathlib

OLD = """template <typename T>
struct is_unsigned
    : integral_constant<bool, (T(0) < T(-1))> {};"""

NEW = """template <typename T, bool IsEnum = std::is_enum<T>::value>
struct is_unsigned_helper
    : integral_constant<bool, (T(0) < T(-1))> {};

template <typename T>
struct is_unsigned_helper<T, true>
    : integral_constant<bool, (static_cast<typename std::underlying_type<T>::type>(0)
                               < static_cast<typename std::underlying_type<T>::type>(-1))> {};

template <typename T>
struct is_unsigned
    : is_unsigned_helper<T> {};"""

INCLUDE_OLD = "#include <utility>                  // For pair"
INCLUDE_NEW = INCLUDE_OLD + "\n#include <type_traits>            // std::is_enum/underlying_type"


def main() -> int:
    if len(sys.argv) != 2:
        print(__doc__)
        return 2
    target = pathlib.Path(sys.argv[1]) / "usr/include/qt5/QtCore/qtypetraits.h"
    if not target.is_file():
        print(f"ERROR: {target} not found")
        return 1
    text = target.read_text()
    if "is_unsigned_helper" in text:
        print(f"    already patched: {target}")
        return 0
    if OLD not in text:
        print(f"ERROR: patch anchor not found in {target}")
        return 1
    text = text.replace(OLD, NEW, 1)
    if INCLUDE_OLD in text and "<type_traits>" not in text:
        text = text.replace(INCLUDE_OLD, INCLUDE_NEW, 1)
    target.write_text(text)
    print(f"    patched for modern clang: {target}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
