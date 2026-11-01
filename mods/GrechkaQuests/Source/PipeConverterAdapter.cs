using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Grechka.Quests
{
    // PipeSystem.CompConvertToThing creates or grows a physical stack without
    // QuestManager.Notify_ThingsProduced. VNPE can replace OutputResource and
    // place the new item nearby, so observe the net map increase during this
    // one output call rather than assuming the converter's own cell.
    internal static class PipeConverterAdapter
    {
        sealed class OutputState
        {
            internal Thing machine;
            internal Map map;
            internal ThingDef product;
            internal int before;
        }

        static FieldInfo parentField;
        static FieldInfo productField;
        static PropertyInfo propsProperty;
        static bool installed;
        internal static bool Installed { get { return installed; } }

        internal static void TryInstall()
        {
            if (installed) return;
            Type converter = AccessTools.TypeByName("PipeSystem.CompConvertToThing");
            Type converterProps = AccessTools.TypeByName("PipeSystem.CompProperties_ConvertResourceToThing");
            if (converter == null || converterProps == null) return;
            MethodInfo output = AccessTools.Method(converter, "OutputResource", new[] { typeof(int) });
            FieldInfo parent = AccessTools.Field(typeof(ThingComp), "parent");
            FieldInfo product = AccessTools.Field(converterProps, "thing");
            PropertyInfo props = converter.GetProperty("Props", BindingFlags.Instance | BindingFlags.Public
                | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (output == null || output.ReturnType != typeof(void) || parent == null
                || !typeof(Thing).IsAssignableFrom(parent.FieldType) || product == null
                || !typeof(ThingDef).IsAssignableFrom(product.FieldType) || props == null
                || !converterProps.IsAssignableFrom(props.PropertyType))
            {
                Log.Warning("[Grechka Quests] PipeSystem: сигнатура вывода предмета изменилась; учёт отключён.");
                return;
            }
            try
            {
                var harmony = new Harmony("grechka.quests.pipeconverter");
                harmony.Patch(output,
                    prefix: new HarmonyMethod(typeof(PipeConverterAdapter), nameof(BeforeOutput)) { priority = Priority.First },
                    postfix: new HarmonyMethod(typeof(PipeConverterAdapter), nameof(AfterOutput)));
                parentField = parent; productField = product; propsProperty = props; installed = true;
            }
            catch (Exception e)
            {
                Log.Error("[Grechka Quests] Не удалось подключить PipeSystem: " + e);
            }
        }

        static int CountOnMap(Map map, ThingDef product)
        {
            if (map == null || product == null) return 0;
            long count = 0;
            foreach (Thing thing in map.listerThings.ThingsOfDef(product))
                if (thing != null && !thing.Destroyed)
                    count += Math.Max(0, thing.stackCount);
            return (int)Math.Min(int.MaxValue, count);
        }

        static void BeforeOutput(object __instance, out OutputState __state)
        {
            __state = null;
            Thing machine = parentField?.GetValue(__instance) as Thing;
            ThingDef product = productField?.GetValue(propsProperty?.GetValue(__instance)) as ThingDef;
            QuestGame game = QuestGame.Current;
            if (machine == null || !machine.Spawned || machine.Map == null || product == null
                || game == null || (!game.WatchesEventTarget("produce", product) && !game.WatchesHaveTarget(product))) return;
            __state = new OutputState { machine = machine, map = machine.Map,
                product = product, before = CountOnMap(machine.Map, product) };
        }

        static void AfterOutput(OutputState __state)
        {
            if (__state == null) return;
            int increase = CountOnMap(__state.map, __state.product) - __state.before;
            if (increase > 0)
            {
                QuestGame.Current?.RecordVerifiedMachineOutput("produce", __state.machine,
                    __state.product, null, null, increase);
                QuestGame.Current?.ObservePotentialItem(__state.product);
            }
        }
    }
}
