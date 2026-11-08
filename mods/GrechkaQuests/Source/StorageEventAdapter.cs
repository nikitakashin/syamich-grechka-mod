using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Grechka.Quests
{
    internal static class StorageEventAdapter
    {
        static bool installed;
        internal static void TryInstall()
        {
            if (installed || !ModsConfig.IsActive("grechka.storage")) return;
            Type source = AccessTools.TypeByName("Grechka.Storage.StorageProduction");
            EventInfo produced = source?.GetEvent("OutputProduced", BindingFlags.Public | BindingFlags.Static);
            if (produced == null || produced.EventHandlerType != typeof(Action<Thing, Thing, int>)) return;
            try { produced.AddEventHandler(null, new Action<Thing, Thing, int>(Record)); installed = true; }
            catch (Exception e) { Log.Warning(QuestText.Get("GrechkaQuests_98760bbe154b", "[Grechka Quests] Storage: адаптер выпуска не подключён: ") + e.Message); }
        }
        static void Record(Thing machine, Thing product, int count) => QuestGame.Current?.RecordVerifiedMachineOutput("produce", machine, product, count);
    }
}
