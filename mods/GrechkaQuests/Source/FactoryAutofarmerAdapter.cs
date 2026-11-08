using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.Quests
{
    // The Factory autofarmer bypasses PipeSystem and writes crops to its own
    // buffer or directly to the map. Offloading that buffer is not a harvest.
    internal static class FactoryAutofarmerAdapter
    {
        sealed class RowAttempt
        {
            internal Thing machine;
            internal readonly Dictionary<ThingDef, int> before = new Dictionary<ThingDef, int>();
        }

        [ThreadStatic] static RowAttempt currentRow;
        static FieldInfo storageField;
        static bool installed;
        internal static bool Installed { get { return installed; } }

        internal static void TryInstall()
        {
            if (installed || !ModsConfig.IsActive("VanillaExpanded.VFEFactory")) return;
            Type farmer = AccessTools.TypeByName("VanillaFurnitureExpandedFactory.Building_Autofarmer");
            MethodInfo row = farmer == null ? null : AccessTools.Method(farmer, "ProcessRow");
            MethodInfo yield = AccessTools.Method(typeof(Plant), "YieldNow");
            FieldInfo storage = farmer == null ? null : AccessTools.Field(farmer, "harvestStorageThings");
            if (row == null || row.ReturnType != typeof(void) || yield == null || yield.ReturnType != typeof(int)
                || storage == null || !typeof(IEnumerable<Thing>).IsAssignableFrom(storage.FieldType))
            {
                Log.Warning(QuestText.Get("GrechkaQuests_dd7084e36a35", "[Grechka Quests] VFE Factory: автоферма изменилась; учёт её урожая отключён."));
                return;
            }

            try
            {
                var harmony = new Harmony("grechka.quests.autofarmer");
                harmony.Patch(yield, postfix: new HarmonyMethod(typeof(FactoryAutofarmerAdapter), nameof(CaptureYield)));
                harmony.Patch(row,
                    prefix: new HarmonyMethod(typeof(FactoryAutofarmerAdapter), nameof(BeginRow)),
                    postfix: new HarmonyMethod(typeof(FactoryAutofarmerAdapter), nameof(EndRow)),
                    finalizer: new HarmonyMethod(typeof(FactoryAutofarmerAdapter), nameof(RestoreRow)));
                storageField = storage;
                installed = true;
            }
            catch (Exception e)
            {
                Log.Error(QuestText.Get("GrechkaQuests_965b3dc209bf", "[Grechka Quests] Не удалось подключить автоферму VFE Factory: ") + e);
            }
        }

        static void BeginRow(object __instance, out RowAttempt __state)
        {
            __state = currentRow;
            Thing machine = __instance as Thing;
            currentRow = machine != null && machine.Spawned && machine.Faction == Faction.OfPlayer
                ? new RowAttempt { machine = machine } : null;
        }

        static void CaptureYield(Plant __instance, int __result)
        {
            RowAttempt row = currentRow;
            ThingDef product = __instance?.def?.plant?.harvestedThingDef;
            QuestGame game = QuestGame.Current;
            if (row == null || __result <= 0 || product == null || row.before.ContainsKey(product)
                || game == null || !game.WatchesEventTarget("harvest", product)) return;
            row.before.Add(product, CountExisting(row.machine, product));
        }

        static int CountExisting(Thing machine, ThingDef product)
        {
            if (machine?.Map == null) return 0;
            long amount = 0;
            var seen = new HashSet<Thing>(ReferenceEqualityComparer.Instance);
            foreach (Thing item in machine.Map.listerThings.ThingsOfDef(product))
                if (item != null && !item.Destroyed && seen.Add(item)) amount += Math.Max(0, item.stackCount);
            if (storageField?.GetValue(machine) is IEnumerable<Thing> stored)
                foreach (Thing item in stored)
                    if (item != null && !item.Destroyed && item.def == product && seen.Add(item)) amount += Math.Max(0, item.stackCount);
            return (int)Math.Min(int.MaxValue, amount);
        }

        static void EndRow(RowAttempt __state)
        {
            RowAttempt finished = currentRow;
            currentRow = __state;
            if (finished == null) return;
            QuestGame game = QuestGame.Current;
            if (game == null) return;
            foreach (var pair in finished.before)
            {
                int increase = CountExisting(finished.machine, pair.Key) - pair.Value;
                if (increase > 0) game.RecordVerifiedMachineOutput("harvest", finished.machine, pair.Key, null, null, increase);
            }
        }

        static Exception RestoreRow(Exception __exception, RowAttempt __state)
        {
            currentRow = __state;
            return __exception;
        }
    }
}
