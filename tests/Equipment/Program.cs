using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using System.Collections;
using Grechka.Equipment;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

static void Check(bool ok, string message) { if(!ok) throw new Exception(message); Console.WriteLine("PASS " + message); }
var assemblies = new[] { typeof(ThingDef).Assembly, typeof(CombatDrone).Assembly };
Type Resolve(string name) => assemblies.SelectMany(a => a.GetTypes()).FirstOrDefault(t => t.FullName == name || t.Name == name);
void Schema(XElement node, Type type, bool root = false)
{
    string specific = (string)node.Attribute("Class");
    if (specific != null)
    {
        Type concrete = Resolve(specific);
        if (concrete == null || !type.IsAssignableFrom(concrete)) throw new Exception($"Invalid Class={specific} for {type}");
        type = concrete;
    }
    if (!node.HasElements) return;
    if (typeof(Def).IsAssignableFrom(type) && !root) return; // def reference
    if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
    {
        Type itemType = type.GetGenericArguments()[0];
        if (new[] {typeof(StatModifier),typeof(ThingDefCountClass),typeof(SkillRequirement)}.Contains(itemType)) return;
        foreach (var item in node.Elements()) Schema(item,itemType);
        return;
    }
    foreach (XElement child in node.Elements())
    {
        FieldInfo f = AccessTools.Field(type,child.Name.LocalName);
        if(f == null) throw new Exception($"Unknown XML field {type.Name}.{child.Name} in {node.Name}");
        Schema(child, f.FieldType);
    }
}
var root = args.Length == 0 ? Directory.GetCurrentDirectory() : args[0];
var defs = Directory.GetFiles(Path.Combine(root,"mods/GrechkaEquipment/Defs"),"*.xml").SelectMany(p=>XDocument.Load(p).Root.Elements()).ToList();
foreach (var def in defs) Schema(def, Resolve(def.Name.LocalName) ?? throw new Exception(def.Name.ToString()), true);
Check(true,"Every Equipment XML field and custom class exists in installed RimWorld 1.7");
var allKeys=defs.Where(d=>d.Element("defName")!=null).Select(d=>d.Name+":"+(string)d.Element("defName")).ToList();
Check(!defs.Where(d=>d.Name=="ThingDef").Any(d=>char.IsDigit(((string)d.Element("defName"))?.LastOrDefault()??'a')),"ThingDef names never end in digits");
Check(allKeys.Count==allKeys.Distinct().Count(),"Definition identities are unique per type");
XElement D(string name) => defs.First(d=>(string)d.Element("defName")==name);
foreach (var upgrade in defs.Where(d => ((string)d.Element("defName"))?.StartsWith("Grechka_Upgrade") == true))
{
    var product = upgrade.Element("products").Elements().Single();
    var target = D(product.Name.LocalName);
    int tier = product.Name.LocalName.Contains("T4") ? 4 : 5;
    string previousName = product.Name.LocalName.Replace("T" + tier, "T" + (tier - 1));
    var previous = D(previousName);
    var amounts = upgrade.Element("ingredients").Elements().ToDictionary(i => i.Element("filter").Element("thingDefs").Element("li").Value, i => decimal.Parse(i.Element("count").Value));
    string material = tier == 4 ? "Grechka_ArchoSubstrate" : "Grechka_GrechkaConcentrate";
    string component = "Grechka_ComponentT" + tier + "_Item";
    var sourceRecipes = Directory.GetFiles(Path.Combine(root, "mods/GrechkaIndustry/Defs/RecipeDefs"), "*.xml").SelectMany(p => XDocument.Load(p).Root.Elements());
    var componentRecipe = sourceRecipes.Single(d => (string)d.Element("defName") == "Grechka_MakeComponentT" + tier);
    decimal Added(string name) => decimal.Parse(componentRecipe.Element("ingredients").Elements().Single(i => i.Element("filter").Element("thingDefs").Element("li").Value == name).Element("count").Value);
    decimal Cost(XElement def, string name) => (decimal?)def.Element("costList").Element(name) ?? 0;
    Check((string)upgrade.Element("workerClass") == typeof(RecipeWorker_EquipmentUpgrade).FullName
        && decimal.Parse(product.Value) == 1 && amounts[previousName] == 1
        && !amounts.Keys.Any(k => k.StartsWith("Grechka_Component")),
        "Equipment conversion credits one previous item and exposes a one-for-one upgrade marker: " + (string)upgrade.Element("defName"));
    Check(amounts["Plasteel"] == Cost(target, "Plasteel") - Cost(previous, "Plasteel") + Cost(target, component) * Added("Plasteel")
        && amounts[material] == Cost(target, material) + Cost(target, component) * Added(material),
        "Upgrade charges only new electronics/material conversion after the previous item's credit: " + (string)upgrade.Element("defName"));
}
Check((int)D("Grechka_CryoBullet").Element("projectile").Element("damageAmountBase")==100 && (int)D("Grechka_CryoRifle").Element("verbs").Element("li").Element("burstShotCount")==10,"Cryo rifle preserves 10 projectiles × 100 damage");
var t2Gun = D("Grechka_SingleRifleT2_Item");
var t4Gun = D("Grechka_BurstLauncherT4_Item");
AccessTools.Field(typeof(Prefs), "data").SetValue(null, new PrefsData());
ProjectileProperties ProjectileXml(string name)
{
    var xml = new System.Xml.XmlDocument(); xml.LoadXml(D(name).Element("projectile").ToString());
    return DirectXmlToObject.ObjectFromXml<ProjectileProperties>(xml.DocumentElement, false);
}
var t2Projectile = ProjectileXml("Grechka_RifleBulletT2_Item");
var t4Projectile = ProjectileXml("Grechka_ExplosiveBulletT4_Item");
int BaseDamage(ProjectileProperties p) => (int)AccessTools.Field(typeof(ProjectileProperties), "damageAmountBase").GetValue(p);
Check(BaseDamage(t2Projectile) == 30 && (int)t2Gun.Element("verbs").Element("li").Element("burstShotCount") == 1
    && t2Projectile.explosionRadius == 0, "T2 uses a single 30-damage bullet without splash");
Check(BaseDamage(t4Projectile) == 50 && (int)t4Gun.Element("verbs").Element("li").Element("burstShotCount") == 3
    && Math.Abs(t4Projectile.explosionRadius - 1.9f) < .001f && !t4Projectile.explosionDamageFalloff,
    "T4 uses three explosive projectiles, each with 50 base area damage and radius 1.9");
Check(t2Projectile.speed == 100 && t4Projectile.speed == 100
    && t2Projectile.arcHeightFactor == 0 && t4Projectile.arcHeightFactor == 0
    && !t2Projectile.flyOverhead && !t4Projectile.flyOverhead && t4Projectile.explosionDelay == 0,
    "Native projectile parser confirms fast straight flight, no overhead arc and immediate T4 detonation");
Check((string)D("Grechka_ExplosiveBulletT4_Item").Element("thingClass") == typeof(Projectile_Explosive).Name
    && (string)D("Grechka_ExplosiveBulletT4_Item").Element("projectile").Element("damageDef") == "Bomb"
    && new[]{t2Gun,t4Gun}.All(d => (string)d.Element("verbs").Element("li").Element("verbClass") == typeof(Verb_Shoot).Name),
    "T4 uses native explosive impact and ordinary direct shooting rather than grenade throwing");
var nativeExplosiveImpact = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(Projectile_Explosive), "Impact"));
Check(nativeExplosiveImpact.Any(i => i.operand is MethodInfo m && m.DeclaringType == typeof(Projectile_Explosive) && m.Name == "Explode")
    && !nativeExplosiveImpact.Any(i => i.operand is MethodInfo m && m.DeclaringType == typeof(Bullet) && m.Name == "Impact"),
    "Installed native explosive impact detonates without adding a second direct Bullet hit");
foreach (var pair in new[]{(gun:t2Gun,tier:2),(gun:t4Gun,tier:4)})
    Check(pair.gun.Element("recipeMaker").Element("recipeUsers").Elements().Select(n=>n.Value).ToHashSet()
        .SetEquals(Enumerable.Range(pair.tier, 6-pair.tier).Select(t=>"Grechka_AssemblyBenchT"+t+"_Building")),
        "Weapon manufacturing uses exactly the matching and higher assembly tiers: T"+pair.tier);
Check((int)D("Grechka_CryoRifle").Element("verbs").Element("li").Element("range")==70,"Cryo range fixed to 70 cells");
Check((string)D("Grechka_CryoRifle").Element("verbs").Element("li").Element("verbClass")==typeof(Verb_CryoShoot).FullName
    && typeof(Verb_CryoShoot).IsSubclassOf(typeof(Verb_Shoot)),"Cryo flash uses the native shooting verb subclass rather than a simulated shot");
Check((string)D("Grechka_DroneBulletT5_Item").Element("thingClass")==typeof(DroneBulletT5).FullName
    && typeof(DroneBulletT5).IsSubclassOf(typeof(Bullet)),"T5 EMP visual retains native Bullet damage and extra EMP damage path");
Check((string)D("Grechka_CryoSlow").Element("hediffClass")==typeof(Hediff_CryoSlow).FullName
    && AccessTools.PropertyGetter(typeof(Hediff_CryoSlow),"StateIcon").GetBaseDefinition().DeclaringType==typeof(Hediff),
    "FreezeStatus binds the real native Hediff StateIcon virtual API");
foreach(var bulletType in new[]{typeof(CryoBullet),typeof(DroneBulletT5)})
{
    var code=PatchProcessor.GetOriginalInstructions(AccessTools.Method(bulletType,"Impact"));
    Check(code.Count(i=>i.operand is MethodInfo method && method.DeclaringType==typeof(Bullet) && method.Name=="Impact")==1,
        "Compiled "+bulletType.Name+" invokes native Bullet.Impact exactly once");
}
Check(File.Exists(Path.Combine(root,"mods/GrechkaEquipment/Textures/UI/FreezeStatus.png")),"Prepared native health status icon is packaged");
foreach(var name in new[]{"Grechka_CryoMuzzleFlash","Grechka_CryoImpactFlash","Grechka_DroneEMPFlash"})
{
    var effect=D(name);var texture=(string)effect.Element("graphicData").Element("texPath");
    Check(effect.Name=="FleckDef" && (string)effect.Attribute("ParentName")=="FleckBase"
        && File.Exists(Path.Combine(root,"mods/GrechkaEquipment/Textures",texture+".png")),
        "Native FleckBase and exact prepared PNG bound: "+name);
}
Check(D("Grechka_BrainChipT5_Hediff").Descendants("offset").Single().Value=="1" && D("Grechka_BrainChipMood").Descendants("baseMoodEffect").Single().Value=="16","Brain chip gives +100 percentage points and conditional +16 mood");
Check(defs.Count(d=>d.Name=="HediffDef" && ((string)d.Element("defName"))?.Contains("Mythic")==true)==32,"T4 covers all 32 distinct human body part types");
foreach (var tier in new[]{4,5})
 foreach(var d in defs.Where(d=>d.Name=="HediffDef" && ((string)d.Element("defName"))?.Contains((tier==4?"Mythic":"Grechka") + "ArmT"+tier)==true))
    Check((float)d.Element("addedPartProps").Element("partEfficiency")== (tier==4?3:6),"True added limb efficiency T"+tier);
var low = new HediffDef {defName="test_low",modExtensions=new(){new ImplantFamily {family="combat"}}};
var high = new HediffDef {defName="test_high",modExtensions=new(){new ImplantFamily {family="combat"}}};
var other = new HediffDef {defName="test_other",modExtensions=new(){new ImplantFamily {family="brain"}}};
Check(ImplantRules.SameFamily(low,high) && !ImplantRules.SameFamily(low,other) && !ImplantRules.SameFamily(new HediffDef(),new HediffDef()),"Replacement only matches explicit family; unrelated implants coexist");
var harmony=new Harmony("grechka.equipment.isolated-check");harmony.CreateClassProcessor(typeof(StableMindHook)).Patch();
Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(MentalBreaker),"TryDoRandomMoodCausedMentalBreak")).Prefixes.Any(p=>p.owner==harmony.Id),"Stability hook binds only the ordinary mood-break lottery");
Check(AccessTools.Method(typeof(IncidentWorker_Raid),"PostProcessSpawnedPawns").IsVirtual,"Raid customization uses vanilla post-generation hook");
Check(typeof(Recipe_RemoveProsthesis).IsSubclassOf(typeof(Recipe_RemoveBodyPart)),"Extracted replacement limbs use vanilla removal rather than regrowing natural anatomy");
harmony.Patch(AccessTools.Method(typeof(BodyPartDef), nameof(BodyPartDef.GetMaxHealth)), prefix:new HarmonyMethod(typeof(HealthFixture),nameof(HealthFixture.FullHealth)));
var part=new BodyPartRecord {def=new BodyPartDef {defName="test_structure",hitPoints=10}};
var childPart=new BodyPartRecord {def=new BodyPartDef {defName="test_organ",hitPoints=10}, parent=part};
var fixturePawn=new Pawn();
var set=new HediffSet(fixturePawn);
foreach(float efficiency in new[]{3f,6f})
{
    var structural=new Hediff_Implant {pawn=fixturePawn,def=new HediffDef {defName="test_structure_implant",stages=new(){new HediffStage {partEfficiencyOffset=efficiency-1}}},Part=part};
    set.hediffs.Clear();set.hediffs.Add(structural);
    Check(PawnCapacityUtility.CalculatePartEfficiency(set,part)==efficiency,"Native capacity formula gives structure "+efficiency*100+"%, actual="+PawnCapacityUtility.CalculatePartEfficiency(set,part)+", stage="+structural.CurStage.partEfficiencyOffset+", hp="+set.GetPartHealth(part)+"/"+part.def.GetMaxHealth(null));
    Check(PawnCapacityUtility.CalculatePartEfficiency(set,childPart)==1f,"Structural reconstruction preserves the nested organ's separate efficiency");
    set.hediffs.Clear();set.hediffs.Add(new Hediff_AddedPart {pawn=fixturePawn,def=new HediffDef {defName="test_complete_limb",addedPartProps=new AddedBodyPartProps {partEfficiency=efficiency}},Part=part});
    Check(PawnCapacityUtility.CalculatePartEfficiency(set,childPart)==efficiency,"Full limb supplies child function once without multiplicative stacking");
}
// Execute the family transaction while isolating only health side effects / map placement.
// Eligibility uses actual HediffSet missing/added-part traversal; the transaction order is real.
var game=(Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
var mapA=(Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));var mapB=(Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
AccessTools.Field(typeof(Game),"maps").SetValue(game,new List<Map>{mapA,mapB}); Current.Game=game;
var patient=new Pawn();patient.health=(Pawn_HealthTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_HealthTracker));
AccessTools.Field(typeof(Pawn_HealthTracker),"pawn").SetValue(patient.health,patient);patient.health.hediffSet=new HediffSet(patient);
AccessTools.Field(typeof(Thing),"mapIndexOrState").SetValue(patient,(sbyte)0);
var brain=new BodyPartRecord{def=new BodyPartDef{defName="test_brain"}};
var skull=new BodyPartRecord{def=new BodyPartDef{defName="test_skull"}};brain.parent=skull;
var oldItem=new ThingDef{defName="test_old_chip",thingClass=typeof(Thing)};low.spawnThingOnRemoved=oldItem;
var implantRecipe=new RecipeDef{defName="test_install_chip",addsHediff=high,appliedOnFixedBodyParts=new(){brain.def}};
var install=new Recipe_InstallFamily{recipe=implantRecipe};
Hediff Old()=>new Hediff_Implant{pawn=patient,def=low,Part=brain};
Hediff Unrelated()=>new Hediff_Implant{pawn=patient,def=other,Part=brain};
patient.health.hediffSet.hediffs.Add(Old());patient.health.hediffSet.hediffs.Add(Unrelated());
Check(ImplantRules.CanInstall(patient,implantRecipe,brain)&&ImplantRules.HasFamily(patient,high,brain),"An older chip permits replacement and already occupies its family for raid augmentation");
patient.health.hediffSet.hediffs.Add(new Hediff_AddedPart{pawn=patient,def=new HediffDef{defName="test_head_added"},Part=skull});
Check(!ImplantRules.CanInstall(patient,implantRecipe,brain),"A replaced parent invalidates a previously queued nested implant");
patient.health.hediffSet.hediffs.RemoveAt(2);
patient.health.hediffSet.hediffs.Add(new Hediff_MissingPart{pawn=patient,def=new HediffDef{defName="test_missing"},Part=skull});
Check(!ImplantRules.CanInstall(patient,implantRecipe,brain),"A lost parent invalidates nested surgery even without a missing Hediff directly on the child");
patient.health.hediffSet.hediffs.RemoveAt(2);
harmony.Patch(AccessTools.GetDeclaredMethods(typeof(Pawn_HealthTracker)).Single(m=>m.Name==nameof(Pawn_HealthTracker.AddHediff)&&m.GetParameters()[0].ParameterType==typeof(HediffDef)),prefix:new HarmonyMethod(typeof(SurgeryFixture),nameof(SurgeryFixture.Add)));
harmony.Patch(AccessTools.Method(typeof(Pawn_HealthTracker),nameof(Pawn_HealthTracker.RemoveHediff)),prefix:new HarmonyMethod(typeof(SurgeryFixture),nameof(SurgeryFixture.Remove)));
harmony.Patch(AccessTools.Method(typeof(ThingMaker),nameof(ThingMaker.MakeThing)),prefix:new HarmonyMethod(typeof(SurgeryFixture),nameof(SurgeryFixture.Make)));
var place=typeof(GenPlace).GetMethods().Single(m=>m.Name==nameof(GenPlace.TryPlaceThing)&&m.GetParameters().Length==8);
harmony.Patch(place,prefix:new HarmonyMethod(typeof(SurgeryFixture),nameof(SurgeryFixture.Place)));
SurgeryFixture.Reject=true;install.ApplyOnPawn(patient,brain,null,new(),null);
Check(patient.health.hediffSet.hediffs.Any(h=>h.def==low)&&SurgeryFixture.Returned.Count==0,"Rejected installation preserves the old implant and does not issue a free returned chip");
SurgeryFixture.Reject=false;install.ApplyOnPawn(patient,brain,null,new(),null);
Check(patient.health.hediffSet.hediffs.Count(h=>h.def==high)==1&&!patient.health.hediffSet.hediffs.Any(h=>h.def==low)
 &&patient.health.hediffSet.hediffs.Any(h=>h.def==other)&&SurgeryFixture.Returned.SequenceEqual(new[]{oldItem}),"Successful family replacement retains unrelated chips and returns exactly one old item");
Check(SurgeryFixture.AddBeforeRemove,"Replacement is installed before removing the old implant, without a transient vital-part gap");
install.ApplyOnPawn(patient,brain,null,new(),null);
Check(SurgeryFixture.Returned.Count==1&&patient.health.hediffSet.hediffs.Count(h=>h.def==high)==1,"Repeated same-tier operation neither stacks the chip nor returns another item");
var tracker=(Pawn_ApparelTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_ApparelTracker));tracker.pawn=patient;
var apparelOwner=new ThingOwner<Apparel>(tracker,false);var drone=new CombatDrone();
AccessTools.Field(typeof(Thing),"holdingOwner").SetValue(drone,apparelOwner);
var canTick=AccessTools.Method(typeof(CombatDrone),"CanTickOn");
bool CanTick(Map m)=>(bool)canTick.Invoke(drone,new object[]{m});
Check(CanTick(mapA)&&!CanTick(mapB),"Drone cache only ticks apparel worn on its own map");
AccessTools.Field(typeof(Thing),"mapIndexOrState").SetValue(patient,(sbyte)1);
Check(!CanTick(mapA)&&CanTick(mapB),"Map transfer immediately invalidates the previous map's drone cache before its refresh");
AccessTools.Field(typeof(Thing),"holdingOwner").SetValue(drone,null);
Check(!CanTick(mapA)&&!CanTick(mapB),"Unequipped drone cannot fire from any stale map cache");

Console.WriteLine("Equipment checks completed. No game session launched.");

static class HealthFixture
{
    // No PawnGenerator/graphics are needed: only the full health input is supplied by the fixture.
    public static bool FullHealth(ref float __result) { __result=10f;return false; }
}

static class SurgeryFixture
{
    public static bool Reject,AddBeforeRemove;
    public static List<ThingDef> Returned=new();
    public static bool Add(Pawn_HealthTracker __instance,HediffDef def,BodyPartRecord part,ref Hediff __result)
    { if(Reject){__result=null;return false;} __result=new Hediff_Implant{pawn=(Pawn)AccessTools.Field(typeof(Pawn_HealthTracker),"pawn").GetValue(__instance),def=def,Part=part};__instance.hediffSet.hediffs.Add(__result);return false; }
    public static bool Remove(Pawn_HealthTracker __instance,Hediff hediff)
    { AddBeforeRemove=__instance.hediffSet.hediffs.Any(h=>h!=hediff&&h.Part==hediff.Part&&ImplantRules.SameFamily(h.def,hediff.def));__instance.hediffSet.hediffs.Remove(hediff);return false; }
    public static bool Make(ThingDef def,ref Thing __result){__result=new Thing{def=def};return false;}
    public static bool Place(Thing thing,ref bool __result){Returned.Add(thing.def);__result=true;return false;}
}
