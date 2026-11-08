using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Grechka.Quests
{
    // JobDriver_PlantWork creates extra ThingComp yields without sending the
    // normal QuestManager harvest notification. Count successful placement
    // inside that job only, and exclude the already-notified main product.
    internal static class AdditionalHarvestAdapter
    {
        sealed class Work
        {
            internal Pawn actor;
            internal Thing mainProduct;
        }

        sealed class Placement
        {
            internal Pawn actor;
            internal ThingDef product;
            internal ThingDef stuff;
            internal string quality;
            internal int count;
        }

        [ThreadStatic] static Work currentWork;
        static FieldInfo cutField;
        static FieldInfo actorField;
        static bool installed;
        internal static bool Installed { get { return installed; } }

        internal static void TryInstall()
        {
            if (installed) return;
            Type closure = AccessTools.TypeByName("RimWorld.JobDriver_PlantWork+<>c__DisplayClass11_0");
            MethodInfo work = closure == null ? null : AccessTools.Method(closure, "<MakeNewToils>b__1", new[] { typeof(int) });
            MethodInfo place = AccessTools.Method(typeof(GenPlace), "TryPlaceThing", new[] {
                typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode),
                typeof(Action<Thing, int>), typeof(Predicate<IntVec3>), typeof(Rot4?), typeof(int)
            });
            FieldInfo cut = closure == null ? null : AccessTools.Field(closure, "cut");
            FieldInfo actor = AccessTools.Field(typeof(Toil), "actor");
            if (work == null || work.ReturnType != typeof(void) || place == null || place.ReturnType != typeof(bool)
                || cut == null || !typeof(Toil).IsAssignableFrom(cut.FieldType)
                || actor == null || !typeof(Pawn).IsAssignableFrom(actor.FieldType))
            {
                Log.Warning(QuestText.Get("GrechkaQuests_7a6cfc3a879d", "[Grechka Quests] Источник дополнительных продуктов урожая не найден в этой версии игры."));
                return;
            }
            try
            {
                var harmony = new Harmony("grechka.quests.additionalharvest");
                harmony.Patch(work,
                    prefix: new HarmonyMethod(typeof(AdditionalHarvestAdapter), nameof(BeginWork)),
                    postfix: new HarmonyMethod(typeof(AdditionalHarvestAdapter), nameof(EndWork)),
                    finalizer: new HarmonyMethod(typeof(AdditionalHarvestAdapter), nameof(RestoreWork)));
                harmony.Patch(place,
                    prefix: new HarmonyMethod(typeof(AdditionalHarvestAdapter), nameof(BeforePlace)),
                    postfix: new HarmonyMethod(typeof(AdditionalHarvestAdapter), nameof(AfterPlace)));
                cutField = cut; actorField = actor; installed = true;
            }
            catch (Exception e)
            {
                Log.Error(QuestText.Get("GrechkaQuests_3ff0ea9edcb7", "[Grechka Quests] Не удалось подключить дополнительные продукты урожая: ") + e);
            }
        }

        static void BeginWork(object __instance, out Work __state)
        {
            __state = currentWork;
            Toil toil = cutField?.GetValue(__instance) as Toil;
            currentWork = new Work { actor = toil == null ? null : actorField?.GetValue(toil) as Pawn };
        }

        internal static void MarkMainProduct(Thing product)
        {
            if (currentWork != null) currentWork.mainProduct = product;
        }

        static void BeforePlace(Thing __0, out Placement __state)
        {
            __state = null;
            Work work = currentWork;
            if (work == null || __0 == null || __0 == work.mainProduct || __0.def == null || __0.stackCount <= 0) return;
            CompQuality quality = __0.TryGetComp<CompQuality>();
            __state = new Placement { actor = work.actor, product = __0.def, stuff = __0.Stuff,
                quality = quality == null ? null : quality.Quality.ToString(), count = __0.stackCount };
        }

        static void AfterPlace(bool __result, Placement __state)
        {
            if (__result && __state != null)
                QuestGame.Current?.RecordVerifiedOutput("harvest", __state.actor, __state.product,
                    __state.stuff, __state.quality, __state.count);
        }

        static void EndWork(Work __state) { currentWork = __state; }
        static Exception RestoreWork(Exception __exception, Work __state)
        {
            currentWork = __state;
            return __exception;
        }
    }
}
