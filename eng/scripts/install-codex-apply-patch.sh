#!/usr/bin/env bash

set -euo pipefail

codex_cli="$(command -v codex)"
npm_root="$(npm root --global)"
native_codex="$(find "$npm_root" -type f -path '*/vendor/*/bin/codex' -perm -u+x -print -quit)"
if [ -z "$native_codex" ]; then
  echo "::error::Native Codex binary not found under the global npm root."
  exit 1
fi

shim="$(dirname "$codex_cli")/apply_patch"
ln -sfn "$native_codex" "$shim"

probe_dir="$(mktemp -d)"
trap 'rm -rf "$probe_dir"' EXIT
touch "$probe_dir/probe.txt"
(
  cd "$probe_dir"
  printf '%s\n' \
    '*** Begin Patch' \
    '*** Update File: probe.txt' \
    '@@' \
    '+ready' \
    '*** End Patch' | "$shim"
)
grep -Fxq 'ready' "$probe_dir/probe.txt"
