#!/usr/bin/env bash
# Isolated installed-library checks. Does not launch or control the game.
set -euo pipefail
repo_root="$(cd "$(dirname "$0")/.." && pwd)"
: "${RIMWORLD_MANAGED_DIR:?Set the installed RimWorld 1.7 library directory}"
: "${HARMONY_ASSEMBLY:?Set the installed Harmony 1.7 assembly}"
dotnet_cmd="${DOTNET:-dotnet}"
for project in StorageGame/StorageGameTests Autocraft/AutocraftTests Industry/Game/IndustryGameTests Industry/Special/SpecialTierTests Equipment/EquipmentTests LuckyBlocks/Native/LuckyNativeTests WorldIntegration/WorldIntegrationTests FactoryUpgrade/FactoryUpgradeTests QuestMachines/QuestMachineTests FamilyWork/FamilyWorkTests Integration/IntegrationTests; do
  "$dotnet_cmd" run --project "$repo_root/tests/$project.csproj" -- "$repo_root"
done

if [[ -n "${PIPESYSTEM_ASSEMBLY:-}" ]]; then
  "$dotnet_cmd" run --project "$repo_root/tests/Factory/FactoryTests.csproj" -- "$repo_root"
  "$dotnet_cmd" run --project "$repo_root/tests/Industry/Factory/FactoryTierTests.csproj" -- "$repo_root"
else
  echo "SKIP optional Factory library test: set PIPESYSTEM_ASSEMBLY to test the installed adapter"
fi
