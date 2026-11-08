#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "$0")/.." && pwd)"
: "${RIMWORLD_MANAGED_DIR:?Set RIMWORLD_MANAGED_DIR to your RimWorld 1.7 library directory}"
: "${HARMONY_ASSEMBLY:?Set HARMONY_ASSEMBLY to the installed Harmony 1.7 0Harmony.dll}"
: "${MISC_ROBOTS_ASSEMBLY:?Set MISC_ROBOTS_ASSEMBLY to the installed Misc. Robots 1.7 AIRobot.dll}"
: "${ACHIEVEMENTS_ASSEMBLY:?Set ACHIEVEMENTS_ASSEMBLY to the installed Vanilla Achievements Expanded 1.7 AchievementsExpanded.dll}"
dotnet_cmd="${DOTNET:-dotnet}"
for module in GrechkaIndustry GrechkaStorage GrechkaEquipment GrechkaLuckyBlocks GrechkaQuests GrechkaPharmacy GrechkaWorldIntegration GrechkaCameraPlus GrechkaSpeedPlus GrechkaTraders GrechkaDrones GrechkaDestroyItem GrechkaKite GrechkaFarms GrechkaTweaks GrechkaAchivements GrechkaFactions GrechkaTreasures GrechkaRealRuinsRobotsFix GrechkaTranslationsRus; do
  "$dotnet_cmd" build "$repo_root/mods/$module/Source/$module.csproj" "-p:GameManagedDir=$RIMWORLD_MANAGED_DIR" "-p:HarmonyAssembly=$HARMONY_ASSEMBLY" --nologo "$@"
done
