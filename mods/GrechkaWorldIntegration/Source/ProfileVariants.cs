using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grechka.WorldIntegration
{
    public sealed class VariantEntry
    {
        public string sourceName, name, ruleName;
        public int tier, minimum;
        public bool worktable, factory;
        public string UpgradeRecipe => "GrechkaWorld_Upgrade_" + sourceName + "_T" + (tier + 1);
        public string TargetName => "GrechkaWorld_" + sourceName + "_T" + (tier + 1) + "_Building";
    }

    public static class ProfileVariants
    {
        public static readonly Dictionary<string, VariantEntry> Entries = new Dictionary<string, VariantEntry>();
        private static readonly Dictionary<ThingDef, List<StatModifier>> stats = new Dictionary<ThingDef, List<StatModifier>>();
        public static readonly float[] Speed = { 1, 1, 2, 5, 15, 60 };
        public static readonly float[] Power = { 1, 1, 2.5f, 6, 20, 80 };
        public static readonly float[] Discount = { 0, 0, .1f, .2f, .35f, .5f };
        public static readonly float[] Output = { 0, 0, .1f, .2f, .5f, 1 };
        public static VariantEntry Entry(ThingDef def) => def != null && Entries.TryGetValue(def.defName, out VariantEntry value) ? value : null;
        public static bool IsRetired(VariantEntry entry) => !string.IsNullOrEmpty(entry?.ruleName)
            && DefDatabase<MachineProfileRuleDef>.GetNamedSilentFail(entry.ruleName)?.retired == true;
        public static bool IsUpgrade(RecipeDef recipe) => recipe?.defName.StartsWith("GrechkaWorld_Upgrade_", StringComparison.Ordinal) == true;
        public static bool IsEquipmentUpgrade(RecipeDef recipe) => recipe?.workerClass?.FullName == "Grechka.Equipment.RecipeWorker_EquipmentUpgrade";
        private static List<StatModifier> CopyStats(List<StatModifier> source) => source?.Select(s => new StatModifier { stat = s.stat, value = s.value }).ToList() ?? new List<StatModifier>();

        public static void Apply(WorldProfileState state, bool approve)
        {
            foreach (VariantEntry entry in Entries.Values)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(entry.name);
                ThingDef original = DefDatabase<ThingDef>.GetNamedSilentFail(entry.sourceName);
                MachineProfileRuleDef rule = DefDatabase<MachineProfileRuleDef>.GetNamedSilentFail(entry.ruleName);
                if (def == null || original == null || rule == null) continue;
                if (rule.retired) continue;
                if (!stats.ContainsKey(def)) stats[def] = CopyStats(stats.TryGetValue(original, out List<StatModifier> originalStats) ? originalStats : original.statBases);
                def.statBases = CopyStats(stats[def]);
                if (entry.name != entry.sourceName)
                {
                    string key = rule.Signature + "/variantT" + entry.tier;
                    ThingDef housing = DefDatabase<ThingDef>.GetNamedSilentFail("Grechka_HousingT" + entry.tier + "_Item");
                    if (approve && WorldProfile.IsApplied(original) && MaterialDictionary.BelongsTo(housing, "grechka.industry") && !state.approvedMachines.Contains(key)) state.approvedMachines.Add(key);
                    if (!state.approvedMachines.Contains(key) || !WorldProfile.IsApplied(original) || housing == null) continue;
                    def.costList = WorldProfile.BaselineCosts(original);
                    def.costList.Add(new ThingDefCountClass(housing, 1));
                    WorldProfile.ApplyDifficultyHousing(def, original, housing);
                    WorldProfile.AppliedTiers[def.defName] = entry.tier;
                    if(entry.worktable || typeof(IBillGiver).IsAssignableFrom(original.thingClass))
                    {
                        // recipeUsers links target the source Def, not its XML-derived variants.
                        // Special bill givers need the same providers without opting into worktable bonuses.
                        def.recipes = original.AllRecipes.Where(r => !IsUpgrade(r)).ToList();
                        foreach (RecipeDef upgrade in DefDatabase<RecipeDef>.AllDefsListForReading.Where(r => r.recipeUsers?.Contains(def) == true && IsUpgrade(r))) def.recipes.Add(upgrade);
                        AccessTools.Field(typeof(ThingDef), "allRecipesCached").SetValue(def, null);
                    }
                }
                if (!WorldProfile.IsApplied(def) || !entry.worktable) continue;
                StatModifier speed = def.statBases.FirstOrDefault(s => s.stat == StatDefOf.WorkTableWorkSpeedFactor);
                if (speed == null) def.statBases.Add(new StatModifier { stat = StatDefOf.WorkTableWorkSpeedFactor, value = Speed[entry.tier] });
                else speed.value *= Speed[entry.tier];
            }
            StatDefOf.WorkTableWorkSpeedFactor?.Worker.DeleteStatCache();
            WorldProfile.Report.Add(WorldText.Get("GrechkaWorldIntegration_ba704a00a6bf", "Machine variants: ") + Entries.Values.Count(e => e.name != e.sourceName && WorldProfile.AppliedTiers.ContainsKey(e.name)) + WorldText.Get("GrechkaWorldIntegration_89d1c68752eb", " applied."));
        }

        public static void Restore()
        {
            foreach (var pair in stats) pair.Key.statBases = CopyStats(pair.Value);
            StatDefOf.WorkTableWorkSpeedFactor?.Worker.DeleteStatCache();
            foreach (VariantEntry entry in Entries.Values.Where(e => e.name != e.sourceName))
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(entry.name);
                ThingDef original = DefDatabase<ThingDef>.GetNamedSilentFail(entry.sourceName);
                if (def != null && original != null)
                {
                    def.costList = WorldProfile.BaselineCosts(original);
                    def.costListForDifficulty = WorldProfile.BaselineDifficultyCosts(original);
                }
            }
        }
    }

    // Runs after all mod patches, before inheritance/Def parsing. The asset map proves the original package identity.
    [HarmonyPatch(typeof(LoadedModManager), "ParseAndProcessXML")]
    public static class ProfileVariantXmlHook
    {
        public static void Prefix(XmlDocument xmlDoc, Dictionary<XmlNode, LoadableXmlAsset> assetlookup)
        {
            EntriesFromXml(xmlDoc, assetlookup);
            ResearchTree.ApplyXml(xmlDoc, assetlookup);
        }
        public static void EntriesFromXml(XmlDocument document, Dictionary<XmlNode, LoadableXmlAsset> owners)
        {
            ProfileVariants.Entries.Clear();
            XmlElement root = document.DocumentElement;
            // No implicit Industry dependency: authoritative housings must be present in this load.
            bool IndustryHousing(int tier)
            {
                XmlNode housing = root.SelectSingleNode("ThingDef[defName='Grechka_HousingT" + tier + "_Item']");
                return housing != null && owners.TryGetValue(housing, out LoadableXmlAsset asset)
                    && string.Equals(asset.mod?.PackageIdPlayerFacing, "grechka.industry", StringComparison.OrdinalIgnoreCase);
            }
            if (!Enumerable.Range(1, 5).All(IndustryHousing)) return;
            var named = root.ChildNodes.Cast<XmlNode>().Where(n => n.Attributes?["Name"] != null).GroupBy(n => n.Attributes["Name"].Value).ToDictionary(g => g.Key, g => g.Last());
            string Inherited(XmlNode node, string field)
            {
                var seen = new HashSet<XmlNode>();
                while (node != null && seen.Add(node))
                {
                    XmlNode value = node.SelectSingleNode(field);
                    if (value != null) return value.InnerText;
                    string parent = node.Attributes?["ParentName"]?.Value;
                    node = parent != null && named.TryGetValue(parent, out XmlNode found) ? found : null;
                }
                return null;
            }
            bool HasInheritedComp(XmlNode node,string className)
            {
                var seen=new HashSet<XmlNode>();
                while(node!=null&&seen.Add(node))
                {
                    XmlNode comps=node.SelectSingleNode("comps");
                    if(comps?.SelectSingleNode("li[@Class='"+className+"']")!=null)return true;
                    if(string.Equals(comps?.Attributes?["Inherit"]?.Value,"False",StringComparison.OrdinalIgnoreCase))return false;
                    string parent=node.Attributes?["ParentName"]?.Value;
                    node=parent!=null&&named.TryGetValue(parent,out XmlNode found)?found:null;
                }
                return false;
            }
            void Set(XmlNode parent, string field, string value)
            {
                XmlNode node = parent.SelectSingleNode(field) ?? parent.AppendChild(document.CreateElement(field)); node.InnerText = value;
            }
            foreach (XmlNode rule in root.SelectNodes("Grechka.WorldIntegration.MachineProfileRuleDef").Cast<XmlNode>().ToList())
            {
                if (rule.SelectSingleNode("addHousing")?.InnerText != "true") continue;
                string source = rule.SelectSingleNode("targetDefName")?.InnerText;
                XmlNode original = root.SelectSingleNode("ThingDef[defName='" + source + "']");
                if (original == null || !owners.TryGetValue(original, out LoadableXmlAsset owner) || !string.Equals(owner.mod?.PackageIdPlayerFacing, rule.SelectSingleNode("packageId")?.InnerText, StringComparison.OrdinalIgnoreCase)) continue;
                string kind = Inherited(original, "thingClass");
                // Unknown subclasses may have extra state or hard-coded Def comparisons; they need explicit adapters.
                bool worktable = kind == "Building_WorkTable" || kind == "Verse.Building_WorkTable" || kind == "RimWorld.Building_WorkTable"
                    || kind == "Building_WorkTable_HeatPush" || kind == "RimWorld.Building_WorkTable_HeatPush";
                bool factory = (kind == "Building" || kind == "Verse.Building")
                    && HasInheritedComp(original,"PipeSystem.CompProperties_AdvancedResourceProcessor");
                bool utility = UtilityTierAdapter.SupportsXmlClass(kind);
                bool specialized = SpecialTierAdapter.SupportsXmlClass(kind);
                if (!worktable && !factory && !utility && !specialized) continue;
                int minimum = int.Parse(rule.SelectSingleNode("minimumTier").InnerText);
                string ruleName = rule.SelectSingleNode("defName").InnerText;
                string baseName = original.Attributes?["Name"]?.Value;
                if (baseName == null) { baseName = "GrechkaWorld_Base_" + source; ((XmlElement)original).SetAttribute("Name", baseName); named[baseName] = original; }
                if (rule.SelectSingleNode("retired")?.InnerText == "true")
                {
                    // Keep historical ThingDef names loadable, without altering or offering the vanilla original.
                    for (int tier = minimum + 1; tier <= 5; tier++)
                    {
                        string name = "GrechkaWorld_" + source + "_T" + tier + "_Building";
                        ProfileVariants.Entries[name] = new VariantEntry { sourceName = source, name = name, ruleName = ruleName, tier = tier, minimum = minimum };
                        if (root.SelectSingleNode("ThingDef[defName='" + name + "']") != null) continue;
                        var clone = document.CreateElement("ThingDef"); clone.SetAttribute("ParentName", baseName);
                        Set(clone, "defName", name); Set(clone, "label", Inherited(original, "label") + " (legacy T" + tier + ")");
                        root.AppendChild(clone); owners[clone] = owner;
                    }
                    continue;
                }
                string groupName = "GrechkaWorld_Group_" + source;
                if (root.SelectSingleNode("DesignatorDropdownGroupDef[defName='" + groupName + "']") == null)
                { var group = document.CreateElement("DesignatorDropdownGroupDef"); Set(group,"defName",groupName);root.AppendChild(group);owners[group]=owner; }
                Set(original,"designatorDropdown",groupName);
                if(worktable)
                {
                    if (Inherited(original,"tickerType") == null || Inherited(original,"tickerType") == "Never") Set(original,"tickerType","Rare");
                    XmlNode comps = original.SelectSingleNode("comps") ?? original.AppendChild(document.CreateElement("comps"));
                    if (comps.SelectSingleNode("li[@Class='Grechka.WorldIntegration.CompProperties_ProfileUpgrade']") == null)
                    { var comp=document.CreateElement("li");comp.SetAttribute("Class","Grechka.WorldIntegration.CompProperties_ProfileUpgrade");comps.AppendChild(comp); }
                }
                if(!worktable)
                {
                    XmlNode comps=original.SelectSingleNode("comps")??original.AppendChild(document.CreateElement("comps"));
                    if(comps.SelectSingleNode("li[@Class='Grechka.WorldIntegration.CompProperties_FactoryUpgrade']")==null)
                    {var comp=document.CreateElement("li");comp.SetAttribute("Class","Grechka.WorldIntegration.CompProperties_FactoryUpgrade");comps.AppendChild(comp);}
                }
                XmlNode tierComps=original.SelectSingleNode("comps")??original.AppendChild(document.CreateElement("comps"));
                if(tierComps.SelectSingleNode("li[@Class='Grechka.WorldIntegration.CompProperties_ProfileTierInfo']")==null)
                {var comp=document.CreateElement("li");comp.SetAttribute("Class","Grechka.WorldIntegration.CompProperties_ProfileTierInfo");tierComps.AppendChild(comp);}
                for(int tier=minimum;tier<=5;tier++)
                {
                    string name=tier==minimum?source:"GrechkaWorld_"+source+"_T"+tier+"_Building";
                    ProfileVariants.Entries[name]=new VariantEntry{sourceName=source,name=name,ruleName=ruleName,tier=tier,minimum=minimum,worktable=worktable,factory=factory};
                    if(tier>minimum && root.SelectSingleNode("ThingDef[defName='"+name+"']")==null)
                    {
                        var clone=document.CreateElement("ThingDef");clone.SetAttribute("ParentName",baseName);Set(clone,"defName",name);Set(clone,"label",Inherited(original,"label")+" (T"+tier+")");
                        // Inherit the source machine's Architect category and tier dropdown together.
                        root.AppendChild(clone);owners[clone]=owner;
                    }
                    if(tier>=5||!worktable)continue;
                    string housing="Grechka_HousingT"+(tier+1)+"_Item";
                    XmlNode housingRecipe=root.SelectNodes("RecipeDef").Cast<XmlNode>().FirstOrDefault(r=>r.SelectSingleNode("products/"+housing)!=null);
                    if(housingRecipe==null || root.SelectSingleNode("RecipeDef[defName='GrechkaWorld_Upgrade_"+source+"_T"+(tier+1)+"']")!=null)continue;
                    var upgrade=document.CreateElement("RecipeDef");Set(upgrade,"defName","GrechkaWorld_Upgrade_"+source+"_T"+(tier+1));Set(upgrade,"label","upgrade this machine to T"+(tier+1));
                    Set(upgrade,"description","Upgrade in place. The previous-tier housing and original machine count towards the cost. Material, hit points, fuel and bills are preserved.");
                    Set(upgrade,"jobString","Upgrading machine.");Set(upgrade,"workAmount","4000");Set(upgrade,"workSpeedStat","GeneralLaborSpeed");Set(upgrade,"workSkill","Crafting");
                    XmlNode ingredients=upgrade.AppendChild(document.CreateElement("ingredients"));XmlNode fixedFilter=upgrade.AppendChild(document.CreateElement("fixedIngredientFilter"));XmlNode allowed=fixedFilter.AppendChild(document.CreateElement("thingDefs"));
                    foreach(XmlNode ingredient in housingRecipe.SelectNodes("ingredients/li"))
                    {
                        string material=ingredient.SelectSingleNode("filter/thingDefs/li")?.InnerText;
                        if(material=="Grechka_HousingT"+tier+"_Item")continue;
                        ingredients.AppendChild(ingredient.CloneNode(true));
                        foreach(XmlNode item in ingredient.SelectNodes("filter/thingDefs/li"))allowed.AppendChild(item.CloneNode(true));
                    }
                    XmlNode users=upgrade.AppendChild(document.CreateElement("recipeUsers"));Set(users,"li",name);root.AppendChild(upgrade);owners[upgrade]=owner;
                }
            }
        }
    }

    [HarmonyPatch(typeof(Designator_Build), "get_Visible")]
    public static class ProfileVariantVisibilityHook
    {
        public static void Postfix(Designator_Build __instance, ref bool __result)
        {
            var def=__instance.PlacingDef as ThingDef;VariantEntry entry=ProfileVariants.Entry(def);
            if (entry != null && entry.name != entry.sourceName && ProfileVariants.IsRetired(entry))
            { __result = false; return; }
            if(entry!=null&&entry.name!=entry.sourceName&&!WorldProfile.IsApplied(def))__result=false;
        }
    }

    [HarmonyPatch(typeof(Designator_Build), nameof(Designator_Build.CanDesignateCell))]
    public static class ProfileVariantPlacementHook
    {
        public static bool Prefix(Designator_Build __instance, ref AcceptanceReport __result)
        {
            ThingDef def = __instance.PlacingDef as ThingDef;
            VariantEntry entry = ProfileVariants.Entry(def);
            if (entry != null && entry.name != entry.sourceName && ProfileVariants.IsRetired(entry))
            { __result = WorldText.Get("GrechkaWorldIntegration_c06d4b9349df", "This legacy variant is retained for loading old colonies."); return false; }
            if (entry == null || entry.name == entry.sourceName || WorldProfile.IsApplied(def)) return true;
            __result = WorldText.Get("GrechkaWorldIntegration_992255432d82", "Apply the Grechka World profile in the mod settings first.");
            return false;
        }
    }

    [HarmonyPatch(typeof(RecipeDef), "get_AvailableNow")]
    public static class ProfileUpgradeAvailabilityHook
    {
        public static void Postfix(RecipeDef __instance, ref bool __result)
        {
            if(!ProfileVariants.IsUpgrade(__instance))return;
            bool ready=__instance.recipeUsers?.Any(user=>WorldProfile.IsApplied(user)
                && ProfileVariants.Entry(user) is VariantEntry entry
                && WorldProfile.IsApplied(DefDatabase<ThingDef>.GetNamedSilentFail(entry.TargetName)))??false;
            if(!ready)__result=false;
        }
    }

    public sealed class CompProperties_ProfileUpgrade : CompProperties
    { public CompProperties_ProfileUpgrade(){compClass=typeof(CompProfileUpgrade);} }

    public sealed class CompProfileUpgrade : ThingComp
    {
        private bool ready;
        private string completedBillId;
        private int retry;
        private Dictionary<string,float> carry=new Dictionary<string,float>();
        private VariantEntry Entry=>ProfileVariants.Entry(parent.def);
        public override void PostExposeData(){Scribe_Values.Look(ref ready,"grechkaProfileUpgradeReady");Scribe_Values.Look(ref completedBillId,"grechkaProfileCompletedBill");Scribe_Collections.Look(ref carry,"grechkaProfileOutputCarry",LookMode.Value,LookMode.Value);carry??=new Dictionary<string,float>();}
        public void Mark(Bill_Production bill){if(Entry!=null&&bill.recipe.defName==Entry.UpgradeRecipe){ready=true;completedBillId=bill.GetUniqueLoadID();}}
        public int Extra(RecipeDef recipe,Thing product)
        {
            int tier=WorldProfile.GetTier(parent);if(tier<=1||ProfileVariants.IsUpgrade(recipe))return 0;
            string key=recipe.defName+"/"+product.def.defName;float amount=(carry.TryGetValue(key,out float previous)?previous:0)+product.stackCount*ProfileVariants.Output[tier];int extra=(int)Math.Floor(amount+.00001f);carry[key]=amount-extra;return extra;
        }
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            VariantEntry entry=Entry;Building_WorkTable bench=parent as Building_WorkTable;
            if(entry==null||entry.tier>=5||ready||bench==null||!WorldProfile.IsApplied(parent.def))yield break;
            RecipeDef recipe=DefDatabase<RecipeDef>.GetNamedSilentFail(entry.UpgradeRecipe);if(recipe==null||!recipe.AvailableNow||bench.BillStack.Bills.Any(b=>b.recipe==recipe))yield break;
            yield return new Command_Action{defaultLabel=WorldText.Get("GrechkaWorldIntegration_392bededaba5", "Upgrade to T")+(entry.tier+1),defaultDesc=WorldText.Get("GrechkaWorldIntegration_f4918772c9e0", "Create an upgrade bill. The previous machine and housing count towards the cost. A pawn will bring only the additional materials."),icon=TexCommand.Install,action=()=>{var bill=new Bill_Production(recipe,null){repeatMode=BillRepeatModeDefOf.RepeatCount,repeatCount=1};bench.BillStack.AddBill(bill);}};
        }
        public override void CompTick(){TryUpgrade();}
        public override void CompTickRare(){TryUpgrade();}
        private void TryUpgrade()
        {
            if(!ready||!parent.Spawned||Find.TickManager.TicksGame<retry)return;
            retry=Find.TickManager.TicksGame+250;
            var old=parent as Building_WorkTable;var entry=Entry;if(old==null||entry==null)return;
            ThingDef target=DefDatabase<ThingDef>.GetNamedSilentFail(entry.TargetName);if(target==null||!WorldProfile.IsApplied(target))return;
            if(!FactoryUpgradeSafety.Compatible(old,target)||FactoryUpgradeSafety.Problem(old)!=null)return;
            ThingDef previous=old.def;var oldProps=old.AllComps.Select(c=>c.props).ToList();
            Map map=old.Map;IntVec3 pos=old.Position;Rot4 rot=old.Rotation;int oldHp=old.HitPoints;float hp=(float)oldHp/old.MaxHitPoints;
            try
            {
                // Keep object identity, all native/mod fields, BillStack and output carry intact.
                old.DeSpawn(DestroyMode.WillReplace);
                FactoryUpgradeSafety.SetDefinition(old,target);
                GenSpawn.Spawn(old,pos,map,rot,WipeMode.Vanish,true,false);
                old.HitPoints=Math.Max(1,(int)Math.Round(old.MaxHitPoints*hp));
                Bill completed=old.BillStack.Bills.FirstOrDefault(b=>b.GetUniqueLoadID()==completedBillId);
                if(completed!=null)old.BillStack.Delete(completed);
                ready=false;completedBillId=null;
            }
            catch(Exception ex)
            {
                if(old.Spawned)old.DeSpawn(DestroyMode.WillReplace);
                old.def=previous;
                WorldProfile.InvalidateThingStats(old);
                for(int i=0;i<old.AllComps.Count&&i<oldProps.Count;i++)old.AllComps[i].props=oldProps[i];
                AccessTools.Field(typeof(Thing),"graphicInt")?.SetValue(old,null);
                if(!old.Spawned)GenSpawn.Spawn(old,pos,map,rot,WipeMode.Vanish,true,false);
                old.HitPoints=oldHp;
                Log.Error(WorldText.Get("GrechkaWorldIntegration_54522b09cc40", "[Grechka World] Upgrade postponed. Original machine and bills preserved: ")+ex);
            }
        }
    }

    [HarmonyPatch(typeof(Bill_Production),nameof(Bill_Production.Notify_IterationCompleted))]
    public static class ProfileUpgradeCompletionHook
    {public static void Postfix(Bill_Production __instance){(__instance.billStack?.billGiver as ThingWithComps)?.GetComp<CompProfileUpgrade>()?.Mark(__instance);}}

    [HarmonyPatch(typeof(RecipeWorker),nameof(RecipeWorker.GetIngredientCount))]
    public static class ProfileIngredientDiscountHook
    {public static void Postfix(Bill bill,ref float __result){var machine=bill?.billStack?.billGiver as ThingWithComps;int tier=WorldProfile.GetTier(machine);if(tier>1&&machine?.GetComp<CompProfileUpgrade>()!=null&&!ProfileVariants.IsUpgrade(bill.recipe)&&!ProfileVariants.IsEquipmentUpgrade(bill.recipe))__result*=1-ProfileVariants.Discount[tier];}}

    [HarmonyPatch(typeof(GenRecipe),nameof(GenRecipe.MakeRecipeProducts))]
    public static class ProfileProductBonusHook
    {
        public static void Postfix(RecipeDef recipeDef,IBillGiver billGiver,ref IEnumerable<Thing> __result)
        {var comp=(billGiver as ThingWithComps)?.GetComp<CompProfileUpgrade>();if(comp!=null&&__result!=null&&!ProfileVariants.IsEquipmentUpgrade(recipeDef))__result=Products(__result,recipeDef,comp);}
        private static IEnumerable<Thing> Products(IEnumerable<Thing> original,RecipeDef recipe,CompProfileUpgrade comp)
        {
            foreach(Thing product in original)
            {
                Thing inner = product is MinifiedThing packed ? packed.InnerThing : product;
                // Returned ingredients, pawns and arbitrary containers cannot be duplicated by a recipe adapter.
                bool fresh = inner != null && recipe.products?.Any(p => p.thingDef == inner.def) == true;
                int extra = fresh ? comp.Extra(recipe,product) : 0;
                if(product.def.stackLimit>1)
                {
                    product.stackCount+=extra;
                    while(product.stackCount>product.def.stackLimit)yield return product.SplitOff(product.def.stackLimit);
                    yield return product;
                }
                else
                {
                    yield return product;
                    for(int i=0;i<extra;i++)
                    {
                        Thing created=ThingMaker.MakeThing(inner.def,inner.Stuff);
                        if(inner.TryGetQuality(out QualityCategory quality))created.TryGetComp<CompQuality>()?.SetQuality(quality,ArtGenerationContext.Colony);
                        yield return product is MinifiedThing ? created.MakeMinified() : created;
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(CompPowerTrader),"get_PowerOutput")]
    public static class ProfilePowerHook
    {
        public static bool HasBenefit(ThingDef def)
        {
            VariantEntry entry=ProfileVariants.Entry(def);
            return entry!=null&&(entry.worktable||entry.factory||SpecialTierAdapter.HasPowerBenefit(def));
        }
        public static void Postfix(CompPowerTrader __instance,ref float __result){int tier=WorldProfile.GetTier(__instance.parent);if(tier>0&&__result<0&&HasBenefit(__instance.parent.def)&&!(__instance.parent.def.modContentPack?.PackageIdPlayerFacing.StartsWith("grechka.",StringComparison.OrdinalIgnoreCase)??false))__result*=ProfileVariants.Power[tier];}}
}
