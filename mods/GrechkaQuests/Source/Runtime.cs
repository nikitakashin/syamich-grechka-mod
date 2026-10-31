using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using Verse;

namespace Grechka.Quests
{
    public sealed class GrechkaMod : Mod
    {
        public static string Root;
        public GrechkaMod(ModContentPack content) : base(content) { Root = content.RootDir; }
    }
    public static class PackFiles
    {
        public static string Folder { get { return Path.Combine(GenFilePaths.ConfigFolderPath, "GrechkaQuests", "Packs"); } }
        public static void EnsureFolder() { Directory.CreateDirectory(Folder); }
        public static List<string> List()
        {
            EnsureFolder(); var files = Directory.GetFiles(Folder, "*.json").ToList();
            if (!string.IsNullOrEmpty(GrechkaMod.Root))
            {
                string bundled = Path.Combine(GrechkaMod.Root, "QuestPacks");
                if (Directory.Exists(bundled)) files.AddRange(Directory.GetFiles(bundled, "*.json"));
            }
            return files.OrderBy(x => x).ToList();
        }
        public static QuestPack Read(string file)
        {
            if (new FileInfo(file).Length > PackCodec.MaxBytes) throw new FormatException("Файл больше 2 МБ.");
            return PackCodec.Decode(File.ReadAllText(file));
        }
        public static string Export(QuestPack pack)
        {
            var errors = PackCodec.Validate(pack); if (errors.Count > 0) throw new FormatException(string.Join("\n", errors));
            EnsureFolder(); string name = pack.id + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".json";
            string path = Path.Combine(Folder, name); string json = PackCodec.Encode(pack);
            if (System.Text.Encoding.UTF8.GetByteCount(json) > PackCodec.MaxBytes) throw new FormatException("Пакет больше 2 МБ.");
            File.WriteAllText(path, json); return path;
        }
    }
    public sealed class QuestGame : GameComponent
    {
        public QuestPack Pack;
        string snapshot;
        List<string> completed = new List<string>();
        List<string> claimed = new List<string>();
        List<string> manual = new List<string>();
        List<Thing> pendingRewards = new List<Thing>();
        readonly Dictionary<string, int> items = new Dictionary<string, int>();
        readonly Dictionary<string, int> buildings = new Dictionary<string, int>();
        public string LoadError;
        public QuestGame(Game game) { }
        public static QuestGame Current { get { return Verse.Current.Game == null ? null : Verse.Current.Game.GetComponent<QuestGame>(); } }
        public int PendingCount { get { return pendingRewards.Count; } }
        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving && Pack != null && LoadError == null) snapshot = PackCodec.Encode(Pack);
            Scribe_Values.Look(ref snapshot, "grechkaQuestPack", null);
            Scribe_Collections.Look(ref completed, "grechkaCompleted", LookMode.Value);
            Scribe_Collections.Look(ref claimed, "grechkaClaimed", LookMode.Value);
            Scribe_Collections.Look(ref manual, "grechkaManual", LookMode.Value);
            Scribe_Collections.Look(ref pendingRewards, "grechkaPendingRewards", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                completed = completed ?? new List<string>(); claimed = claimed ?? new List<string>(); manual = manual ?? new List<string>(); pendingRewards = pendingRewards ?? new List<Thing>();
                Restore();
            }
        }
        void Restore()
        {
            if (!string.IsNullOrEmpty(snapshot))
            {
                try { Pack = PackCodec.Decode(snapshot); return; }
                catch (Exception e) { LoadError = "Не удалось прочитать пакет из сохранения: " + e.Message; Log.Error("[Grechka Quests] " + LoadError); }
            }
            if (Pack == null) Pack = EmptyPack();
        }
        public static QuestPack EmptyPack()
        {
            var p = new QuestPack { id = "custom." + Guid.NewGuid().ToString("N"), title = "Мои квесты" };
            p.chapters.Add(new Chapter()); return p;
        }
        public override void StartedNewGame() { Restore(); }
        public override void LoadedGame() { if (Pack == null) Restore(); }
        public void Apply(QuestPack p)
        {
            // Keep all historical completion/claim keys. Reimporting the same IDs must never duplicate rewards.
            Pack = PackCodec.Decode(PackCodec.Encode(p)); snapshot = PackCodec.Encode(Pack); LoadError = null; Refresh();
        }
        public bool Completed(Quest q) { return completed.Contains(PackCodec.Key(Pack.id, q.id)); }
        public bool Claimed(Quest q) { return claimed.Contains(PackCodec.Key(Pack.id, q.id)); }
        public bool Unlocked(Quest q) { return q.requires.All(id => completed.Contains(PackCodec.Key(Pack.id, id))); }
        public bool Manual(Quest q, Objective o) { return manual.Contains(PackCodec.ObjectiveKey(Pack.id, q.id, o.id)); }
        public void MarkManual(Quest q, Objective o)
        {
            if (!Unlocked(q) || o.type != "manual") return;
            string key = PackCodec.ObjectiveKey(Pack.id, q.id, o.id); if (!manual.Contains(key)) manual.Add(key); Refresh();
        }
        public int Value(Quest q, Objective o)
        {
            if (o.type == "manual") return Manual(q, o) ? o.count : 0;
            int n; return (o.type == "build" ? buildings : items).TryGetValue(o.target, out n) ? n : 0;
        }
        public static string ObjectiveProblem(Objective o)
        {
            if (o.type == "manual") return null;
            ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(o.target);
            if (d == null) return "Нет предмета/постройки: " + o.target;
            if (o.type == "build" && d.category != ThingCategory.Building) return "Цель постройки не является зданием: " + o.target;
            if (o.type == "have" && d.category != ThingCategory.Item) return "Цель наличия не является предметом: " + o.target;
            return null;
        }
        public static string RewardProblem(Reward r)
        {
            ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(r.def);
            if (d == null) return "Нет предмета награды: " + r.def;
            if (d.category != ThingCategory.Item || d.IsCorpse || d.thingClass == null || typeof(MinifiedThing).IsAssignableFrom(d.thingClass) || typeof(UnfinishedThing).IsAssignableFrom(d.thingClass)) return "Награда должна быть обычным предметом: " + r.def;
            if (!string.IsNullOrEmpty(r.stuff))
            {
                ThingDef s = DefDatabase<ThingDef>.GetNamedSilentFail(r.stuff);
                if (s == null || !s.IsStuff || !d.MadeFromStuff || !s.stuffProps.CanMake(d)) return "Неподходящий материал награды: " + r.stuff;
            }
            if (d.MadeFromStuff && string.IsNullOrEmpty(r.stuff) && GenStuff.DefaultStuffFor(d) == null) return "Не найден материал для " + r.def;
            return null;
        }
        public static List<string> Problems(Quest q)
        {
            return q.objectives.Select(ObjectiveProblem).Concat(q.rewards.Select(RewardProblem)).Where(x => x != null).ToList();
        }
        static void Add(Dictionary<string, int> dict, Thing t)
        {
            if (t == null || t.Destroyed) return;
            int n; dict.TryGetValue(t.def.defName, out n); dict[t.def.defName] = (int)Math.Min(int.MaxValue, (long)n + t.stackCount);
        }
        public void Refresh()
        {
            if (Pack == null) Restore(); items.Clear(); buildings.Clear();
            var itemTargets = new HashSet<string>(Pack.quests.SelectMany(q => q.objectives).Where(o => o.type == "have").Select(o => o.target));
            var buildingTargets = new HashSet<string>(Pack.quests.SelectMany(q => q.objectives).Where(o => o.type == "build").Select(o => o.target));
            foreach (Map map in Find.Maps.Where(m => m.IsPlayerHome))
            {
                foreach (string name in itemTargets)
                {
                    ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(name); if (d == null) continue;
                    foreach (Thing t in map.listerThings.ThingsOfDef(d)) if (!t.IsForbidden(Faction.OfPlayer) && !t.Position.Fogged(map)) Add(items, t);
                }
                foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
                {
                    if (pawn.inventory == null) continue;
                    foreach (Thing t in pawn.inventory.innerContainer) if (itemTargets.Contains(t.def.defName)) Add(items, t);
                }
                foreach (string name in buildingTargets)
                {
                    ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(name); if (d == null) continue;
                    foreach (Thing t in map.listerThings.ThingsOfDef(d)) if (t is Building && t.Faction == Faction.OfPlayer) Add(buildings, t);
                }
            }
            // A completed goal is a milestone, not a demand to keep items forever.
            foreach (Quest q in Pack.quests)
                if (!Completed(q) && Unlocked(q) && Problems(q).Count == 0 && q.objectives.All(o => Value(q, o) >= o.count)) completed.Add(PackCodec.Key(Pack.id, q.id));
        }
        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % 250 == 0) { if (Pack != null && Pack.quests.Count > 0) Refresh(); DeliverPending(); }
        }
        public string Claim(Quest q)
        {
            Refresh(); if (!Completed(q) || Claimed(q)) return "Награда недоступна или уже получена.";
            var problems = Problems(q); if (problems.Count > 0) return string.Join("\n", problems);
            long stacks = q.rewards.Sum(r => ((long)r.count + Math.Max(1, DefDatabase<ThingDef>.GetNamed(r.def).stackLimit) - 1) / Math.Max(1, DefDatabase<ThingDef>.GetNamed(r.def).stackLimit));
            if (stacks + pendingRewards.Count > 500) return "Слишком много стопок наград (лимит 500). Уменьшите награду или освободите место для ожидающих.";
            var staged = new List<Thing>();
            try
            {
                foreach (Reward r in q.rewards)
                {
                    ThingDef d = DefDatabase<ThingDef>.GetNamed(r.def);
                    ThingDef stuff = d.MadeFromStuff ? (string.IsNullOrEmpty(r.stuff) ? GenStuff.DefaultStuffFor(d) : DefDatabase<ThingDef>.GetNamed(r.stuff)) : null;
                    int remaining = r.count;
                    while (remaining > 0)
                    {
                        Thing t = ThingMaker.MakeThing(d, stuff); t.stackCount = Math.Min(remaining, Math.Max(1, d.stackLimit)); remaining -= t.stackCount;
                        CompQuality quality = t.TryGetComp<CompQuality>(); if (quality != null) quality.SetQuality(QualityCategory.Normal, ArtGenerationContext.Outsider);
                        staged.Add(t);
                    }
                }
            }
            catch (Exception e) { foreach (var t in staged) if (!t.Destroyed) t.Destroy(); return "Не удалось подготовить награду: " + e.Message; }
            pendingRewards.AddRange(staged); claimed.Add(PackCodec.Key(Pack.id, q.id)); DeliverPending();
            return null;
        }
        public void DeliverPending()
        {
            Map map = Find.CurrentMap;
            if (map == null || !map.IsPlayerHome) map = Find.Maps.FirstOrDefault(m => m.IsPlayerHome);
            if (map == null || pendingRewards.Count == 0) return;
            var pawn = map.mapPawns.FreeColonistsSpawned.FirstOrDefault(); IntVec3 cell = pawn == null ? map.Center : pawn.Position;
            for (int i = pendingRewards.Count - 1; i >= 0; i--)
            {
                Thing t = pendingRewards[i];
                if (t == null || t.Destroyed || t.Spawned) { pendingRewards.RemoveAt(i); continue; }
                if (GenPlace.TryPlaceThing(t, cell, map, ThingPlaceMode.Near)) pendingRewards.RemoveAt(i);
            }
        }
    }
}
