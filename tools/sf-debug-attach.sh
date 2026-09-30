#!/bin/bash
# Former name of `tools/sf debug-attach`, kept for the .vscode files of apps generated before tools/sf existed.
exec bash "$(cd "$(dirname "$0")" && pwd)/sf" debug-attach "$@"
