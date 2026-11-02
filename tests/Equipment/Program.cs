using System.Reflection;
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
Check((int)D("Grechka_CryoBullet").Element("projectile").Element("damageAmountBase")==100 && (int)D("Grechka_CryoRifle").Element("verbs").Element("li").Element("burstShotCount")==10,"Cryo rifle preserves 10 projectiles × 100 damage");
Check((int)D("Grechka_CryoRifle").Element("verbs").Element("li").Element("range")==70,"Cryo range fixed to 70 cells");
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
Console.WriteLine("Equipment checks completed. No game session launched.");

static class HealthFixture
{
    // No PawnGenerator/graphics are needed: only the full health input is supplied by the fixture.
    public static bool FullHealth(ref float __result) { __result=10f;return false; }
}
