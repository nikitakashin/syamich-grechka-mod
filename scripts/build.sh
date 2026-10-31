#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "$0")/.." && pwd)"
: "${RIMWORLD_MANAGED_DIR:?Set RIMWORLD_MANAGED_DIR to your RimWorld 1.7 library directory}"
dotnet_cmd="${DOTNET:-dotnet}"
"$dotnet_cmd" build "$repo_root/mods/GrechkaQuests/Source/GrechkaQuests.csproj" "-p:GameManagedDir=$RIMWORLD_MANAGED_DIR" --nologo
