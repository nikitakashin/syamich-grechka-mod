#!/usr/bin/env bash
set -euo pipefail
# Runs only isolated library tests. Never launches, closes or controls RimWorld.
repo_root="$(cd "$(dirname "$0")/.." && pwd)"
: "${RIMWORLD_MANAGED_DIR:?Set the installed RimWorld 1.7 managed library directory}"
: "${HARMONY_ASSEMBLY:?Set the installed Harmony 1.7 assembly}"
dotnet_cmd="${DOTNET:-dotnet}"
"$dotnet_cmd" run --project "$repo_root/tests/StorageGame/StorageGameTests.csproj" "$@"
