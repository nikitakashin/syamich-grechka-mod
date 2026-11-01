using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Grechka.Quests
{
    public sealed class GrechkaSettings : ModSettings
    {
        public int questHotkey = (int)KeyCode.Alpha5;
        public bool authorMode;
        public string exportFolder = "";
        public string startingPack = "";
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref questHotkey, "questHotkey", (int)KeyCode.Alpha5);
            Scribe_Values.Look(ref authorMode, "authorMode", false);
            Scribe_Values.Look(ref exportFolder, "exportFolder", "");
            Scribe_Values.Look(ref startingPack, "startingPack", "");
        }
    }
    public sealed class GrechkaMod : Mod
    {
        public static string Root;
        public static GrechkaSettings Settings;
        public static bool CapturingHotkey;
        public GrechkaMod(ModContentPack content) : base(content)
        {
            Root = content.RootDir;
            Settings = GetSettings<GrechkaSettings>();
            new Harmony("grechka.quests.events").PatchAll(typeof(GrechkaMod).Assembly);
        }
        public override string SettingsCategory() { return "Grechka Quests"; }
        public override void WriteSettings() { CapturingHotkey = false; base.WriteSettings(); }
        public override void DoSettingsWindowContents(Rect rect)
        {
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 32), "Клавиша открытия книги квестов");
            var key = (KeyCode)Settings.questHotkey;
            string name = key == KeyCode.Alpha5 ? "5" : key.ToString();
            if (Widgets.ButtonText(new Rect(rect.x, rect.y + 38, 240, 36), CapturingHotkey ? "Нажмите клавишу…" : name))
                CapturingHotkey = true;
            Event ev = Event.current;
            if (CapturingHotkey && ev != null && ev.type == EventType.KeyDown)
            {
                if (ev.keyCode != KeyCode.Escape && ev.keyCode != KeyCode.None) Settings.questHotkey = (int)ev.keyCode;
                CapturingHotkey = false;
                ev.Use();
            }
            Widgets.Label(new Rect(rect.x, rect.y + 84, rect.width, 54), "Нажмите кнопку и новую клавишу. Escape отменяет изменение.");
            var conflicts = HotkeyConflicts((KeyCode)Settings.questHotkey);
            if (conflicts.Count > 0)
            {
                Rect warning = new Rect(rect.x + 250, rect.y + 38, Math.Max(0, rect.width - 250), 54);
                Widgets.Label(warning, "⚠ Возможный конфликт: " + string.Join(", ", conflicts.Take(2)) + (conflicts.Count > 2 ? " и ещё " + (conflicts.Count - 2) : ""));
                TooltipHandler.TipRegion(warning, "Та же клавиша назначена: " + string.Join(", ", conflicts));
            }
            Widgets.CheckboxLabeled(new Rect(rect.x, rect.y + 150, rect.width, 36), "Режим автора", ref Settings.authorMode);
            Widgets.Label(new Rect(rect.x, rect.y + 190, rect.width, 55), "Открывает редактор и перенос книг. Изменения сохраняются в отдельный черновик.");
            Widgets.Label(new Rect(rect.x, rect.y + 255, rect.width, 30), "Папка экспорта (пусто — папка Grechka Quests по умолчанию)");
            Settings.exportFolder = Widgets.TextField(new Rect(rect.x, rect.y + 288, rect.width, 32), Settings.exportFolder ?? "", 1024);
            Widgets.Label(new Rect(rect.x, rect.y + 335, rect.width, 30), "Начальная книга для новых колоний");
            string startLabel = string.IsNullOrEmpty(Settings.startingPack) ? "Пример из мода" : Path.GetFileName(Settings.startingPack);
            if (Widgets.ButtonText(new Rect(rect.x, rect.y + 369, Math.Min(rect.width, 400), 36), startLabel))
            {
                try
                {
                    var choices = new List<FloatMenuOption> { new FloatMenuOption("Пример из мода", () => Settings.startingPack = "") };
                    choices.AddRange(PackFiles.List().Select(path => new FloatMenuOption(Path.GetFileName(path), () => Settings.startingPack = path)));
                    Find.WindowStack.Add(new FloatMenu(choices));
                }
                catch (Exception e) { Log.Warning("[Grechka Quests] Не удалось получить список книг: " + e.Message); }
            }
            Widgets.Label(new Rect(rect.x, rect.y + 410, rect.width, 52), "Выбор влияет только на новые колонии. Существующие сохранения сохраняют свою книгу.");
        }
        static List<string> HotkeyConflicts(KeyCode key)
        {
            var result = new List<string>();
            if (key == KeyCode.None) return result;
            var prefs = KeyPrefs.KeyPrefsData?.keyPrefs;
            foreach (KeyBindingDef binding in DefDatabase<KeyBindingDef>.AllDefsListForReading)
            {
                KeyBindingData configured = default;
                bool saved = prefs != null && prefs.TryGetValue(binding, out configured);
                KeyCode first = saved ? configured.keyBindingA : binding.defaultKeyCodeA;
                KeyCode second = saved ? configured.keyBindingB : binding.defaultKeyCodeB;
                if (first == key || second == key) result.Add(binding.LabelCap.ToString());
            }
            return result.Distinct().OrderBy(label => label).ToList();
        }
    }
    public static class PackFiles
    {
        public static string DataRoot { get { return Path.Combine(GenFilePaths.ConfigFolderPath, "GrechkaQuests"); } }
        public static string Folder { get { return string.IsNullOrWhiteSpace(GrechkaMod.Settings?.exportFolder) ? Path.Combine(DataRoot, "Exports") : GrechkaMod.Settings.exportFolder.Trim(); } }
        public static string LegacyFolder { get { return Path.Combine(DataRoot, "Packs"); } }
        public static string StartingPackPath
        {
            get
            {
                string configured = GrechkaMod.Settings?.startingPack;
                if (!string.IsNullOrWhiteSpace(configured)) return configured;
                if (string.IsNullOrEmpty(GrechkaMod.Root)) throw new InvalidOperationException("Не найдена папка Grechka Quests.");
                return Path.Combine(GrechkaMod.Root, "QuestPacks", "grechka-starter.json");
            }
        }
        public static void EnsureFolder() { Directory.CreateDirectory(Folder); }
        public static List<string> List()
        {
            EnsureFolder(); var files = Directory.GetFiles(Folder, "*.json").ToList();
            if (Directory.Exists(LegacyFolder)) files.AddRange(Directory.GetFiles(LegacyFolder, "*.json"));
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
        // Retained for legacy saves only; no new delivery is added here.
        List<Thing> pendingRewards = new List<Thing>();
        List<string> goalKeys = new List<string>(), goalSignatures = new List<string>();
        List<string> legacyKeys = new List<string>(), legacySignatures = new List<string>();
        List<string> choiceKeys = new List<string>(), choiceValues = new List<string>();
        List<string> eventKeys = new List<string>(), eventSignatures = new List<string>();
        List<int> eventValues = new List<int>();
        List<string> bookKeys = new List<string>(), bookSnapshots = new List<string>();
        List<string> definitionKeys = new List<string>(), definitionSignatures = new List<string>();
        int progressVersion;
        string draftOwnerId;
        int rewardMapId = -1;
        List<int> knownHomeMaps = new List<int>();
        List<string> ownedLandingPodIds = new List<string>();
        List<string> graphViewKeys = new List<string>(), chapterBookKeys = new List<string>(), chapterValues = new List<string>();
        List<float> graphViewX = new List<float>(), graphViewY = new List<float>(), graphViewZoom = new List<float>();
        QuestProgress progress;
        QuestProgress Progress { get { return progress ?? (progress = new QuestProgress(null, claimed)); } }
        BookHistory history;
        BookHistory History
        {
            get
            {
                if (history == null) { history = new BookHistory(null, null); history.Seed(Pack, Progress.CopyKnownDefinitions()); }
                return history;
            }
        }
        public string DraftOwnerId
        {
            get
            {
                Guid parsed;
                if (string.IsNullOrEmpty(draftOwnerId))
                {
                    if (Find.World == null || Find.World.info == null) throw new InvalidOperationException("Мир ещё не загружен.");
                    draftOwnerId = DraftStore.OwnerForWorld(Find.World.info.seedString, Find.World.info.persistentRandomValue);
                }
                if (!Guid.TryParseExact(draftOwnerId, "N", out parsed))
                    throw new FormatException("Некорректный сохранённый ID колонии для черновиков.");
                return draftOwnerId;
            }
        }
        public bool HasColony { get { return Find.Maps.Any(m => m.IsPlayerHome); } }
        // An adapter reports only a completed, newly created output. The worker
        // must belong to the colony; a caller may not turn purchases or transfers
        // into production by observing inventory changes.
        public void RecordVerifiedOutput(string kind, Pawn worker, ThingDef product, ThingDef stuff, string quality, int amount)
        {
            if (LoadError != null || Pack == null || !HasColony || !IsOwnedPawn(worker) || product == null || amount <= 0) return;
            Progress.RecordEvent(Pack, kind, product.defName, stuff?.defName, quality, amount, true, CanonicalThingName);
        }
        // Automatic factories authenticate their own building instead of a pawn.
        public void RecordVerifiedMachineOutput(string kind, Thing machine, Thing product, int amount)
        {
            if (product == null) return;
            CompQuality quality = product.TryGetComp<CompQuality>();
            RecordVerifiedMachineOutput(kind, machine, product.def, product.Stuff,
                quality == null ? null : quality.Quality.ToString(), amount);
        }
        public void RecordVerifiedMachineOutput(string kind, Thing machine, ThingDef product, ThingDef stuff, string quality, int amount)
        {
            if (LoadError != null || Pack == null || !HasColony || machine == null || !machine.Spawned
                || machine.Map == null || !machine.Map.IsPlayerHome
                || machine.Faction != Faction.OfPlayer || product == null || amount <= 0) return;
            Progress.RecordEvent(Pack, kind, product.defName, stuff?.defName, quality, amount, true, CanonicalThingName);
        }
        Map RewardMap()
        {
            var homes = Find.Maps.Where(m => m.IsPlayerHome).ToList();
            foreach (Map home in homes) if (!knownHomeMaps.Contains(home.uniqueID)) knownHomeMaps.Add(home.uniqueID);
            Map map = homes.FirstOrDefault(m => m.uniqueID == rewardMapId);
            if (map == null)
            {
                foreach (int id in knownHomeMaps)
                {
                    map = homes.FirstOrDefault(m => m.uniqueID == id);
                    if (map != null) break;
                }
                rewardMapId = map == null ? -1 : map.uniqueID;
            }
            return map;
        }
        ObjectiveTally tally;
        QuestPack watchedHavePack;
        HashSet<string> watchedHaveDefs;
        QuestPack watchedBuildPack;
        HashSet<string> watchedBuildDefs;
        public string LoadError;
        public QuestGame(Game game)
        {
            IndustryEventAdapter.TryInstall();
            FactoryEventAdapter.TryInstall();
            FactoryAutofarmerAdapter.TryInstall();
            AdditionalHarvestAdapter.TryInstall();
            PipeConverterAdapter.TryInstall();
        }
        public static QuestGame Current { get { return Verse.Current.Game == null ? null : Verse.Current.Game.GetComponent<QuestGame>(); } }
        static IntVec3 RewardDropCell(Map map)
        {
            var def = DefDatabase<DesignationDef>.GetNamedSilentFail("Grechka_RewardMarker");
            if (def != null)
            {
                var marker = map.designationManager.AllDesignations.FirstOrDefault(d => d.def == def);
                IntVec3 spot;
                if (marker != null && DropCellFinder.TryFindDropSpotNear(marker.target.Cell, map, out spot, allowFogged: false, canRoofPunch: true)) return spot;
            }
            return DropCellFinder.TradeDropSpot(map);
        }
        public int PendingCount { get { return pendingRewards.Count; } }
        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving && Pack != null && LoadError == null) snapshot = PackCodec.Encode(Pack);
            Scribe_Values.Look(ref snapshot, "grechkaQuestPack", null);
            Scribe_Collections.Look(ref completed, "grechkaCompleted", LookMode.Value);
            Scribe_Collections.Look(ref claimed, "grechkaClaimed", LookMode.Value);
            Scribe_Collections.Look(ref manual, "grechkaManual", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.Saving && progress != null && LoadError == null)
            {
                var goals = progress.CopyAchieved().OrderBy(x => x.Key).ToList();
                goalKeys = goals.Select(x => x.Key).ToList(); goalSignatures = goals.Select(x => x.Value).ToList();
                var legacy = progress.CopyLegacyCompletions().OrderBy(x => x.Key).ToList();
                legacyKeys = legacy.Select(x => x.Key).ToList(); legacySignatures = legacy.Select(x => x.Value).ToList();
                var choices = progress.CopyRewardChoices().OrderBy(x => x.Key).ToList();
                choiceKeys = choices.Select(x => x.Key).ToList(); choiceValues = choices.Select(x => x.Value).ToList();
                var counts = progress.CopyEventCounts().OrderBy(x => x.Key).ToList();
                var signatures = progress.CopyEventSignatures();
                eventKeys = counts.Select(x => x.Key).ToList(); eventValues = counts.Select(x => x.Value).ToList();
                eventSignatures = eventKeys.Select(key => signatures[key]).ToList();
                claimed = progress.CopyClaimed();
            }
            if (Scribe.mode == LoadSaveMode.Saving && Pack != null && LoadError == null)
            {
                History.Seed(Pack, Progress.CopyKnownDefinitions());
                var books = History.CopyBooks().OrderBy(x => x.Key).ToList();
                var definitions = History.CopyDefinitions().OrderBy(x => x.Key).ToList();
                bookKeys = books.Select(x => x.Key).ToList(); bookSnapshots = books.Select(x => x.Value).ToList();
                definitionKeys = definitions.Select(x => x.Key).ToList(); definitionSignatures = definitions.Select(x => x.Value).ToList();
            }
            Scribe_Collections.Look(ref goalKeys, "grechkaGoalKeys", LookMode.Value);
            Scribe_Collections.Look(ref goalSignatures, "grechkaGoalSignatures", LookMode.Value);
            Scribe_Collections.Look(ref legacyKeys, "grechkaLegacyCompletionKeys", LookMode.Value);
            Scribe_Collections.Look(ref legacySignatures, "grechkaLegacyCompletionSignatures", LookMode.Value);
            Scribe_Collections.Look(ref choiceKeys, "grechkaRewardChoiceKeys", LookMode.Value);
            Scribe_Collections.Look(ref choiceValues, "grechkaRewardChoiceValues", LookMode.Value);
            Scribe_Collections.Look(ref eventKeys, "grechkaEventKeys", LookMode.Value);
            Scribe_Collections.Look(ref eventValues, "grechkaEventCounts", LookMode.Value);
            Scribe_Collections.Look(ref eventSignatures, "grechkaEventSignatures", LookMode.Value);
            Scribe_Collections.Look(ref bookKeys, "grechkaBookArchiveKeys", LookMode.Value);
            Scribe_Collections.Look(ref bookSnapshots, "grechkaBookArchiveValues", LookMode.Value);
            Scribe_Collections.Look(ref definitionKeys, "grechkaObjectiveDefinitionKeys", LookMode.Value);
            Scribe_Collections.Look(ref definitionSignatures, "grechkaObjectiveDefinitionValues", LookMode.Value);
            Scribe_Values.Look(ref progressVersion, "grechkaProgressVersion", 0);
            Scribe_Values.Look(ref draftOwnerId, "grechkaDraftOwnerId", null);
            Scribe_Values.Look(ref rewardMapId, "grechkaRewardMapId", -1);
            Scribe_Collections.Look(ref knownHomeMaps, "grechkaKnownHomeMaps", LookMode.Value);
            Scribe_Collections.Look(ref ownedLandingPodIds, "grechkaOwnedLandingPodIds", LookMode.Value);
            Scribe_Collections.Look(ref graphViewKeys, "grechkaGraphViewKeys", LookMode.Value);
            Scribe_Collections.Look(ref graphViewX, "grechkaGraphViewX", LookMode.Value);
            Scribe_Collections.Look(ref graphViewY, "grechkaGraphViewY", LookMode.Value);
            Scribe_Collections.Look(ref graphViewZoom, "grechkaGraphViewZoom", LookMode.Value);
            Scribe_Collections.Look(ref chapterBookKeys, "grechkaChapterBookKeys", LookMode.Value);
            Scribe_Collections.Look(ref chapterValues, "grechkaChapterValues", LookMode.Value);
            Scribe_Collections.Look(ref pendingRewards, "grechkaPendingRewards", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                completed = completed ?? new List<string>(); claimed = claimed ?? new List<string>(); manual = manual ?? new List<string>(); pendingRewards = pendingRewards ?? new List<Thing>(); knownHomeMaps = knownHomeMaps ?? new List<int>(); ownedLandingPodIds = ownedLandingPodIds ?? new List<string>();
                NormalizeGraphViews();
                Restore();
                try
                {
                    QuestProgress.ValidateProgressVersion(progressVersion);
                    var goals = QuestProgress.ReadPairs(goalKeys, goalSignatures);
                    var legacy = QuestProgress.ReadPairs(legacyKeys, legacySignatures);
                    var choices = QuestProgress.ReadPairs(choiceKeys, choiceValues);
                    var counts = QuestProgress.ReadCounts(eventKeys, eventValues);
                    var signatures = QuestProgress.ReadPairs(eventKeys, eventSignatures);
                    progress = new QuestProgress(goals, claimed, legacy, choices, counts, signatures);
                    if (progressVersion == 0 && LoadError == null)
                    {
                        foreach (Quest q in Pack.quests)
                        {
                            if (completed.Contains(PackCodec.Key(Pack.id, q.id))) Progress.RememberLegacyCompletion(Pack, q);
                            foreach (Objective o in q.objectives.Where(x => x.type == "manual"))
                                if (manual.Contains(PackCodec.ObjectiveKey(Pack.id, q.id, o.id))) Progress.Observe(Pack, q, o, o.count, true);
                        }
                        progressVersion = 1;
                    }
                    history = new BookHistory(QuestProgress.ReadPairs(bookKeys, bookSnapshots), QuestProgress.ReadPairs(definitionKeys, definitionSignatures));
                    history.Seed(Pack, Progress.CopyKnownDefinitions());
                }
                catch (Exception e) { LoadError = "Не удалось прочитать прогресс заданий: " + e.Message; Log.Error("[Grechka Quests] " + LoadError); }
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
        public override void StartedNewGame()
        {
            progressVersion = 1;
            try { Pack = PackFiles.Read(PackFiles.StartingPackPath); snapshot = PackCodec.Encode(Pack); }
            catch (Exception e)
            {
                Pack = EmptyPack();
                LoadError = "Не удалось загрузить выбранную стартовую книгу: " + e.Message;
                Log.Error("[Grechka Quests] " + LoadError);
            }
        }
        public override void LoadedGame() { if (Pack == null) Restore(); }
        void NormalizeGraphViews()
        {
            graphViewKeys = graphViewKeys ?? new List<string>(); graphViewX = graphViewX ?? new List<float>();
            graphViewY = graphViewY ?? new List<float>(); graphViewZoom = graphViewZoom ?? new List<float>();
            chapterBookKeys = chapterBookKeys ?? new List<string>(); chapterValues = chapterValues ?? new List<string>();
            if (graphViewKeys.Count != graphViewX.Count || graphViewKeys.Count != graphViewY.Count || graphViewKeys.Count != graphViewZoom.Count)
            {
                Log.Warning("[Grechka Quests] Настройки вида графа повреждены и сброшены; история квестов не затронута.");
                graphViewKeys.Clear(); graphViewX.Clear(); graphViewY.Clear(); graphViewZoom.Clear();
            }
            if (chapterBookKeys.Count != chapterValues.Count) { chapterBookKeys.Clear(); chapterValues.Clear(); }
        }
        public void GraphView(string book, string chapter, out Vector2 pan, out float zoom)
        {
            int i = graphViewKeys.IndexOf(PackCodec.Key(book, chapter));
            if (i < 0 || !float.IsFinite(graphViewX[i]) || !float.IsFinite(graphViewY[i]) || !float.IsFinite(graphViewZoom[i]))
            { pan = new Vector2(20, 20); zoom = 1f; return; }
            pan = new Vector2(graphViewX[i], graphViewY[i]); zoom = Mathf.Clamp(graphViewZoom[i], 0.45f, 1.6f);
        }
        public void SetGraphView(string book, string chapter, Vector2 pan, float zoom)
        {
            string key = PackCodec.Key(book, chapter); int i = graphViewKeys.IndexOf(key);
            if (i < 0) { i = graphViewKeys.Count; graphViewKeys.Add(key); graphViewX.Add(20); graphViewY.Add(20); graphViewZoom.Add(1); }
            graphViewX[i] = Mathf.Clamp(pan.x, -100000, 100000); graphViewY[i] = Mathf.Clamp(pan.y, -100000, 100000);
            graphViewZoom[i] = Mathf.Clamp(zoom, 0.45f, 1.6f);
        }
        public string LastChapter(string book)
        {
            int i = chapterBookKeys.IndexOf(book); return i < 0 ? null : chapterValues[i];
        }
        public void SetLastChapter(string book, string chapter)
        {
            int i = chapterBookKeys.IndexOf(book);
            if (i < 0) { chapterBookKeys.Add(book); chapterValues.Add(chapter); }
            else chapterValues[i] = chapter;
        }
        public QuestPack ValidateApply(QuestPack p)
        {
            if (LoadError != null) throw new InvalidOperationException(LoadError);
            QuestPack candidate = PackCodec.Decode(PackCodec.Encode(p));
            var conflicts = History.Conflicts(candidate);
            if (conflicts.Count != 0) throw new InvalidOperationException("Смысл цели изменён с прежним ID: " + string.Join(", ", conflicts.Take(5)) + ". Назначьте новый ID в редакторе.");
            return candidate;
        }
        public List<QuestPack> SavedBooks() { return History.SavedBooks(); }
        public void Apply(QuestPack p)
        {
            QuestPack candidate = ValidateApply(p);
            History.Seed(Pack, Progress.CopyKnownDefinitions());
            History.Remember(candidate);
            Pack = candidate; snapshot = PackCodec.Encode(Pack); LoadError = null; Refresh();
        }
        public bool Completed(Quest q) { return Claimed(q) || Progress.ObjectivesComplete(Pack, q); }
        public bool Claimed(Quest q) { return Progress.Claimed(Pack, q); }
        public string ChosenReward(Quest q) { return Progress.ChosenReward(Pack, q); }
        public bool Unlocked(Quest q) { return Progress.Unlocked(Pack, q); }
        public bool CanClaim(Quest q)
        {
            return Pack != null && LoadError == null && HasColony && !Claimed(q) && Unlocked(q)
                && Completed(q) && Problems(q).Count == 0;
        }
        public bool HasClaimableQuest()
        {
            return Pack != null && LoadError == null && HasColony && Pack.quests.Any(CanClaim);
        }
        public bool Manual(Quest q, Objective o) { return Progress.Achieved(Pack, q, o); }
        public bool ObjectiveAchieved(Quest q, Objective o) { return Progress.Achieved(Pack, q, o); }
        public void MarkManual(Quest q, Objective o)
        {
            if (LoadError != null || !HasColony || Claimed(q) || o.type != "manual") return;
            Progress.Observe(Pack, q, o, o.count, true); Refresh();
        }
        public int Value(Quest q, Objective o)
        {
            if (o.type == "manual") return 0;
            if (IsEventObjective(o)) return Progress.EventValue(Pack, q, o);
            return tally == null ? 0 : tally.Value(Pack, q, o);
        }
        public static bool IsEventObjective(Objective o) { return o.type == "produce" || o.type == "mine" || o.type == "harvest"; }
        public static string EventSourceSummary(Objective o)
        {
            string standard = o.type == "produce" ? "Стандартные рецепты и выдача питательной пасты"
                : o.type == "mine" ? "Добыча ванильных жил"
                : "Стандартный сбор растений" + (AdditionalHarvestAdapter.Installed
                    ? " и дополнительные продукты урожая" : "; дополнительные продукты не подключены");
            if (o.type == "produce" && PipeConverterAdapter.Installed)
                standard += "; физический выход PipeSystem";
            if (!ModsConfig.IsActive("VanillaExpanded.VFEFactory")) return standard + ".";
            bool ready = o.type == "harvest" ? FactoryAutofarmerAdapter.Installed : FactoryEventAdapter.Installed;
            if (!ready) return standard + ". VFE Factory установлен, но его адаптер не подключился.";
            return standard + (o.type == "harvest" ? "; автоферма VFE Factory." : o.type == "mine"
                ? "; буровая VFE Factory." : "; предметные процессы VFE Factory.");
        }
        private static string CompatibleThingName(string name)
        {
            return string.IsNullOrEmpty(name) ? name
                : BackCompatibility.BackCompatibleDefName(typeof(ThingDef), name, false, null) ?? name;
        }
        public static ThingDef ResolveThingDef(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            ThingDef current = DefDatabase<ThingDef>.GetNamedSilentFail(name);
            return current ?? DefDatabase<ThingDef>.GetNamedSilentFail(CompatibleThingName(name));
        }
        public static ThingDef RequireThingDef(string name)
        {
            return ResolveThingDef(name) ?? DefDatabase<ThingDef>.GetNamed(CompatibleThingName(name));
        }
        internal static string CanonicalThingName(string name)
        {
            return string.IsNullOrEmpty(name) ? name : QuestGame.ResolveThingDef(name)?.defName ?? name;
        }
        private static bool GoalTargetsDef(Objective goal, string defName)
        {
            return CanonicalThingName(goal.target) == defName
                || goal.variants.Any(name => CanonicalThingName(name) == defName);
        }
        internal bool WatchesEventTarget(string kind, ThingDef product)
        {
            return product != null && Pack != null && LoadError == null && HasColony
                && Pack.quests.Any(q => q.objectives.Any(o => o.type == kind
                    && GoalTargetsDef(o, product.defName)));
        }
        internal bool WatchesHaveTarget(ThingDef product)
        {
            return product != null && Pack != null && LoadError == null && HasColony
                && Pack.quests.Any(q => q.objectives.Any(o => o.type == "have"
                    && GoalTargetsDef(o, product.defName)));
        }
        public static string ObjectiveProblem(Objective o)
        {
            if (o.type == "manual") return null;
            foreach (string name in new[] { o.target }.Concat(o.variants))
            {
                ThingDef d = QuestGame.ResolveThingDef(name);
                if (d == null) return "Нет предмета/постройки: " + name;
                if (o.type == "build" && d.category != ThingCategory.Building) return "Цель постройки не является зданием: " + name;
                if (o.type != "build" && d.category != ThingCategory.Item) return "Цель не является предметом: " + name;
                if (!string.IsNullOrEmpty(o.stuff))
                {
                    ThingDef s = QuestGame.ResolveThingDef(o.stuff);
                    if (s == null || !s.IsStuff || !d.MadeFromStuff || !s.stuffProps.CanMake(d)) return "Неподходящий материал цели: " + o.stuff + " / " + name;
                }
                if (!string.IsNullOrEmpty(o.minQuality) && (d.comps == null || !d.comps.Any(c => c.compClass != null && typeof(CompQuality).IsAssignableFrom(c.compClass))))
                    return "У цели нет качества: " + name;
            }
            return null;
        }
        public static string RewardProblem(Reward r)
        {
            ThingDef d = QuestGame.ResolveThingDef(r.def);
            if (d == null) return "Нет предмета награды: " + r.def;
            if (d.category != ThingCategory.Item || d.IsCorpse || d.thingClass == null || typeof(MinifiedThing).IsAssignableFrom(d.thingClass) || typeof(UnfinishedThing).IsAssignableFrom(d.thingClass)) return "Награда должна быть обычным предметом: " + r.def;
            if (!string.IsNullOrEmpty(r.stuff))
            {
                ThingDef s = QuestGame.ResolveThingDef(r.stuff);
                if (s == null || !s.IsStuff || !d.MadeFromStuff || !s.stuffProps.CanMake(d)) return "Неподходящий материал награды: " + r.stuff;
            }
            if (d.MadeFromStuff && string.IsNullOrEmpty(r.stuff) && GenStuff.DefaultStuffFor(d) == null) return "Не найден материал для " + r.def;
            if (!string.IsNullOrEmpty(r.quality) && (d.comps == null || !d.comps.Any(c => c.compClass != null && typeof(CompQuality).IsAssignableFrom(c.compClass))))
                return "У этой награды нет качества: " + r.def;
            return null;
        }
        public static List<string> Problems(Quest q)
        {
            return q.objectives.Select(ObjectiveProblem).Concat(q.rewards.Select(RewardProblem))
                .Concat(q.alternatives.SelectMany(a => a.items).Select(RewardProblem)).Where(x => x != null).ToList();
        }
        static void CountThing(ObjectiveTally tally, string type, Thing t, string location)
        {
            if (t == null || t.Destroyed) return;
            var quality = (t as ThingWithComps)?.GetComp<CompQuality>();
            tally.Add(t, type, t.def.defName, t.Stuff?.defName, quality == null ? null : quality.Quality.ToString(), t.stackCount, location);
            if (type == "have")
                foreach (Thing stored in StorageInventoryAdapter.Held(t)) CountThing(tally, "have", stored, location);
        }
        static bool IsOwnedPawn(Pawn pawn)
        {
            return pawn != null && pawn.Faction == Faction.OfPlayer && !pawn.IsPrisoner;
        }
        static void CountPawn(ObjectiveTally tally, Pawn pawn, string location)
        {
            if (!IsOwnedPawn(pawn)) return;
            if (pawn.inventory != null) foreach (Thing item in pawn.inventory.innerContainer) CountThing(tally, "have", item, location);
            if (pawn.equipment != null) foreach (Thing item in pawn.equipment.AllEquipmentListForReading) CountThing(tally, "have", item, location);
            if (pawn.apparel != null) foreach (Thing item in pawn.apparel.WornApparel) CountThing(tally, "have", item, location);
            if (pawn.carryTracker != null) CountThing(tally, "have", pawn.carryTracker.CarriedThing, location);
        }
        static List<ThingDef> transportDefs;
        static List<ThingDef> skyfallerDefs;
        static readonly System.Reflection.FieldInfo flightPodsField = AccessTools.Field(typeof(TravellingTransporters), "transporters");
        internal void MarkOwnedLandingPod(Skyfaller pod)
        {
            if (pod == null || string.IsNullOrEmpty(pod.ThingID)) return;
            if (!ownedLandingPodIds.Contains(pod.ThingID)) ownedLandingPodIds.Add(pod.ThingID);
        }
        static void CountCargo(ObjectiveTally tally, ThingOwner contents, string location)
        {
            if (contents == null) return;
            foreach (Thing thing in contents)
            {
                if (thing is Pawn pawn) CountPawn(tally, pawn, location);
                else CountThing(tally, "have", thing, location);
            }
        }
        void CountOwnTransportCargo(ObjectiveTally tally)
        {
            if (transportDefs == null)
                transportDefs = DefDatabase<ThingDef>.AllDefsListForReading
                    .Where(d => d.comps != null && d.comps.Any(c => c is CompProperties_Transporter)).ToList();
            foreach (Map map in Find.Maps)
                foreach (ThingDef def in transportDefs)
                    foreach (Thing thing in map.listerThings.ThingsOfDef(def))
                    {
                        if (thing.Faction != Faction.OfPlayer || !(thing is ThingWithComps building)) continue;
                        CountCargo(tally, building.GetComp<CompTransporter>()?.SearchableContents, "В своих загруженных капсулах");
                    }
            foreach (TravellingTransporters flight in Find.WorldObjects.AllWorldObjects.OfType<TravellingTransporters>())
            {
                if (!flight.IsPlayerControlled) continue;
                if (!(flightPodsField?.GetValue(flight) is List<ActiveTransporterInfo> pods)) continue;
                foreach (ActiveTransporterInfo pod in pods)
                    CountCargo(tally, pod.innerContainer, "В своих капсулах в пути");
            }
            if (ownedLandingPodIds.Count == 0) return;
            if (skyfallerDefs == null)
                skyfallerDefs = DefDatabase<ThingDef>.AllDefsListForReading
                    .Where(d => d.thingClass != null && typeof(Skyfaller).IsAssignableFrom(d.thingClass)).ToList();
            var marked = new HashSet<string>(ownedLandingPodIds);
            var stillPresent = new HashSet<string>();
            foreach (Map map in Find.Maps)
                foreach (ThingDef def in skyfallerDefs)
                    foreach (Thing thing in map.listerThings.ThingsOfDef(def))
                    {
                        if (!(thing is Skyfaller pod) || !marked.Contains(pod.ThingID)) continue;
                        stillPresent.Add(pod.ThingID);
                        foreach (Thing inner in pod.GetDirectlyHeldThings())
                            if (inner is ActiveTransporter transporter)
                                CountCargo(tally, transporter.Contents?.innerContainer, "В своих капсулах при снижении");
                    }
            ownedLandingPodIds.RemoveAll(id => !stillPresent.Contains(id));
        }
        public Dictionary<string, int> ObjectiveBreakdown(Quest q, Objective o)
        {
            return tally == null ? new Dictionary<string, int>() : tally.Breakdown(Pack, q, o);
        }
        // Called after a relevant item is placed or merged. Check the complete
        // count for goals mentioning this def, including their alternate defs.
        // This records a brief threshold immediately without treating a transfer
        // as production or summing the same physical item twice.
        internal void ObservePotentialItem(ThingDef changed)
        {
            if (changed == null || Pack == null || LoadError != null || !HasColony) return;
            if (!ReferenceEquals(watchedHavePack, Pack))
            {
                watchedHavePack = Pack;
                watchedHaveDefs = new HashSet<string>(Pack.quests.SelectMany(q => q.objectives)
                    .Where(o => o.type == "have").SelectMany(o => new[] { o.target }.Concat(o.variants)).Select(CanonicalThingName));
            }
            if (!watchedHaveDefs.Contains(changed.defName)) return;
            var affected = Pack.quests.SelectMany(q => q.objectives.Where(o => o.type == "have"
                && GoalTargetsDef(o, changed.defName))
                .Select(o => new KeyValuePair<Quest, Objective>(q, o))).ToList();
            if (affected.Count == 0) return;
            var names = affected.SelectMany(pair => new[] { pair.Value.target }.Concat(pair.Value.variants)).Distinct();
            var current = new ObjectiveTally(Pack, CanonicalThingName);
            foreach (Map map in Find.Maps)
            {
                if (map.IsPlayerHome)
                {
                    foreach (string name in names)
                    {
                        ThingDef def = QuestGame.ResolveThingDef(name);
                        if (def == null) continue;
                        foreach (Thing item in map.listerThings.ThingsOfDef(def))
                            if (!item.Position.Fogged(map)) CountThing(current, "have", item, null);
                    }
                }
                foreach (Thing stored in StorageInventoryAdapter.OnMap(map)) CountThing(current, "have", stored, null);
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned) CountPawn(current, pawn, null);
            }
            foreach (Caravan caravan in Find.WorldObjects.Caravans)
                if (caravan.Faction == Faction.OfPlayer)
                    foreach (Pawn pawn in caravan.PawnsListForReading) CountPawn(current, pawn, null);
            CountOwnTransportCargo(current);
            foreach (var pair in affected)
                if (ObjectiveProblem(pair.Value) == null)
                    Progress.Observe(Pack, pair.Key, pair.Value, current.Value(Pack, pair.Key, pair.Value), true);
        }
        internal void ObservePotentialBuilding(ThingDef changed)
        {
            if (changed == null || Pack == null || LoadError != null || !HasColony) return;
            if (!ReferenceEquals(watchedBuildPack, Pack))
            {
                watchedBuildPack = Pack;
                watchedBuildDefs = new HashSet<string>(Pack.quests.SelectMany(q => q.objectives)
                    .Where(o => o.type == "build").SelectMany(o => new[] { o.target }.Concat(o.variants)).Select(CanonicalThingName));
            }
            if (!watchedBuildDefs.Contains(changed.defName)) return;
            var affected = Pack.quests.SelectMany(q => q.objectives.Where(o => o.type == "build"
                && GoalTargetsDef(o, changed.defName))
                .Select(o => new KeyValuePair<Quest, Objective>(q, o))).ToList();
            var names = affected.SelectMany(pair => new[] { pair.Value.target }.Concat(pair.Value.variants)).Distinct();
            var current = new ObjectiveTally(Pack, CanonicalThingName);
            foreach (Map map in Find.Maps.Where(m => m.IsPlayerHome))
                foreach (string name in names)
                {
                    ThingDef def = QuestGame.ResolveThingDef(name);
                    if (def == null) continue;
                    foreach (Thing building in map.listerThings.ThingsOfDef(def))
                        if (building is Building && building.Faction == Faction.OfPlayer)
                            CountThing(current, "build", building, null);
                }
            foreach (var pair in affected)
                if (ObjectiveProblem(pair.Value) == null)
                    Progress.Observe(Pack, pair.Key, pair.Value, current.Value(Pack, pair.Key, pair.Value), true);
        }
        public void Refresh()
        {
            if (Pack == null) Restore();
            if (LoadError != null || !HasColony) { tally = null; return; }
            tally = new ObjectiveTally(Pack, CanonicalThingName);
            RewardMap();
            var itemTargets = new HashSet<string>(Pack.quests.SelectMany(q => q.objectives).Where(o => o.type == "have").SelectMany(o => new[] { o.target }.Concat(o.variants)));
            var buildingTargets = new HashSet<string>(Pack.quests.SelectMany(q => q.objectives).Where(o => o.type == "build").SelectMany(o => new[] { o.target }.Concat(o.variants)));
            foreach (Map map in Find.Maps)
            {
                if (map.IsPlayerHome)
                {
                    foreach (string name in itemTargets)
                    {
                        ThingDef d = QuestGame.ResolveThingDef(name); if (d == null) continue;
                        foreach (Thing item in map.listerThings.ThingsOfDef(d)) if (!item.Position.Fogged(map)) CountThing(tally, "have", item, "На домашних картах");
                    }
                    foreach (string name in buildingTargets)
                    {
                        ThingDef d = QuestGame.ResolveThingDef(name); if (d == null) continue;
                        foreach (Thing building in map.listerThings.ThingsOfDef(d)) if (building is Building && building.Faction == Faction.OfPlayer) CountThing(tally, "build", building, "На домашних картах");
                    }
                }
                foreach (Thing stored in StorageInventoryAdapter.OnMap(map)) CountThing(tally, "have", stored, "В Grechka Storage");
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned) CountPawn(tally, pawn, "У своих пешек на картах");
            }
            foreach (Caravan caravan in Find.WorldObjects.Caravans)
                if (caravan.Faction == Faction.OfPlayer) foreach (Pawn pawn in caravan.PawnsListForReading) CountPawn(tally, pawn, "В своих караванах");
            CountOwnTransportCargo(tally);
            foreach (Quest q in Pack.quests)
                foreach (Objective o in q.objectives)
                    if (ObjectiveProblem(o) == null)
                        Progress.Observe(Pack, q, o, Value(q, o), true);
        }
        public override void GameComponentOnGUI()
        {
            base.GameComponentOnGUI();
            Event ev = Event.current;
            if (GrechkaMod.Settings == null || GrechkaMod.CapturingHotkey || ev == null || ev.type != EventType.KeyDown
                || GUIUtility.keyboardControl != 0 || ev.keyCode != (KeyCode)GrechkaMod.Settings.questHotkey) return;
            MainButtonDef button = DefDatabase<MainButtonDef>.GetNamedSilentFail("GrechkaQuests");
            if (button == null || Find.MainTabsRoot == null) return;
            Find.MainTabsRoot.ToggleTab(button, false);
            ev.Use();
        }
        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % 250 == 0)
            {
                RewardMap();
                if (Pack != null && Pack.quests.Count > 0) Refresh();
            }
        }
        public QuestClaimPlan PrepareClaim(Quest one, out string error)
        {
            error = null;
            if (!HasColony) { error = "Создайте колонию"; return null; }
            if (LoadError != null) { error = LoadError; return null; }
            Refresh();
            List<string> ids;
            if (one == null) ids = Progress.PlanClaimAll(Pack, true, q => Problems(q).Count == 0);
            else
            {
                if (!Pack.quests.Contains(one) || !CanClaim(one)) { error = "Награда недоступна или уже получена."; return null; }
                ids = new List<string> { one.id };
            }
            if (ids.Count == 0) { error = "Нет доступных наград."; return null; }
            return new QuestClaimPlan(PackCodec.Encode(Pack), ids);
        }

        public string FinishClaim(QuestClaimPlan plan, IDictionary<string, string> choices)
        {
            if (LoadError != null) return LoadError;
            string problem = Progress.ValidatePlan(Pack, plan, choices, HasColony, q => Problems(q).Count == 0);
            if (problem != null) return problem;
            Map map = RewardMap();
            if (map == null) return "Создайте колонию";

            var prepared = new List<KeyValuePair<Quest, List<Thing>>>();
            try
            {
                foreach (string id in plan.questIds)
                {
                    Quest q = Pack.quests.First(x => x.id == id);
                    var items = new List<Thing>();
                    prepared.Add(new KeyValuePair<Quest, List<Thing>>(q, items));
                    IEnumerable<Reward> rewards = q.rewards;
                    if (q.alternatives.Count > 0) rewards = rewards.Concat(q.alternatives.First(a => a.id == choices[q.id]).items);
                    foreach (Reward r in rewards)
                    {
                        ThingDef d = QuestGame.RequireThingDef(r.def);
                        ThingDef stuff = d.MadeFromStuff ? (string.IsNullOrEmpty(r.stuff) ? GenStuff.DefaultStuffFor(d) : QuestGame.RequireThingDef(r.stuff)) : null;
                        int remaining = r.count;
                        while (remaining > 0)
                        {
                            if (items.Count >= 500) throw new InvalidOperationException("Слишком много стопок в одном задании (лимит 500).");
                            Thing item = ThingMaker.MakeThing(d, stuff);
                            items.Add(item);
                            item.stackCount = Math.Min(remaining, Math.Max(1, d.stackLimit)); remaining -= item.stackCount;
                            CompQuality quality = item.TryGetComp<CompQuality>();
                            if (quality != null) quality.SetQuality(string.IsNullOrEmpty(r.quality) ? QualityCategory.Normal : (QualityCategory)Enum.Parse(typeof(QualityCategory), r.quality), ArtGenerationContext.Outsider);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                DestroyUntransferred(prepared.SelectMany(entry => entry.Value));
                return "Не удалось подготовить награды: " + e.Message;
            }

            int count = 0;
            foreach (var entry in prepared)
            {
                Quest q = entry.Key; var items = entry.Value;
                string chosen = q.alternatives.Count > 0 ? choices[q.id] : null;
                try
                {
                    if (items.Count > 0) DropPodUtility.DropThingsNear(RewardDropCell(map), map, items, forbid: false);
                }
                catch (Exception e)
                {
                    bool transferred = items.Any(item => item.Spawned || item.ParentHolder != null || item.Destroyed);
                    if (transferred) { Progress.RecordClaim(Pack, q, chosen); claimed = Progress.CopyClaimed(); }
                    DestroyUntransferred(prepared.SelectMany(x => x.Value));
                    Log.Error("[Grechka Quests] Ошибка создания капсул: " + e);
                    return "Получено заданий: " + count + ". " + (transferred ? "Часть текущей награды могла быть отправлена; повторная выдача заблокирована." : "Не удалось отправить текущую награду: " + e.Message);
                }
                Progress.RecordClaim(Pack, q, chosen); claimed = Progress.CopyClaimed(); count++;
            }
            return null;
        }
        static void DestroyUntransferred(IEnumerable<Thing> items)
        {
            foreach (Thing item in items.Where(item => !item.Destroyed && !item.Spawned && item.ParentHolder == null)) item.Destroy();
        }
    }
}
