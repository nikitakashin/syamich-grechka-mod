#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "$0")/.." && pwd)"
: "${RIMWORLD_MANAGED_DIR:?Set RIMWORLD_MANAGED_DIR to your RimWorld 1.7 library directory}"
: "${HARMONY_ASSEMBLY:?Set HARMONY_ASSEMBLY to the installed Harmony 1.7 0Harmony.dll}"
dotnet_cmd="${DOTNET:-dotnet}"
"$dotnet_cmd" build "$repo_root/mods/GrechkaQuests/Source/GrechkaQuests.csproj" "-p:GameManagedDir=$RIMWORLD_MANAGED_DIR" "-p:HarmonyAssembly=$HARMONY_ASSEMBLY" --nologo
"$dotnet_cmd" build "$repo_root/mods/GrechkaWorldIntegration/Source/GrechkaWorldIntegration.csproj" "-p:GameManagedDir=$RIMWORLD_MANAGED_DIR" "-p:HarmonyAssembly=$HARMONY_ASSEMBLY" --nologo
