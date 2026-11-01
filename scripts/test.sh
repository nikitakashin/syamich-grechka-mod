#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "$0")/.." && pwd)"
dotnet_cmd="${DOTNET:-dotnet}"
"$dotnet_cmd" run --project "$repo_root/tests/DomainTests.csproj" -- "$repo_root/mods/GrechkaQuests/QuestPacks/grechka-starter.json"
"$dotnet_cmd" run --project "$repo_root/tests/Industry/IndustryTests.csproj"
"$dotnet_cmd" run --project "$repo_root/tests/Storage/StorageTests.csproj"
python3 "$repo_root/scripts/check-industry-bootstrap.py"
python3 "$repo_root/scripts/check-mecha-bootstrap.py"
python3 "$repo_root/scripts/check-world-integration.py"
python3 "$repo_root/scripts/check-storage-bootstrap.py"
python3 "$repo_root/scripts/check-linked-ores.py"
