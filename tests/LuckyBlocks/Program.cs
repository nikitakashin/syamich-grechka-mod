using Grechka.LuckyBlocks;
using System.Text.Json;
int checks = 0;
void Check(string name, Action action) { action(); checks++; Console.WriteLine("PASS " + name); }
var path = args.Length > 0 ? args[0] : "mods/GrechkaLuckyBlocks/Config/pools.json";
string source = File.ReadAllText(path);
LuckyConfig Fresh() => LuckyCodec.Decode(source);
void Reject(Action<LuckyConfig> edit) { var config=Fresh(); edit(config); try { LuckyCodec.Decode(LuckyCodec.Encode(config)); } catch(FormatException) { return; } throw new Exception("Invalid config accepted"); }
Check("Default config and JSON round trip",()=>{var a=Fresh(); if(LuckyCodec.Encode(LuckyCodec.Decode(LuckyCodec.Encode(a)))!=LuckyCodec.Encode(a))throw new Exception();});
Check("Tier weights skip zero and preserve boundaries",()=>{if(LuckyCodec.PickIndex(new double[]{0,1,3,0},0)!=1 || LuckyCodec.PickIndex(new double[]{0,1,3,0},0.25)!=2 || LuckyCodec.PickIndex(new double[]{0,1,3,0},0.99999)!=2)throw new Exception();});
Check("No empty or duplicate pools",()=>{Reject(c=>c.pools.Clear());Reject(c=>c.pools.Add(c.pools[0]));Reject(c=>c.pools[0].outcomes.Clear());});
Check("No executable effects or malformed rewards",()=>{Reject(c=>c.pools[0].outcomes[0].effect="execute");Reject(c=>c.pools[0].outcomes[0].items[0].count=-1);Reject(c=>c.pools[0].outcomes[0].items[0].def="../Steel");Reject(c=>c.pools[0].outcomes[0].items[0].quality="Magic");});
Check("Probabilities and effect bounds validated",()=>{Reject(c=>c.raiderChance=2);Reject(c=>c.raiderTierWeights[0]=-1);Reject(c=>c.raiderTierWeights=new(){0,0,0,0,0});Reject(c=>c.pools[0].outcomes[0].weight=0);Reject(c=>{c.pools[0].outcomes[0].effect="animals";c.pools[0].outcomes[0].pawnKind="Cat";c.pools[0].outcomes[0].pawnCount=101;});});
Check("General tiers never contain hostile/animal effects",()=>{if(Fresh().pools.Where(p=>p.id.StartsWith("general.")).SelectMany(p=>p.outcomes).Any(o=>o.effect!="items"))throw new Exception();});
Check("100 cats stays exactly 100",()=>{var cats=Fresh().pools.SelectMany(p=>p.outcomes).Where(o=>o.pawnKind=="Cat").ToList();if(cats.Count==0||cats.Any(o=>o.pawnCount!=100))throw new Exception();});
Check("Every general and thematic tier is present",()=>{var c=Fresh();foreach(var theme in new[]{"general","resources","industry","energy","medical","combat","farm","zoo","mechs","chaos","storage"})for(int t=1;t<=5;t++)if(!c.pools.Any(p=>p.id==theme+".t"+t))throw new Exception();});
Check("Outcome snapshot preserves all reward details",()=>{var o=Fresh().pools.SelectMany(p=>p.outcomes).First(o=>o.items.Any(i=>i.quality!=""));var copy=LuckyCodec.DecodeOutcome(LuckyCodec.EncodeOutcome(o));if(LuckyCodec.EncodeOutcome(copy)!=LuckyCodec.EncodeOutcome(o))throw new Exception();});
Check("Saved outcome is validated before resuming materialization",()=>{
 foreach(string json in new[]{"null", "{\"id\":\"bad\",\"items\":[{\"def\":\"Steel\",\"count\":-1}]}",
  "{\"id\":\"bad\",\"effect\":\"animals\",\"pawnKind\":\"Cat\",\"pawnCount\":101}",
  "{\"id\":\"bad\",\"effect\":\"execute\",\"pawnKind\":\"Cat\"}", "{\"id\":\"bad\",\"requiredMods\":null}"}) {
  bool rejected=false;try{LuckyCodec.DecodeOutcome(json);}catch(FormatException){rejected=true;}if(!rejected)throw new Exception("Invalid saved outcome accepted: "+json);
 }
});
Check("Overflowing raid weights cannot escape validation into raid generation",()=>{Reject(c=>c.raiderTierWeights=new(){double.MaxValue,double.MaxValue,1,1,1});});
Check("Unmapped JSON keys are rejected",()=>{try {LuckyCodec.Decode(source.Replace("\"schemaVersion\": 1","\"schemaVersion\": 1, \"runScript\": \"no\""));}catch(JsonException){return;}throw new Exception();});
Console.WriteLine($"Lucky Blocks: {checks} configuration checks passed.");
