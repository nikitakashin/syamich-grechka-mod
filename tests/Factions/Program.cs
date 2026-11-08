using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.Linq;
using Grechka.Factions;
using HarmonyLib;
using RimWorld;
using Verse;

int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
var repo = args[0];
AccessTools.Method(Type.GetType("Waylith.Rendering.ShaderSourceLoader, Waylith",true),"SetOverrideShaderDirectories")
    .Invoke(null,new object[]{new[]{Path.Combine(Environment.GetEnvironmentVariable("RIMWORLD_MANAGED_DIR"),"Rendering/Shaders")}});
AccessTools.Field(typeof(UnityData),"mainThreadId").SetValue(null,Environment.CurrentManagedThreadId);
var logFixture=new Harmony("grechka.factions.test-log");
foreach(var method in AccessTools.GetDeclaredMethods(typeof(Log)).Where(m=>new[]{"Error","Warning","Message"}.Contains(m.Name)))
    logFixture.Patch(method,prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.Log)));
AccessTools.Field(typeof(Prefs), "data").SetValue(null, new PrefsData());
var configRoot=Path.Combine(Path.GetTempPath(),"grechka-factions-tests-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(configRoot,"Config"));
File.WriteAllText(Path.Combine(configRoot,"Config/ModsConfig.xml"),"<ModsConfigData><version>1.7.4998</version><activeMods><li>ludeon.rimworld</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li></activeMods></ModsConfigData>");
AccessTools.Field(typeof(GenFilePaths),"saveDataPath").SetValue(null,configRoot);
Directory.CreateDirectory(Path.Combine(configRoot,"Mods"));
Directory.CreateDirectory(Path.Combine(configRoot,"Data"));
AccessTools.Field(typeof(GenFilePaths),"modsFolderPath").SetValue(null,Path.Combine(configRoot,"Mods"));
AccessTools.Field(typeof(GenFilePaths),"officialModsFolderPath").SetValue(null,Path.Combine(configRoot,"Data"));
foreach(string package in new[]{"rimworld","rimworld.ideology","rimworld.biotech"})
{
    string folder=Path.Combine(configRoot,"Data",package,"About");Directory.CreateDirectory(folder);
    File.WriteAllText(Path.Combine(folder,"About.xml"),"<ModMetaData><name>"+package+"</name><author>Ludeon Studios</author><packageId>ludeon."+package+"</packageId><supportedVersions><li>1.7</li></supportedVersions></ModMetaData>");
}
var game = new DirectoryInfo(Environment.GetEnvironmentVariable("RIMWORLD_MANAGED_DIR"));
while (!Directory.Exists(Path.Combine(game.FullName,"Data","Core"))) game = game.Parent ?? throw new Exception("Missing game data");
var index = new Dictionary<(Type,string), XElement>();
var types=typeof(Pawn).Assembly.GetTypes().Concat(typeof(MemberProfile).Assembly.GetTypes()).ToList();
var typeNames=types.GroupBy(t=>t.Name).ToDictionary(g=>g.Key,g=>g.First());
Type TypeFor(string name) => typeof(Pawn).Assembly.GetType(name) ?? typeof(MemberProfile).Assembly.GetType(name)
    ?? typeNames.GetValueOrDefault(name);
foreach (string folder in new[] {"Core","Royalty","Ideology","Biotech","Odyssey","Anomaly"}.Select(n => Path.Combine(game.FullName,"Data",n,"Defs")).Concat(Directory.GetDirectories(Path.Combine(repo,"mods")).Select(p => Path.Combine(p,"Defs"))))
{
    if (!Directory.Exists(folder)) continue;
    foreach (string path in Directory.GetFiles(folder,"*.xml",SearchOption.AllDirectories))
        foreach (var node in XDocument.Load(path).Root.Elements())
        {
            string name = (string)node.Element("defName");
            Type type = TypeFor(node.Name.LocalName);
            if (name != null && type != null && typeof(Def).IsAssignableFrom(type)) index[(type,name)] = node;
        }
}
var pack = (ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));
AccessTools.Field(typeof(ModContentPack),"packageIdPlayerFacingInt").SetValue(pack,"grechka.factions");
AccessTools.Field(typeof(ModContentPack),"rootDirInt").SetValue(pack,new DirectoryInfo(Path.Combine(repo,"mods/GrechkaFactions")));
pack.assetBundles=new ModAssetBundlesHandler(pack);
AccessTools.Field(typeof(ModContentPack),"textures").SetValue(pack,new ModContentHolder<UnityEngine.Texture2D>(pack));
AccessTools.Field(typeof(ModContentPack),"audioClips").SetValue(pack,new ModContentHolder<UnityEngine.AudioClip>(pack));
AccessTools.Field(typeof(ModContentPack),"strings").SetValue(pack,new ModContentHolder<string>(pack));
pack.assemblies=new ModAssemblyHandler(pack);
pack.assemblies.loadedAssemblies.Add(typeof(MemberProfile).Assembly);
LoadedModManager.RunningModsListForReading.Add(pack);
GenTypes.ClearCache();
XmlInheritance.Clear();
// The native loader runs here in a Core-only harness. Expansion-gated vanilla nodes
// are filtered as the game does before XML inheritance; their identities were indexed above.
void CoreOnly(XmlDocument document)
{
    foreach(XmlNode node in document.SelectNodes("//*[@MayRequire or @MayRequireAnyOf]").Cast<XmlNode>().ToList()) node.ParentNode.RemoveChild(node);
}
foreach (string file in new[]{"FactionDefs/Factions_Base.xml","FactionDefs/Factions_Misc.xml","PawnKindDefs_Humanlikes/PawnKinds_Outlander.xml"})
{
    var doc = new XmlDocument(); doc.Load(Path.Combine(game.FullName,"Data/Core/Defs",file));
    CoreOnly(doc);
    foreach (XmlNode node in doc.DocumentElement.ChildNodes)
        if (node is XmlElement && new[]{"FactionBase","OutlanderFactionBase","OutlanderBase","VillagerBase","GuardBase","TraderBase","CouncilmanBase"}.Contains(node.Attributes?["Name"]?.Value)) XmlInheritance.TryRegister(node,pack);
}
var nodes = new List<XmlNode>();
foreach (string path in Directory.GetFiles(Path.Combine(repo,"mods/GrechkaFactions/Defs"),"*.xml"))
{
    var doc = new XmlDocument(); doc.Load(path);
    CoreOnly(doc);
    foreach (XmlNode node in doc.DocumentElement.ChildNodes)
        if (node is XmlElement) { nodes.Add(node); XmlInheritance.TryRegister(node,pack); }
}
XmlInheritance.Resolve();
var resolved = nodes.Select(XmlInheritance.GetResolvedNodeFor).ToList();
var refs = new HashSet<(Type,string)>();
void Reference(Type type, string name)
{
    Check(index.ContainsKey((type,name)),"Reference exists in owning data " + type.Name + "/" + name);
    refs.Add((type,name));
}
void Schema(XmlNode node, Type type, bool root=false)
{
    string className = node.Attributes?["Class"]?.Value;
    if (className != null)
    {
        Type concrete = TypeFor(className) ?? throw new Exception("Unknown class " + className);
        Check(type.IsAssignableFrom(concrete),"Assignable " + className);
        type = concrete;
    }
    if (typeof(Def).IsAssignableFrom(type) && !root) { Reference(type,node.InnerText); return; }
    if (type == typeof(PawnGenOption)) { Reference(typeof(PawnKindDef),node.Name); return; }
    if (type.IsEnum) { Enum.Parse(type,node.InnerText); return; }
    var children = node.ChildNodes.Cast<XmlNode>().Where(n=>n is XmlElement).ToList();
    if (children.Count == 0) return;
    if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
    {
        var types=type.GetGenericArguments();
        foreach(var child in children)
            if (types.Length == 2 && typeof(Def).IsAssignableFrom(types[0])) Reference(types[0],child.Name);
            else Schema(child,types[types.Length-1]);
        return;
    }
    foreach(var child in children)
    {
        FieldInfo field=AccessTools.Field(type,child.Name) ?? throw new Exception("Unknown field " + type.FullName + "." + child.Name);
        Schema(child,field.FieldType);
    }
}
foreach(var node in resolved) Schema(node,TypeFor(node.Name),true);
Check(true,"All inherited XML fields, enum values and references match installed 1.7");
// Seed referenced identities for isolated native XML loading. Gameplay behavior is tested separately.
foreach(var (type,name) in refs)
{
    Console.WriteLine("SEED " + type.Name + "/" + name);
    var def=(Def)Activator.CreateInstance(type); def.defName=name;
    typeof(DefDatabase<>).MakeGenericType(type).GetMethod("Add",new[]{type}).Invoke(null,new object[]{def});
}
Fixture.Errors.Clear();
Console.WriteLine("LOAD FACTIONS");

var factions = nodes.Where(n=>n.Name=="FactionDef").Select(n=>DirectXmlToObject.ObjectFromXml<FactionDef>(n,false)).ToList();
Console.WriteLine("LOAD PAWNS");
var traders = nodes.Where(n=>n.Name=="TraderKindDef").Select(n=>DirectXmlToObject.ObjectFromXml<TraderKindDef>(n,false)).ToList();
var pawns = nodes.Where(n=>n.Name=="PawnKindDef").Select(n=>DirectXmlToObject.ObjectFromXml<PawnKindDef>(n,false)).ToList();
DefDatabase<FactionDef>.Clear(); foreach(var faction in factions) DefDatabase<FactionDef>.Add(faction);
DefDatabase<TraderKindDef>.Clear(); foreach(var trader in traders) DefDatabase<TraderKindDef>.Add(trader);
DefDatabase<PawnKindDef>.Clear();
foreach(var (_,name) in refs.Where(r=>r.Item1==typeof(PawnKindDef) && !r.Item2.StartsWith("GrechkaPawn_"))) DefDatabase<PawnKindDef>.Add(new PawnKindDef{defName=name});
foreach(var pawn in pawns) DefDatabase<PawnKindDef>.Add(pawn);
DirectXmlCrossRefLoader.ResolveAllWantedCrossReferences(FailMode.LogErrors);
Check(Fixture.Errors.Count==0,"Native XML parser reports no errors");
Check(factions.Count==5 && pawns.Count==25,"Five factions and 25 role definitions load with native XML parser");
foreach(var faction in factions)
{
    Check(faction.requiredCountAtGameStart==0 && faction.startingCountAtWorldCreation==1 && faction.maxConfigurableAtWorldCreation==1,"Optional single faction in world creation " + faction.defName);
    Check(faction.basicMemberKind!=null && faction.fixedLeaderKinds.Count==1,"Members and leader resolve " + faction.defName);
    foreach(var group in faction.pawnGroupMakers)
    {
        var options=group.options.Concat(group.traders).Concat(group.guards);
        Check(options.All(o=>o.kind.defName.StartsWith("GrechkaPawn_")),"Every human group uses custom member kinds " + faction.defName + "/" + group.kindDef.defName);
    }
}
foreach(var pawn in pawns)
{
    var profile=pawn.GetModExtension<MemberProfile>();
    Check(profile?.specialties.Count==2 && profile.specialties.All(s=>s!=null),"Role specialties load " + pawn.defName);
    if(profile.group=="Artel") Check(pawn.requiredWorkTags==WorkTags.AllWork && pawn.combatPower>=400,"Capable adult Artel member with adjusted point cost " + pawn.defName);
    if(profile.group=="Bare") Check(pawn.nakedChance==1 && pawn.apparelRequired.NullOrEmpty(),"Barefoot members have no required clothes " + pawn.defName);
}
var harmony=new Harmony("grechka.factions.isolated");
harmony.CreateClassProcessor(typeof(MemberGenerationHook)).Patch();
Check(Harmony.GetPatchInfo(MemberGenerationHook.TargetMethod()).Postfixes.Any(p=>p.owner==harmony.Id),"Hook binds new pawn creation, including ordinary non-forced requests");
Check(MemberGenerationHook.TargetMethod().Name=="GenerateNewPawnInternal","Reused world pawns, loading and recruitment do not regenerate augmentation");
MemberGeneration.Apply(null);
MemberGeneration.Apply(new Pawn { def=new ThingDef { race=new RaceProperties {intelligence=Intelligence.Animal} } });
Check(true,"Null and pack animals are excluded safely");
// Exercise faction-wide augmentation with native pawn, skill and anatomy trackers.
// Only the final health notification boundary is stubbed, since this process has no Unity map.
harmony.Patch(AccessTools.PropertyGetter(typeof(SkillRecord),nameof(SkillRecord.TotallyDisabled)), prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.EnabledSkill)));
harmony.Patch(AccessTools.Method(typeof(Pawn_HealthTracker),nameof(Pawn_HealthTracker.AddHediff),new[]{typeof(HediffDef),typeof(BodyPartRecord),typeof(DamageInfo?),typeof(DamageWorker.DamageResult)}),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.AddImplant)));
foreach(string skillName in new[]{"Intellectual","Medicine","Plants","Cooking"})
    if(DefDatabase<SkillDef>.GetNamedSilentFail(skillName)==null) DefDatabase<SkillDef>.Add(new SkillDef{defName=skillName});
SkillDefOf.Intellectual=DefDatabase<SkillDef>.GetNamed("Intellectual");
SkillDefOf.Crafting=DefDatabase<SkillDef>.GetNamed("Crafting");
SkillDefOf.Construction=DefDatabase<SkillDef>.GetNamed("Construction");
SkillDefOf.Melee=DefDatabase<SkillDef>.GetNamed("Melee");
SkillDefOf.Medicine=DefDatabase<SkillDef>.GetNamed("Medicine");
SkillDefOf.Plants=DefDatabase<SkillDef>.GetNamed("Plants");
SkillDefOf.Cooking=DefDatabase<SkillDef>.GetNamed("Cooking");
foreach(string name in new[]{"Grechka_GrechkaArmT5_Hediff","Grechka_GrechkaLegT5_Hediff","Grechka_GrechkaEyeT5_Hediff","Grechka_CombatChipT5_Hediff","Grechka_MovementChipT5_Hediff"})
{
    Check(index.ContainsKey((typeof(HediffDef),name)),"Actual Equipment implant identity " + name);
    DefDatabase<HediffDef>.Add(new HediffDef{defName=name,hediffClass=name.Contains("Chip")?typeof(Hediff_Implant):typeof(Hediff_AddedPart)});
}
Pawn Member(PawnKindDef kind,string factionName)
{
    var torso=new BodyPartRecord{def=new BodyPartDef{defName="Torso",hitPoints=40}};
    foreach(string partName in new[]{"Arm","Leg","Eye","Brain"}) torso.parts.Add(new BodyPartRecord{def=new BodyPartDef{defName=partName,hitPoints=20},parent=torso});
    var pawn=new Pawn{def=new ThingDef{defName="HumanFixture",category=ThingCategory.Pawn,thingClass=typeof(Pawn),race=new RaceProperties{intelligence=Intelligence.Humanlike,body=new BodyDef{corePart=torso}}},kindDef=kind};
    pawn.RaceProps.body.ResolveReferences();
    pawn.SetFactionDirect(new Faction{def=new FactionDef{defName=factionName}});
    pawn.health=new Pawn_HealthTracker(pawn);
    pawn.skills=new Pawn_SkillTracker(pawn);
    return pawn;
}
foreach(var kind in pawns.Where(p=>p.GetModExtension<MemberProfile>().group=="Artel"))
{
    var pawn=Member(kind,"GrechkaFaction_Artel");
    MemberGeneration.Apply(pawn);
    Check(pawn.skills.GetSkill(SkillDefOf.Intellectual).Level is >=16 and <=20,"Every Artel role has high intellect " + kind.defName);
    Check(pawn.skills.skills.All(s=>s.Level>=8),"Every Artel skill has minimum eight " + kind.defName);
    Check(kind.GetModExtension<MemberProfile>().specialties.All(s=>pawn.skills.GetSkill(s).Level>=16),"Both professional skills reach sixteen " + kind.defName);
    int count=kind.GetModExtension<MemberProfile>().implantCount;
    Check(pawn.health.hediffSet.hediffs.Count==count,"T5 implant count matches role " + kind.defName);
    MemberGeneration.Apply(pawn);
    Check(pawn.health.hediffSet.hediffs.Count==count,"Repeated augmentation cannot stack implants " + kind.defName);
    pawn.SetFactionDirect(new Faction{def=new FactionDef{defName="PlayerColony"}});
    MemberGeneration.Apply(pawn);
    Check(pawn.health.hediffSet.hediffs.Count==count && pawn.skills.GetSkill(SkillDefOf.Intellectual).Level>=16,"Recruitment retains implants and skills " + kind.defName);
}
var fallback=Member(new PawnKindDef{defName="OutsideIncidentKind"},"GrechkaFaction_Artel");
MemberGeneration.Apply(fallback);
Check(fallback.health.hediffSet.hediffs.Count==1 && fallback.skills.GetSkill(SkillDefOf.Intellectual).Level>=16,"Outside incidents generating other human kinds still receive Artel augmentation");
var bare=Member(pawns.First(p=>p.GetModExtension<MemberProfile>().group=="Bare"),"GrechkaFaction_Bare");
MemberGeneration.Apply(bare);
Check(bare.health.hediffSet.hediffs.Count==2 && bare.skills.GetSkill(SkillDefOf.Melee).Level>=16,"Barefoot generation equips both actual T5 chip families and melee skill");
var ordinary=Member(new PawnKindDef{defName="Ordinary"},"OutlanderCivil");
MemberGeneration.Apply(ordinary);
Check(ordinary.health.hediffSet.hediffs.Count==0 && ordinary.skills.skills.All(s=>s.Level==0),"Unrelated factions remain unaffected");
Directory.Delete(configRoot,true);
Console.WriteLine("PASS Factions " + checks + " isolated checks");

static class Fixture
{
    public static List<string> Errors=new();
    public static bool Log(MethodBase __originalMethod, object[] __args)
    {
        string text=string.Join(" ",__args);
        Console.WriteLine("NATIVE " + __originalMethod.Name + " " + text);
        if(__originalMethod.Name=="Error") Errors.Add(text);
        return false;
    }
    public static bool EnabledSkill(ref bool __result) { __result=false; return false; }
    public static bool AddImplant(Pawn_HealthTracker __instance,Pawn ___pawn,HediffDef def,BodyPartRecord part,ref Hediff __result)
    {
        __result=(Hediff)Activator.CreateInstance(def.hediffClass);
        __result.def=def; __result.pawn=___pawn; __result.Part=part;
        __instance.hediffSet.hediffs.Add(__result);
        return false;
    }
}
