using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.Quests
{
    // Pawn.Kill receives ExecutionCut with the slaughtering pawn as instigator too.
    // One transition hook therefore covers combat and slaughter without double reporting.
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class KillEventHook
    {
        public sealed class Attempt
        {
            public QuestGame game;
            public ThingDef victim;
        }
        public static bool OwnedKiller(Thing source)
        {
            if (source == null || source.Faction == null || source.Faction != Faction.OfPlayer) return false;
            return !(source is Pawn pawn) || !pawn.IsPrisoner;
        }
        public static void Prefix(Pawn __instance, DamageInfo? __0, out Attempt __state)
        {
            __state = null;
            QuestGame game = QuestGame.Current;
            if (game == null || __instance?.health == null || __instance.Dead
                || !__0.HasValue || !OwnedKiller(__0.Value.Instigator)) return;
            __state = new Attempt { game = game, victim = __instance.def };
        }
        public static void Postfix(Pawn __instance, Attempt __state)
        {
            if (__state != null && __instance?.health != null && __instance.Dead)
                __state.game.RecordVerifiedKill(__state.victim);
        }
    }
}
