#!/usr/bin/env bash
# Isolated installed-library checks. Does not launch or control the game.
set -euo pipefail
repo_root="$(cd "$(dirname "$0")/.." && pwd)"
: "${RIMWORLD_MANAGED_DIR:?Set the installed RimWorld 1.7 library directory}"
: "${HARMONY_ASSEMBLY:?Set the installed Harmony 1.7 assembly}"
: "${MISC_ROBOTS_ASSEMBLY:?Set MISC_ROBOTS_ASSEMBLY to the installed Misc. Robots 1.7 AIRobot.dll}"
: "${ACHIEVEMENTS_ASSEMBLY:?Set the installed Vanilla Achievements Expanded 1.7 assembly}"
dotnet_cmd="${DOTNET:-dotnet}"
"$dotnet_cmd" run --project "$repo_root/tests/Research/ResearchTests.csproj" -- "$repo_root"
"$dotnet_cmd" run --project "$repo_root/tests/TierRecipes/TierRecipeTests.csproj" -- "$repo_root"
"$dotnet_cmd" run --project "$repo_root/tests/Floors/FloorsTests.csproj" -- "$repo_root"
if [[ -d "${MISC_ROBOTS_PLUS_ROOT:-$repo_root/work/external-mods/294100/3253308913}" ]]; then
  "$dotnet_cmd" run --project "$repo_root/tests/MiscRobots/MiscRobotsTests.csproj" -- "$repo_root"
fi
for project in Treasures/TreasuresTests Treasures/Expeditions/ExpeditionTests Pharmacy/PharmacyTests Factions/FactionsTests Localization/LocalizationTests Achivements/AchivementsTests Tweaks/TweaksTests Farms/FarmsTests Kite/KiteTests DestroyItem/DestroyItemTests Drones/DronesTests Traders/TradersTests TradersArrival/TradersArrivalTests SpeedPlus/SpeedPlusTests CameraPlus/CameraPlusTests Architect/ArchitectTests StorageGame/StorageGameTests Autocraft/AutocraftTests DynamicCraft/DynamicCraftTests Industry/Game/IndustryGameTests Industry/Special/SpecialTierTests Equipment/EquipmentTests LuckyBlocks/Native/LuckyNativeTests WorldIntegration/WorldIntegrationTests FactoryUpgrade/FactoryUpgradeTests QuestMachines/QuestMachineTests QuestPractical/QuestPracticalTests QuestKills/QuestKillTests QuestPresentation/QuestPresentationTests FamilyWork/FamilyWorkTests PawnWork/PawnWorkTests Integration/IntegrationTests; do
  "$dotnet_cmd" run --project "$repo_root/tests/$project.csproj" -- "$repo_root"
done

if [[ -n "${PIPESYSTEM_ASSEMBLY:-}" ]]; then
  "$dotnet_cmd" run --project "$repo_root/tests/Factory/FactoryTests.csproj" -- "$repo_root"
  "$dotnet_cmd" run --project "$repo_root/tests/Industry/Factory/FactoryTierTests.csproj" -- "$repo_root"
  if [[ -n "${SPECIAL_FACTORY_DLL:-}" ]]; then
    "$dotnet_cmd" run --project "$repo_root/tests/FactoryDrill/FactoryDrillTests.csproj" -- "$repo_root"
  fi
  "$dotnet_cmd" run --project "$repo_root/tests/ProductionStates/ProductionStateTests.csproj" -- "$repo_root"
else
  echo "SKIP optional Factory library test: set PIPESYSTEM_ASSEMBLY to test the installed adapter"
fi
