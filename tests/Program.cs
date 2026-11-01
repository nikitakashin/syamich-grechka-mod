using Grechka.Quests;
using System.Text.Json;

int passed = 0;
string source = File.ReadAllText(args[0]);
QuestPack Fresh() => PackCodec.Decode(source);
void Check(string name, Action action)
{
    try { action(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { Console.Error.WriteLine("FAIL " + name + ": " + ex); Environment.Exit(1); }
}
void Reject(Action<QuestPack> edit)
{
    var p = Fresh(); edit(p);
    if (PackCodec.Validate(p).Count == 0) throw new Exception("Expected validation failure");
    try { PackCodec.Decode(PackCodec.Encode(p)); } catch (FormatException) { return; }
    throw new Exception("Invalid JSON accepted");
}
Check("Russian JSON round trip preserves IDs, text, dependencies and rewards", () => {
    var p = Fresh(); var round = PackCodec.Decode(PackCodec.Encode(p));
    if (PackCodec.Encode(p) != PackCodec.Encode(round) || round.quests[1].requires[0] != "start.steel") throw new Exception("Round trip mismatch");
});
Check("Editing clone does not change active pack", () => { var p = Fresh(); var d = PackCodec.Clone(p); d.quests[0].rewards[0].count = 99; if (p.quests[0].rewards[0].count != 20) throw new Exception(); });
Check("No turn-in objective", () => Reject(p => p.quests[0].objectives[0].type = "submit"));
Check("Unknown objective is rejected", () => Reject(p => p.quests[0].objectives[0].type = "recycle"));
Check("Production, mining and harvesting objectives round-trip as distinct types", () => {
    foreach(string kind in new[]{"produce","mine","harvest"}) { var p=Fresh(); p.quests[0].objectives[0].type=kind; if(PackCodec.Decode(PackCodec.Encode(p)).quests[0].objectives[0].type!=kind) throw new Exception(); }
});
Check("Cycle rejected", () => Reject(p => p.quests[0].requires.Add(p.quests[1].id)));
Check("Self dependency rejected", () => Reject(p => p.quests[0].requires.Add(p.quests[0].id)));
Check("Missing dependency rejected", () => Reject(p => p.quests[0].requires.Add("missing")));
Check("Duplicate quest ID rejected", () => Reject(p => p.quests[1].id = p.quests[0].id));
Check("Duplicate goal ID rejected", () => Reject(p => p.quests[0].objectives.Add(p.quests[0].objectives[0])));
Check("Duplicate chapter ID rejected", () => Reject(p => p.chapters.Add(p.chapters[0])));
Check("Missing chapter rejected", () => Reject(p => p.quests[0].chapter = "missing"));
Check("Path traversal in package ID rejected", () => Reject(p => p.id = "../../Mods/evil"));
Check("Negative reward cannot remove items", () => Reject(p => p.quests[0].rewards[0].count = -1));
Check("Oversized rewards rejected", () => Reject(p => p.quests[0].rewards[0].count = 10001));
Check("Instruction quest without objectives is valid", () => { var p = Fresh(); p.quests[0].objectives.Clear(); if (PackCodec.Validate(p).Count != 0) throw new Exception(); });
Check("Null quest lists rejected", () => Reject(p => p.quests[0].objectives = null));
Check("Null entry rejected", () => Reject(p => p.quests.Add(null)));
Check("Null target rejected", () => Reject(p => p.quests[0].objectives[0].target = null));
Check("Unknown schema rejected", () => Reject(p => p.schemaVersion = 2));
Check("Unknown fields rejected", () => {
    try { PackCodec.Decode(source.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"execute\": \"bad\"")); }
    catch (JsonException) { return; } throw new Exception();
});
Check("Oversized import rejected", () => { try { PackCodec.Decode(new string(' ', PackCodec.MaxBytes + 1)); } catch (FormatException) { return; } throw new Exception(); });
Check("Absent optional mod reference retained for in-game diagnostics", () => { var p=Fresh(); p.quests[0].objectives[0].target="SomeOtherMod_Copper"; if(PackCodec.Decode(PackCodec.Encode(p)).quests[0].objectives[0].target!="SomeOtherMod_Copper")throw new Exception(); });
Check("Namespaced stable keys survive edit and export", () => { var p=Fresh(); var key=PackCodec.Key(p.id,p.quests[0].id); var ledger=new HashSet<string>{key}; p.quests[0].title="Changed"; var imported=PackCodec.Decode(PackCodec.Encode(p)); if(!ledger.Contains(PackCodec.Key(imported.id,imported.quests[0].id))) throw new Exception(); if(key==PackCodec.Key("another.pack",p.quests[0].id))throw new Exception(); });
Check("Independent empty book allowed", () => { var p = new QuestPack { chapters = new List<Chapter>{new Chapter()} }; if(PackCodec.Validate(p).Count!=0)throw new Exception(); });
Check("Locked quest tracks objectives but requires claimed predecessor", () => {
    var p=Fresh(); var a=p.quests[0]; var b=p.quests[1]; var state=new QuestProgress(null,null);
    foreach(var o in a.objectives) state.Observe(p,a,o,o.count,true);
    foreach(var o in b.objectives) state.Observe(p,b,o,o.count,true);
    if(!state.Ready(p,a,true) || state.Unlocked(p,b) || state.Ready(p,b,true)) throw new Exception();
    state.RecordClaim(p,a); if(!state.Ready(p,b,true)) throw new Exception();
});
Check("Claim all includes only previously achieved chain and does not mutate state", () => {
    var p=Fresh(); var a=p.quests[0]; var b=p.quests[1]; var state=new QuestProgress(null,null);
    foreach(var o in a.objectives) state.Observe(p,a,o,o.count,true);
    if(string.Join(",",state.PlanClaimAll(p,true,null)) != a.id || state.Claimed(p,a)) throw new Exception();
    foreach(var o in b.objectives) state.Observe(p,b,o,o.count,true);
    if(string.Join(",",state.PlanClaimAll(p,true,null)) != a.id+","+b.id) throw new Exception();
    if(state.PlanClaimAll(p,true,q => q.id != a.id).Count != 0) throw new Exception();
});
Check("Milestone survives quantity edits but not target changes", () => {
    var p=Fresh(); var q=p.quests[0]; var o=q.objectives[0]; var state=new QuestProgress(null,null);
    state.Observe(p,q,o,o.count,true); o.count += 100;
    if(!state.Achieved(p,q,o)) throw new Exception(); o.target="Gold";
    if(state.Achieved(p,q,o)) throw new Exception();
});
Check("No colony blocks observation and claims, preserving recorded state", () => {
    var p=Fresh(); var q=p.quests[0]; var o=q.objectives[0]; var state=new QuestProgress(null,null);
    state.Observe(p,q,o,o.count,false); if(state.Achieved(p,q,o) || state.Ready(p,q,false)) throw new Exception();
    state.Observe(p,q,o,o.count,true); if(!state.Achieved(p,q,o) || state.PlanClaimAll(p,false,null).Count!=0) throw new Exception();
    var restored=new QuestProgress(state.CopyAchieved(),state.CopyClaimed()); if(!restored.Achieved(p,q,o)) throw new Exception();
});
Check("Empty quest needs explicit claim before unlocking its child", () => {
    var p=Fresh(); var a=p.quests[0]; var b=p.quests[1]; a.objectives.Clear(); var state=new QuestProgress(null,null);
    if(!state.Ready(p,a,true) || state.Unlocked(p,b)) throw new Exception(); state.RecordClaim(p,a);
    if(!state.Unlocked(p,b) || state.Ready(p,a,true)) throw new Exception();
});
Check("Legacy completion preserved without manufacturing individual milestones", () => {
    var p=Fresh(); var q=p.quests[0]; var state=new QuestProgress(null,null);
    state.RememberLegacyCompletion(p,q);
    if(!state.ObjectivesComplete(p,q) || state.Achieved(p,q,q.objectives[0])) throw new Exception();
    var restored=new QuestProgress(state.CopyAchieved(),state.CopyClaimed(),state.CopyLegacyCompletions());
    q.objectives[0].count++; if(!restored.ObjectivesComplete(p,q)) throw new Exception();
    q.objectives[0].target="Gold"; if(restored.ObjectivesComplete(p,q)) throw new Exception();
});
Check("Save progress rejects mismatched and duplicate keys without silently resetting", () => {
    var keys=new List<string>{"pack/quest/goal"}; var values=new List<string>{"have:Steel"};
    var saved=QuestProgress.ReadPairs(keys,values); if(saved[keys[0]]!=values[0]) throw new Exception();
    try { QuestProgress.ReadPairs(keys,new List<string>()); throw new Exception("mismatched accepted"); } catch(FormatException) { }
    try { QuestProgress.ReadPairs(new List<string>{"x","x"},new List<string>{"a","b"}); throw new Exception("duplicate accepted"); } catch(FormatException) { }
    if(QuestProgress.ReadPairs(null,null).Count!=0) throw new Exception();
});
Check("Unknown saved progress version is rejected", () => {
    QuestProgress.ValidateProgressVersion(0); QuestProgress.ValidateProgressVersion(1);
    try { QuestProgress.ValidateProgressVersion(2); throw new Exception("future version accepted"); } catch(FormatException) { }
});
Check("Drafts preserve incomplete fields and remain separate per colony and book", () => {
    string root=Path.Combine(Path.GetTempPath(),"grechka-drafts-"+Guid.NewGuid().ToString("N"));
    try {
        var store=new DraftStore(root); var ownerA=Guid.NewGuid().ToString("N"); var ownerB=Guid.NewGuid().ToString("N");
        var pack=Fresh(); pack.quests[0].title="";
        var draft=new DraftDocument { pack=pack, numberBuffers=new Dictionary<string,string>{{"steel","-"}} };
        store.Save(ownerA,draft); var saved=store.LoadLatest(ownerA,out var pathA);
        if(saved.pack.quests[0].title!="" || saved.numberBuffers["steel"]!="-" || PackCodec.Validate(saved.pack).Count==0) throw new Exception();
        if(store.LoadLatest(ownerB,out var absent)!=null || absent!=null) throw new Exception();
        store.Save(ownerB,new DraftDocument {pack=Fresh()});
        var other=Fresh(); other.id="independent.book"; store.Save(ownerA,new DraftDocument {pack=other});
        if(Directory.GetFiles(Path.Combine(root,"Drafts",ownerA),"*.json").Length!=2) throw new Exception();
        if(PackCodec.ValidId("../wrong")) throw new Exception();
        try { store.Save("../wrong",draft); throw new Exception("Unsafe colony ID accepted"); } catch(FormatException) { }
    } finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
});
Check("Legacy colony draft owner is reproducible before saving the game", () => {
    string owner=DraftStore.OwnerForWorld("seed-42",12345);
    if(owner!=DraftStore.OwnerForWorld("seed-42",12345)) throw new Exception();
    if(owner==DraftStore.OwnerForWorld("seed-42",12346) || owner==DraftStore.OwnerForWorld("other",12345)) throw new Exception();
    try { DraftStore.OwnerForWorld("",0); throw new Exception("Missing world identity accepted"); } catch(FormatException) { }
});
Check("Draft recovery retains damaged data and chooses the latest valid backup", () => {
    string root=Path.Combine(Path.GetTempPath(),"grechka-drafts-"+Guid.NewGuid().ToString("N"));
    try {
        var store=new DraftStore(root); var owner=Guid.NewGuid().ToString("N"); var pack=Fresh();
        store.Save(owner,new DraftDocument{pack=pack});
        for(int i=0;i<12;i++){ pack.title="Edition "+i; store.Save(owner,new DraftDocument{pack=pack}); }
        var loaded=store.LoadLatest(owner,out var path); if(loaded.pack.title!="Edition 11") throw new Exception();
        var backups=Directory.GetFiles(Path.Combine(root,"Backups",owner),"*.json",SearchOption.AllDirectories);
        if(backups.Length!=10) throw new Exception("Expected ten backups, got "+backups.Length);
        File.WriteAllText(path,"broken json");
        try { store.Save(owner,new DraftDocument{pack=pack}); throw new Exception("Corrupt draft overwritten"); } catch(JsonException) { }
        if(File.ReadAllText(path)!="broken json") throw new Exception();
        var recovered=store.RecoverLatest(owner,path);
        if(recovered.pack.title!="Edition 10" || store.LoadLatest(owner,out _).pack.title!="Edition 10") throw new Exception();
        var originals=Directory.GetFiles(Path.GetDirectoryName(path),Path.GetFileName(path)+".damaged-*");
        if(originals.Length!=1 || File.ReadAllText(originals[0])!="broken json") throw new Exception();
        try { store.RecoverLatest(Guid.NewGuid().ToString("N"),path); throw new Exception("Cross-colony recovery accepted"); } catch(FormatException) { }
    } finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
});
Check("Explicitly starting over archives an unreadable draft", () => {
    string root=Path.Combine(Path.GetTempPath(),"grechka-drafts-"+Guid.NewGuid().ToString("N"));
    try {
        var store=new DraftStore(root); string owner=Guid.NewGuid().ToString("N"); var pack=Fresh();
        store.Save(owner,new DraftDocument{pack=pack}); store.LoadLatest(owner,out string path);
        File.WriteAllText(path,"{broken");
        var replacement=Fresh(); replacement.title="Restarted";
        store.ArchiveDamagedAndStart(owner,path,new DraftDocument{pack=replacement});
        if(store.LoadLatest(owner,out _).pack.title!="Restarted") throw new Exception();
        var copies=Directory.GetFiles(Path.GetDirectoryName(path),Path.GetFileName(path)+".damaged-*");
        if(copies.Length!=1 || File.ReadAllText(copies[0])!="{broken") throw new Exception();
    } finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
});
Check("Editor rejects cycles immediately without modifying existing edges", () => {
    var p=Fresh(); var a=p.quests[0]; var b=p.quests[1];
    if(QuestEditor.TryAddDependency(p,a,b,out var cycle) || string.IsNullOrEmpty(cycle) || a.requires.Count!=0) throw new Exception();
    if(QuestEditor.TryAddDependency(p,a,a,out _) || a.requires.Count!=0) throw new Exception();
    var c=p.quests[2]; if(!QuestEditor.TryAddDependency(p,c,b,out var error) || error!=null || c.requires.Single()!=b.id) throw new Exception();
});
Check("Copying a quest or branch creates new IDs and retains only internal links", () => {
    var p=Fresh(); var first=p.quests[0]; var second=p.quests[1];
    var single=QuestEditor.CopySelected(p,new[]{second.id}).Single();
    if(single.id==second.id || single.objectives[0].id==second.objectives[0].id || single.requires.Count!=0) throw new Exception();
    var copies=QuestEditor.CopySelected(p,new[]{first.id,second.id});
    if(copies[1].requires.Single()!=copies[0].id || copies[0].id==first.id || copies[1].id==second.id) throw new Exception();
    copies[0].rewards[0].count++; if(copies[0].rewards[0].count==first.rewards[0].count) throw new Exception();
    var state=new QuestProgress(null,new[]{PackCodec.Key(p.id,first.id)}); if(state.Claimed(p,copies[0])) throw new Exception();
});
Check("Copying a chapter remaps internal edges and removing a quest preserves dependents", () => {
    var p=Fresh(); var first=p.quests[0]; var second=p.quests[1];
    var copied=QuestEditor.CopyChapter(p,p.chapters[0]); var group=p.quests.Where(q=>q.chapter==copied.id).ToList();
    if(group.Count!=2 || copied.id==p.chapters[0].id || group[1].requires.Single()!=group[0].id) throw new Exception();
    QuestEditor.RemoveQuest(p,first); if(!p.quests.Contains(second) || second.requires.Contains(first.id)) throw new Exception();
});
Check("Changing goal semantics makes a new ID; changing its amount does not", () => {
    var p=Fresh(); var o=p.quests[0].objectives[0]; var id=o.id; o.count++;
    if(o.id!=id || QuestEditor.ChangeObjective(o,"have","Steel") || o.id!=id) throw new Exception();
    if(!QuestEditor.ChangeObjective(o,"have","Gold") || o.id==id) throw new Exception();
});
Check("Undo and redo affect only the draft and preserve unfinished numeric input", () => {
    var active=Fresh(); var draft=new DraftDocument {pack=PackCodec.Clone(active)}; var history=new DraftHistory(draft);
    draft.pack.title="Draft A"; draft.numberBuffers["steel"]="-"; history.Record(draft);
    draft.pack.title="Draft B"; history.Record(draft);
    var a=history.Undo(); if(a.pack.title!="Draft A" || a.numberBuffers["steel"]!="-" || active.title=="Draft A") throw new Exception();
    if(history.Redo().pack.title!="Draft B") throw new Exception();
    var old=history.Undo(); old.pack.title="Another edit"; history.Record(old);
    if(history.CanRedo || !history.CanUndo || history.Undo().pack.title!="Draft A") throw new Exception();
});
Check("Graph positions and icons round-trip without changing quest progress identity", () => {
    var p=Fresh(); var q=p.quests[0]; var before=QuestProgress.QuestSignature(q);
    q.graphPositioned=true; q.graphX=-120.5f; q.graphY=440.25f; q.icon="Steel";
    p.chapters[0].icon="Steel"; p.chapters[0].description="Описание главы";
    var restored=PackCodec.Decode(PackCodec.Encode(p)); var edited=restored.quests[0];
    if(!edited.graphPositioned || edited.graphX!=-120.5f || edited.graphY!=440.25f || edited.icon!="Steel" || restored.chapters[0].description!="Описание главы") throw new Exception();
    if(QuestProgress.QuestSignature(edited)!=before) throw new Exception();
    q.graphX=float.NaN; if(PackCodec.Validate(p).Count==0) throw new Exception();
});
Check("Reward alternatives round-trip and copies do not share IDs or item data", () => {
    var p=Fresh(); var q=p.quests[0];
    q.alternatives.Add(new RewardOption{id="pick.one",title="Первый",items=new List<Reward>{new Reward{def="Steel",count=3,quality="Good"}}});
    q.alternatives.Add(new RewardOption{id="pick.two",title="Второй",items=new List<Reward>{new Reward{def="WoodLog",count=7}}});
    var restored=PackCodec.Decode(PackCodec.Encode(p)); if(restored.quests[0].alternatives[0].items[0].quality!="Good") throw new Exception();
    var copy=QuestEditor.CopySelected(restored,new[]{q.id}).Single();
    if(copy.alternatives[0].id==q.alternatives[0].id) throw new Exception();
    copy.alternatives[0].items[0].count=99; if(restored.quests[0].alternatives[0].items[0].count!=3) throw new Exception();
    copy.alternatives[1].id=copy.alternatives[0].id; if(PackCodec.Validate(restored).Count==0) throw new Exception();
});
Check("Invalid reward choices and quality are rejected without changing legacy JSON support", () => {
    var p=Fresh(); p.quests[0].alternatives.Add(new RewardOption{id="choice.test",items=new List<Reward>()});
    if(PackCodec.Validate(p).Count==0) throw new Exception();
    p.quests[0].alternatives[0].items.Add(new Reward{quality="Unobtainable"}); if(PackCodec.Validate(p).Count==0) throw new Exception();
    if(Fresh().quests[0].alternatives.Count!=0) throw new Exception();
});
Check("Reward plan requires explicit valid choices before any history is changed", () => {
    var p=Fresh(); var q=p.quests[0];
    q.alternatives.Add(new RewardOption{id="choice.a",title="А",items=new List<Reward>{new Reward()}});
    q.alternatives.Add(new RewardOption{id="choice.b",title="Б",items=new List<Reward>{new Reward()}});
    var state=new QuestProgress(null,null); foreach(var o in q.objectives) state.Observe(p,q,o,o.count,true);
    var plan=new QuestClaimPlan(PackCodec.Encode(p),state.PlanClaimAll(p,true,null));
    if(state.ValidatePlan(p,plan,null,true,null)==null || state.Claimed(p,q)) throw new Exception();
    var choice=new Dictionary<string,string>{{q.id,"choice.a"}};
    if(state.ValidatePlan(p,plan,choice,true,null)!=null) throw new Exception();
    state.RecordClaim(p,q,"choice.a"); state.RecordClaim(p,q,"choice.b");
    if(state.ChosenReward(p,q)!="choice.a" || state.ValidatePlan(p,plan,choice,true,null)==null) throw new Exception();
    var restored=new QuestProgress(state.CopyAchieved(),state.CopyClaimed(),state.CopyLegacyCompletions(),state.CopyRewardChoices());
    if(restored.ChosenReward(p,q)!="choice.a") throw new Exception();
});
Check("A prepared reward plan never expands when another objective is reached", () => {
    var p=Fresh(); var a=p.quests[0]; var b=p.quests[1]; var state=new QuestProgress(null,null);
    foreach(var o in a.objectives) state.Observe(p,a,o,o.count,true);
    var plan=new QuestClaimPlan(PackCodec.Encode(p),state.PlanClaimAll(p,true,null));
    foreach(var o in b.objectives) state.Observe(p,b,o,o.count,true);
    if(plan.questIds.Count!=1 || plan.questIds[0]!=a.id || state.ValidatePlan(p,plan,null,true,null)!=null) throw new Exception();
    p.quests[0].title="Edited while dialog was open"; if(state.ValidatePlan(p,plan,null,true,null)==null) throw new Exception();
});
Check("Possession adds explicit variants and counts each physical stack once", () => {
    var p=Fresh(); var q=p.quests[0]; var o=q.objectives[0]; o.variants.Add("Gold");
    var tally=new ObjectiveTally(p); var steel=new object(); var gold=new object();
    tally.Add(steel,"have","Steel",null,null,40,"home"); tally.Add(steel,"have","Steel",null,null,40,"caravan");
    tally.Add(gold,"have","Gold",null,null,60,"caravan"); tally.Add(new object(),"have","Silver",null,null,999);
    var breakdown=tally.Breakdown(p,q,o);
    if(tally.Value(p,q,o)!=100 || breakdown["home"]!=40 || breakdown["caravan"]!=60) throw new Exception();
});
Check("Material and minimum quality require both filters and count correct building amount", () => {
    var p=Fresh(); var q=p.quests[0]; var o=q.objectives[0]; o.type="build"; o.target="Bed"; o.stuff="WoodLog"; o.minQuality="Good";
    var tally=new ObjectiveTally(p);
    tally.Add(new object(),"build","Bed","WoodLog","Excellent",1); tally.Add(new object(),"build","Bed","WoodLog","Good",1);
    tally.Add(new object(),"build","Bed","Steel","Legendary",1); tally.Add(new object(),"build","Bed","WoodLog","Normal",1);
    tally.Add(new object(),"build","Bed","WoodLog",null,1); if(tally.Value(p,q,o)!=2) throw new Exception();
});
Check("Goal filtering maintains legacy signatures and creates new IDs only for semantic changes", () => {
    var p=Fresh(); var q=p.quests[0]; var o=q.objectives[0]; var state=new QuestProgress(null,null);
    string legacy=o.type+":"+o.target; if(QuestProgress.Signature(o)!=legacy) throw new Exception();
    state.Observe(p,q,o,o.count,true); string id=o.id;
    if(!QuestEditor.EditObjective(o,x=>x.variants.Add("Gold")) || o.id==id || state.Achieved(p,q,o)) throw new Exception();
    id=o.id; if(QuestEditor.EditObjective(o,x=>x.count++) || o.id!=id) throw new Exception();
    if(!QuestEditor.EditObjective(o,x=>x.minQuality="Good") || o.id==id) throw new Exception();
    var copy=QuestEditor.CopySelected(p,new[]{q.id})[0].objectives[0];
    if(copy.variants[0]!="Gold" || copy.minQuality!="Good") throw new Exception(); copy.variants.Clear();
    if(o.variants.Count!=1) throw new Exception();
});
Check("Variant order does not change objective identity and invalid lists are rejected", () => {
    var p=Fresh(); var o=p.quests[0].objectives[0]; o.variants.AddRange(new[]{"Gold","Silver"}); string before=QuestProgress.Signature(o);
    o.variants.Reverse(); if(QuestProgress.Signature(o)!=before) throw new Exception();
    Reject(x=>x.quests[0].objectives[0].variants.Add(x.quests[0].objectives[0].target));
    Reject(x=>x.quests[0].objectives[0].minQuality="Impossible");
    Reject(x=>x.quests[0].objectives[0].variants=null);
});
Check("Switching a goal to manual clears incompatible filters", () => {
    var o=new Objective{variants=new List<string>{"Gold"}, stuff="WoodLog", minQuality="Good"};
    QuestEditor.ChangeObjective(o,"manual",""); if(o.variants.Count!=0 || o.stuff!=null || o.minQuality!=null) throw new Exception();
});
Check("Book updates reject changed semantics under an existing goal ID", () => {
    var p=Fresh(); var history=new BookHistory(null,null); history.Seed(p,null);
    var replacement=PackCodec.Clone(p); replacement.quests[0].objectives[0].target="Gold";
    if(history.Conflicts(replacement).Count!=1) throw new Exception();
    try { history.Remember(replacement); throw new Exception("conflict accepted"); } catch(InvalidOperationException) { }
    if(history.SavedBooks().Single().quests[0].objectives[0].target!="Steel") throw new Exception();
    QuestEditor.ChangeObjective(replacement.quests[0].objectives[0],"have","Silver"); history.Remember(replacement);
    if(history.SavedBooks().Single().quests[0].objectives[0].target!="Silver") throw new Exception();
});
Check("Book archive retains previous books and historical goal identities after removal", () => {
    var first=Fresh(); var history=new BookHistory(null,null); history.Seed(first,null);
    var second=PackCodec.Clone(first); second.id="different.book"; second.title="Вторая книга"; history.Remember(second);
    var updated=PackCodec.Clone(first); updated.quests[0].objectives.Clear(); history.Remember(updated);
    var restored=new BookHistory(history.CopyBooks(),history.CopyDefinitions());
    if(restored.SavedBooks().Count!=2 || restored.SavedBooks().Single(x=>x.id==first.id).quests[0].objectives.Count!=0) throw new Exception();
    var invalid=PackCodec.Clone(first); invalid.quests[0].objectives[0].target="Gold";
    if(restored.Conflicts(invalid).Count!=1) throw new Exception();
    var newPack=PackCodec.Clone(invalid); newPack.id="third.book"; if(restored.Conflicts(newPack).Count!=0) throw new Exception();
});
Check("Book update summary includes removed links and changed quests", () => {
    var before=Fresh(); var after=PackCodec.Clone(before); after.quests[1].requires.Clear(); after.quests[0].objectives.Clear();
    string description=BookHistory.DescribeChange(before,after);
    if(!description.Contains("изменено 2") || !description.Contains("Удалённые связи:") || !description.Contains("целей: 1")) throw new Exception(description);
});
Check("Legacy achieved definitions are protected when seeding history", () => {
    var p=Fresh(); var o=p.quests[0].objectives[0]; string key=PackCodec.ObjectiveKey(p.id,p.quests[0].id,o.id);
    var history=new BookHistory(null,null); history.Seed(p,new Dictionary<string,string>{{key,"have:Gold"}});
    if(history.Conflicts(p).Count!=1 || history.CopyDefinitions()[key]!="have:Gold") throw new Exception();
});
Check("Event counters distinguish production, mining and harvest and preserve locked progress", () => {
    var p=Fresh(); var q=p.quests[1]; var o=q.objectives[0]; o.type="produce"; o.target="Steel"; o.count=8;
    var state=new QuestProgress(null,null);
    state.RecordEvent(p,"mine","Steel",null,null,5,true);
    state.RecordEvent(p,"produce","Steel",null,null,4,false);
    if(state.EventValue(p,q,o)!=0) throw new Exception();
    state.RecordEvent(p,"produce","Steel",null,null,4,true);
    var restored=new QuestProgress(state.CopyAchieved(),state.CopyClaimed(),null,null,state.CopyEventCounts(),state.CopyEventSignatures());
    if(restored.EventValue(p,q,o)!=4 || restored.Unlocked(p,q) || restored.Achieved(p,q,o)) throw new Exception();
    o.count=9; restored.RecordEvent(p,"produce","Steel",null,null,5,true);
    if(restored.EventValue(p,q,o)!=9 || !restored.Achieved(p,q,o) || restored.Unlocked(p,q)) throw new Exception();
});
Check("Event filters apply to the output and do not reuse another goal's history", () => {
    var p=Fresh(); var q=p.quests[0]; var o=q.objectives[0]; o.type="produce"; o.target="Bed"; o.variants.Add("Bedroll"); o.stuff="WoodLog"; o.minQuality="Good"; o.count=3;
    var state=new QuestProgress(null,null);
    state.RecordEvent(p,"produce","Bedroll","WoodLog","Excellent",2,true);
    state.RecordEvent(p,"produce","Bed","Steel","Legendary",50,true);
    state.RecordEvent(p,"produce","Bed","WoodLog","Normal",50,true);
    if(state.EventValue(p,q,o)!=2 || state.Achieved(p,q,o)) throw new Exception();
    QuestEditor.EditObjective(o,x=>x.stuff="Steel");
    if(state.EventValue(p,q,o)!=0) throw new Exception();
    state.RecordEvent(p,"produce","Bed","Steel","Good",3,true); if(!state.Achieved(p,q,o)) throw new Exception();
});
Check("Incomplete event counters are rejected rather than silently reset", () => {
    try { QuestProgress.ReadCounts(new[]{"a"}, Array.Empty<int>()); throw new Exception(); } catch(FormatException) { }
    try { new QuestProgress(null,null,null,null,new Dictionary<string,int>{{"a",3}},new Dictionary<string,string>()); throw new Exception(); } catch(FormatException) { }
});
Check("Preview can complete locked goals and chains without changing the draft or real progress", () => {
    var draft=Fresh(); var real=new QuestProgress(null,null); string original=PackCodec.Encode(draft);
    var preview=new QuestPreview(draft); var a=preview.Pack.quests[0]; var b=preview.Pack.quests[1];
    foreach(var goal in b.objectives) preview.Mark(b,goal);
    if(preview.Unlocked(b) || preview.Ready(b) || real.Claimed(draft,draft.quests[0])) throw new Exception();
    foreach(var goal in a.objectives) preview.Mark(a,goal);
    if(preview.Claim(a,null)!=null || !preview.Ready(b)) throw new Exception();
    if(preview.Claim(b,null)!=null || !preview.Claimed(b) || real.Claimed(draft,draft.quests[1])) throw new Exception();
    preview.Reset(); if(preview.Claimed(a) || preview.Achieved(b,b.objectives[0]) || PackCodec.Encode(draft)!=original) throw new Exception();
});
Check("Preview requires a valid reward choice before a simulated claim", () => {
    var draft=Fresh(); var q=draft.quests[0]; q.alternatives.Add(new RewardOption{id="choice.a",title="А",items=new List<Reward>{new Reward()}});
    var preview=new QuestPreview(draft); var pq=preview.Pack.quests[0]; foreach(var goal in pq.objectives) preview.Mark(pq,goal);
    if(preview.Claim(pq,null)==null || preview.Claimed(pq)) throw new Exception();
    if(preview.Claim(pq,"choice.a")!=null || !preview.Claimed(pq)) throw new Exception();
});
Check("Preview claim all validates every choice before changing the simulated chain", () => {
    var draft=Fresh(); var original=PackCodec.Encode(draft);
    draft.quests[1].alternatives.Add(new RewardOption { id="choice.a", title="A", items=new List<Reward>{new Reward()} });
    var preview=new QuestPreview(draft); var first=preview.Pack.quests[0]; var next=preview.Pack.quests[1];
    foreach(var goal in first.objectives) preview.Mark(first,goal);
    foreach(var goal in next.objectives) preview.Mark(next,goal);
    var plan=preview.PrepareClaimAll(); if(plan.questIds.Count!=2) throw new Exception();
    if(preview.FinishClaimAll(plan,null)==null || preview.Claimed(first) || preview.Claimed(next)) throw new Exception();
    if(preview.FinishClaimAll(plan,new Dictionary<string,string>{{next.id,"choice.a"}})!=null) throw new Exception();
    if(!preview.Claimed(first) || !preview.Claimed(next) || preview.FinishClaimAll(plan,new Dictionary<string,string>{{next.id,"choice.a"}})==null) throw new Exception();
    draft.quests[1].alternatives.Clear(); if(PackCodec.Encode(draft)!=original) throw new Exception("draft mutated");
});
Check("Preview claim-all plan does not expand after another goal is reached", () => {
    var preview=new QuestPreview(Fresh()); var first=preview.Pack.quests[0]; var next=preview.Pack.quests[1];
    foreach(var goal in first.objectives) preview.Mark(first,goal);
    var plan=preview.PrepareClaimAll(); foreach(var goal in next.objectives) preview.Mark(next,goal);
    if(plan.questIds.Count!=1 || preview.FinishClaimAll(plan,null)!=null || !preview.Claimed(first) || preview.Claimed(next) || !preview.Ready(next)) throw new Exception();
});
Check("Def aliases count one physical item once without rewriting book identities", () => {
    var p=Fresh(); var q=p.quests[0]; var o=q.objectives[0];
    o.type="have"; o.target="OldWidget"; o.variants=new List<string>{"Widget"};
    string before=PackCodec.Encode(p), signature=QuestProgress.Signature(o);
    string Canonical(string name) => name=="OldWidget"?"Widget":name;
    var tally=new ObjectiveTally(p,Canonical); var item=new object();
    tally.Add(item,"have","Widget",null,null,3);
    tally.Add(item,"have","OldWidget",null,null,3);
    if(tally.Value(p,q,o)!=3 || PackCodec.Encode(p)!=before || QuestProgress.Signature(o)!=signature) throw new Exception();
});
Check("Def aliases preserve saved event counters and do not multiply equivalent variants", () => {
    var p=Fresh(); var q=p.quests[0]; var o=q.objectives[0];
    o.type="produce"; o.target="OldWidget"; o.variants=new List<string>{"Widget"}; o.count=5;
    string before=PackCodec.Encode(p), signature=QuestProgress.Signature(o);
    string Canonical(string name) => name=="OldWidget"?"Widget":name;
    var state=new QuestProgress(null,null);
    state.RecordEvent(p,"produce","OldWidget",null,null,2,true);
    var restored=new QuestProgress(state.CopyAchieved(),state.CopyClaimed(),null,null,state.CopyEventCounts(),state.CopyEventSignatures());
    restored.RecordEvent(p,"produce","Widget",null,null,3,true,Canonical);
    if(restored.EventValue(p,q,o)!=5 || !restored.Achieved(p,q,o) || restored.Claimed(p,q)
        || PackCodec.Encode(p)!=before || QuestProgress.Signature(o)!=signature) throw new Exception();
});
Console.WriteLine($"{passed} checks passed.");
