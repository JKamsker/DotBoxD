#!/usr/bin/env bash
set -euo pipefail

dotnet run --project tools/AgentQueue/src/AgentQueue -- "$@"
