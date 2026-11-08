using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Grechka.Tweaks;
using HarmonyLib;
using RimWorld;
using Verse;

int checks = 0;
void Check(bool ok, string name)
{
    if (!ok) throw new Exception(name);
    Console.WriteLine("PASS " + name);
    checks++;
}
var module = typeof(Mastery).Assembly;
Type T(string name) => module.GetType("Grechka.Tweaks." + name, true);
object Call(Type t, string name, params object[] args) => AccessTools.Method(t,name).Invoke(null,args);
var ht = new Harmony("grechka.tweaks.test");
var skin = AccessTools.Field(typeof(Prefs),"data");
skin.SetValue(null,Activator.CreateInstance(skin.FieldType));
Current.Game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
Current.Game.tickManager = new TickManager();
foreach (var key in new[]{"CHARACTER_EDITOR_ASSEMBLY","PREPARE_CAREFULLY_ASSEMBLY"})
    if (Environment.GetEnvironmentVariable(key) is string path && path.Length > 0) Assembly.LoadFrom(path);
ht.Patch(AccessTools.Method(typeof(StatExtension),nameof(StatExtension.GetStatValue),new[]{typeof(Thing),typeof(StatDef),typeof(bool),typeof(int)}),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.UnitStat)));
StatDefOf.GlobalLearningFactor = new StatDef { defName="GlobalLearningFactor", defaultBaseValue=1 };
Patch(typeof(SkillRecord),"get_TotallyDisabled",nameof(Fixture.False));
Patch(typeof(SkillRecord),"get_Aptitude",nameof(Fixture.Zero));
Patch(typeof(Pawn),"get_IsColonist",nameof(Fixture.False));
Patch(typeof(Pawn_SkillTracker),"CanGainXP",nameof(Fixture.True));
void Patch(Type type,string method,string prefix) => ht.Patch(AccessTools.Method(type,method),prefix:new HarmonyMethod(typeof(Fixture),prefix));

var live = new Harmony("grechka.tweaks");
live.PatchAll(module);
Call(T("Editors"),"Install");
foreach (var (key,owner) in new[]{("CHARACTER_EDITOR_ASSEMBLY","charactereditor"),("PREPARE_CAREFULLY_ASSEMBLY","preparecarefully")})
    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
        Check(Harmony.GetAllPatchedMethods().Count(m=>Harmony.GetPatchInfo(m).Owners.Contains("grechka.tweaks."+owner)) >= 10,owner+" adapter bound to actual DLL");

SkillDef def = new SkillDef{defName="Crafting",skillLabel="Crafting"};
DefDatabase<SkillDef>.Add(def);
SkillDefOf.Social = new SkillDef {defName="Social",skillLabel="Social"};
var pawn = new Pawn();
pawn.skills = new Pawn_SkillTracker(pawn);
var s = new SkillRecord(pawn,def);
pawn.skills.skills = new List<SkillRecord> { s, new SkillRecord(pawn,SkillDefOf.Social) };
for (int grade=0;grade<7;grade++)
{
    Mastery.SetGrade(s,grade);
    Check(Mastery.Grade(s)==grade && (int)s.passion<=2 && Math.Abs(s.LearnRateFactor(true)-Mastery.Rates[grade])<.0001f, "grade "+grade+" applied exactly once in native LearnRateFactor");
}
Mastery.SetGrade(s,2);
s.Level=19;
s.xpSinceLastLevel = 29990;
s.Learn(30025,false,true);
Check(Mastery.Level(s)==21 && Mastery.Progress(s)==15 && s.xpSinceMidnight==30025 && s.Level==20,"native 19→21 overflow, daily XP and compatible base");
s.Learn(30250,true,true);
Check(Mastery.Level(s)==22 && Mastery.Progress(s)==15 && s.xpSinceMidnight==30025,"direct award skips daily total");
s.Interval();
Check(Mastery.Progress(s)==15,"passive decay does not destroy mastery");
s.Level=167;
Mastery.SetLevel(s,167,1234.5f);
Mastery.SetGrade(s,6);
string temp = Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".xml");
try
{
    Scribe.saver.InitSaving(temp,"tweaks"); s.ExposeData(); Scribe.saver.FinalizeSaving();
    var loaded=new SkillRecord(pawn,def);
    Scribe.loader.InitLoading(temp); loaded.ExposeData(); Scribe.loader.FinalizeLoading();
    Check(Mastery.Level(loaded)==167 && Mastery.Grade(loaded)==6 && Mastery.Progress(loaded)==1234.5f,"Scribe game-save round trip at 167");
    var doc=XDocument.Load(temp);
    doc.Root.Elements().Where(x=>x.Name.LocalName.StartsWith("grechka")).Remove(); doc.Save(temp);
    Scribe.loader.InitLoading(temp); loaded.ExposeData(); Scribe.loader.FinalizeLoading();
    Check(Mastery.Level(loaded)==20 && Mastery.Grade(loaded)==3,"legacy save without extension");
}
finally { Scribe.mode=LoadSaveMode.Inactive; File.Delete(temp); }
if (AccessTools.TypeByName("CharacterEditor.SkillTool") is Type ce)
{
    string saved = (string)Call(ce,"GetAllSkillsAsSeparatedString",pawn);
    Check(saved.Contains(":~GT1|Crafting|167|6|"),"Character Editor serializes the optional row");
    Mastery.SetLevel(s,0,0);
    Call(ce,"SetSkillsFromSeparatedString",pawn,saved);
    Check(Mastery.Level(s)==167 && Mastery.Grade(s)==6 && Mastery.Progress(s)==1234.5f,"Character Editor actual slot parser round trip");
    Call(ce,"SetSkillsFromSeparatedString",pawn,"Crafting|11|1|180|200");
    Check(Mastery.Level(s)==11 && Mastery.Grade(s)==2 && Mastery.Progress(s)==180,"legacy Character Editor slot replaces mastery");
    Mastery.SetLevel(s,167,1234.5f); Mastery.SetGrade(s,6);
    var bio = AccessTools.TypeByName("CharacterEditor.CEditor+EditorUI+BlockBio");
    var panel=RuntimeHelpers.GetUninitializedObject(bio);
    AccessTools.Method(bio,"AAddSkillLevel").Invoke(panel,new object[]{s});
    Check(Mastery.Level(s)==168 && s.levelInt==20,"Character Editor plus button");
    var clipboard=new List<SkillRecord>{s};
    Call(T("CharacterEditorAdapter"),"FreezeClipboard",clipboard);
    s.Level=15; Mastery.SetGrade(s,0);
    Call(ce,"CopySkillFromSkillRecord",pawn,clipboard[0]);
    Check(Mastery.Level(s)==168 && Mastery.Grade(s)==6 && !ReferenceEquals(s,clipboard[0]),"Character Editor clipboard is a snapshot");
}
if (AccessTools.TypeByName("EdB.PrepareCarefully.PawnSaver") is Type saver)
{
    Type E(string n)=>saver.Assembly.GetType("EdB.PrepareCarefully."+n,true);
    object Instance(string n)=>Activator.CreateInstance(E(n));
    Mastery.SetLevel(s,167,1234.5f); Mastery.SetGrade(s,6);
    var model=Instance("CustomizationsPawn");
    AccessTools.Method(E("MapperPawnToCustomizations"),"MapSkills").Invoke(Instance("MapperPawnToCustomizations"),new object[]{pawn,model});
    var record=Instance("SaveRecordPawnV5");
    var writer = Instance("PawnSaver");
    AccessTools.Property(saver,"ProviderPassions").SetValue(writer,Instance("ProviderPassions"));
    AccessTools.Method(saver,"ConvertSkills").Invoke(writer,new object[]{record,model});
    var rows=(System.Collections.IList)AccessTools.Field(record.GetType(),"skills").GetValue(record);
    Check((int)AccessTools.Field(rows[0].GetType(),"value").GetValue(rows[0])==20,"EdB preset keeps compatible base level");
    var savedSkill=(IExposable)rows[0];
    try
    {
        Scribe.saver.InitSaving(temp,"edb"); savedSkill.ExposeData(); Scribe.saver.FinalizeSaving();
        var copy=(IExposable)Instance("SaveRecordSkillV4");
        Scribe.loader.InitLoading(temp); copy.ExposeData(); Scribe.loader.FinalizeLoading(); rows[0]=copy;
    }
    finally { Scribe.mode=LoadSaveMode.Inactive; File.Delete(temp); }
    Mastery.SetLevel(s,20,0);Mastery.SetGrade(s,3);
    var imported=Instance("CustomizationsPawn");
    AccessTools.Method(E("MapperPawnToCustomizations"),"MapSkills").Invoke(Instance("MapperPawnToCustomizations"),new object[]{pawn,imported});
    AccessTools.Method(E("PawnLoaderV5"),"LoadOtherValues").Invoke(RuntimeHelpers.GetUninitializedObject(E("PawnLoaderV5")),new[]{record,imported,null});
    var first=((System.Collections.IList)AccessTools.Property(imported.GetType(),"Skills").GetValue(imported))[0];
    Check((int)AccessTools.Property(first.GetType(),"Level").GetValue(first)==167,"EdB actual V5 loader restores extended level");
    pawn.story = new Pawn_StoryTracker(pawn);
    AccessTools.Method(E("PawnCustomizer"),"ApplySkillCustomizationsToPawn").Invoke(RuntimeHelpers.GetUninitializedObject(E("PawnCustomizer")),new object[]{pawn,imported});
    Check(Mastery.Level(s)==167 && Mastery.Grade(s)==6 && Mastery.Progress(s)==1234.5f,"EdB preset apply restores grade and progress");
    var selected = Instance("CustomizedPawn");
    var view = Instance("ViewState");
    var ctl = RuntimeHelpers.GetUninitializedObject(E("ControllerTabViewPawns"));
    void Prop(object obj,string name,object value) => AccessTools.Property(obj.GetType(),name).SetValue(obj,value);
    Prop(selected,"Pawn",pawn); Prop(selected,"Customizations",imported); Prop(view,"CurrentPawn",selected);
    Prop(ctl,"ViewState",view); Prop(ctl,"PawnManager",RuntimeHelpers.GetUninitializedObject(E("ManagerPawns")));
    AccessTools.Method(ctl.GetType(),"AdjustSkillPassion").Invoke(ctl,new object[]{def,-1});
    Check(Mastery.Grade(s)==5,"EdB passion button cycles from violet to pink");
}
Mastery.SetLevel(s,100,0); s.Learn(Mastery.Threshold(100),true,true);
Check(Mastery.Level(s)==101,"former cap can gain another level");
Mastery.SetLevel(s,199,74745); s.Learn(100000,true,true);
Check(Mastery.Level(s)==200 && Mastery.Progress(s)==0,"cap at 200");
s.Learn(-600,true,true); s.Learn(-600,true,true);
Check(Mastery.Level(s)==199 && Mastery.Progress(s)==73550,"explicit XP losses at cap accumulate");
s.Level=15; Check(Mastery.Level(s)==15 && s.Level==15,"deliberate editor downgrade");
Check(Math.Abs((float)Call(T("QualityCurve"),"Center",100)-4.8f)<.0001,"quality center at 100");
double MeanQuality(int level)
{
    Rand.PushState(147);
    try
    {
        int sum=0;
        for(int i=0;i<8000;i++) sum += (int)QualityUtility.GenerateQualityCreatedByPawn(level,false);
        return sum/8000d;
    }
    finally { Rand.PopState(); }
}
double quality20=MeanQuality(20), quality21=MeanQuality(21), quality100=MeanQuality(100);
Check(quality21>=quality20-.02 && quality100>quality20+.25,"actual seeded quality distribution 20/21/100");
Check((float)Call(T("TradeExperience"),"Value",-10,30f)==300,"trade uses absolute volume");
// Supply only the global learning stat and cached negotiated item prices; real Learn and trade rows run.
SkillDefOf.Animals = new SkillDef {defName="Animals"};
Mastery.SetLevel(s,8,0); Mastery.SetGrade(s,6); s.xpSinceMidnight=0;
var social=pawn.skills.GetSkill(SkillDefOf.Social);
Mastery.SetLevel(social,8,0); Mastery.SetGrade(social,0); social.xpSinceMidnight=0;
Call(T("SleepExperience"),"Add",pawn,199);
Check(Mastery.Progress(s)==0,"subinterval sleep carried");
Call(T("SleepExperience"),"Add",pawn,19801);
Check(Math.Abs(Mastery.Progress(s)-960)<.001 && Math.Abs(Mastery.Progress(social)-12)<.001,"eight hours for each skill at its own learning rate");
Mastery.SetGrade(social,2);
TradeSession.playerNegotiator=pawn;
TradeSession.trader=new TradeShip {def=new TraderKindDef()};
ThingDefOf.Silver=new ThingDef {defName="Silver"};
var deal=(TradeDeal)RuntimeHelpers.GetUninitializedObject(typeof(TradeDeal));
Tradeable Line(ThingDef item,int count,float price)
{
    var t=new Tradeable(); t.thingsColony.Add(new ThingWithComps{def=item}); AccessTools.Field(typeof(Tradeable),"countToTransfer").SetValue(t,count);
    AccessTools.Field(typeof(Tradeable),"pricePlayerBuy").SetValue(t,price);
    AccessTools.Field(typeof(Tradeable),"pricePlayerSell").SetValue(t,price);
    return t;
}
var rowsInDeal = Traverse.Create(deal).Field("tradeables");
rowsInDeal.SetValue(new List<Tradeable>{ Line(new ThingDef{defName="Buy"},3,200),Line(new ThingDef{defName="Sell"},-8,50),Line(ThingDefOf.Silver,200,1) });
object Snapshot() { object[] a={deal,null}; Call(T("TradeExperience"),"Prefix",a); return a[1]; }
void Complete(bool success,bool traded,object state) => Call(T("TradeExperience"),"Postfix",success,traded,state);
TradeSession.giftMode=false;
var confirmed=Snapshot(); Complete(false,true,confirmed); Complete(true,false,confirmed);
Check(Math.Abs(Mastery.Progress(social)-12)<.001,"failed and empty transactions give no XP");
Complete(true,true,confirmed);
Check(Math.Abs(Mastery.Progress(social)-137)<.001,"orbital negotiation and barter award 125 XP, silver excluded");
TradeSession.giftMode=true; Complete(true,true,Snapshot());
Check(Math.Abs(Mastery.Progress(social)-137)<.001,"gifts excluded");
TradeSession.giftMode=false; ((TradeShip)TradeSession.trader).def.tradeCurrency=TradeCurrency.Favor;
Complete(true,true,Snapshot()); Check(Math.Abs(Mastery.Progress(social)-137)<.001,"honor trades excluded");
Mastery.SetLevel(s,80,730); s.xpSinceMidnight=5000; Mastery.SetGrade(s,6);
s.Learn(100);
Check(Math.Abs(Mastery.Progress(s)-970)<.001,"saturation applied once to master with violet interest");
var worker = T("MasterStats");
Mastery.SetLevel(s,100,0);
object[] speed = { StatRequest.For(pawn), new StatDef {defName="GeneralLaborSpeed"}, 2f };
Call(worker,"Postfix",speed);
Check(Math.Abs((float)speed[2]-3f)<.0001,"level 100 crafting boosts supported work by 50%");
// Same composition as the reported summary: higher level, different strongest interest, plus a left-behind pawn.
Pawn TeamPawn(int level, int grade)
{
    var member=new Pawn();
    member.skills=new Pawn_SkillTracker(member);
    var record=new SkillRecord(member,def);
    member.skills.skills=new List<SkillRecord>{record};
    Mastery.SetLevel(record,level,0);
    Mastery.SetGrade(record,grade);
    return member;
}
var veteran=TeamPawn(9,3);
var talent=TeamPawn(7,5);
var optional=TeamPawn(200,6);
var roster=new GameInitData {startingAndOptionalPawns=new List<Pawn>{veteran,talent,optional},startingPawnCount=2};
Current.Game.InitData=roster;
var team=T("TeamSkills");
var bestMethod=AccessTools.Method(typeof(StartingPawnUtility),"FindBestSkillOwner");
var leader=(Pawn)bestMethod.Invoke(null,new object[]{def});
Check(leader==veteran && (SkillRecord)Call(team,"StrongestInterest",leader.skills.GetSkill(def),roster)==talent.skills.GetSkill(def),
    "Team skills displays level 9 with pink interest and excludes the optional violet candidate");
var drawRow = AccessTools.Method(typeof(SkillUI),"DrawSkill",new[]{typeof(SkillRecord),typeof(UnityEngine.Rect),typeof(SkillUI.SkillDrawMode),typeof(string)});
Check(PatchProcessor.GetCurrentInstructions(drawRow).Any(i=>i.Calls(AccessTools.Method(team,"IconSource"))),"native skill row reads the summary icon source");
ht.Patch(AccessTools.Method(typeof(Mouse),"IsOver",new[]{typeof(UnityEngine.Rect)}),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.False)));
ht.Patch(drawRow,prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.CaptureRow)));
Call(team,"DrawTeamSkill",veteran.skills.GetSkill(def),new UnityEngine.Rect(0,0,150,24),SkillUI.SkillDrawMode.Menu,"veteran");
Check(Fixture.RenderedLevel==veteran.skills.GetSkill(def) && Fixture.RenderedIcon==talent.skills.GetSkill(def),
      "actual team renderer routes the level and icon to their separate owners");
Check((SkillRecord)Call(team,"IconSource",veteran.skills.GetSkill(def))==veteran.skills.GetSkill(def),"individual card uses its own icon after team draw");
Mastery.SetLevel(veteran.skills.GetSkill(def),100,0);
Mastery.SetLevel(talent.skills.GetSkill(def),170,0);
Check((Pawn)bestMethod.Invoke(null,new object[]{def})==talent,"team owner compares extended mastery above 100");
var teamRow=AccessTools.Method(typeof(StartingPawnUtility),"DrawSkillSummaries");
Check(PatchProcessor.GetCurrentInstructions(teamRow).Any(ins=>ins.Calls(AccessTools.Method(team,"DrawTeamSkill"))),"installed team panel calls the team-specific renderer");
Mastery.SetLevel(s,200,0);
object[] lateSpeed={StatRequest.For(pawn),new StatDef{defName="GeneralLaborSpeed"},2f};
Call(worker,"Postfix",lateSpeed);
Check((float)lateSpeed[2]>3 && MeanQuality(200)>quality100+.2,"mastery continues to improve work and quality through 200");

var rollSpecialist=AccessTools.Method(T("StartingSpecialist"),"Postfix");
var rollInterest=AccessTools.Method(T("GenerateInterest"),"Postfix");
var request=new PawnGenerationRequest {Context=PawnGenerationContext.PlayerStarter};
var sample=TeamPawn(12,3);
var main=sample.skills.GetSkill(def);
var second=new SkillRecord(sample,SkillDefOf.Social);
sample.skills.skills.Add(second);
int specialists=0;
var grades=new int[7];
int upgradedMinor=0;
Rand.PushState(973196);
try
{
    for (int n=0;n<10000;n++)
    {
        Mastery.SetLevel(main,12,0); Mastery.SetGrade(main,3);
        Mastery.SetLevel(second,6,0); Mastery.SetGrade(second,2);
        rollInterest.Invoke(null,new object[]{sample,request});
        grades[Mastery.Grade(main)]++;
        if(Mastery.Grade(second)==4) upgradedMinor++;
        rollSpecialist.Invoke(null,new object[]{sample});
        int level=Mastery.Level(main);
        if(level>=21 && level<=35) specialists++;
        else if(level!=12) throw new Exception("Invalid starter specialism");
        if(Mastery.Level(second)!=6) throw new Exception("Two specialisms on one pawn");
    }
}
finally { Rand.PopState(); }
Console.WriteLine($"Starting sample of 10000: {specialists} specialists; Major rolls Triple {grades[4]}, Pink {grades[5]}, Violet {grades[6]}; Minor to Triple {upgradedMinor}");
Check(specialists is >1800 and <2200 && grades[4] is >2700 and <3300 && grades[5] is >1350 and <1650 &&
      grades[6] is >425 and <575 && upgradedMinor is >850 and <1150,"starter frequencies on 10,000 reproducible draws");
ThingDef StackDef(string name, ThingCategory category, int limit)
{
    var item = new ThingDef { defName=name, category=category, stackLimit=limit };
    DefDatabase<ThingDef>.Add(item);
    return item;
}
var steel = StackDef("FixtureStackSteel",ThingCategory.Item,75);
var largeResource = StackDef("FixtureStackResource",ThingCategory.Item,10000);
var rifle = StackDef("FixtureRifle",ThingCategory.Item,1);
var building = StackDef("FixtureBuilding",ThingCategory.Building,15);
var stackType=T("StackLimits");
RuntimeHelpers.RunClassConstructor(stackType.TypeHandle);
Check(steel.stackLimit==750 && largeResource.stackLimit==750 && new TweaksSettings().stackSize==750,"default 750 for existing stackable definitions at startup");
Check(rifle.stackLimit==1 && building.stackLimit==15,"single items and non-items keep their original limits");
var pile = new Thing { def=steel, stackCount=740 };
var incoming = new Thing { def=steel, stackCount=40 };
Check(ThingUtility.TryAbsorbStackNumToTake(pile,incoming,true)==10,"real merge calculation caps a 740 pile at 750");
Call(stackType,"Apply",300);
Check(steel.stackLimit==300 && largeResource.stackLimit==300 && pile.stackCount==740 &&
    ThingUtility.TryAbsorbStackNumToTake(pile,incoming,true)==0,"reducing limit keeps an old pile and prevents negative transfers");
var keepStack = AccessTools.Method(T("PreserveStackCount"),"KeepStack");
Check((bool)keepStack.Invoke(null,new object[]{pile}) && !(bool)keepStack.Invoke(null,new object[]{new Thing {def=rifle}}) && PatchProcessor.GetCurrentInstructions(
    AccessTools.Method(typeof(Thing),nameof(Thing.SpawnSetup))).Any(ins=>ins.Calls(keepStack)),"native spawn guard preserves oversized saved stacks");
if (Environment.GetEnvironmentVariable("PIPESYSTEM_ASSEMBLY") is string pipePath && pipePath.Length > 0)
{
    var target = AccessTools.Method(typeof(Thing), nameof(Thing.SpawnSetup));
    var pipeClass = Assembly.LoadFrom(pipePath).GetType("PipeSystem.PipeSystem_Thing_SpawnSetup_Patch", true);
    var pipe = new Harmony("grechka.tweaks.test.pipesystem");
    pipe.CreateClassProcessor(pipeClass).Patch();
    var helper = AccessTools.Method(pipeClass, "SpawnSetupHelper");
    Check(PatchProcessor.GetCurrentInstructions(target).Any(op => op.Calls(helper)), "PipeSystem can patch SpawnSetup after Grechka Tweaks");
    var ours = AccessTools.Method(T("PreserveStackCount"), "Transpiler");
    live.Unpatch(target, ours);
    live.Patch(target, transpiler: new HarmonyMethod(ours));
    var combined = PatchProcessor.GetCurrentInstructions(target);
    Check(combined.Any(op => op.Calls(helper)) && combined.Any(op => op.Calls(keepStack)),
        "both spawn guards bind when PipeSystem patches first");
    pipe.UnpatchAll(pipe.Id);
}
Call(stackType,"Apply",900);
Check(ThingUtility.TryAbsorbStackNumToTake(pile,incoming,true)==40 && rifle.stackLimit==1,"increasing the setting allows further stacking");
try
{
    var settings=new TweaksSettings {stackSize=1200};
    Scribe.saver.InitSaving(temp,"settings"); settings.ExposeData(); Scribe.saver.FinalizeSaving();
    var restored=new TweaksSettings();
    Scribe.loader.InitLoading(temp); restored.ExposeData(); Scribe.loader.FinalizeLoading();
    Check(restored.stackSize==1200,"custom limit survives settings reload");
    foreach (var (xml,expected) in new[]{("<settings/>",750),("<settings><stackSize>0</stackSize></settings>",10),
        ("<settings><stackSize>20000</stackSize></settings>",10000)})
    {
        File.WriteAllText(temp,xml);
        Scribe.loader.InitLoading(temp); restored.ExposeData(); Scribe.loader.FinalizeLoading();
        Check(restored.stackSize==expected,"absent or out-of-range settings load as "+expected);
    }
}
finally { Scribe.mode=LoadSaveMode.Inactive; File.Delete(temp); }
checks += TrainerChecks.Run(ht);
checks += SoundMufflerChecks.Run(ht, Path.GetFullPath(args.FirstOrDefault() ?? "."));
Console.WriteLine($"{checks} checks passed");
static class Fixture
{
    public static SkillRecord RenderedLevel, RenderedIcon;
    public static bool CaptureRow(SkillRecord __0)
    {
        RenderedLevel=__0;
        var team=typeof(Mastery).Assembly.GetType("Grechka.Tweaks.TeamSkills");
        RenderedIcon=(SkillRecord)AccessTools.Method(team,"IconSource").Invoke(null,new object[]{__0});
        return false;
    }
    public static bool UnitStat(ref float __result) { __result=1;return false; }
    public static bool True(ref bool __result) { __result=true;return false; }
    public static bool False(ref bool __result) { __result=false;return false; }
    public static bool Zero(ref int __result) { __result=0;return false; }
}
