#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "$0")/.." && pwd)"
dotnet_cmd="${DOTNET:-dotnet}"
"$dotnet_cmd" run --project "$repo_root/tests/DomainTests.csproj" -- "$repo_root/mods/GrechkaQuests/QuestPacks/grechka-starter.json"
