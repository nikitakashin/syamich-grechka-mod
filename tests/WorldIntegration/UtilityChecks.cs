using System.Reflection;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml;
using Grechka.WorldIntegration;
using HarmonyLib;
using RimWorld;
using Verse;

internal static class UtilityChecks
{
    private static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);}
    private static ModContentPack Pack(string id)
    {
        var pack=(ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));
        AccessTools.Field(typeof(ModContentPack),"packageIdPlayerFacingInt").SetValue(pack,id);return pack;
    }
    internal static void Run()
    {
        var original=new ThingDef{defName="FixtureBattery",thingClass=typeof(Building_Battery),size=new IntVec2(1,1)};
        var upgraded=new ThingDef{defName="FixtureBatteryUpgraded",thingClass=typeof(Building_Battery),size=new IntVec2(1,1)};
        var oldProps=new CompProperties_Battery();var newProps=new CompProperties_Battery();
        original.comps=new(){oldProps};upgraded.comps=new(){newProps};
        var battery=new Building_Battery{def=original,thingIDNumber=428};
        var comp=new CompPowerBattery{parent=battery,props=oldProps};
        AccessTools.Field(typeof(ThingWithComps),"comps").SetValue(battery,new List<ThingComp>{comp});
        AccessTools.Field(typeof(CompPowerBattery),"storedEnergy").SetValue(comp,123f);
        FactoryUpgradeSafety.SetDefinition(battery,upgraded);
        Check(battery.thingIDNumber==428&&ReferenceEquals(comp,battery.AllComps.Single())&&(float)AccessTools.Field(typeof(CompPowerBattery),"storedEnergy").GetValue(comp)==123f,
            "Utility definition swap retains the exact battery object, identity, component and stored energy");
        BatteryChecks(battery, comp);
        var building=new Building{def=new ThingDef{defName="FixtureFuelled",thingClass=typeof(Building),size=new IntVec2(1,1)}};
        var fuelProps=new CompProperties_Refuelable();var tempProps=new CompProperties_TempControl();
        building.def.comps=new(){fuelProps,tempProps};
        var fuel=new CompRefuelable{parent=building,props=fuelProps};var temp=new CompTempControl{parent=building,props=tempProps};
        AccessTools.Field(typeof(ThingWithComps),"comps").SetValue(building,new List<ThingComp>{fuel,temp});
        AccessTools.Field(typeof(CompRefuelable),"fuel").SetValue(fuel,17.25f);
        temp.targetTemperature=31;
        var target=new ThingDef{defName="FixtureFuelledUpgrade",thingClass=typeof(Building),size=new IntVec2(1,1),comps=new(){new CompProperties_Refuelable(),new CompProperties_TempControl()}};
        FactoryUpgradeSafety.SetDefinition(building,target);
        Check(fuel.Fuel==17.25f&&temp.targetTemperature==31,"Same-instance utility swap preserves native fuel and thermostat settings");
        var cachedStat=new StatDef{defName="FixtureCachedStat",defaultBaseValue=1,workerClass=typeof(StatWorker)};DefDatabase<StatDef>.Add(cachedStat);
        var worker=cachedStat.Worker;var immutable=new ConcurrentDictionary<Thing,float>();immutable[building]=99;
        AccessTools.Field(typeof(StatWorker),"immutableStatCache").SetValue(worker,immutable);
        WorldProfile.InvalidateThingStats(building);
        Check(!immutable.ContainsKey(building),"Keeping the same ThingID does not preserve stale immutable stat values after a tier change");
        var unequal=new ThingDef{defName="FixtureWrongShape",thingClass=typeof(Building),size=new IntVec2(2,1),comps=target.comps};
        Check(!FactoryUpgradeSafety.Compatible(building,unequal),"Utility upgrade rejects changed footprint before replacing anything");
        var utilityDef=new ThingDef{defName="FixtureUtilityPowered",thingClass=typeof(Building_Battery)};
        ProfileVariants.Entries[utilityDef.defName]=new VariantEntry{sourceName=utilityDef.defName,name=utilityDef.defName,tier=5,minimum=2};WorldProfile.AppliedTiers[utilityDef.defName]=5;
        var power=new CompPowerTrader{parent=new Building_Battery{def=utilityDef}};float draw=-100;
        ProfilePowerHook.Postfix(power,ref draw);Check(draw==-100,"Service-only T5 does not draw eightyfold power without a functional benefit");
        Check(UtilityTierAdapter.SupportsXmlClass("RimWorld.Building_Bed")&&UtilityTierAdapter.SupportsXmlClass("Building_Storage")&&!UtilityTierAdapter.SupportsXmlClass("SomeUnknown.Building"),"Utility support is an explicit exact-class allowlist");
        var shipOriginal=new ThingDef{defName="FixtureShipPart"};DefDatabase<ThingDef>.Add(shipOriginal);
        var shipVariant=new ThingDef{defName="FixtureShipPartUpgraded"};DefDatabase<ThingDef>.Add(shipVariant);
        ProfileVariants.Entries[shipVariant.defName]=new VariantEntry{sourceName=shipOriginal.defName,name=shipVariant.defName,tier=5,minimum=4};
        var harmony=new Harmony("grechka.world.ship-identity-check");harmony.CreateClassProcessor(typeof(ShipVariantIdentityHook)).Patch();
        Type predicateType=typeof(ShipUtility).GetNestedTypes(BindingFlags.NonPublic).Single(t=>AccessTools.Field(t,"partDef")!=null);
        object predicate=RuntimeHelpers.GetUninitializedObject(predicateType);
        AccessTools.Field(predicateType,"partDef").SetValue(predicate,new KeyValuePair<ThingDef,int>(shipOriginal,1));
        MethodInfo check=predicateType.GetMethods(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Single(m=>m.Name.Contains("LaunchFailReasons")&&m.ReturnType==typeof(bool));
        Check((bool)check.Invoke(predicate,new object[]{new Building{def=shipVariant}}),"Native ship launch part predicate recognises a tiered reactor/computer/casket as its original ship part");
        Check(!(bool)check.Invoke(predicate,new object[]{new Building{def=new ThingDef{defName="UnrelatedShipPart"}}}),"Ship identity adapter does not turn an unrelated building into a required part");
        Catalog();
    }
    private static void BatteryChecks(Building_Battery battery, CompPowerBattery comp)
    {
        var harmony = new Harmony("grechka.world.battery-check");
        var patches = harmony.CreateClassProcessor(typeof(BatteryTierPropertiesHook)).Patch();
        Check(patches != null && patches.Count == 1, "Native battery properties getter is actually patched");
        var source = new CompProperties_Battery { storedEnergyMax = 1400, efficiency = .5f };
        comp.props = source;
        var definitions = Enumerable.Range(2, 4).Select(t => new ThingDef
        {
            defName = "FixtureBatteryT" + t, thingClass = typeof(Building_Battery), size = new IntVec2(1,1), comps = new() { source }
        }).ToArray();
        foreach (var def in definitions)
        {
            int tier = int.Parse(def.defName.Substring(def.defName.Length - 1));
            ProfileVariants.Entries[def.defName] = new VariantEntry { sourceName = definitions[0].defName, name = def.defName, tier = tier, minimum = 2 };
            WorldProfile.AppliedTiers[def.defName] = tier;
        }
        float[] capacities = { 1400, 11200, 89600, 716800 };
        float[] efficiencies = { .5f, .7f, .85f, .95f };
        try
        {
            for (int i = 0; i < definitions.Length; i++)
            {
                FactoryUpgradeSafety.SetDefinition(battery, definitions[i]);
                Check(comp.Props.storedEnergyMax == capacities[i] && comp.Props.efficiency == efficiencies[i]
                    && Math.Abs(comp.StoredEnergyPct - 123 / capacities[i]) < .000001,
                    "Native T" + (i + 2) + " capacity/efficiency changes immediately, retaining 123 Wd");
                Check(comp.CompInspectStringExtra().Contains(capacities[i].ToString("F0")), "Native battery inspector reports the effective capacity at T" + (i+2));
            }
            Check(source.storedEnergyMax == 1400 && source.efficiency == .5f, "Tier scaling does not mutate the source or shared component properties");
            comp.AddEnergy(100);
            Check(Math.Abs(comp.StoredEnergy - 218) < .0001, "Native charging uses 95% efficiency at T5");
            string path = Path.Combine(Path.GetTempPath(), "grechka-battery-" + Guid.NewGuid() + ".xml");
            try
            {
                Scribe.saver.InitSaving(path, "fixture"); comp.PostExposeData(); Scribe.saver.FinalizeSaving();
                var restored = new CompPowerBattery { parent = battery, props = source };
                Scribe.loader.InitLoading(path); restored.PostExposeData(); Scribe.loader.FinalizeLoading();
                Check(Math.Abs(restored.StoredEnergy - 218) < .0001 && restored.Props.storedEnergyMax == 716800,
                    "Native Scribe restores charge and resolves the upgraded capacity without a migration");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
            var advanced = new CompProperties_Battery { storedEnergyMax = 2000, efficiency = .9f };
            comp.props = advanced;
            var entry = ProfileVariants.Entries[battery.def.defName];
            entry.minimum = 3;
            Check(comp.Props.storedEnergyMax == 128000 && comp.Props.efficiency == .95f,
                "T3-native large advanced battery keeps its 2000 Wd baseline and gains two capacity steps at T5");
            entry.tier = 4; WorldProfile.AppliedTiers[battery.def.defName] = 4;
            Check(comp.Props.storedEnergyMax == 16000 && comp.Props.efficiency == .9f,
                "Advanced battery upgrade never reduces its native 90% efficiency");
            WorldProfile.AppliedTiers.Remove(battery.def.defName);
            Check(ReferenceEquals(comp.Props, advanced), "An unapplied old-save profile keeps the exact original battery properties");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            foreach (var def in definitions) { WorldProfile.AppliedTiers.Remove(def.defName); ProfileVariants.Entries.Remove(def.defName); }
        }
    }
    private static void Catalog()
    {
        string audit=Path.Combine(Directory.GetCurrentDirectory(),"work/machine-audit/data.json");
        if(!File.Exists(audit)){Console.WriteLine("SKIP local G01 audit fixture is not available");return;}
        using var data=JsonDocument.Parse(File.ReadAllText(audit));var things=data.RootElement.GetProperty("things");
        var document=new XmlDocument();document.LoadXml("<Defs/>");var root=document.DocumentElement;
        var owners=new Dictionary<XmlNode,LoadableXmlAsset>();
        XmlNode Add(string xml,string package)
        {
            var parsed=new XmlDocument();parsed.LoadXml(xml);var node=document.ImportNode(parsed.DocumentElement,true);root.AppendChild(node);
            var asset=(LoadableXmlAsset)RuntimeHelpers.GetUninitializedObject(typeof(LoadableXmlAsset));AccessTools.Field(typeof(LoadableXmlAsset),"mod").SetValue(asset,Pack(package));owners[node]=asset;return node;
        }
        for(int tier=1;tier<=5;tier++)Add("<ThingDef><defName>Grechka_HousingT"+tier+"_Item</defName></ThingDef>","grechka.industry");
        var rules=new XmlDocument();rules.Load("mods/GrechkaWorldIntegration/Defs/MachineProfile.xml");
        int expected=0,variants=0,excluded=0,retired=0;
        foreach(XmlNode rule in rules.DocumentElement.ChildNodes)
        {
            string ruleName=rule.SelectSingleNode("defName")?.InnerText;if(ruleName?.StartsWith("GrechkaWorld_G01_")!=true)continue;
            string name=rule.SelectSingleNode("targetDefName").InnerText;var item=things.GetProperty(name);
            string package=rule.SelectSingleNode("packageId").InnerText;string cls=item.GetProperty("class").GetString();
            var node=Add("<ThingDef><defName>"+name+"</defName><thingClass>"+cls+"</thingClass><label>fixture</label><comps/></ThingDef>",package);
            if(item.TryGetProperty("comps",out var comps))foreach(var comp in comps.EnumerateArray())
            {
                string compClass=comp.GetString().Split(':')[0];if(compClass.Length==0)continue;
                var c=document.CreateElement("li");c.SetAttribute("Class",compClass);node.SelectSingleNode("comps").AppendChild(c);
            }
            Add(rule.OuterXml,"grechka.world.integration");
            if(rule.SelectSingleNode("retired")?.InnerText=="true")
            {
                retired++;
                variants+=5-int.Parse(rule.SelectSingleNode("minimumTier").InnerText);
            }
            else if(rule.SelectSingleNode("addHousing")?.InnerText=="true")
            { expected++;variants+=6-int.Parse(rule.SelectSingleNode("minimumTier").InnerText); }
            else excluded++;
        }
        ProfileVariantXmlHook.EntriesFromXml(document,owners);
        var families=ProfileVariants.Entries.Values.Select(e=>e.sourceName).Distinct().ToList();
        Check(families.Count==expected+retired,"Every one of "+expected+" active housing-bearing G01 families has a concrete minimum–T5 variant route; "+retired+" retired family keeps legacy Def names; "+excluded+" exclusions remain untouched");
        Check(ProfileVariants.Entries.Count==variants,"G01 contains exactly "+variants+" generated-and-original tier forms without fictitious lower tiers");
        Check(!ProfileVariants.Entries.ContainsKey("LightBall") && ProfileVariants.Entries.ContainsKey("GrechkaWorld_LightBall_T5_Building"),"LightBall remains vanilla while old tier names stay loadable");
        Check(ProfileVariants.Entries.Values.All(entry=>root.SelectSingleNode("ThingDef[defName='"+entry.name+"']")!=null),"Every reported catalogue route has an actual emitted ThingDef");
        Console.WriteLine("Coverage uses the local audited source-class catalogue, not a running game load.");
    }
}
