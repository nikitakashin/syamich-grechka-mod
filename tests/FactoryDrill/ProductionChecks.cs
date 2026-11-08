using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml;
using Grechka.WorldIntegration;
using HarmonyLib;
using PipeSystem;
using Verse;
using Process = PipeSystem.Process;

internal static class ProductionChecks
{
    private static void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); }

    internal static void Run(string root, ThingDef steel)
    {
        var patches = new XmlDocument(); patches.Load(Path.Combine(root, "mods/GrechkaWorldIntegration/Patches/FactoryBalance.xml"));
        var source = new XmlDocument(); source.LoadXml("<Defs><ThingDef><defName>VFEFactory_AutomatedDrillPlatform</defName><comps><li Class='PipeSystem.CompProperties_AdvancedResourceProcessor'><processes><li>VFEFactory_DigForMetals</li></processes></li></comps></ThingDef><RecipeDef><defName>Grechka_MakeComponentT3</defName></RecipeDef><PipeSystem.ProcessDef><defName>VFEFactory_AssembleAdvancedComponent</defName><ticks>40000</ticks><ingredients><li><thing>Steel</thing><countNeeded>20</countNeeded></li></ingredients></PipeSystem.ProcessDef></Defs>");
        foreach (XmlNode node in patches.SelectNodes("Patch/Operation"))
            Check(DirectXmlToObject.ObjectFromXml<PatchOperation>(node, false).Apply(source), "Native optional Factory balance patch applies");
        Check(source.SelectNodes("Defs/ThingDef/comps/li/processes/li").Count == 4
            && source.SelectSingleNode("Defs/ThingDef/comps/li/processes/li[text()='VFEFactory_DigForMetals']") != null,
            "Three targeted modes are added without removing the saved random mode");
        Check(source.SelectNodes("Defs/PipeSystem.ProcessDef/ingredients/li").Count == 4
            && source.SelectSingleNode("Defs/PipeSystem.ProcessDef/ingredients/li[thing='Gold']/countNeeded").InnerText == "2"
            && source.SelectSingleNode("Defs/PipeSystem.ProcessDef/modExtensions/li[@Class='Grechka.WorldIntegration.FactoryProcessBalanceProperties']") != null,
            "Advanced Factory component uses the same four physical ingredients as manual production");
        var empty = new XmlDocument(); empty.LoadXml("<Defs/>");
        foreach (XmlNode node in patches.SelectNodes("Patch/Operation"))
            Check(DirectXmlToObject.ObjectFromXml<PatchOperation>(node, false).Apply(empty), "Factory balance XML is harmless without optional modules");

        var definitions = new XmlDocument(); definitions.Load(Path.Combine(root, "mods/GrechkaWorldIntegration/Defs/FactoryDrillProduction.xml"));
        foreach (XmlNode node in definitions.DocumentElement.ChildNodes)
        {
            Check(node.Attributes?["MayRequire"]?.Value == "vanillaexpanded.vfefactory", "Targeted process requires only the existing Factory package");
            var def = DirectXmlToObject.ObjectFromXml<ProcessDef>(node, false);
            Check(def.ticks > 0 && def.results.Count == 1 && def.onlyGrabAndOutputToFactoryHoppers && def.spawnOnInteractionCell,
                def.defName + " parses as a native hopper-output process");
            int count = int.Parse(node.SelectSingleNode("results/li/count").InnerText);
            string material = node.SelectSingleNode("results/li/thing").InnerText;
            double perDay = 60000d / def.ticks * ProfileVariants.Speed[2] * (1 + ProfileVariants.Output[2]) * count;
            Check(material == "Steel" ? perDay >= 300 && perDay <= 500 : perDay >= 20 && perDay <= 40,
                material + " T2 ideal daily throughput meets the agreed supply range");
            Check(count * 2 <= 75, material + " T5 full output fits one standard resource stack");
        }

        var machine = new ThingWithComps { def = new ThingDef { defName = "FactoryBalanceFixture" } };
        var recipe = new ProcessDef { defName = "FactoryBalanceRecipe", ticks = 12000, ingredients = new() { new ProcessDef.Ingredient { thing = steel, countNeeded = 12 } },
            modExtensions = new() { new FactoryProcessBalanceProperties { refreshFixedInputs = true, previousTicks = 40000 } } };
        var owner = new ThingAndResourceOwner(steel, null, 20, null);
        var process = (Process)RuntimeHelpers.GetUninitializedObject(typeof(Process));
        void Set(string field, object value) => AccessTools.Field(typeof(Process), field).SetValue(process, value);
        Set("parent", machine); Set("def", recipe); Set("ingredientsOwners", new List<ThingAndResourceOwner> { owner });
        Set("spawning", true); Set("cachedInitialTicks", 40000); Set("ticksOrQualityTicks", 50000); Set("tickLeft", 50000);
        var adjust = AccessTools.Method(typeof(FactoryTierAdapter), "SetInputs");
        Set("progress", .5f); adjust.Invoke(null, new object[] { process, true });
        Check(owner.Required == 20, "A saved in-progress batch retains its paid original requirement");
        Set("progress", 0f); AccessTools.Field(typeof(ThingAndResourceOwner), "count").SetValue(owner, 3);
        adjust.Invoke(null, new object[] { process, false });
        Check(owner.Required == 17 && owner.Count == 3, "Partially supplied old batches retain their exact materials and requirement");
        AccessTools.Field(typeof(ThingAndResourceOwner), "count").SetValue(owner, 0);
        Set("currentResult", 1); adjust.Invoke(null, new object[] { process, false });
        Check(owner.Required == 20, "A partially delivered old result cannot be rebased even when progress is zero");
        Set("currentResult", 0);
        adjust.Invoke(null, new object[] { process, false });
        Check(owner.Required == 12, "The next empty batch adopts the new physical price without discount compounding");
        Check((int)AccessTools.Field(typeof(Process), "cachedInitialTicks").GetValue(process) == 12000
            && (int)AccessTools.Field(typeof(Process), "ticksOrQualityTicks").GetValue(process) == 15000,
            "Empty saved batch adopts the new cadence while preserving native room and overclock factors");

        var outputHook = new Harmony("grechka.test.targeted-output");
        Check(outputHook.CreateClassProcessor(typeof(FactoryTargetedOutputHook)).Patch()?.Count > 0, "Targeted full-batch guard binds to native TrySpawnAtCell");
        Game previousGame = Current.Game;
        try
        {
            var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map)); map.info = new MapInfo { Size = new IntVec3(10, 1, 10) };
            map.cellIndices = new CellIndices(map); map.thingGrid = new ThingGrid(map);
            var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game)); AccessTools.Field(typeof(Game), "maps").SetValue(game, new List<Map> { map }); Current.Game = game;
            var cell = new IntVec3(3, 0, 3); steel.stackLimit = 75;
            var existing = new Thing { def = steel, stackCount = 60 }; map.thingGrid.ThingsListAtFast(cell).Add(existing);
            var result = new ProcessDef.Result { thing = steel, count = 20 };
            Set("def", new ProcessDef { defName = "GrechkaWorld_DrillSteel", results = new() { result } });
            object[] call = { result, cell, map, null };
            bool success = (bool)AccessTools.Method(typeof(Process), "TrySpawnAtCell").Invoke(process, call);
            Check(!success && call[3] == null && existing.stackCount == 60,
                "Real native output refuses a partial merge without losing resources or declaring success");
            existing.stackCount = 55; Thing output = null; bool accepted = false;
            Check(FactoryTargetedOutputHook.Prefix(process, result, cell, map, ref output, ref accepted),
                "Exactly enough stack capacity permits native delivery");
        }
        finally { Current.Game = previousGame; }
    }
}
