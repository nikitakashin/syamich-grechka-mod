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
python3 "$repo_root/scripts/check-economy.py"
python3 "$repo_root/scripts/check-research.py"
python3 "$repo_root/scripts/check-storage-bootstrap.py"
python3 "$repo_root/scripts/check-linked-ores.py"

"$dotnet_cmd" run --project "$repo_root/tests/LuckyBlocks/LuckyBlocksTests.csproj"
python3 "$repo_root/scripts/check-equipment.py"
python3 "$repo_root/scripts/check-lucky-blocks.py"
python3 "$repo_root/scripts/check-industry-late.py"
python3 "$repo_root/scripts/check-world-profile.py"
python3 "$repo_root/scripts/check-architect-categories.py"
python3 "$repo_root/scripts/check-traders.py"
python3 "$repo_root/tests/Artwork/test_tier_intake.py"

python3 "$repo_root/scripts/check-drones.py"
python3 "$repo_root/tests/Drones/test_config.py"

python3 "$repo_root/scripts/check-destroy-item.py"

python3 "$repo_root/scripts/check-kite.py"

python3 "$repo_root/scripts/check-farms.py"

python3 "$repo_root/scripts/check-localization.py"
"$dotnet_cmd" run --project "$repo_root/tests/Localization/Quests/QuestsLocalization.csproj" -- "$repo_root/mods/GrechkaQuests"

python3 "$repo_root/scripts/check-pharmacy.py"

python3 "$repo_root/scripts/check-treasures.py"
