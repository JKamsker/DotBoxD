#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fixture="$(mktemp -d)"
trap 'rm -rf "$fixture"' EXIT
mkdir -p "$fixture/default/crashes" "$fixture/default/hangs"

expect_status() {
  local expected="$1" actual=0
  bash "$repo_root/eng/scripts/check-fuzz-findings.sh" "$fixture" > "$fixture/output" 2>&1 || actual=$?
  if [[ "$actual" != "$expected" ]]; then
    cat "$fixture/output" >&2
    echo "Expected status $expected, got $actual" >&2
    exit 1
  fi
}

expect_status 1 # missing statistics
printf 'execs_done : 0\n' > "$fixture/default/fuzzer_stats"
expect_status 1
printf 'execs_done : 100\nsaved_crashes : 0\nsaved_hangs : 0\n' > "$fixture/default/fuzzer_stats"
expect_status 0
for category in crashes hangs; do
  touch "$fixture/default/$category/id:000000"
  expect_status 1
  rm "$fixture/default/$category/id:000000"
  expect_status 0
done
for counter in saved_crashes saved_hangs; do
  printf 'execs_done : 100\n%s : 1\n' "$counter" > "$fixture/default/fuzzer_stats"
  expect_status 1 # reported finding is fatal even if its input file went missing
done
printf 'execs_done : invalid\n' > "$fixture/default/fuzzer_stats"
expect_status 1
printf 'not_statistics : 100\n' > "$fixture/default/fuzzer_stats"
expect_status 1
echo 'Fuzz finding gate tests passed.'
