#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "Usage: check-fuzz-findings.sh <AFL-output-directory>" >&2
  exit 2
fi

root="$1/default"
if [[ ! -s "$root/fuzzer_stats" ]]; then
  echo "Missing AFL statistics: $root/fuzzer_stats" >&2
  exit 1
fi

if ! awk -F: '$1 ~ /^[[:space:]]*execs_done[[:space:]]*$/ && $2 + 0 > 0 { ran = 1 } END { exit !ran }' "$root/fuzzer_stats"; then
  echo "AFL did not execute any inputs." >&2
  exit 1
fi

failed=0
if awk -F: '$1 ~ /^[[:space:]]*(saved_crashes|saved_hangs)[[:space:]]*$/ && $2 + 0 > 0 { found = 1 } END { exit !found }' "$root/fuzzer_stats"; then
  echo "AFL reported saved crashes or hangs." >&2
  failed=1
fi
for category in crashes hangs; do
  for input in "$root/$category"/id:*; do
    [[ -f "$input" ]] || continue
    echo "Fuzz finding: $input" >&2
    failed=1
  done
done
exit "$failed"
