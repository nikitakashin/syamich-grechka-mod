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
Check("Unknown production objective is not mistaken for possession", () => Reject(p => p.quests[0].objectives[0].type = "produce"));
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
Check("Empty objective list rejected", () => Reject(p => p.quests[0].objectives.Clear()));
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
Console.WriteLine($"{passed} checks passed.");
