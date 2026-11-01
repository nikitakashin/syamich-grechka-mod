using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Grechka.Quests
{
    internal static class IndustryEventAdapter
    {
        private static bool installed;

        internal static void TryInstall()
        {
            if (installed || !ModsConfig.IsActive("grechka.industry")) return;
            Type source = AccessTools.TypeByName("Grechka.Industry.IndustryProduction");
            EventInfo produced = source?.GetEvent("OutputProduced", BindingFlags.Public | BindingFlags.Static);
            if (produced == null || produced.EventHandlerType != typeof(Action<Thing, ThingDef, int>))
            {
                Log.Warning("[Grechka Quests] Industry: событие выпуска не найдено; адаптер отключён.");
                return;
            }
            try
            {
                produced.AddEventHandler(null, new Action<Thing, ThingDef, int>(RecordOutput));
                installed = true;
            }
            catch (Exception error)
            {
                Log.Warning("[Grechka Quests] Industry: адаптер выпуска не подключён: " + error.Message);
            }
        }

        private static void RecordOutput(Thing machine, ThingDef product, int count)
        {
            QuestGame.Current?.RecordVerifiedMachineOutput("produce", machine, product, null, null, count);
        }
    }
}
