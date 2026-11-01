using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace Grechka.Quests
{
    internal static class StorageInventoryAdapter
    {
        private static bool initialized;
        private static Func<Thing, IEnumerable<Thing>> held;
        private static Func<Map, IEnumerable<Thing>> map;
        private static void Initialize()
        {
            if (initialized) return;
            initialized = true;
            Type type = AccessTools.TypeByName("Grechka.Storage.StorageInventory");
            if (type == null) return;
            var heldMethod = AccessTools.Method(type, "HeldContents", new[] { typeof(Thing) });
            var mapMethod = AccessTools.Method(type, "MapContents", new[] { typeof(Map) });
            if (heldMethod != null) held = (Func<Thing, IEnumerable<Thing>>)Delegate.CreateDelegate(typeof(Func<Thing, IEnumerable<Thing>>), heldMethod);
            if (mapMethod != null) map = (Func<Map, IEnumerable<Thing>>)Delegate.CreateDelegate(typeof(Func<Map, IEnumerable<Thing>>), mapMethod);
        }
        internal static IEnumerable<Thing> Held(Thing carrier) { Initialize(); return held?.Invoke(carrier) ?? Array.Empty<Thing>(); }
        internal static IEnumerable<Thing> OnMap(Map source) { Initialize(); return map?.Invoke(source) ?? Array.Empty<Thing>(); }
        internal static void ObserveCarrier(Thing carrier)
        {
            var seen = new HashSet<ThingDef>();
            foreach (Thing item in Held(carrier)) if (seen.Add(item.def)) QuestGame.Current?.ObservePotentialItem(item.def);
        }
    }
}
