using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Grechka.LuckyBlocks
{
    public sealed class LuckySettings : ModSettings
    {
        public string configJson = "";
        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref configJson, "configJson", ""); }
    }
    public sealed class LuckyMod : Mod
    {
        public static LuckyMod Instance;
        public static LuckyConfig Config;
        public LuckySettings Settings;
        string draft, status = "";
        Vector2 scroll;
        public LuckyMod(ModContentPack content) : base(content)
        {
            LuckyText.Resolve = (key, fallback) => LanguageDatabase.activeLanguage == null ? fallback : key.Translate().ToString();
            Instance = this; Settings = GetSettings<LuckySettings>();
            try { Config = LuckyCodec.Decode(string.IsNullOrEmpty(Settings.configJson) ? File.ReadAllText(Path.Combine(content.RootDir, "Config", "pools.json")) : Settings.configJson); }
            catch (Exception e) { Config = new LuckyConfig(); status = LuckyText.Get("GrechkaLuckyBlocks_Text020", "Конфиг не загружен: ") + e.Message; Log.Error("[Grechka Lucky Blocks] " + status); }
            draft = Settings.configJson.Length > 0 ? Settings.configJson : LuckyCodec.Encode(Config);
            new Harmony("grechka.luckyblocks").PatchAll(typeof(LuckyMod).Assembly);
        }
        public override string SettingsCategory() => "Grechka Lucky Blocks";
        public override void DoSettingsWindowContents(Rect rect)
        {
            float y = rect.y;
            Widgets.Label(new Rect(rect.x, y, rect.width, 54), LuckyText.Get("GrechkaLuckyBlocks_Text021", "Пулы наград (JSON). Правьте веса, предметы, число животных и шанс блока у рейдера. Применение проверяет конфиг; недоступные опциональные исходы пропускаются.")); y += 58;
            if (Widgets.ButtonText(new Rect(rect.x, y, 130, 32), LuckyText.Get("GrechkaLuckyBlocks_Text022", "Применить"))) Try(() => {
                LuckyConfig candidate = LuckyCodec.Decode(draft);
                var empty = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.GetModExtension<LuckyProperties>() != null)
                    .Select(d => d.GetModExtension<LuckyProperties>().pool).Distinct().Where(id => LuckyResolver.Available(candidate, id).Count == 0).ToList();
                if (empty.Count > 0) throw new FormatException(LuckyText.Get("GrechkaLuckyBlocks_Text023", "Нет доступных исходов: ") + string.Join(", ", empty));
                Config = candidate; Settings.configJson = LuckyCodec.Encode(candidate); WriteSettings(); LuckyMarketValues.Refresh(); status = LuckyText.Get("GrechkaLuckyBlocks_Text024", "Конфиг применён.");
            });
            if (Widgets.ButtonText(new Rect(rect.x + 140, y, 130, 32), LuckyText.Get("GrechkaLuckyBlocks_Text025", "Экспорт"))) Try(() => { LuckyCodec.Decode(draft); Directory.CreateDirectory(Folder); string path = Path.Combine(Folder, "pools-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json"); File.WriteAllText(path, draft); status = path; });
            if (Widgets.ButtonText(new Rect(rect.x + 280, y, 130, 32), LuckyText.Get("GrechkaLuckyBlocks_Text026", "Импорт"))) Try(() => {
                Directory.CreateDirectory(Folder);
                var files = Directory.GetFiles(Folder, "*.json");
                if (files.Length == 0) { status = LuckyText.Get("GrechkaLuckyBlocks_Text027", "Положите JSON в ") + Folder; return; }
                Find.WindowStack.Add(new FloatMenu(files.Select(path => new FloatMenuOption(Path.GetFileName(path), () => Try(() => {
                    if (new FileInfo(path).Length > LuckyCodec.MaxBytes) throw new FormatException(LuckyText.Get("GrechkaLuckyBlocks_Text028", "Файл больше 2 МБ."));
                    string json = File.ReadAllText(path); LuckyCodec.Decode(json); draft = json; status = LuckyText.Get("GrechkaLuckyBlocks_Text029", "Импортирован черновик. Нажмите Применить.");
                }))).ToList()));
            });
            y += 40; Widgets.Label(new Rect(rect.x, y, rect.width, 60), status); y += 66;
            Rect area = new Rect(rect.x, y, rect.width, Math.Max(80, rect.yMax - y));
            Rect content = new Rect(0, 0, Math.Max(100, area.width - 22), Math.Max(area.height, (draft?.Count(c => c == '\n') ?? 1) * 20 + 30));
            Widgets.BeginScrollView(area, ref scroll, content);
            try { draft = Widgets.TextArea(content, draft ?? ""); }
            finally { Widgets.EndScrollView(); }
        }
        static string Folder => Path.Combine(GenFilePaths.ConfigFolderPath, "GrechkaLuckyBlocks");
        void Try(Action action) { try { action(); } catch (Exception e) { status = e.Message; } }
    }
    public sealed class LuckyProperties : DefModExtension
    {
        public string pool = "general.t1";
        public bool priceFromPool = true;
    }
    public static class LuckyResolver
    {
        public static List<LuckyOutcome> Available(LuckyConfig config, string pool) => Available(config, pool, true);
        public static List<LuckyOutcome> Available(LuckyConfig config, string pool, bool requireWorld)
            => config?.pools?.FirstOrDefault(p => p.id == pool)?.outcomes.Where(o => Problem(o, requireWorld) == null).ToList() ?? new List<LuckyOutcome>();
        public static string Problem(LuckyOutcome outcome) => Problem(outcome, true);
        public static string Problem(LuckyOutcome outcome, bool requireWorld)
        {
            if (outcome == null) return LuckyText.Get("GrechkaLuckyBlocks_Text030", "Нет исхода.");
            foreach (string mod in outcome.requiredMods) if (!ModsConfig.IsActive(mod)) return LuckyText.Get("GrechkaLuckyBlocks_Text031", "Нужен мод ") + mod;
            foreach (LuckyItem item in outcome.items)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(item.def);
                if (def == null || def.category != ThingCategory.Item) return LuckyText.Get("GrechkaLuckyBlocks_Text032", "Нет предмета ") + item.def;
                ThingDef stuff = string.IsNullOrEmpty(item.stuff) ? null : DefDatabase<ThingDef>.GetNamedSilentFail(item.stuff);
                if (def.MadeFromStuff && (stuff == null || !GenStuff.AllowedStuffsFor(def).Contains(stuff))) return LuckyText.Get("GrechkaLuckyBlocks_Text033", "Неверный материал ") + item.def;
                if (!def.MadeFromStuff && stuff != null) return LuckyText.Get("GrechkaLuckyBlocks_Text034", "Предмет не сделан из материала: ") + item.def;
                if (!string.IsNullOrEmpty(item.quality) && !def.HasComp(typeof(CompQuality))) return LuckyText.Get("GrechkaLuckyBlocks_Text035", "У предмета нет качества: ") + item.def;
            }
            if (outcome.effect != "items")
            {
                PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(outcome.pawnKind);
                if (kind == null || kind.RaceProps == null) return LuckyText.Get("GrechkaLuckyBlocks_Text036", "Нет вида пешки ") + outcome.pawnKind;
                if (outcome.effect == "animals" && !kind.RaceProps.Animal) return LuckyText.Get("GrechkaLuckyBlocks_Text037", "Нужен вид животного.");
                if (outcome.effect == "hostileMechs" && (!kind.RaceProps.IsMechanoid || requireWorld && Faction.OfMechanoids == null)) return LuckyText.Get("GrechkaLuckyBlocks_Text038", "Недоступны механоиды.");
            }
            return null;
        }
    }
    public sealed class LuckyBlock : ThingWithComps, IThingHolder
    {
        ThingOwner<Thing> remaining;
        string outcomeJson;
        int itemEntry, itemCount, pawnCount;
        bool prepared;
        public LuckyBlock() { remaining = new ThingOwner<Thing>(this, false); }
        public ThingOwner GetDirectlyHeldThings() => remaining;
        public void GetChildHolders(List<IThingHolder> holders) => ThingOwnerUtility.AppendThingHoldersFromThings(holders, remaining);
        public override void ExposeData()
        {
            base.ExposeData(); Scribe_Deep.Look(ref remaining, "remaining", this);
            Scribe_Values.Look(ref outcomeJson, "outcomeJson", null); Scribe_Values.Look(ref itemEntry, "itemEntry", 0);
            Scribe_Values.Look(ref itemCount, "itemCount", 0); Scribe_Values.Look(ref pawnCount, "pawnCount", 0); Scribe_Values.Look(ref prepared, "prepared", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && remaining == null) remaining = new ThingOwner<Thing>(this, false);
        }
        public override void Destroy(DestroyMode mode = DestroyMode.Vanish) { remaining.ClearAndDestroyContents(); base.Destroy(mode); }
        public string Problem()
        {
            if (!string.IsNullOrEmpty(outcomeJson))
            {
                try
                {
                    LuckyOutcome outcome = LuckyCodec.DecodeOutcome(outcomeJson);
                    int expectedPawns = outcome.effect == "items" ? 0 : outcome.pawnCount;
                    if (itemEntry < 0 || itemEntry > outcome.items.Count || itemCount < 0
                        || (itemEntry == outcome.items.Count ? itemCount != 0 : itemCount > outcome.items[itemEntry].count)
                        || pawnCount < 0 || pawnCount > expectedPawns
                        || pawnCount > 0 && itemEntry != outcome.items.Count
                        || prepared && (itemEntry != outcome.items.Count || itemCount != 0 || pawnCount != expectedPawns))
                        return LuckyText.Get("GrechkaLuckyBlocks_Text039", "Повреждено сохранённое состояние открытия; блок и оставшиеся вещи сохранены.");
                    return LuckyResolver.Problem(outcome);
                }
                catch (Exception e) { return e.Message; }
            }
            if (prepared || itemEntry != 0 || itemCount != 0 || pawnCount != 0 || remaining.Count != 0)
                return LuckyText.Get("GrechkaLuckyBlocks_Text040", "У незавершённого открытия отсутствует сохранённый исход; повторный выбор заблокирован.");
            return LuckyResolver.Available(LuckyMod.Config, def.GetModExtension<LuckyProperties>().pool).Count == 0 ? LuckyText.Get("GrechkaLuckyBlocks_Text041", "Нет доступных исходов в настройках Lucky Blocks.") : null;
        }
        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
        {
            foreach (var option in base.GetFloatMenuOptions(selPawn)) yield return option;
            string error = Problem();
            if (error != null) { yield return new FloatMenuOption(LuckyText.Get("GrechkaLuckyBlocks_Text042", "Открыть: ") + error, null); yield break; }
            if (!selPawn.CanReserveAndReach(this, PathEndMode.Touch, Danger.Some)) { yield return new FloatMenuOption(LuckyText.Get("GrechkaLuckyBlocks_Text043", "Открыть: недоступно"), null); yield break; }
            yield return new FloatMenuOption(LuckyText.Get("GrechkaLuckyBlocks_Text044", "Открыть лаки-блок"), () => selPawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Grechka_OpenLuckyBlock"), this), JobTag.Misc));
        }
        public bool Open()
        {
            if (!Spawned || Destroyed) return false;
            string error = Problem();
            if (error != null) { Messages.Message(error, this, MessageTypeDefOf.RejectInput, false); return false; }
            try
            {
                if (string.IsNullOrEmpty(outcomeJson))
                {
                    var choices = LuckyResolver.Available(LuckyMod.Config, def.GetModExtension<LuckyProperties>().pool);
                    outcomeJson = LuckyCodec.EncodeOutcome(choices[LuckyCodec.PickIndex(choices.Select(o => o.weight).ToList(), Math.Min(0.999999999, Rand.Value))]);
                }
                LuckyOutcome outcome = LuckyCodec.DecodeOutcome(outcomeJson);
                if (!prepared)
                {
                    for (; itemEntry < outcome.items.Count; itemEntry++, itemCount = 0)
                    {
                        LuckyItem reward = outcome.items[itemEntry]; ThingDef itemDef = DefDatabase<ThingDef>.GetNamed(reward.def);
                        while (itemCount < reward.count)
                        {
                            Thing thing = ThingMaker.MakeThing(itemDef, string.IsNullOrEmpty(reward.stuff) ? null : DefDatabase<ThingDef>.GetNamed(reward.stuff));
                            thing.stackCount = Math.Min(itemDef.stackLimit, reward.count - itemCount);
                            if (!string.IsNullOrEmpty(reward.quality)) thing.TryGetComp<CompQuality>().SetQuality(Enum.Parse<QualityCategory>(reward.quality), ArtGenerationContext.Outsider);
                            int count = thing.stackCount; if (!remaining.TryAdd(thing, false)) { thing.Destroy(); throw new InvalidOperationException(LuckyText.Get("GrechkaLuckyBlocks_Text045", "Не удалось сохранить результат.")); }
                            itemCount += count;
                        }
                    }
                    if (outcome.effect != "items")
                        while (pawnCount < outcome.pawnCount)
                        {
                            Pawn generated = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed(outcome.pawnKind), outcome.effect == "hostileMechs" ? Faction.OfMechanoids : null);
                            if (!remaining.TryAdd(generated, false)) { generated.Destroy(); throw new InvalidOperationException(LuckyText.Get("GrechkaLuckyBlocks_Text046", "Не удалось сохранить пешку.")); }
                            pawnCount++;
                        }
                    prepared = true;
                }
                var spawnedMechs = new List<Pawn>();
                foreach (Thing thing in remaining.ToList())
                {
                    if (thing is Pawn pawn)
                    {
                        IntVec3 cell = GenRadial.RadialCellsAround(Position, 12, true).FirstOrDefault(c => c.InBounds(Map) && c.Standable(Map) && c.GetFirstPawn(Map) == null, IntVec3.Invalid);
                        if (!cell.IsValid) continue;
                        remaining.Remove(pawn);
                        try { GenSpawn.Spawn(pawn, cell, Map); }
                        catch { if (!pawn.Spawned && !pawn.Destroyed) remaining.TryAdd(pawn, false); throw; }
                        if (outcome.effect == "hostileMechs") spawnedMechs.Add(pawn);
                    }
                    else remaining.TryDrop(thing, Position, Map, ThingPlaceMode.Near, out Thing _, null, null, false);
                }
                if (spawnedMechs.Count > 0) LordMaker.MakeNewLord(Faction.OfMechanoids, new LordJob_AssaultColony(Faction.OfMechanoids), Map, spawnedMechs);
                if (remaining.Count == 0) { Destroy(); return true; }
                Messages.Message(LuckyText.Get("GrechkaLuckyBlocks_Text047", "Часть результата осталась в блоке: освободите место рядом и откройте его снова."), this, MessageTypeDefOf.RejectInput, false);
            }
            catch (Exception e) { Log.Error(LuckyText.Get("GrechkaLuckyBlocks_Text048", "[Grechka Lucky Blocks] Результат сохранён в блоке: ") + e); Messages.Message(LuckyText.Get("GrechkaLuckyBlocks_Text049", "Не удалось завершить открытие. Блок и выбранный результат сохранены."), this, MessageTypeDefOf.RejectInput, false); }
            return false;
        }
    }
    public sealed class JobDriver_OpenLuckyBlock : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.WaitWith(TargetIndex.A, 120, true, true);
            yield return Toils_General.Do(() => (job.targetA.Thing as LuckyBlock)?.Open());
        }
    }
    [HarmonyPatch(typeof(IncidentWorker_RaidEnemy), "GenerateRaidLoot")]
    public static class RaiderLuckyLoot
    {
        static void Postfix(List<Pawn> __2)
        {
            List<Pawn> pawns = __2;
            LuckyConfig config = LuckyMod.Config; if (config == null || pawns == null) return;
            foreach (Pawn pawn in pawns)
            {
                if (!pawn.RaceProps.Humanlike || pawn.Faction == null || !pawn.Faction.HostileTo(Faction.OfPlayer) || pawn.inventory == null
                    || pawn.inventory.innerContainer.Any(t => t is LuckyBlock) || Rand.Value >= config.raiderChance) continue;
                int tier = LuckyCodec.PickIndex(config.raiderTierWeights, Math.Min(0.999999999, Rand.Value)) + 1;
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("Grechka_LuckyBlockT" + tier + "_Item");
                if (def == null || LuckyResolver.Available(config, "general.t" + tier).Count == 0) continue;
                Thing block = ThingMaker.MakeThing(def); if (!pawn.inventory.innerContainer.TryAdd(block)) block.Destroy();
            }
        }
    }
}
