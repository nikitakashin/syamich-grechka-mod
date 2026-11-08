using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grechka.Quests
{
    public sealed partial class QuestWindow : MainTabWindow
    {
        int tab;
        QuestPack draft;
        string bookSelection, draftSelection;
        string bookChapter, draftChapter, searchText = "";
        readonly HashSet<string> graphSelection = new HashSet<string>();
        readonly HashSet<string> expandedObjectives = new HashSet<string>();
        string draggingNode, linkingFrom, selectedEdgeFrom, selectedEdgeTo;
        bool draggingCanvas;
        Vector2 dragOffset;
        Rect lastGraphBounds;
        string currentChapter
        {
            get { return tab == 0 ? bookChapter : draftChapter; }
            set { if (tab == 0) bookChapter = value; else draftChapter = value; }
        }
        string selected
        {
            get { return tab == 0 ? bookSelection : draftSelection; }
            set { if (tab == 0) bookSelection = value; else draftSelection = value; }
        }
        Vector2 listScroll, detailScroll, fileScroll, chapterDescriptionScroll;
        string chapterHeaderKey;
        float detailHeight = 1000;
        string status = "";
        string jsonInput = "";
        List<string> files = new List<string>();
        readonly Dictionary<string, string> numberBuffers = new Dictionary<string, string>();
        DraftStore draftStore;
        DraftHistory draftHistory;
        string draftOwner, damagedDraftPath, lastSavedDraft;
        bool draftInitialized, draftSaveBlocked;
        float nextDraftSave;
        public override Vector2 RequestedTabSize => new Vector2(UI.screenWidth, UI.screenHeight);
        protected override float Margin => 12f;
        protected override void SetInitialSizeAndPosition() { windowRect = new Rect(0, 0, UI.screenWidth, UI.screenHeight); }
        public override void PostOpen()
        {
            base.PostOpen(); var game = QuestGame.Current; if (game == null) return;
            game.Refresh(); draft = PackCodec.Clone(game.Pack); tab = 0;
            bookSelection = game.Pack.quests.FirstOrDefault()?.id; draftSelection = draft.quests.FirstOrDefault()?.id;
            bookChapter = ChooseChapter(game.Pack, game.LastChapter(game.Pack.id));
            draftChapter = ChooseChapter(draft, game.LastChapter(draft.id));
            bookSelection = game.Pack.quests.FirstOrDefault(q => q.chapter == bookChapter)?.id;
            questDetailOpen = false; draggingCanvas = false; fittedChapters.Clear(); searchText = ""; graphSelection.Clear(); draggingNode = null; linkingFrom = null;
            draftInitialized = false; draftSaveBlocked = false; damagedDraftPath = null; lastSavedDraft = null; draftHistory = null;
            numberBuffers.Clear(); status = game.LoadError ?? ""; FindBundledUpdate(game);
            if (GrechkaMod.Settings != null && GrechkaMod.Settings.authorMode) LoadDraft(game);
        }
        public override void PostClose()
        {
            if (draftInitialized && (draftSaveBlocked || !SaveDraft(true)))
                Messages.Message(QuestText.Get("GrechkaQuests_d9dd9b88dcec", "Черновик квестов не удалось сохранить. Проверьте журнал игры и папку GrechkaQuests."), MessageTypeDefOf.RejectInput, false);
            base.PostClose();
        }
        DraftDocument CurrentDraft()
        {
            return new DraftDocument { pack = draft, numberBuffers = new Dictionary<string, string>(numberBuffers) };
        }
        void SetDraft(DraftDocument document)
        {
            string previousBook = draft?.id, previousQuest = draftSelection, previousChapter = draftChapter;
            draft = document.pack;
            numberBuffers.Clear();
            foreach (var entry in document.numberBuffers) numberBuffers.Add(entry.Key, entry.Value);
            var game = QuestGame.Current;
            string preferred = previousBook == draft.id && draft.chapters.Any(c => c.id == previousChapter)
                ? previousChapter : game == null ? null : game.LastChapter(draft.id);
            draftChapter = ChooseChapter(draft, preferred);
            draftSelection = previousBook == draft.id && draft.quests.Any(q => q.id == previousQuest && q.chapter == draftChapter)
                ? previousQuest : draft.quests.FirstOrDefault(q => q.chapter == draftChapter)?.id;
            graphSelection.Clear(); detailScroll = Vector2.zero;
        }
        static string ChooseChapter(QuestPack pack, string preferred)
        {
            return pack.chapters.Any(c => c.id == preferred) ? preferred : pack.chapters.FirstOrDefault()?.id;
        }
        void LoadDraft(QuestGame game)
        {
            draftInitialized = true;
            try
            {
                draftOwner = game.DraftOwnerId;
                draftStore = new DraftStore(PackFiles.DataRoot);
                ReloadFiles();
                DraftDocument saved = draftStore.LoadLatest(draftOwner, out damagedDraftPath);
                if (saved != null)
                {
                    SetDraft(saved); lastSavedDraft = DraftStore.Encode(saved);
                    status = QuestText.Get("GrechkaQuests_aca831ee4bad", "Восстановлен отдельный черновик «") + QuestPackText.Content(draft.title) + QuestText.Get("GrechkaQuests_83b46bfa6a87", "». Активная книга колонии: «") + QuestPackText.Content(game.Pack.title) + QuestText.Get("GrechkaQuests_0e4ec4905e7c", "». Черновик не применён автоматически.");
                }
                else status = QuestText.Get("GrechkaQuests_a6f33ec6f51f", "Редактируется копия активной книги; изменения сохраняются отдельно.");
                if (game.LoadError != null) status = game.LoadError + " " + status;
            }
            catch (Exception e)
            {
                draftSaveBlocked = true;
                status = QuestText.Get("GrechkaQuests_5317d622ce49", "Черновик не прочитан и не будет перезаписан: ") + e.Message + QuestText.Get("GrechkaQuests_939bc56fe00f", ". Во вкладке файлов можно восстановить резервную копию.");
                Log.Warning("[Grechka Quests] " + status);
            }
            try { draftHistory = new DraftHistory(CurrentDraft()); }
            catch (Exception e) { status = QuestText.Get("GrechkaQuests_39e2764385f2", "Нельзя открыть историю черновика: ") + e.Message; }
        }
        bool SaveDraft(bool force)
        {
            if (!draftInitialized || draft == null || draftSaveBlocked) return !draftSaveBlocked;
            if (!force && Time.realtimeSinceStartup < nextDraftSave) return true;
            nextDraftSave = Time.realtimeSinceStartup + 2f;
            try
            {
                DraftDocument document = CurrentDraft();
                string encoded = DraftStore.Encode(document);
                if (draftHistory != null) draftHistory.Record(document);
                if (encoded != lastSavedDraft)
                {
                    draftStore.Save(draftOwner, document);
                    lastSavedDraft = encoded;
                }
                return true;
            }
            catch (Exception e)
            {
                status = QuestText.Get("GrechkaQuests_5d2835e21753", "Черновик не сохранён: ") + e.Message;
                Log.Warning("[Grechka Quests] " + status);
                return false;
            }
        }
        void ValidateNumberBuffers()
        {
            if (numberBuffers.Values.Any(v => !int.TryParse(v, out int n) || n < 1 || n > 1000000))
                throw new FormatException(QuestText.Get("GrechkaQuests_511ac64159bc", "Проверьте числовые поля: нужны положительные целые числа."));
        }
        void ReplaceDraft(QuestPack pack, string message)
        {
            if (!SaveDraft(true)) return;
            SetDraft(new DraftDocument { pack = pack });
            lastSavedDraft = null;
            if (SaveDraft(true)) status = message;
        }
        void ResetDraft(QuestGame game)
        {
            try
            {
                var replacement = new DraftDocument { pack = PackCodec.Clone(game.Pack) };
                if (draftSaveBlocked)
                {
                    if (draftStore == null || string.IsNullOrEmpty(damagedDraftPath))
                        throw new InvalidOperationException(QuestText.Get("GrechkaQuests_ce20199a61d6", "Повреждённый файл не определён; проверьте доступ к папке черновиков."));
                    draftStore.ArchiveDamagedAndStart(draftOwner, damagedDraftPath, replacement);
                    draftSaveBlocked = false; SetDraft(replacement); lastSavedDraft = DraftStore.Encode(replacement);
                    draftHistory = new DraftHistory(replacement);
                    status = QuestText.Get("GrechkaQuests_dd8880cc03de", "Повреждённый файл сохранён отдельно, начат черновик активной книги.");
                }
                else ReplaceDraft(replacement.pack, QuestText.Get("GrechkaQuests_a267711b04c1", "Черновик восстановлен из активной книги колонии."));
            }
            catch (Exception e) { Fail(e); }
        }
        void RecoverDraft()
        {
            try
            {
                if (draftStore == null || string.IsNullOrEmpty(damagedDraftPath))
                    throw new InvalidOperationException(QuestText.Get("GrechkaQuests_ce20199a61d6", "Повреждённый файл не определён; проверьте доступ к папке черновиков."));
                DraftDocument restored = draftStore.RecoverLatest(draftOwner, damagedDraftPath);
                SetDraft(restored); draftSaveBlocked = false; lastSavedDraft = DraftStore.Encode(restored);
                draftHistory = new DraftHistory(restored);
                status = QuestText.Get("GrechkaQuests_a6182365e5a7", "Черновик восстановлен из резервной копии; повреждённый файл сохранён отдельно.");
            }
            catch (Exception e) { Fail(e); }
        }
        void ReloadFiles() { try { files = PackFiles.List(); } catch (Exception e) { status = e.Message; } }
        void Fail(Exception e) { status = QuestText.Get("GrechkaQuests_37eceb9f52a5", "Ошибка: ") + e.Message; Log.Warning("[Grechka Quests] " + e); }
        public override void DoWindowContents(Rect rect)
        {
            EventType initialEvent = Event.current.type;
            var game = QuestGame.Current; if (game == null || draft == null) { Widgets.Label(rect, QuestText.Get("GrechkaQuests_9b16ddfd2662", "Откройте колонию, чтобы работать с квестами.")); return; }
            Text.Font = GameFont.Small;
            bool author = GrechkaMod.Settings != null && GrechkaMod.Settings.authorMode;
            if (author && !draftInitialized) LoadDraft(game);
            if (!author && tab != 0) tab = 0;
            Event shortcut = Event.current;
            if (author && tab == 1 && shortcut.type == EventType.KeyDown && (shortcut.control || shortcut.command)
                && !shortcut.alt && GUIUtility.keyboardControl == 0
                && (shortcut.keyCode == KeyCode.Z || shortcut.keyCode == KeyCode.Y))
            {
                EditHistory(shortcut.keyCode == KeyCode.Y || shortcut.shift);
                shortcut.Use(); initialEvent = EventType.Used;
            }
            if (tab == 0)
            {
                FullscreenBook(rect, game, author);
                if (draftInitialized && Event.current.type == EventType.Repaint) SaveDraft(false);
                return;
            }
            if (Widgets.ButtonText(new Rect(rect.width - 36, 0, 32, 32), "×")) Close();
            if (QuestArt.Button(new Rect(0, 0, 140, 32), QuestText.Get("GrechkaQuests_dbf132de1b6d", "Книга"), "QuestBook")) { tab = 0; game.Refresh(); detailScroll = Vector2.zero; }
            if (author && Widgets.ButtonText(new Rect(148, 0, 140, 32), QuestText.Get("GrechkaQuests_119d528f623e", "Редактор"))) { tab = 1; detailScroll = Vector2.zero; }
            if (author && Widgets.ButtonText(new Rect(296, 0, 180, 32), QuestText.Get("GrechkaQuests_965587e9f297", "Импорт / экспорт"))) { tab = 2; ReloadFiles(); }
            float titleX = author ? 490f : 155f;
            Widgets.Label(new Rect(titleX, 3, Math.Max(0, rect.width - titleX - 210), 30), "Grechka Quests · " + (tab == 0 ? QuestPackText.Content(game.Pack.title) : QuestPackText.Content(draft.title)));
            if (tab == 0 && QuestArt.Button(new Rect(rect.width - 160, 0, 160, 32), QuestText.Get("GrechkaQuests_7445c6f3b8f7", "Забрать всё"), "QuestClaimAll"))
            {
                StartClaim(game, null);
            }
            float footer = 72;
            Rect body = new Rect(0, 42, rect.width, rect.height - 42 - footer);
            if (tab == 2) Files(body, game); else QuestPane(body, game);
            Widgets.DrawLineHorizontal(0, rect.height - footer, rect.width);
            Widgets.Label(new Rect(0, rect.height - footer + 6, rect.width, footer - 6), status);
            if (tab == 1 && draftHistory != null && (initialEvent == EventType.KeyDown || initialEvent == EventType.MouseUp))
            {
                try { draftHistory.Record(CurrentDraft()); } catch (Exception e) { status = QuestText.Get("GrechkaQuests_6a6cddddc562", "Ошибка истории черновика: ") + e.Message; }
            }
            if (draftInitialized && Event.current.type == EventType.Repaint) SaveDraft(false);
        }
        void QuestPane(Rect r, QuestGame game)
        {
            QuestPack p = tab == 0 ? game.Pack : draft;
            if (!p.chapters.Any(c => c.id == currentChapter)) currentChapter = ChooseChapter(p, game.LastChapter(p.id));
            float leftWidth = 190f, detailWidth = Mathf.Clamp(r.width * 0.34f, 285f, 390f);
            float graphWidth = Math.Max(160, r.width - leftWidth - detailWidth - 24);
            Rect left = new Rect(r.x, r.y, leftWidth, r.height);
            Rect graph = new Rect(left.xMax + 8, r.y, graphWidth, r.height);
            Rect right = new Rect(graph.xMax + 8, r.y, Math.Max(200, r.xMax - graph.xMax - 8), r.height);

            float top = 0;
            QuestArt.Draw(new Rect(left.x + 2, left.y + 4, 22, 22), "QuestSearch");
            searchText = Widgets.TextField(new Rect(left.x + 29, left.y, left.width - 29, 30), searchText ?? "", 120);
            TooltipHandler.TipRegion(new Rect(left.x, left.y, left.width, 30), QuestText.Get("GrechkaQuests_95e16b1cf050", "Поиск по названию и описанию задания"));
            top += 36;
            if (tab == 1)
            {
                if (QuestArt.Button(new Rect(left.x, left.y + top, 90, 30), QuestText.Get("GrechkaQuests_017f4052e4ff", "+ Квест"), "QuestAdd"))
                    CreateQuestAt(new Vector2(40, 40));
                if (Widgets.ButtonText(new Rect(left.x + 96, left.y + top, 94, 30), QuestText.Get("GrechkaQuests_2f28392d2a82", "Главы"))) selected = null;
                top += 36;
                if (selectedEdgeFrom != null && selectedEdgeTo != null)
                {
                    if (QuestArt.Button(new Rect(left.x, left.y + top, left.width, 30), QuestText.Get("GrechkaQuests_3e3f2f08de0d", "Удалить связь"), "QuestUnlink")) RemoveSelectedEdge();
                    top += 36;
                }
            }
            var matches = string.IsNullOrWhiteSpace(searchText) ? null : p.quests.Where(q =>
                (QuestPackText.Content(q.title) + " " + QuestPackText.Content(q.description)).IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            Rect nav = new Rect(left.x, left.y + top, left.width, Math.Max(0, left.height - top));
            float buttonWidth = nav.width - 20;
            // Measure exactly the label column we later draw, including the badge reservation.
            var chapterRows = matches == null ? p.chapters.Select(chapter => {
                var quests = p.quests.Where(q => q.chapter == chapter.id).ToList();
                string label = QuestPackText.Content(chapter.title) + (tab == 0 ? " (" + quests.Count(game.Claimed) + "/" + quests.Count + ")" : "");
                ThingDef icon = string.IsNullOrEmpty(chapter.icon) ? null : QuestGame.ResolveThingDef(chapter.icon);
                bool hasIcon = icon != null || QuestArt.Get(chapter.icon) != null;
                bool badge = tab == 0 && quests.Any(game.CanClaim);
                float textX = hasIcon ? 36 : 5;
                float textWidth = Math.Max(20, buttonWidth - textX - 5 - (badge ? 23 : 0));
                return new { chapter, quests, label, icon, badge, textX, textWidth,
                    height = Math.Max(40, Text.CalcHeight(label, textWidth) + 12) };
            }).ToList() : null;
            var searchRows = matches?.Select(q => {
                string label = QuestPackText.Content(q.title) + "\n" + QuestPackText.Content(p.chapters.FirstOrDefault(c => c.id == q.chapter)?.title ?? "");
                return new { quest = q, label, height = Math.Max(46, Text.CalcHeight(label, buttonWidth - 12) + 12) };
            }).ToList();
            float contentHeight = chapterRows != null ? chapterRows.Sum(row => row.height + 5) : searchRows.Sum(row => row.height + 5);
            Widgets.BeginScrollView(nav, ref listScroll, new Rect(0, 0, nav.width - 18, Math.Max(nav.height, contentHeight + 10)));
            float y = 0;
            if (searchRows != null)
            {
                foreach (var row in searchRows)
                {
                    Rect button = new Rect(0, y, buttonWidth, row.height);
                    if (Widgets.ButtonText(button, ""))
                    { SelectQuest(p, game, row.quest); searchText = ""; }
                    Widgets.Label(button.ContractedBy(6), row.label);
                    y += row.height + 5;
                }
            }
            else
            {
                foreach (var row in chapterRows)
                {
                    Rect button = new Rect(0, y, buttonWidth, row.height);
                    if (Widgets.ButtonText(button, ""))
                    {
                        currentChapter = row.chapter.id; game.SetLastChapter(p.id, row.chapter.id);
                        selected = row.quests.FirstOrDefault()?.id; graphSelection.Clear(); selectedEdgeFrom = selectedEdgeTo = null;
                        detailScroll = Vector2.zero;
                    }
                    Rect iconRect = new Rect(button.x + 3, button.y + (button.height - 30) / 2, 30, 30);
                    if (row.icon != null) Widgets.ThingIcon(iconRect, row.icon);
                    else QuestArt.Draw(iconRect, row.chapter.icon);
                    Widgets.Label(new Rect(button.x + row.textX, button.y + 6, row.textWidth, button.height - 12), row.label);
                    TooltipHandler.TipRegion(button, row.label);
                    if (row.badge) QuestBadge.Draw(new Rect(button.xMax - 22, button.y, 20, 20));
                    y += row.height + 5;
                }
            }
            Widgets.EndScrollView();
            graph = ChapterHeader(graph, p, game);
            DrawGraph(graph, p, game);
            Quest current = p.quests.FirstOrDefault(q => q.id == selected);
            if (tab == 0) Book(right, game, current); else Editor(right, current, game);
        }
        Rect ChapterHeader(Rect area, QuestPack pack, QuestGame game)
        {
            Chapter chapter = pack.chapters.FirstOrDefault(c => c.id == currentChapter);
            if (chapter == null) return area;
            string key = pack.id + "/" + chapter.id + "/" + tab;
            if (chapterHeaderKey != key) { chapterHeaderKey = key; chapterDescriptionScroll = Vector2.zero; }
            float y = area.y;
            Text.Font = GameFont.Medium;
            float titleHeight = Text.CalcHeight(QuestPackText.Content(chapter.title), area.width);
            Widgets.Label(new Rect(area.x, y, area.width, titleHeight), QuestPackText.Content(chapter.title));
            y += titleHeight + 5;
            Text.Font = GameFont.Small;
            var quests = pack.quests.Where(q => q.chapter == chapter.id).ToList();
            string progress = tab == 0
                ? QuestText.Get("GrechkaQuests_2685f28c8d34", "Цели выполнены: ") + quests.Count(q => q.objectives.All(o => game.ObjectiveAchieved(q, o))) + "/" + quests.Count
                    + QuestText.Get("GrechkaQuests_86a68248dc75", " · Награды: ") + quests.Count(game.Claimed) + "/" + quests.Count
                : QuestText.Get("GrechkaQuests_9f449ad07fe5", "Заданий в главе: ") + quests.Count;
            float progressHeight = Text.CalcHeight(progress, area.width);
            Widgets.Label(new Rect(area.x, y, area.width, progressHeight), progress);
            y += progressHeight + 5;
            if (!string.IsNullOrWhiteSpace(QuestPackText.Content(chapter.description)))
            {
                float width = Math.Max(50, area.width - 18);
                float fullHeight = Text.CalcHeight(QuestPackText.Content(chapter.description), width) + 6;
                float height = Math.Min(fullHeight, Math.Min(120, area.height * 0.22f));
                Widgets.BeginScrollView(new Rect(area.x, y, area.width, height), ref chapterDescriptionScroll, new Rect(0, 0, width, fullHeight));
                Widgets.Label(new Rect(0, 0, width, fullHeight), QuestPackText.Content(chapter.description));
                Widgets.EndScrollView();
                y += height + 8;
            }
            Widgets.DrawLineHorizontal(area.x, y, area.width);
            y += 7;
            return new Rect(area.x, y, area.width, Math.Max(60, area.yMax - y));
        }
        void SelectQuest(QuestPack pack, QuestGame game, Quest quest)
        {
            currentChapter = quest.chapter; selected = quest.id; game.SetLastChapter(pack.id, quest.chapter);
            graphSelection.Clear(); graphSelection.Add(quest.id); selectedEdgeFrom = selectedEdgeTo = null;
            detailScroll = Vector2.zero;
            var chapterQuests = pack.quests.Where(q => q.chapter == quest.chapter).ToList();
            Vector2 pan; float zoom; game.GraphView(pack.id, quest.chapter, out pan, out zoom);
            if (lastGraphBounds.width > 0 && chapterQuests.Contains(quest))
            {
                Vector2 p = GraphPosition(quest, chapterQuests.IndexOf(quest));
                pan = new Vector2(lastGraphBounds.width / 2 - (p.x + 32) * zoom, lastGraphBounds.height / 2 - (p.y + 32) * zoom);
                game.SetGraphView(pack.id, quest.chapter, pan, zoom);
            }
        }
        static Vector2 GraphPosition(Quest quest, int index)
        {
            return quest.graphPositioned ? new Vector2(quest.graphX, quest.graphY) : new Vector2((index % 5) * 140, (index / 5) * 140);
        }
        static Rect GraphNodeRect(Quest quest, int index, Vector2 pan, float zoom)
        {
            Vector2 p = GraphPosition(quest, index) * zoom + pan;
            return new Rect(p.x, p.y, 64 * zoom, 64 * zoom);
        }
        sealed class GraphEdge
        {
            public string from, to;
            public Vector2 start, end;
        }
        static float DistanceToLine(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 line = end - start; float length = line.sqrMagnitude;
            if (length < 0.01f) return (point - start).magnitude;
            float part = Mathf.Clamp01(Vector2.Dot(point - start, line) / length);
            return (point - (start + part * line)).magnitude;
        }
        void DrawGraph(Rect bounds, QuestPack pack, QuestGame game)
        {
            lastGraphBounds = bounds;
            Widgets.DrawBoxSolid(bounds, new Color(0.10f, 0.11f, 0.12f, 1));
            if (currentChapter == null) { Widgets.Label(bounds.ContractedBy(10), QuestText.Get("GrechkaQuests_4187dccf94ef", "Добавьте главу в редакторе.")); return; }
            var quests = pack.quests.Where(q => q.chapter == currentChapter).ToList();
            Vector2 pan; float zoom; game.GraphView(pack.id, currentChapter, out pan, out zoom);
            var rects = new Dictionary<string, Rect>();
            for (int i = 0; i < quests.Count; i++) rects[quests[i].id] = GraphNodeRect(quests[i], i, pan, zoom);
            var edges = new List<GraphEdge>();
            foreach (Quest q in quests)
            {
                Rect target = rects[q.id];
                foreach (string parentId in q.requires)
                {
                    Rect parent;
                    if (!rects.TryGetValue(parentId, out parent)) continue;
                    edges.Add(new GraphEdge { from = parentId, to = q.id,
                        start = parent.center + (target.center - parent.center).normalized * parent.width / 2, end = target.center - (target.center - parent.center).normalized * target.width / 2 });
                }
            }
            GUI.BeginGroup(bounds);
            try
            {
                Event ev = Event.current; Vector2 mouse = ev.mousePosition;
                float grid = 32 * zoom;
                for (float x = pan.x % grid; x < bounds.width; x += grid)
                    Widgets.DrawLine(new Vector2(x, 0), new Vector2(x, bounds.height), new Color(0.17f, 0.19f, 0.21f), 1);
                for (float y = pan.y % grid; y < bounds.height; y += grid)
                    Widgets.DrawLine(new Vector2(0, y), new Vector2(bounds.width, y), new Color(0.17f, 0.19f, 0.21f), 1);
                foreach (GraphEdge edge in edges)
                {
                    bool picked = edge.from == selectedEdgeFrom && edge.to == selectedEdgeTo;
                    bool complete = tab == 0 && game.Claimed(quests.First(q => q.id == edge.from));
                    Color edgeColor = picked ? Color.yellow : complete ? BookGreen : new Color(0.42f, 0.46f, 0.50f);
                    Widgets.DrawLine(edge.start, edge.end, edgeColor, picked ? 3f : 2f);
                    Vector2 direction = (edge.end - edge.start).normalized;
                    Vector2 wing = new Vector2(-direction.y, direction.x) * 4;
                    Widgets.DrawLine(edge.end, edge.end - direction * 9 + wing, edgeColor, 2);
                    Widgets.DrawLine(edge.end, edge.end - direction * 9 - wing, edgeColor, 2);
                }
                if (linkingFrom != null && rects.TryGetValue(linkingFrom, out Rect fromRect))
                    Widgets.DrawLine(new Vector2(fromRect.xMax, fromRect.center.y), mouse, Color.yellow, 2f);
                foreach (Quest q in quests)
                {
                    Rect node = rects[q.id];
                    bool chosen = selected == q.id || (tab == 1 && graphSelection.Contains(q.id));
                    bool claimed = tab == 0 && game.Claimed(q), ready = tab == 0 && game.CanClaim(q);
                    Color border = chosen ? new Color(0.95f, 0.82f, 0.48f) : claimed ? BookGreen : ready ? new Color(0.9f, 0.72f, 0.25f) : new Color(0.48f, 0.53f, 0.58f);
                    QuestVisuals.Circle(node, border);
                    QuestVisuals.Circle(node.ContractedBy(Math.Max(2, 3 * zoom)), new Color(0.16f, 0.19f, 0.22f));
                    string iconId = QuestVisuals.QuestIcon(q);
                    QuestVisuals.Icon(node.ContractedBy(node.width * .19f), iconId);
                    float stateSize = Mathf.Clamp(21 * zoom, 13, 27);
                    Rect stateRect = new Rect(node.xMax - stateSize * .7f, node.y - 3, stateSize, stateSize);
                    if (tab == 1)
                    {
                        QuestArt.Draw(new Rect(node.xMax - 15, node.center.y - 7, 14, 14), "QuestLink");
                        TooltipHandler.TipRegion(new Rect(node.xMax - 16, node.center.y - 10, 16, 20), QuestText.Get("GrechkaQuests_5dccf645fbce", "Потяните к следующему заданию, чтобы создать связь"));
                    }
                    else if (ready) QuestBadge.Draw(stateRect);
                    else if (claimed || game.Completed(q)) QuestVisuals.Check(stateRect);
                    if (chosen && !questDetailOpen)
                    {
                        Text.Anchor = TextAnchor.UpperCenter;
                        Widgets.Label(new Rect(node.center.x - 100, node.yMax + 5, 200, 44), QuestPackText.Content(q.title));
                        Text.Anchor = TextAnchor.UpperLeft;
                    }
                    string state = tab == 1 ? QuestText.Get("GrechkaQuests_119d528f623e", "Редактор") : game.Claimed(q) ? QuestText.Get("GrechkaQuests_f27a15f94fd4", "Награда получена") : game.CanClaim(q) ? QuestText.Get("GrechkaQuests_c48aa4188b7d", "Награда готова") : !game.Unlocked(q) ? QuestText.Get("GrechkaQuests_7aaf6df87e72", "Сначала получите награды предшественников") : game.Completed(q) ? QuestText.Get("GrechkaQuests_7bd286e6556c", "Цели выполнены") : QuestText.Get("GrechkaQuests_590e5be43f6b", "В процессе");
                    if (!questDetailOpen) TooltipHandler.TipRegion(node, QuestPackText.Content(q.title) + "\n" + state);
                }
                if (quests.Count == 0) Widgets.Label(new Rect(12, 12, Math.Max(0, bounds.width - 24), 50), tab == 1 ? QuestText.Get("GrechkaQuests_7634f2cb75c4", "ПКМ: создать квест") : QuestText.Get("GrechkaQuests_1a1c4c3ff7b3", "В этой главе пока нет заданий."));
                if (GUI.enabled && (tab != 0 || !questDetailOpen)) HandleGraphInput(ev, mouse, bounds, pack, game, quests, rects, edges, ref pan, ref zoom);
            }
            finally { GUI.EndGroup(); }
        }
        void HandleGraphInput(Event ev, Vector2 mouse, Rect bounds, QuestPack pack, QuestGame game, List<Quest> quests,
            Dictionary<string, Rect> rects, List<GraphEdge> edges, ref Vector2 pan, ref float zoom)
        {
            bool inside = new Rect(0, 0, bounds.width, bounds.height).Contains(mouse);
            if (ev.type == EventType.ScrollWheel && inside)
            {
                Vector2 world = (mouse - pan) / zoom;
                float next = Mathf.Clamp(zoom * (ev.delta.y > 0 ? 0.9f : 1.1f), 0.45f, 1.6f);
                pan = mouse - world * next; zoom = next; game.SetGraphView(pack.id, currentChapter, pan, zoom); ev.Use(); return;
            }
            if (ev.type == EventType.MouseDown && inside)
            {
                Quest hit = quests.LastOrDefault(q => rects[q.id].Contains(mouse));
                GraphEdge edge = hit == null ? edges.Where(x => DistanceToLine(mouse, x.start, x.end) < 7).LastOrDefault() : null;
                if (ev.button == 1 && tab == 1)
                {
                    if (hit != null)
                    {
                        if (!graphSelection.Contains(hit.id)) { graphSelection.Clear(); graphSelection.Add(hit.id); }
                        selected = hit.id;
                        var menu = new List<FloatMenuOption> {
                            new FloatMenuOption(QuestText.Get("GrechkaQuests_6e7ffbb60db1", "Дублировать задание"), () => CopyGraphSelection(new[] { hit.id })),
                            new FloatMenuOption(QuestText.Get("GrechkaQuests_c3baaca6a979", "Копировать выделенные (") + graphSelection.Count + ")", () => CopyGraphSelection(graphSelection.ToArray())),
                            new FloatMenuOption(QuestText.Get("GrechkaQuests_e49eaaa488ad", "Удалить задание"), () => ConfirmRemoveQuest(hit))
                        };
                        Find.WindowStack.Add(new FloatMenu(menu));
                    }
                    else if (edge != null)
                    {
                        selectedEdgeFrom = edge.from; selectedEdgeTo = edge.to;
                        Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption> { new FloatMenuOption(QuestText.Get("GrechkaQuests_3e3f2f08de0d", "Удалить связь"), RemoveSelectedEdge) }));
                    }
                    else
                    {
                        Vector2 position = (mouse - pan) / zoom;
                        Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption> { new FloatMenuOption(QuestText.Get("GrechkaQuests_69ef9a087543", "Создать квест"), () => CreateQuestAt(position)) }));
                    }
                    ev.Use(); return;
                }
                if (ev.button == 0)
                {
                    GUIUtility.keyboardControl = 0;
                    if (hit != null)
                    {
                        Rect node = rects[hit.id]; selected = hit.id; detailScroll = Vector2.zero;
                        if (tab == 0) { questDetailOpen = true; draggingCanvas = false; }
                        selectedEdgeFrom = selectedEdgeTo = null;
                        if (tab == 1 && new Rect(node.xMax - 16, node.center.y - 10, 16, 20).Contains(mouse))
                        { linkingFrom = hit.id; draggingNode = null; }
                        else
                        {
                            if (tab == 1 && (ev.control || ev.command))
                            { if (!graphSelection.Add(hit.id)) graphSelection.Remove(hit.id); }
                            else if (!graphSelection.Contains(hit.id)) { graphSelection.Clear(); graphSelection.Add(hit.id); }
                            if (tab == 1 && graphSelection.Contains(hit.id)) { draggingNode = hit.id; dragOffset = mouse - node.position; }
                        }
                    }
                    else if (tab == 1 && edge != null)
                    {
                        selectedEdgeFrom = edge.from; selectedEdgeTo = edge.to; status = QuestText.Get("GrechkaQuests_7d32d61f2226", "Связь выделена. Нажмите «Удалить связь».");
                    }
                    else { draggingCanvas = true; selectedEdgeFrom = selectedEdgeTo = null; }
                    ev.Use(); return;
                }
            }
            if (ev.type == EventType.MouseDrag && ev.button == 0)
            {
                if (draggingCanvas) { pan += ev.delta; game.SetGraphView(pack.id, currentChapter, pan, zoom); ev.Use(); }
                else if (draggingNode != null)
                {
                    Quest q = quests.FirstOrDefault(x => x.id == draggingNode);
                    if (q != null)
                    {
                        Vector2 position = (mouse - dragOffset - pan) / zoom;
                        q.graphX = Mathf.Clamp(position.x, -100000, 100000); q.graphY = Mathf.Clamp(position.y, -100000, 100000); q.graphPositioned = true;
                    }
                    ev.Use();
                }
                else if (linkingFrom != null) ev.Use();
                return;
            }
            if (ev.type == EventType.MouseUp && ev.button == 0)
            {
                if (linkingFrom != null && tab == 1)
                {
                    Quest target = inside ? quests.LastOrDefault(q => rects[q.id].Contains(mouse)) : null;
                    Quest source = quests.FirstOrDefault(q => q.id == linkingFrom);
                    if (target != null && source != null)
                    {
                        string error;
                        status = QuestEditor.TryAddDependency(draft, target, source, out error) ? QuestText.Get("GrechkaQuests_712860885352", "Связь создана.") : error;
                    }
                    else status = QuestText.Get("GrechkaQuests_4ff12f6241a5", "Связь не создана: выберите второе задание.");
                    ev.Use();
                }
                draggingCanvas = false; draggingNode = null; linkingFrom = null;
            }
        }
        void CreateQuestAt(Vector2 position)
        {
            if (draft.quests.Count >= 500) { status = QuestText.Get("GrechkaQuests_8210d9d1210a", "Лимит: 500 заданий."); return; }
            if (draft.chapters.Count == 0) { draft.chapters.Add(new Chapter()); draftChapter = draft.chapters[0].id; }
            if (!draft.chapters.Any(c => c.id == draftChapter)) draftChapter = draft.chapters[0].id;
            var q = new Quest { id = PackCodec.NewId("quest."), chapter = draftChapter,
                graphX = Mathf.Clamp(position.x, -100000, 100000), graphY = Mathf.Clamp(position.y, -100000, 100000), graphPositioned = true };
            q.objectives.Add(new Objective { id = PackCodec.NewId("goal.") }); draft.quests.Add(q);
            draftSelection = q.id; graphSelection.Clear(); graphSelection.Add(q.id); detailScroll = Vector2.zero;
        }
        void RemoveSelectedEdge()
        {
            Quest child = draft.quests.FirstOrDefault(q => q.id == selectedEdgeTo);
            if (child != null && selectedEdgeFrom != null) child.requires.Remove(selectedEdgeFrom);
            selectedEdgeFrom = selectedEdgeTo = null; status = QuestText.Get("GrechkaQuests_c169018fb461", "Связь удалена.");
        }
        void CopyGraphSelection(IEnumerable<string> selectedIds)
        {
            try
            {
                var ids = new HashSet<string>(selectedIds);
                var sources = draft.quests.Where(q => ids.Contains(q.id)).ToList();
                var positions = sources.Select(q => GraphPosition(q, draft.quests.Where(x => x.chapter == q.chapter).ToList().IndexOf(q))).ToList();
                var copies = QuestEditor.CopySelected(draft, ids);
                graphSelection.Clear();
                for (int i = 0; i < copies.Count; i++)
                {
                    copies[i].graphX = Mathf.Clamp(positions[i].x + 35, -100000, 100000);
                    copies[i].graphY = Mathf.Clamp(positions[i].y + 35, -100000, 100000);
                    copies[i].graphPositioned = true; graphSelection.Add(copies[i].id);
                }
                selected = copies.First().id; status = QuestText.Get("GrechkaQuests_ccd078300be5", "Копия создана с новыми ID и внутренними связями.");
            }
            catch (Exception e) { Fail(e); }
        }
        void Book(Rect r, QuestGame game, Quest q) { if (q != null) QuestDetails(r, game, q); }
        void StartClaim(QuestGame game, Quest one)
        {
            try
            {
                string error; QuestClaimPlan plan = game.PrepareClaim(one, out error);
                if (plan == null) { status = error; return; }
                if (plan.questIds.Any(id => game.Pack.quests.First(q => q.id == id).alternatives.Count > 0))
                    Find.WindowStack.Add(new RewardChoiceWindow(game, plan, message => status = message));
                else
                {
                    string result = game.FinishClaim(plan, null);
                    status = result ?? QuestText.Get("GrechkaQuests_8fff9941d5fd", "Получено заданий: ") + plan.questIds.Count + QuestText.Get("GrechkaQuests_05313ea5f998", ". Предметы целей остаются у вас.");
                }
            }
            catch (Exception e) { Fail(e); }
        }
        static string DefLabel(string name) { var d = QuestGame.ResolveThingDef(name); return d == null ? QuestText.Get("GrechkaQuests_5774b9e5bdb3", "[нет: ") + name + "]" : d.LabelCap.ToString(); }
        static string QualityLabel(string quality)
        {
            switch (quality)
            {
                case "Awful": return QuestText.Get("GrechkaQuests_0f59b87638ab", "ужасное");
                case "Poor": return QuestText.Get("GrechkaQuests_57253f66a1ce", "плохое");
                case "Normal": return QuestText.Get("GrechkaQuests_0024c6ce960f", "обычное");
                case "Good": return QuestText.Get("GrechkaQuests_1cd5f8d70605", "хорошее");
                case "Excellent": return QuestText.Get("GrechkaQuests_0967e1aca5e1", "отличное");
                case "Masterwork": return QuestText.Get("GrechkaQuests_fe988821c3d0", "шедевр");
                case "Legendary": return QuestText.Get("GrechkaQuests_72915114127a", "легендарное");
                default: return quality;
            }
        }
        static string ObjectiveTitle(Objective o)
        {
            string prefix = o.type == "manual" ? QuestText.Get("GrechkaQuests_e7ac87608ab9", "Ручная цель") : o.type == "build" ? QuestText.Get("GrechkaQuests_8c5cc1b4abbf", "Построить: ") : o.type == "produce" ? QuestText.Get("GrechkaQuests_a4b9c57f231d", "Произвести: ") : o.type == "mine" ? QuestText.Get("GrechkaQuests_4e940f3a951e", "Добыть: ") : o.type == "harvest" ? QuestText.Get("GrechkaQuests_2d8662bf063c", "Собрать урожай: ") : o.type == "kill" ? QuestText.Get("GrechkaQuests_f5441a4fc197", "Убить или забить: ") : QuestText.Get("GrechkaQuests_361deb1c3839", "Иметь: ");
            return o.type == "manual" ? prefix : prefix + string.Join(" / ", new[] { o.target }.Concat(o.variants).Select(DefLabel));
        }
        static void Label(ref float y, float w, string text, float h = 30) { Widgets.Label(new Rect(0, y, w, h), text); y += h + 4; }
        static string Field(ref float y, float w, string title, string value, int max = 200)
        {
            Label(ref y, w, title, 24); string s = Widgets.TextField(new Rect(0, y, w, 30), value ?? "", max); y += 38; return s;
        }
        int Number(Rect r, string key, int value, int max)
        {
            string buffer; if (!numberBuffers.TryGetValue(key, out buffer)) buffer = value.ToString();
            buffer = Widgets.TextField(r, buffer, 8); numberBuffers[key] = buffer;
            int n; return int.TryParse(buffer, out n) ? n : value;
        }
        void Editor(Rect r, Quest q, QuestGame game)
        {
            float half = Math.Max(80, (r.width - 8) / 2);
            if (QuestArt.Button(new Rect(r.x, r.y, half, 32), QuestText.Get("GrechkaQuests_e15f960d93eb", "Применить"), "QuestApply"))
            {
                try
                {
                    ValidateNumberBuffers();
                    if (!SaveDraft(true)) return;
                    QuestPack candidate = game.ValidateApply(draft);
                    int pendingEventGoals = candidate.quests.Sum(item => item.objectives.Count(QuestGame.IsEventObjective));
                    string summary = BookHistory.DescribeChange(game.Pack, candidate) + "\n" + CompatibilitySummary(candidate)
                        + (pendingEventGoals == 0 ? "" : QuestText.Get("GrechkaQuests_1cfc390905c4", "\nСобытийных целей: ") + pendingEventGoals + QuestText.Get("GrechkaQuests_29b09f035039", ". Стандартные источники подключены; нестандартным процессам нужны адаптеры."));
                    Find.WindowStack.Add(new ConfirmWindow(summary, () => {
                        try
                        {
                            game.Apply(candidate); bookSelection = game.Pack.quests.FirstOrDefault()?.id;
                            bookChapter = ChooseChapter(game.Pack, game.LastChapter(game.Pack.id));
                            status = QuestText.Get("GrechkaQuests_1f79f6f01ea5", "Применено. Сохраните игру для записи прогресса; экспортируйте JSON для переноса. ") + CompatibilitySummary(candidate);
                        }
                        catch (Exception e) { Fail(e); }
                    }));
                }
                catch (Exception e) { Fail(e); }
            }
            if (Widgets.ButtonText(new Rect(r.x + half + 8, r.y, half, 32), QuestText.Get("GrechkaQuests_9fdd5b0ba528", "Из активной книги")))
            {
                Find.WindowStack.Add(new ConfirmWindow(QuestText.Get("GrechkaQuests_81b60bf0ac9b", "Начать черновик заново из активной книги? Текущие неприменённые правки сохраняются в резервной копии, повреждённый файл — отдельно."), () => ResetDraft(game)));
            }
            if (Widgets.ButtonText(new Rect(r.x, r.y + 38, half, 30), QuestText.Get("GrechkaQuests_7838feeb116d", "← Отменить"))) EditHistory(false);
            if (Widgets.ButtonText(new Rect(r.x + half + 8, r.y + 38, half, 30), QuestText.Get("GrechkaQuests_672972818e48", "Повторить →"))) EditHistory(true);
            if (QuestArt.Button(new Rect(r.x, r.y + 76, half, 30), QuestText.Get("GrechkaQuests_dcbc3fd5f524", "Проверить книгу"), "QuestValidate")) CheckDraft(game);
            if (QuestArt.Button(new Rect(r.x + half + 8, r.y + 76, half, 30), QuestText.Get("GrechkaQuests_32060a34f5be", "Предпросмотр"), "QuestPreview"))
            {
                try { ValidateNumberBuffers(); Find.WindowStack.Add(new QuestPreviewWindow(draft)); }
                catch (Exception e) { Fail(e); }
            }
            Rect view = new Rect(r.x, r.y + 114, r.width, Math.Max(0, r.height - 114));
            Widgets.BeginScrollView(view, ref detailScroll, new Rect(0, 0, view.width - 20, detailHeight));
            float y = 0, w = view.width - 24;
            if (q == null) EditPack(ref y, w);
            else EditQuest(ref y, w, q);
            detailHeight = y + 20; Widgets.EndScrollView();
        }
        void CheckDraft(QuestGame game)
        {
            try
            {
                var choices = new List<Choice>();
                try { ValidateNumberBuffers(); }
                catch (Exception e) { choices.Add(new Choice(e.Message, QuestText.Get("GrechkaQuests_ac4d2ceed8f6", "Числовые поля"), () => { })); }
                List<string> structural = PackCodec.Validate(draft);
                foreach (string issue in structural)
                {
                    Quest target = draft.quests?.FirstOrDefault(q => q != null && !string.IsNullOrEmpty(q.id) && issue.Contains(q.id));
                    choices.Add(new Choice(issue, target == null ? QuestText.Get("GrechkaQuests_dbf132de1b6d", "Книга") : QuestPackText.Content(target.title), () => { if (target != null) SelectQuest(draft, game, target); }));
                }
                if (structural.Count == 0)
                    foreach (Quest item in draft.quests)
                    {
                        foreach (Objective goal in item.objectives)
                        {
                            string issue = QuestGame.ObjectiveProblem(goal);
                            if (issue != null) choices.Add(new Choice(issue, QuestPackText.Content(item.title) + " / " + goal.id, () => SelectQuest(draft, game, item)));
                            if (QuestGame.IsEventObjective(goal)) choices.Add(new Choice(QuestText.Get("GrechkaQuests_b5c45fbe4822", "Проверьте источник события для модовой машины: ") + goal.type, QuestPackText.Content(item.title) + " / " + goal.id, () => SelectQuest(draft, game, item)));
                        }
                        foreach (Reward reward in item.rewards.Concat(item.alternatives.SelectMany(option => option.items)))
                        {
                            string issue = QuestGame.RewardProblem(reward);
                            if (issue != null) choices.Add(new Choice(issue, QuestPackText.Content(item.title), () => SelectQuest(draft, game, item)));
                        }
                    }
                if (choices.Count == 0) choices.Add(new Choice(QuestText.Get("GrechkaQuests_cffb1f8368ba", "Ошибок не найдено"), QuestText.Get("GrechkaQuests_26f00066a243", "Проверены формат и доступные определения"), () => { }));
                Find.WindowStack.Add(new ChoiceWindow(QuestText.Get("GrechkaQuests_2dfa0207e372", "Проверка книги"), choices));
            }
            catch (Exception e) { Fail(e); }
        }
        void EditHistory(bool redo)
        {
            try
            {
                if (draftHistory == null) { status = QuestText.Get("GrechkaQuests_dc147f37319f", "История правок недоступна."); return; }
                draftHistory.Record(CurrentDraft());
                DraftDocument restored = redo ? draftHistory.Redo() : draftHistory.Undo();
                if (restored == null) { status = redo ? QuestText.Get("GrechkaQuests_56ee2655e2ab", "Нечего повторять.") : QuestText.Get("GrechkaQuests_d6707aea72e7", "Нечего отменять."); return; }
                SetDraft(restored); status = redo ? QuestText.Get("GrechkaQuests_3c6058b817d8", "Правка повторена в черновике.") : QuestText.Get("GrechkaQuests_1746ac60161d", "Правка отменена в черновике.");
            }
            catch (Exception e) { Fail(e); }
        }
        static string CompatibilitySummary(QuestPack p)
        {
            var issues = p.quests.SelectMany(QuestGame.Problems).Distinct().ToList();
            return issues.Count == 0 ? QuestText.Get("GrechkaQuests_8d08c0bd64f1", "Ссылки на предметы доступны.") : QuestText.Get("GrechkaQuests_02166534630f", "Проблемы совместимости: ") + string.Join("; ", issues.Take(5));
        }
        void EditPack(ref float y, float w)
        {
            draft.title = Field(ref y, w, QuestText.Get("GrechkaQuests_244a4f31fc86", "Название книги"), draft.title);
            Label(ref y, w, QuestText.Get("GrechkaQuests_bac2cc54582f", "ID пакета: ") + draft.id, 34);
            Label(ref y, w, QuestText.Get("GrechkaQuests_c0833c147c31", "Постоянные ID сохраняют историю выполнения и наград при повторном импорте. Для независимой книги используйте «Новая книга» во вкладке файлов."), 70);
            Chapter remove = null;
            foreach (var c in draft.chapters)
            {
                c.title = Field(ref y, Math.Max(120, w - 180), QuestText.Get("GrechkaQuests_7153fa98ed30", "Глава · ") + c.id, c.title);
                if (QuestArt.Button(new Rect(w - 170, y - 38, 80, 30), QuestText.Get("GrechkaQuests_6bdb0526f5aa", "Копия"), "QuestDuplicate"))
                {
                    try { QuestEditor.CopyChapter(draft, c); status = QuestText.Get("GrechkaQuests_6cec098425e7", "Глава скопирована с новыми ID и внутренними связями."); break; }
                    catch (Exception e) { Fail(e); }
                }
                if (QuestArt.Button(new Rect(w - 80, y - 38, 80, 30), QuestText.Get("GrechkaQuests_be99b1361201", "Удалить"), "QuestDelete")) remove = c;
                c.description = Field(ref y, w, QuestText.Get("GrechkaQuests_40c302a3732f", "Описание главы"), c.description ?? "", 1000);
                if (Widgets.ButtonText(new Rect(0, y, Math.Max(100, w - 150), 30), QuestText.Get("GrechkaQuests_5b9fd04c2eea", "Иконка: ") + (string.IsNullOrEmpty(c.icon) ? QuestText.Get("GrechkaQuests_5bb7aeb89a5a", "не выбрана") : DefLabel(c.icon))))
                    PickIcon(id => c.icon = id);
                int index = draft.chapters.IndexOf(c);
                if (Widgets.ButtonText(new Rect(w - 145, y, 70, 30), QuestText.Get("GrechkaQuests_3f151d97646a", "Вверх")) && index > 0)
                { draft.chapters.RemoveAt(index); draft.chapters.Insert(index - 1, c); break; }
                if (Widgets.ButtonText(new Rect(w - 70, y, 70, 30), QuestText.Get("GrechkaQuests_0ab592e07dee", "Вниз")) && index + 1 < draft.chapters.Count)
                { draft.chapters.RemoveAt(index); draft.chapters.Insert(index + 1, c); break; }
                y += 42;
            }
            if (remove != null) ConfirmRemoveChapter(remove);
            if (draft.chapters.Count < 100 && QuestArt.Button(new Rect(0, y, 170, 30), QuestText.Get("GrechkaQuests_fede6bf71417", "+ Глава"), "QuestChapterAdd")) draft.chapters.Add(new Chapter { id = PackCodec.NewId("chapter."), title = QuestText.Get("GrechkaQuests_ee4d74b27d47", "Новая глава") });
            y += 40;
        }
        void EditQuest(ref float y, float w, Quest q)
        {
            q.title = Field(ref y, w, QuestText.Get("GrechkaQuests_c204007ca197", "Название задания"), q.title);
            Label(ref y, w, "ID: " + q.id, 30);
            if (Widgets.ButtonText(new Rect(0, y, w, 30), QuestText.Get("GrechkaQuests_5b9fd04c2eea", "Иконка: ") + (string.IsNullOrEmpty(q.icon) ? QuestText.Get("GrechkaQuests_88f3e93de88a", "автоматическая") : DefLabel(q.icon))))
                PickIcon(id => q.icon = id);
            y += 40;
            string chapter = draft.chapters.FirstOrDefault(c => c.id == q.chapter)?.title;
            chapter = chapter == null ? QuestText.Get("GrechkaQuests_bf312ad49ff8", "Выбрать главу") : QuestPackText.Content(chapter);
            if (Widgets.ButtonText(new Rect(0, y, w, 30), QuestText.Get("GrechkaQuests_afa00e6527cb", "Глава: ") + chapter)) Find.WindowStack.Add(new FloatMenu(draft.chapters.Select(c => new FloatMenuOption(QuestPackText.Content(c.title), () => q.chapter = c.id)).ToList()));
            y += 40;
            Label(ref y, w, QuestText.Get("GrechkaQuests_b3680f2cba5f", "Описание"), 24);
            q.description = Widgets.TextArea(new Rect(0, y, w, 100), q.description); if (q.description.Length > 12000) q.description = q.description.Substring(0, 12000); y += 110;
            Label(ref y, w, QuestText.Get("GrechkaQuests_ff6dfee98e81", "Предыдущие задания (все перечисленные)"), 28);
            string removeDep = null;
            foreach (string id in q.requires)
            {
                Label(ref y, w - 90, QuestPackText.Content(draft.quests.FirstOrDefault(x => x.id == id)?.title ?? id), 32);
                if (Widgets.ButtonText(new Rect(w - 80, y - 36, 80, 30), QuestText.Get("GrechkaQuests_07cb65087477", "Убрать"))) removeDep = id;
            }
            if (removeDep != null) q.requires.Remove(removeDep);
            if (QuestArt.Button(new Rect(0, y, 220, 30), QuestText.Get("GrechkaQuests_7e1c88ee1f61", "+ Предыдущее задание"), "QuestLink"))
            {
                var candidates = draft.quests.Where(x => x.id != q.id && !q.requires.Contains(x.id)).ToList();
                if (candidates.Count > 0) Find.WindowStack.Add(new ChoiceWindow(QuestText.Get("GrechkaQuests_0c14805d3694", "Выберите задание"), candidates.Select(x => new Choice(QuestPackText.Content(x.title), x.id, () => {
                    string error; if (!QuestEditor.TryAddDependency(draft, q, x, out error)) status = error;
                })).ToList()));
                else status = QuestText.Get("GrechkaQuests_3b6d8ddf20c0", "Нет доступных заданий. Сначала создайте ещё одно.");
            }
            y += 44;
            Label(ref y, w, QuestText.Get("GrechkaQuests_e64dc5942e9e", "Цели — ничего не расходуют"), 30);
            Objective removeGoal = null;
            foreach (var o in q.objectives)
            {
                string typeLabel = o.type == "have" ? QuestText.Get("GrechkaQuests_8edc91bb702c", "Иметь предмет") : o.type == "build" ? QuestText.Get("GrechkaQuests_2163e8ff5f5a", "Иметь постройку") : o.type == "produce" ? QuestText.Get("GrechkaQuests_b7404d6d9ba7", "Произвести") : o.type == "mine" ? QuestText.Get("GrechkaQuests_96fc5b936c1d", "Добыть") : o.type == "harvest" ? QuestText.Get("GrechkaQuests_906be2792204", "Собрать урожай") : o.type == "kill" ? QuestText.Get("GrechkaQuests_ec89da0c31c3", "Убить или забить") : QuestText.Get("GrechkaQuests_fc875330d19a", "Ручная отметка");
                if (Widgets.ButtonText(new Rect(0, y, 200, 30), typeLabel))
                    Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption> {
                        new FloatMenuOption(QuestText.Get("GrechkaQuests_8edc91bb702c", "Иметь предмет"), () => ChangeObjective(o, "have", o.type == "have" ? o.target : "Steel")),
                        new FloatMenuOption(QuestText.Get("GrechkaQuests_2163e8ff5f5a", "Иметь постройку"), () => ChangeObjective(o, "build", o.type == "build" ? o.target : "TableStonecutter")),
                        new FloatMenuOption(QuestText.Get("GrechkaQuests_b7404d6d9ba7", "Произвести"), () => ChangeObjective(o, "produce", o.type == "produce" ? o.target : "Steel")),
                        new FloatMenuOption(QuestText.Get("GrechkaQuests_96fc5b936c1d", "Добыть"), () => ChangeObjective(o, "mine", o.type == "mine" ? o.target : "Steel")),
                        new FloatMenuOption(QuestText.Get("GrechkaQuests_906be2792204", "Собрать урожай"), () => ChangeObjective(o, "harvest", o.type == "harvest" ? o.target : "RawRice")),
                        new FloatMenuOption(QuestText.Get("GrechkaQuests_ec89da0c31c3", "Убить или забить"), () => ChangeObjective(o, "kill", o.type == "kill" ? o.target : "Cow")),
                        new FloatMenuOption(QuestText.Get("GrechkaQuests_fc875330d19a", "Ручная отметка"), () => { ChangeObjective(o, "manual", ""); o.count = 1; }) }));
                if (QuestArt.Button(new Rect(w - 80, y, 80, 30), QuestText.Get("GrechkaQuests_be99b1361201", "Удалить"), "QuestDelete")) removeGoal = o;
                y += 38;
                Widgets.Label(new Rect(0, y, Math.Max(80, w - 140), 30), "ID: " + o.id);
                if (Widgets.ButtonText(new Rect(w - 135, y, 135, 30), QuestText.Get("GrechkaQuests_d8648c2e6ab4", "Новый ID")))
                    Find.WindowStack.Add(new ConfirmWindow(QuestText.Get("GrechkaQuests_0479eac08e94", "Назначить цели новый ID? Это новое испытание: её прежняя отметка останется в истории, но не перейдёт на новую цель."), () => {
                        numberBuffers.Remove(o.id); o.id = PackCodec.NewId("goal.");
                    }));
                y += 38;
                if (o.type != "manual")
                {
                    if (Widgets.ButtonText(new Rect(0, y, w - 120, 30), DefLabel(o.target))) PickGoalDef(o, d => ChangeObjective(o, o.type, d.defName));
                    o.count = Number(new Rect(w - 110, y, 110, 30), o.id, o.count, 1000000); y += 38;
                    foreach (string variant in o.variants.ToList())
                    {
                        Label(ref y, w - 90, QuestText.Get("GrechkaQuests_671e30713c51", "или ") + DefLabel(variant), 30);
                        if (Widgets.ButtonText(new Rect(w - 80, y - 34, 80, 30), QuestText.Get("GrechkaQuests_07cb65087477", "Убрать"))) EditObjective(o, goal => goal.variants.Remove(variant));
                    }
                    if (o.variants.Count < 100 && Widgets.ButtonText(new Rect(0, y, w, 30), QuestText.Get("GrechkaQuests_ab18abdd5071", "+ Допустимый вариант")))
                        PickGoalDef(o, d => {
                            if (d.defName == o.target || o.variants.Contains(d.defName)) { status = QuestText.Get("GrechkaQuests_7401beaf57ff", "Этот вариант уже добавлен."); return; }
                            EditObjective(o, goal => goal.variants.Add(d.defName));
                        });
                    y += 38;
                    var defs = new[] { o.target }.Concat(o.variants).Select(QuestGame.ResolveThingDef).ToList();
                    bool materialApplicable = o.type != "kill" && defs.All(d => d != null && d.MadeFromStuff);
                    bool qualityApplicable = o.type != "kill" && defs.All(d => d != null && d.comps != null && d.comps.Any(c => c.compClass != null && typeof(CompQuality).IsAssignableFrom(c.compClass)));
                    if (materialApplicable || !string.IsNullOrEmpty(o.stuff))
                    {
                        if (Widgets.ButtonText(new Rect(0, y, w, 30), QuestText.Get("GrechkaQuests_149e3d5b66a9", "Материал: ") + (string.IsNullOrEmpty(o.stuff) ? QuestText.Get("GrechkaQuests_9ff894fa2d06", "любой") : DefLabel(o.stuff))))
                        {
                            var materials = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => materialApplicable && d.IsStuff && defs.All(item => d.stuffProps.CanMake(item)))
                                .Select(d => new Choice(d.LabelCap.ToString(), d.defName, () => EditObjective(o, goal => goal.stuff = d.defName))).ToList();
                            materials.Insert(0, new Choice(QuestText.Get("GrechkaQuests_5eede88fcfe5", "Любой"), "", () => EditObjective(o, goal => goal.stuff = null)));
                            Find.WindowStack.Add(new ChoiceWindow(QuestText.Get("GrechkaQuests_1831b9788471", "Материал цели"), materials));
                        }
                        y += 38;
                    }
                    if (qualityApplicable || !string.IsNullOrEmpty(o.minQuality))
                    {
                        if (Widgets.ButtonText(new Rect(0, y, w, 30), QuestText.Get("GrechkaQuests_95a11d8e2899", "Качество не ниже: ") + (o.minQuality == null ? QuestText.Get("GrechkaQuests_2fef9bf5fdab", "любое") : QualityLabel(o.minQuality))))
                        {
                            var qualities = new List<FloatMenuOption> { new FloatMenuOption(QuestText.Get("GrechkaQuests_9a30bb73d386", "Любое"), () => EditObjective(o, goal => goal.minQuality = null)) };
                            if (qualityApplicable) qualities.AddRange(Enum.GetValues(typeof(QualityCategory)).Cast<QualityCategory>()
                                .Select(level => new FloatMenuOption(QualityLabel(level.ToString()), () => EditObjective(o, goal => goal.minQuality = level.ToString()))));
                            Find.WindowStack.Add(new FloatMenu(qualities));
                        }
                        y += 38;
                    }
                }
                o.text = Field(ref y, w, QuestText.Get("GrechkaQuests_7a7eef319f69", "Своя подпись (необязательно)"), o.text, 1000);
                if (QuestGame.IsEventObjective(o)) Label(ref y, w, QuestGame.EventSourceSummary(o), 48);
                string issue = QuestGame.ObjectiveProblem(o); if (issue != null) Label(ref y, w, issue, 48);
                y += 8;
            }
            if (removeGoal != null) { q.objectives.Remove(removeGoal); numberBuffers.Clear(); }
            if (q.objectives.Count < 30 && QuestArt.Button(new Rect(0, y, 160, 30), QuestText.Get("GrechkaQuests_af144f4f41ee", "+ Цель"), "QuestObjectiveAdd")) q.objectives.Add(new Objective { id = PackCodec.NewId("goal.") });
            y += 42;
            Label(ref y, w, QuestText.Get("GrechkaQuests_4d4349d031cf", "Гарантированные предметные награды"), 32);
            EditRewardRows(ref y, w, q.rewards, q.id + ".r");
            Label(ref y, w, QuestText.Get("GrechkaQuests_d88e0b6143a2", "Наборы на выбор — игрок получит ровно один"), 48);
            RewardOption removeOption = null;
            foreach (RewardOption option in q.alternatives)
            {
                option.title = Field(ref y, Math.Max(100, w - 90), QuestText.Get("GrechkaQuests_c9eb348f52b2", "Название набора"), option.title);
                if (QuestArt.Button(new Rect(w - 80, y - 38, 80, 30), QuestText.Get("GrechkaQuests_be99b1361201", "Удалить"), "QuestDelete")) removeOption = option;
                EditRewardRows(ref y, w, option.items, q.id + ".a." + option.id);
            }
            if (removeOption != null) { q.alternatives.Remove(removeOption); numberBuffers.Clear(); }
            if (q.alternatives.Count < 30 && QuestArt.Button(new Rect(0, y, Math.Min(210, w), 30), QuestText.Get("GrechkaQuests_ad7097b26dc5", "+ Набор на выбор"), "RewardChoice"))
                q.alternatives.Add(new RewardOption { id = PackCodec.NewId("choice."), items = new List<Reward> { new Reward() } });
            y += 42;
            if (QuestArt.Button(new Rect(0, y, 200, 32), QuestText.Get("GrechkaQuests_6e7ffbb60db1", "Дублировать задание"), "QuestDuplicate"))
            {
                try { selected = QuestEditor.CopySelected(draft, new[] { q.id }).Single().id; status = QuestText.Get("GrechkaQuests_c6b82561f0b1", "Задание скопировано с новым ID, без связей и истории наград."); }
                catch (Exception e) { Fail(e); }
            }
            y += 44;
            if (QuestArt.Button(new Rect(0, y, 200, 32), QuestText.Get("GrechkaQuests_e49eaaa488ad", "Удалить задание"), "QuestDelete")) ConfirmRemoveQuest(q);
            y += 44;
        }
        void EditRewardRows(ref float y, float w, List<Reward> rewards, string keyPrefix)
        {
            Reward removeReward = null;
            foreach (var reward in rewards)
            {
                if (Widgets.ButtonText(new Rect(0, y, Math.Max(55, w - 210), 30), DefLabel(reward.def))) PickDef(false, true, d => { reward.def = d.defName; reward.stuff = ""; reward.quality = null; });
                reward.count = Number(new Rect(w - 200, y, 110, 30), keyPrefix + "." + rewards.IndexOf(reward), reward.count, 10000);
                if (QuestArt.Button(new Rect(w - 80, y, 80, 30), QuestText.Get("GrechkaQuests_be99b1361201", "Удалить"), "QuestDelete")) removeReward = reward;
                y += 38;
                var def = QuestGame.ResolveThingDef(reward.def);
                if (def != null && def.MadeFromStuff)
                {
                    if (Widgets.ButtonText(new Rect(0, y, w, 30), QuestText.Get("GrechkaQuests_149e3d5b66a9", "Материал: ") + (string.IsNullOrEmpty(reward.stuff) ? QuestText.Get("GrechkaQuests_fe7ef244bbd0", "по умолчанию") : DefLabel(reward.stuff))))
                    {
                        var choices = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.IsStuff && d.stuffProps.CanMake(def)).Select(d => new Choice(d.LabelCap.ToString(), d.defName, () => reward.stuff = d.defName)).ToList();
                        choices.Insert(0, new Choice(QuestText.Get("GrechkaQuests_fd0560ebbf83", "По умолчанию"), "", () => reward.stuff = "")); Find.WindowStack.Add(new ChoiceWindow(QuestText.Get("GrechkaQuests_96d950d1b42d", "Материал награды"), choices));
                    }
                    y += 38;
                }
                if (def != null && def.comps != null && def.comps.Any(c => c.compClass != null && typeof(CompQuality).IsAssignableFrom(c.compClass)))
                {
                    if (Widgets.ButtonText(new Rect(0, y, w, 30), QuestText.Get("GrechkaQuests_d48fb87eba0e", "Качество: ") + QualityLabel(reward.quality ?? "Normal")))
                    {
                        var qualityChoices = Enum.GetValues(typeof(QualityCategory)).Cast<QualityCategory>()
                            .Select(level => new FloatMenuOption(QualityLabel(level.ToString()), () => reward.quality = level.ToString())).ToList();
                        Find.WindowStack.Add(new FloatMenu(qualityChoices));
                    }
                    y += 38;
                }
                string issue = QuestGame.RewardProblem(reward); if (issue != null) Label(ref y, w, issue, 48);
            }
            if (removeReward != null) { rewards.Remove(removeReward); numberBuffers.Clear(); }
            if (rewards.Count < 30 && QuestArt.Button(new Rect(0, y, Math.Min(180, w), 30), QuestText.Get("GrechkaQuests_2f88b89396bf", "+ Награда"), "QuestRewardAdd")) rewards.Add(new Reward());
            y += 42;
        }
        void ChangeObjective(Objective objective, string type, string target)
        {
            string oldId = objective.id;
            if (QuestEditor.ChangeObjective(objective, type, target)) numberBuffers.Remove(oldId);
        }
        void EditObjective(Objective objective, Action<Objective> edit)
        {
            string oldId = objective.id;
            if (QuestEditor.EditObjective(objective, edit)) numberBuffers.Remove(oldId);
        }
        void ConfirmRemoveQuest(Quest q)
        {
            var affected = draft.quests.Where(x => x.requires.Contains(q.id)).Select(x => QuestPackText.Content(x.title)).ToList();
            string links = affected.Count == 0 ? QuestText.Get("GrechkaQuests_d746c6040ab7", "Связанных последующих заданий нет.") : QuestText.Get("GrechkaQuests_c463518b6bb5", "Будут удалены связи с заданиями: ") + string.Join(", ", affected) + QuestText.Get("GrechkaQuests_238179e65ab4", ". Сами задания останутся.");
            Find.WindowStack.Add(new ConfirmWindow(QuestText.Get("GrechkaQuests_e5e1e8cf4876", "Удалить «") + QuestPackText.Content(q.title) + QuestText.Get("GrechkaQuests_744df93bfe92", "» из черновика? ") + links + QuestText.Get("GrechkaQuests_f6d62809b3b9", " История наград останется в сохранении."), () => {
                QuestEditor.RemoveQuest(draft, q); if (selected == q.id) selected = null; numberBuffers.Clear();
            }));
        }
        void ConfirmRemoveChapter(Chapter chapter)
        {
            var inside = draft.quests.Where(q => q.chapter == chapter.id).ToList();
            if (inside.Count == 0)
            {
                Find.WindowStack.Add(new ConfirmWindow(QuestText.Get("GrechkaQuests_1f1e2ccb20dc", "Удалить пустую главу «") + QuestPackText.Content(chapter.title) + "»?", () => draft.chapters.Remove(chapter)));
                return;
            }
            var choices = new List<FloatMenuOption>();
            foreach (Chapter destination in draft.chapters.Where(c => c != chapter))
                choices.Add(new FloatMenuOption(QuestText.Get("GrechkaQuests_edc92b3efaf2", "Перенести задания в «") + QuestPackText.Content(destination.title) + "»", () =>
                    Find.WindowStack.Add(new ConfirmWindow(QuestText.Get("GrechkaQuests_4a2440ea9580", "Перенести ") + inside.Count + QuestText.Get("GrechkaQuests_2f1bf0c6e707", " заданий и удалить главу «") + QuestPackText.Content(chapter.title) + QuestText.Get("GrechkaQuests_6b40394f3dd4", "»? ID и связи сохранятся."), () => {
                        foreach (Quest quest in inside) quest.chapter = destination.id;
                        draft.chapters.Remove(chapter);
                    }))));
            var ids = new HashSet<string>(inside.Select(q => q.id));
            var affected = draft.quests.Where(q => !ids.Contains(q.id) && q.requires.Any(ids.Contains)).Select(q => QuestPackText.Content(q.title)).ToList();
            choices.Add(new FloatMenuOption(QuestText.Get("GrechkaQuests_5512767edf2a", "Удалить главу вместе с заданиями"), () =>
                Find.WindowStack.Add(new ConfirmWindow(QuestText.Get("GrechkaQuests_002a48d03e7f", "Удалить главу «") + QuestPackText.Content(chapter.title) + QuestText.Get("GrechkaQuests_a2e56cf1f06b", "» и заданий: ") + inside.Count + "? " +
                    (affected.Count == 0 ? QuestText.Get("GrechkaQuests_0f314aede865", "Внешних связей нет.") : QuestText.Get("GrechkaQuests_9d25a20182a2", "Будут удалены связи с: ") + string.Join(", ", affected) + QuestText.Get("GrechkaQuests_07e9cbb5392b", ". Эти задания останутся.")) +
                    QuestText.Get("GrechkaQuests_f6d62809b3b9", " История наград останется в сохранении."), () => {
                        foreach (Quest quest in inside) QuestEditor.RemoveQuest(draft, quest);
                        draft.chapters.Remove(chapter); selected = null; numberBuffers.Clear();
                    }))));
            Find.WindowStack.Add(new FloatMenu(choices));
        }
        static void PickIcon(Action<string> choose)
        {
            var defs = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.label != null &&
                (d.category == ThingCategory.Item || (d.category == ThingCategory.Building && d.designationCategory != null)));
            var choices = defs.OrderBy(d => d.label).Select(d => new Choice(d.LabelCap.ToString(), d.defName + " · " + (d.modContentPack?.Name ?? "Core"), () => choose(d.defName))).ToList();
            choices.InsertRange(0, QuestArt.Ids.Select(id => new Choice(id, QuestText.Get("GrechkaQuests_c27bc098af68", "Grechka · встроенная иконка"), () => choose(id))));
            Find.WindowStack.Add(new ChoiceWindow(QuestText.Get("GrechkaQuests_b9032bb3b45f", "Выберите иконку"), choices));
        }
        static void PickGoalDef(Objective goal, Action<ThingDef> choose)
        {
            if (goal.type == "have" || goal.type == "produce")
            {
                var candidates = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.label != null
                    && QuestGame.ObjectiveProblem(new Objective { type = goal.type, target = d.defName }) == null)
                    .OrderBy(d => d.label).Select(d => new Choice(d.LabelCap.ToString(), d.defName + " · " + (d.modContentPack?.Name ?? "Core"), () => choose(d))).ToList();
                Find.WindowStack.Add(new ChoiceWindow(QuestText.Get("GrechkaQuests_2877d37e284d", "Выберите ") + QuestText.Get("GrechkaQuests_d76943589150", "предмет"), candidates));
                return;
            }
            if (goal.type != "kill") { PickDef(goal.type == "build", false, choose); return; }
            var choices = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => d.category == ThingCategory.Pawn && d.race != null)
                .OrderBy(d => d.label).Select(d => new Choice(d.LabelCap.ToString(), d.defName + " · " + (d.modContentPack?.Name ?? "Core"), () => choose(d))).ToList();
            Find.WindowStack.Add(new ChoiceWindow(QuestText.Get("GrechkaQuests_bdc8dbc2fc49", "Выберите животное, человека или меха"), choices));
        }
        static void PickDef(bool building, bool reward, Action<ThingDef> choose)
        {
            var defs = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => building ? d.category == ThingCategory.Building && d.designationCategory != null : d.category == ThingCategory.Item && !d.IsCorpse && d.thingClass != null && !typeof(MinifiedThing).IsAssignableFrom(d.thingClass) && !typeof(UnfinishedThing).IsAssignableFrom(d.thingClass));
            if (reward) defs = defs.Where(d => QuestGame.RewardProblem(new Reward { def = d.defName }) == null);
            var choices = defs.OrderBy(d => d.label).Select(d => new Choice(d.LabelCap.ToString(), d.defName + " · " + (d.modContentPack?.Name ?? "Core"), () => choose(d))).ToList();
            Find.WindowStack.Add(new ChoiceWindow(QuestText.Get("GrechkaQuests_2877d37e284d", "Выберите ") + (building ? QuestText.Get("GrechkaQuests_9552f1770669", "постройку") : QuestText.Get("GrechkaQuests_d76943589150", "предмет")), choices));
        }
        void Files(Rect r, QuestGame game)
        {
            float y = r.y;
            Widgets.Label(new Rect(r.x, y, r.width, 50), QuestText.Get("GrechkaQuests_585fb37abc44", "Экспорт содержит черновик определений, без прогресса колонии. Импорт заменяет черновик; затем нажмите «Применить к этой колонии». Повторный импорт тех же ID не сбрасывает награды.")); y += 58;
            if (QuestArt.Button(new Rect(r.x, y, 200, 32), QuestText.Get("GrechkaQuests_5dadbd9317ca", "Экспортировать JSON"), "QuestExport"))
            { try { ValidateNumberBuffers(); if (SaveDraft(true)) { status = QuestText.Get("GrechkaQuests_74b29de477d6", "Экспорт: ") + PackFiles.Export(draft); ReloadFiles(); } } catch (Exception e) { Fail(e); } }
            if (Widgets.ButtonText(new Rect(r.x + 210, y, 180, 32), QuestText.Get("GrechkaQuests_0c05eada321b", "Обновить файлы"))) ReloadFiles();
            if (Widgets.ButtonText(new Rect(r.x + 400, y, 170, 32), QuestText.Get("GrechkaQuests_875b0b9be28e", "Новая книга")))
                Find.WindowStack.Add(new ConfirmWindow(QuestText.Get("GrechkaQuests_e5fc1b0d3d57", "Создать пустой черновик с новым ID? Текущий черновик сохранится отдельно; активная книга колонии не изменится."), () => ReplaceDraft(QuestGame.EmptyPack(), QuestText.Get("GrechkaQuests_bda3827616ab", "Новая книга создана в черновике."))));
            y += 40;
            if (Widgets.ButtonText(new Rect(r.x, y, 250, 32), QuestText.Get("GrechkaQuests_33d62695895e", "Книги из этой колонии")))
            {
                try
                {
                    var saved = game.SavedBooks();
                    var choices = saved.Select(p => new Choice(QuestPackText.Content(p.title), p.id, () => ImportDraft(p))).ToList();
                    Find.WindowStack.Add(new ChoiceWindow(QuestText.Get("GrechkaQuests_220f10e02d3e", "Сохранённые книги"), choices));
                }
                catch (Exception e) { Fail(e); }
            }
            y += 40;
            if (draftSaveBlocked)
            {
                if (Widgets.ButtonText(new Rect(r.x, y, 240, 32), QuestText.Get("GrechkaQuests_4876dc347fe9", "Восстановить из копии")))
                    Find.WindowStack.Add(new ConfirmWindow(QuestText.Get("GrechkaQuests_87262be79fd5", "Восстановить последнюю исправную копию? Повреждённый файл сохранится отдельно."), RecoverDraft));
                if (Widgets.ButtonText(new Rect(r.x + 250, y, 270, 32), QuestText.Get("GrechkaQuests_7b931b140ac7", "Начать из активной книги")))
                    Find.WindowStack.Add(new ConfirmWindow(QuestText.Get("GrechkaQuests_6ccc3e4d9dae", "Сохранить повреждённый файл отдельно и начать черновик из активной книги?"), () => ResetDraft(game)));
                y += 40;
            }
            Widgets.Label(new Rect(r.x, y, r.width, 54), QuestText.Get("GrechkaQuests_409e988c261b", "Папка обмена: ") + PackFiles.Folder); y += 60;
            float fileHeight = Math.Max(80, (r.height - 280) * 0.55f);
            Rect view = new Rect(r.x, y, r.width, fileHeight);
            Widgets.BeginScrollView(view, ref fileScroll, new Rect(0, 0, r.width - 20, Math.Max(fileHeight, files.Count * 44)));
            float fy = 0;
            foreach (string file in files)
            {
                string suffix = file.StartsWith(PackFiles.Folder, StringComparison.Ordinal) ? "" : file.StartsWith(PackFiles.LegacyFolder, StringComparison.Ordinal) ? QuestText.Get("GrechkaQuests_5c652774014c", " · старый экспорт") : QuestText.Get("GrechkaQuests_d9d30d541b3d", " · пример мода");
                Widgets.Label(new Rect(0, fy, r.width - 160, 38), Path.GetFileName(file) + suffix);
                if (QuestArt.Button(new Rect(r.width - 150, fy, 120, 32), QuestText.Get("GrechkaQuests_6e5142c48f0b", "Импорт"), "QuestImport"))
                { try { ImportDraft(PackFiles.Read(file)); } catch (Exception e) { Fail(e); } }
                fy += 44;
            }
            Widgets.EndScrollView(); y += fileHeight + 10;
            Widgets.Label(new Rect(r.x, y, r.width, 26), QuestText.Get("GrechkaQuests_afc29ad61804", "Или вставьте JSON сюда:")); y += 30;
            float textHeight = Math.Max(40, r.yMax - y - 42);
            jsonInput = Widgets.TextArea(new Rect(r.x, y, r.width, textHeight), jsonInput);
            if (jsonInput.Length > PackCodec.MaxBytes) jsonInput = jsonInput.Substring(0, PackCodec.MaxBytes);
            y += textHeight + 6;
            if (QuestArt.Button(new Rect(r.x, y, 220, 30), QuestText.Get("GrechkaQuests_01a236d180f4", "Импортировать из текста"), "QuestImport"))
            { try { ImportDraft(PackCodec.Decode(jsonInput)); } catch (Exception e) { Fail(e); } }
        }
        void ImportDraft(QuestPack pack)
        {
            Find.WindowStack.Add(new ConfirmWindow(QuestText.Get("GrechkaQuests_7807e73086ba", "Заменить черновик пакетом «") + QuestPackText.Content(pack.title) + "» (" + pack.quests.Count + QuestText.Get("GrechkaQuests_afdc988c2409", " заданий)?"), () =>
            {
                ReplaceDraft(pack, QuestText.Get("GrechkaQuests_10dfaee5c8d5", "Импортировано в черновик. ") + CompatibilitySummary(pack) + QuestText.Get("GrechkaQuests_869213e94951", " Для активации откройте редактор и примените."));
            }));
        }
    }
    public sealed class Choice
    {
        public readonly string Title, Detail; public readonly Action Select;
        public Choice(string title, string detail, Action select) { Title = title; Detail = detail; Select = select; }
    }
    public sealed class RewardChoiceWindow : Window
    {
        readonly QuestGame game;
        readonly QuestClaimPlan plan;
        readonly Action<string> report;
        readonly List<Quest> quests;
        readonly Dictionary<string, string> choices = new Dictionary<string, string>();
        Vector2 scroll;
        string error = "";
        public override Vector2 InitialSize => new Vector2(Math.Min(900, UI.screenWidth - 40), Math.Min(760, UI.screenHeight - 40));
        public RewardChoiceWindow(QuestGame game, QuestClaimPlan plan, Action<string> report)
        {
            this.game = game; this.plan = plan; this.report = report;
            quests = plan.questIds.Select(id => game.Pack.quests.First(q => q.id == id)).Where(q => q.alternatives.Count > 0).ToList();
            doCloseX = true; absorbInputAroundWindow = true; forcePause = false;
        }
        static string RewardName(Reward reward) => QuestGame.ResolveThingDef(reward.def)?.LabelCap.ToString() ?? reward.def;
        static float OptionHeight(RewardOption option, float width) => 52 + option.items.Sum(item => QuestVisuals.ItemRowHeight(width - 16, RewardName(item)) + 6);
        public override void DoWindowContents(Rect rect)
        {
            Widgets.Label(new Rect(0, 0, rect.width - 36, 50), QuestText.Get("GrechkaQuests_6e9fcad262ed", "Выберите один набор в каждом задании. Предметы и их количество показаны ниже."));
            Rect area = new Rect(0, 56, rect.width, Math.Max(0, rect.height - 154));
            float width = area.width - 24, col = (width - 12) / 2;
            float total = 0;
            foreach (Quest q in quests)
            {
                total += Text.CalcHeight(QuestPackText.Content(q.title), width) + 12;
                for (int i = 0; i < q.alternatives.Count; i += 2)
                    total += Math.Max(OptionHeight(q.alternatives[i], col), i + 1 < q.alternatives.Count ? OptionHeight(q.alternatives[i + 1], col) : 0) + 12;
            }
            Widgets.BeginScrollView(area, ref scroll, new Rect(0, 0, area.width - 20, Math.Max(area.height, total)));
            try
            {
                float y = 0;
                foreach (Quest q in quests)
                {
                    float titleHeight = Text.CalcHeight(QuestPackText.Content(q.title), width) + 12;
                    Widgets.Label(new Rect(0, y, width, titleHeight), QuestPackText.Content(q.title)); y += titleHeight;
                    for (int i = 0; i < q.alternatives.Count; i += 2)
                    {
                        float rowHeight = 0;
                        for (int j = i; j < Math.Min(i + 2, q.alternatives.Count); j++)
                        {
                            RewardOption option = q.alternatives[j];
                            float x = (j - i) * (col + 12), h = OptionHeight(option, col);
                            rowHeight = Math.Max(rowHeight, h);
                            bool picked = choices.TryGetValue(q.id, out string id) && id == option.id;
                            Rect card = new Rect(x, y, col, h);
                            Widgets.DrawBoxSolid(card, picked ? new Color(.22f, .34f, .23f) : new Color(.13f, .17f, .20f));
                            if (Widgets.ButtonText(new Rect(x + 6, y + 6, col - 12, 38), (picked ? "✓ " : "") + QuestPackText.Content(option.title))) choices[q.id] = option.id;
                            float itemY = y + 50;
                            foreach (Reward reward in option.items)
                            {
                                float itemHeight = QuestVisuals.ItemRowHeight(col - 16, RewardName(reward));
                                Rect itemRect = new Rect(x + 8, itemY, col - 16, itemHeight);
                                QuestVisuals.ItemRow(itemRect, reward.def, RewardName(reward), "× " + reward.count, false, reward.stuff);
                                TooltipHandler.TipRegion(itemRect, RewardName(reward) + " × " + reward.count + (string.IsNullOrEmpty(reward.quality) ? "" : " · " + reward.quality));
                                if (Widgets.ButtonInvisible(itemRect)) choices[q.id] = option.id;
                                itemY += itemHeight + 6;
                            }
                        }
                        y += rowHeight + 12;
                    }
                }
            }
            finally { Widgets.EndScrollView(); }
            Widgets.Label(new Rect(0, rect.height - 90, rect.width, 42), error);
            bool ready = quests.All(q => choices.ContainsKey(q.id));
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && ready;
            bool claim = QuestArt.Button(new Rect(0, rect.height - 42, 250, 36), QuestText.Get("GrechkaQuests_f5a3a3032915", "Получить выбранное"), "QuestClaimReward");
            GUI.enabled = enabled;
            if (claim)
            {
                try
                {
                    string result = game.FinishClaim(plan, choices);
                    if (result != null) error = result;
                    else { report(QuestText.Get("GrechkaQuests_8fff9941d5fd", "Получено заданий: ") + plan.questIds.Count + QuestText.Get("GrechkaQuests_05313ea5f998", ". Предметы целей остаются у вас.")); Close(); }
                }
                catch (Exception e) { error = QuestText.Get("GrechkaQuests_9ecf847b5748", "Ошибка выдачи: ") + e.Message; Log.Error("[Grechka Quests] " + error); }
            }
            if (Widgets.ButtonText(new Rect(rect.width - 170, rect.height - 42, 170, 36), QuestText.Get("GrechkaQuests_8fbe9b75cbdf", "Отмена"))) Close();
        }
    }
    public sealed class ChoiceWindow : Window
    {
        readonly string title; readonly List<Choice> choices; string search = ""; Vector2 scroll;
        public override Vector2 InitialSize { get { return new Vector2(720, 600); } }
        public ChoiceWindow(string title, List<Choice> choices) { this.title = title; this.choices = choices; doCloseX = true; absorbInputAroundWindow = true; forcePause = false; }
        public override void DoWindowContents(Rect r)
        {
            Widgets.Label(new Rect(0, 0, r.width - 30, 32), title);
            string next = Widgets.TextField(new Rect(0, 38, r.width, 32), search, 120); if (next != search) { search = next; scroll = Vector2.zero; }
            var list = choices.Where(c => (c.Title + " " + c.Detail).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            Rect view = new Rect(0, 80, r.width, r.height - 80);
            Widgets.BeginScrollView(view, ref scroll, new Rect(0, 0, r.width - 20, list.Count * 58));
            // Only draw visible rows; large modpacks can have thousands of defs.
            int start = Math.Max(0, (int)(scroll.y / 58) - 1), end = Math.Min(list.Count, start + (int)(view.height / 58) + 3);
            for (int i = start; i < end; i++)
            {
                var c = list[i];
                if (Widgets.ButtonText(new Rect(0, i * 58, r.width - 24, 54), c.Title + "\n" + c.Detail)) { c.Select(); Close(); break; }
            }
            Widgets.EndScrollView();
        }
    }
    public sealed class ConfirmWindow : Window
    {
        readonly string message; readonly Action action;
        Vector2 messageScroll;
        public override Vector2 InitialSize { get { return new Vector2(520, 240); } }
        public ConfirmWindow(string message, Action action) { this.message = message; this.action = action; doCloseX = true; absorbInputAroundWindow = true; forcePause = false; }
        public override void DoWindowContents(Rect r)
        {
            Rect area = new Rect(0, 0, r.width, r.height - 50);
            float messageHeight = Math.Max(area.height, Text.CalcHeight(message, area.width - 20) + 8);
            Widgets.BeginScrollView(area, ref messageScroll, new Rect(0, 0, area.width - 20, messageHeight));
            Widgets.Label(new Rect(0, 0, area.width - 20, messageHeight), message);
            Widgets.EndScrollView();
            if (Widgets.ButtonText(new Rect(0, r.height - 40, 190, 36), QuestText.Get("GrechkaQuests_27bca309f5f8", "Подтвердить"))) { action(); Close(); }
            if (Widgets.ButtonText(new Rect(r.width - 190, r.height - 40, 190, 36), QuestText.Get("GrechkaQuests_8fbe9b75cbdf", "Отмена"))) Close();
        }
    }
}
