using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grechka.Quests
{
    public sealed class QuestWindow : MainTabWindow
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
        Vector2 listScroll, detailScroll, fileScroll;
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
        public override Vector2 RequestedTabSize { get { return new Vector2(1100, 760); } }
        public override void PostOpen()
        {
            base.PostOpen(); var game = QuestGame.Current; if (game == null) return;
            game.Refresh(); draft = PackCodec.Clone(game.Pack); tab = 0;
            bookSelection = game.Pack.quests.FirstOrDefault()?.id; draftSelection = draft.quests.FirstOrDefault()?.id;
            bookChapter = ChooseChapter(game.Pack, game.LastChapter(game.Pack.id));
            draftChapter = ChooseChapter(draft, game.LastChapter(draft.id));
            bookSelection = game.Pack.quests.FirstOrDefault(q => q.chapter == bookChapter)?.id;
            searchText = ""; graphSelection.Clear(); draggingNode = null; linkingFrom = null;
            draftInitialized = false; draftSaveBlocked = false; damagedDraftPath = null; lastSavedDraft = null; draftHistory = null;
            numberBuffers.Clear(); status = game.LoadError ?? "";
            if (GrechkaMod.Settings != null && GrechkaMod.Settings.authorMode) LoadDraft(game);
        }
        public override void PostClose()
        {
            if (draftInitialized && (draftSaveBlocked || !SaveDraft(true)))
                Messages.Message("Черновик квестов не удалось сохранить. Проверьте журнал игры и папку GrechkaQuests.", MessageTypeDefOf.RejectInput, false);
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
                    status = "Восстановлен отдельный черновик «" + draft.title + "». Активная книга колонии: «" + game.Pack.title + "». Черновик не применён автоматически.";
                }
                else status = "Редактируется копия активной книги; изменения сохраняются отдельно.";
                if (game.LoadError != null) status = game.LoadError + " " + status;
            }
            catch (Exception e)
            {
                draftSaveBlocked = true;
                status = "Черновик не прочитан и не будет перезаписан: " + e.Message + ". Во вкладке файлов можно восстановить резервную копию.";
                Log.Warning("[Grechka Quests] " + status);
            }
            try { draftHistory = new DraftHistory(CurrentDraft()); }
            catch (Exception e) { status = "Нельзя открыть историю черновика: " + e.Message; }
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
                status = "Черновик не сохранён: " + e.Message;
                Log.Warning("[Grechka Quests] " + status);
                return false;
            }
        }
        void ValidateNumberBuffers()
        {
            if (numberBuffers.Values.Any(v => !int.TryParse(v, out int n) || n < 1 || n > 1000000))
                throw new FormatException("Проверьте числовые поля: нужны положительные целые числа.");
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
                        throw new InvalidOperationException("Повреждённый файл не определён; проверьте доступ к папке черновиков.");
                    draftStore.ArchiveDamagedAndStart(draftOwner, damagedDraftPath, replacement);
                    draftSaveBlocked = false; SetDraft(replacement); lastSavedDraft = DraftStore.Encode(replacement);
                    draftHistory = new DraftHistory(replacement);
                    status = "Повреждённый файл сохранён отдельно, начат черновик активной книги.";
                }
                else ReplaceDraft(replacement.pack, "Черновик восстановлен из активной книги колонии.");
            }
            catch (Exception e) { Fail(e); }
        }
        void RecoverDraft()
        {
            try
            {
                if (draftStore == null || string.IsNullOrEmpty(damagedDraftPath))
                    throw new InvalidOperationException("Повреждённый файл не определён; проверьте доступ к папке черновиков.");
                DraftDocument restored = draftStore.RecoverLatest(draftOwner, damagedDraftPath);
                SetDraft(restored); draftSaveBlocked = false; lastSavedDraft = DraftStore.Encode(restored);
                draftHistory = new DraftHistory(restored);
                status = "Черновик восстановлен из резервной копии; повреждённый файл сохранён отдельно.";
            }
            catch (Exception e) { Fail(e); }
        }
        void ReloadFiles() { try { files = PackFiles.List(); } catch (Exception e) { status = e.Message; } }
        void Fail(Exception e) { status = "Ошибка: " + e.Message; Log.Warning("[Grechka Quests] " + e); }
        public override void DoWindowContents(Rect rect)
        {
            EventType initialEvent = Event.current.type;
            var game = QuestGame.Current; if (game == null || draft == null) { Widgets.Label(rect, "Откройте колонию, чтобы работать с квестами."); return; }
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
            if (Widgets.ButtonText(new Rect(0, 0, 140, 32), "Книга")) { tab = 0; game.Refresh(); detailScroll = Vector2.zero; }
            if (author && Widgets.ButtonText(new Rect(148, 0, 140, 32), "Редактор")) { tab = 1; detailScroll = Vector2.zero; }
            if (author && Widgets.ButtonText(new Rect(296, 0, 180, 32), "Импорт / экспорт")) { tab = 2; ReloadFiles(); }
            float titleX = author ? 490f : 155f;
            Widgets.Label(new Rect(titleX, 3, Math.Max(0, rect.width - titleX - 170), 30), "Grechka Quests · " + (tab == 0 ? game.Pack.title : draft.title));
            if (tab == 0 && Widgets.ButtonText(new Rect(rect.width - 160, 0, 160, 32), "Забрать всё"))
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
                try { draftHistory.Record(CurrentDraft()); } catch (Exception e) { status = "Ошибка истории черновика: " + e.Message; }
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
            searchText = Widgets.TextField(new Rect(left.x, left.y, left.width, 30), searchText ?? "", 120);
            top += 36;
            if (tab == 1)
            {
                if (Widgets.ButtonText(new Rect(left.x, left.y + top, 90, 30), "+ Квест"))
                    CreateQuestAt(new Vector2(40, 40));
                if (Widgets.ButtonText(new Rect(left.x + 96, left.y + top, 94, 30), "Главы")) selected = null;
                top += 36;
                if (selectedEdgeFrom != null && selectedEdgeTo != null)
                {
                    if (Widgets.ButtonText(new Rect(left.x, left.y + top, left.width, 30), "Удалить связь")) RemoveSelectedEdge();
                    top += 36;
                }
            }
            var matches = string.IsNullOrWhiteSpace(searchText) ? null : p.quests.Where(q =>
                (q.title + " " + q.description).IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            float contentHeight = matches == null ? p.chapters.Count * 45f : matches.Count * 50f;
            Rect nav = new Rect(left.x, left.y + top, left.width, Math.Max(0, left.height - top));
            Widgets.BeginScrollView(nav, ref listScroll, new Rect(0, 0, nav.width - 18, Math.Max(nav.height, contentHeight + 10)));
            float y = 0;
            if (matches != null)
            {
                foreach (Quest q in matches)
                {
                    string chapter = p.chapters.FirstOrDefault(c => c.id == q.chapter)?.title ?? "";
                    if (Widgets.ButtonText(new Rect(0, y, nav.width - 20, 46), q.title + "\n" + chapter))
                    { SelectQuest(p, game, q); searchText = ""; }
                    y += 50;
                }
            }
            else
            {
                foreach (Chapter chapter in p.chapters)
                {
                    var quests = p.quests.Where(q => q.chapter == chapter.id).ToList();
                    string progress = tab == 0 ? " (" + quests.Count(game.Claimed) + "/" + quests.Count + ")" : "";
                    Rect button = new Rect(0, y, nav.width - 20, 40);
                    if (Widgets.ButtonText(button, ""))
                    {
                        currentChapter = chapter.id; game.SetLastChapter(p.id, chapter.id);
                        selected = quests.FirstOrDefault()?.id; graphSelection.Clear(); selectedEdgeFrom = selectedEdgeTo = null;
                        detailScroll = Vector2.zero;
                    }
                    ThingDef chapterIcon = string.IsNullOrEmpty(chapter.icon) ? null : DefDatabase<ThingDef>.GetNamedSilentFail(chapter.icon);
                    if (chapterIcon != null) Widgets.ThingIcon(new Rect(button.x + 3, button.y + 4, 30, 30), chapterIcon);
                    Widgets.Label(new Rect(button.x + (chapterIcon == null ? 5 : 36), button.y + 8, button.width - (chapterIcon == null ? 10 : 41), 30), chapter.title + progress);
                    if (tab == 0 && quests.Any(game.CanClaim)) QuestBadge.Draw(new Rect(button.xMax - 22, button.y, 20, 20));
                    y += 45;
                }
            }
            Widgets.EndScrollView();
            DrawGraph(graph, p, game);
            Quest current = p.quests.FirstOrDefault(q => q.id == selected);
            if (tab == 0) Book(right, game, current); else Editor(right, current, game);
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
                pan = new Vector2(lastGraphBounds.width / 2 - (p.x + 75) * zoom, lastGraphBounds.height / 2 - (p.y + 31) * zoom);
                game.SetGraphView(pack.id, quest.chapter, pan, zoom);
            }
        }
        static Vector2 GraphPosition(Quest quest, int index)
        {
            return quest.graphPositioned ? new Vector2(quest.graphX, quest.graphY) : new Vector2((index % 3) * 170, (index / 3) * 100);
        }
        static Rect GraphNodeRect(Quest quest, int index, Vector2 pan, float zoom)
        {
            Vector2 p = GraphPosition(quest, index) * zoom + pan;
            return new Rect(p.x, p.y, 150 * zoom, 62 * zoom);
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
            if (currentChapter == null) { Widgets.Label(bounds.ContractedBy(10), "Добавьте главу в редакторе."); return; }
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
                        start = new Vector2(parent.xMax, parent.center.y), end = new Vector2(target.xMin, target.center.y) });
                }
            }
            GUI.BeginGroup(bounds);
            try
            {
                Event ev = Event.current; Vector2 mouse = ev.mousePosition;
                foreach (GraphEdge edge in edges)
                {
                    bool picked = edge.from == selectedEdgeFrom && edge.to == selectedEdgeTo;
                    Widgets.DrawLine(edge.start, edge.end, picked ? new Color(1f, 0.8f, 0.2f) : new Color(0.55f, 0.60f, 0.65f), picked ? 3f : 2f);
                }
                if (linkingFrom != null && rects.TryGetValue(linkingFrom, out Rect fromRect))
                    Widgets.DrawLine(new Vector2(fromRect.xMax, fromRect.center.y), mouse, Color.yellow, 2f);
                foreach (Quest q in quests)
                {
                    Rect node = rects[q.id];
                    bool chosen = selected == q.id || (tab == 1 && graphSelection.Contains(q.id));
                    Color background = tab == 0 && game.Claimed(q) ? new Color(0.18f, 0.34f, 0.20f) :
                        tab == 0 && !game.Unlocked(q) ? new Color(0.19f, 0.20f, 0.22f) : new Color(0.25f, 0.28f, 0.32f);
                    Widgets.DrawBoxSolid(node, background);
                    if (chosen) Widgets.DrawHighlightSelected(node); else Widgets.DrawHighlightIfMouseover(node);
                    string iconId = !string.IsNullOrEmpty(q.icon) ? q.icon : q.objectives.FirstOrDefault(o => o.type != "manual")?.target;
                    ThingDef iconDef = string.IsNullOrEmpty(iconId) ? null : DefDatabase<ThingDef>.GetNamedSilentFail(iconId);
                    float iconSize = Mathf.Min(40 * zoom, node.height - 6);
                    if (iconDef != null && iconSize > 5) Widgets.ThingIcon(new Rect(node.x + 3, node.y + 3, iconSize, iconSize), iconDef);
                    float textX = iconDef == null ? node.x + 5 : node.x + iconSize + 6;
                    Widgets.Label(new Rect(textX, node.y + 4, Math.Max(0, node.xMax - textX - 13), Math.Max(0, node.height - 6)), q.title);
                    if (tab == 1) Widgets.DrawBoxSolid(new Rect(node.xMax - 11, node.center.y - 5, 10, 10), Color.white);
                    else if (game.CanClaim(q)) QuestBadge.Draw(new Rect(node.xMax - 22, node.y, 20, 20));
                }
                if (quests.Count == 0) Widgets.Label(new Rect(12, 12, Math.Max(0, bounds.width - 24), 50), tab == 1 ? "ПКМ: создать квест" : "В этой главе пока нет заданий.");
                HandleGraphInput(ev, mouse, bounds, pack, game, quests, rects, edges, ref pan, ref zoom);
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
                            new FloatMenuOption("Дублировать задание", () => CopyGraphSelection(new[] { hit.id })),
                            new FloatMenuOption("Копировать выделенные (" + graphSelection.Count + ")", () => CopyGraphSelection(graphSelection.ToArray())),
                            new FloatMenuOption("Удалить задание", () => ConfirmRemoveQuest(hit))
                        };
                        Find.WindowStack.Add(new FloatMenu(menu));
                    }
                    else if (edge != null)
                    {
                        selectedEdgeFrom = edge.from; selectedEdgeTo = edge.to;
                        Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption> { new FloatMenuOption("Удалить связь", RemoveSelectedEdge) }));
                    }
                    else
                    {
                        Vector2 position = (mouse - pan) / zoom;
                        Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption> { new FloatMenuOption("Создать квест", () => CreateQuestAt(position)) }));
                    }
                    ev.Use(); return;
                }
                if (ev.button == 0)
                {
                    GUIUtility.keyboardControl = 0;
                    if (hit != null)
                    {
                        Rect node = rects[hit.id]; selected = hit.id; detailScroll = Vector2.zero;
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
                        selectedEdgeFrom = edge.from; selectedEdgeTo = edge.to; status = "Связь выделена. Нажмите «Удалить связь».";
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
                        status = QuestEditor.TryAddDependency(draft, target, source, out error) ? "Связь создана." : error;
                    }
                    else status = "Связь не создана: выберите второе задание.";
                    ev.Use();
                }
                draggingCanvas = false; draggingNode = null; linkingFrom = null;
            }
        }
        void CreateQuestAt(Vector2 position)
        {
            if (draft.quests.Count >= 500) { status = "Лимит: 500 заданий."; return; }
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
            selectedEdgeFrom = selectedEdgeTo = null; status = "Связь удалена.";
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
                selected = copies.First().id; status = "Копия создана с новыми ID и внутренними связями.";
            }
            catch (Exception e) { Fail(e); }
        }
        void Book(Rect r, QuestGame game, Quest q)
        {
            if (q == null) { Widgets.Label(r, !game.HasColony ? "Создайте колонию" : game.Pack.quests.Count == 0 ? "Книга пока пуста. Создайте задания в редакторе или импортируйте пример во вкладке «Импорт / экспорт»." : "Выберите задание слева."); return; }
            Widgets.BeginScrollView(r, ref detailScroll, new Rect(0, 0, r.width - 20, detailHeight));
            float w = r.width - 24, y = 0;
            Label(ref y, w, q.title, 42);
            Label(ref y, w, q.description, Math.Max(60, Text.CalcHeight(q.description, w) + 8));
            if (q.requires.Count > 0)
            {
                Label(ref y, w, game.Unlocked(q) ? "Предшественники:" : "Сначала получите награды предшественников:", 34);
                foreach (string parentId in q.requires)
                {
                    Quest parent = game.Pack.quests.FirstOrDefault(x => x.id == parentId);
                    if (parent != null && Widgets.ButtonText(new Rect(0, y, w, 30), (game.Claimed(parent) ? "✓ " : "→ ") + parent.title))
                        SelectQuest(game.Pack, game, parent);
                    y += 36;
                }
            }
            foreach (var problem in QuestGame.Problems(q)) Label(ref y, w, "⚠ " + problem, 48);
            foreach (var o in q.objectives)
            {
                string text = string.IsNullOrWhiteSpace(o.text) ? ObjectiveTitle(o) : o.text;
                string progress = o.type == "manual" ? (game.Manual(q, o) ? " — отмечено" : "")
                    : game.ObjectiveAchieved(q, o) ? " — выполнено" : " — " + game.Value(q, o) + " / " + o.count;
                Label(ref y, w, text + progress, 48);
                if (QuestGame.IsEventObjective(o))
                    Label(ref y, w, QuestGame.EventSourceSummary(o), 48);
                if (o.type != "manual" && (!string.IsNullOrEmpty(o.stuff) || !string.IsNullOrEmpty(o.minQuality)))
                    Label(ref y, w, (string.IsNullOrEmpty(o.stuff) ? "" : "Материал: " + DefLabel(o.stuff) + "  ")
                        + (string.IsNullOrEmpty(o.minQuality) ? "" : "Качество не ниже: " + QualityLabel(o.minQuality)), 34);
                if (o.type != "manual")
                {
                    string key = PackCodec.ObjectiveKey(game.Pack.id, q.id, o.id);
                    if (Widgets.ButtonText(new Rect(0, y, 150, 30), expandedObjectives.Contains(key) ? "Скрыть" : "Подробнее"))
                    { if (!expandedObjectives.Add(key)) expandedObjectives.Remove(key); }
                    y += 36;
                    if (expandedObjectives.Contains(key))
                    {
                        var breakdown = game.ObjectiveBreakdown(q, o);
                        string scope = QuestGame.IsEventObjective(o) ? "События до подключения цели не восстанавливаются по текущим остаткам. " + QuestGame.EventSourceSummary(o) : o.type == "build" ? "Считаются готовые свои здания на домашних картах." :
                            "Считается имущество на домашних картах, у своих пешек на картах и в караванах. Туман, чужие пешки и груз в полёте не учитываются.";
                        string detail = scope + "\nПодходит: " + string.Join(", ", new[] { o.target }.Concat(o.variants).Select(DefLabel))
                            + "\n" + (QuestGame.IsEventObjective(o) ? "Учтено событий: " + game.Value(q, o) : "Сейчас: " + (breakdown.Count == 0 ? "0" : string.Join(", ", breakdown.Select(pair => pair.Key + " — " + pair.Value)))) + ".";
                        Label(ref y, w, detail, Math.Max(90, Text.CalcHeight(detail, w) + 8));
                    }
                }
                if (o.type == "manual" && !game.Manual(q, o) && game.HasColony && !game.Claimed(q))
                {
                    if (Widgets.ButtonText(new Rect(0, y, 200, 30), "Отметить выполнение")) game.MarkManual(q, o);
                    y += 38;
                }
            }
            if (!game.HasColony) Label(ref y, w, "Создайте колонию", 34);
            Label(ref y, w, "Гарантированно: " + (q.rewards.Count == 0 ? "нет предметных наград" : string.Join(", ", q.rewards.Select(x => x.count + " × " + DefLabel(x.def)))), 70);
            if (q.alternatives.Count > 0)
            {
                Label(ref y, w, "Выберите один набор:", 30);
                foreach (RewardOption option in q.alternatives)
                    Label(ref y, w, option.title + ": " + string.Join(", ", option.items.Select(x => x.count + " × " + DefLabel(x.def))), 50);
            }
            if (game.Claimed(q))
            {
                string choice = game.ChosenReward(q);
                string chosenLabel = choice == null ? "" : " Выбран набор: " + (q.alternatives.FirstOrDefault(x => x.id == choice)?.title ?? choice) + ".";
                Label(ref y, w, "Награда уже получена." + chosenLabel, 45);
            }
            else if (game.HasColony && game.Unlocked(q) && game.Completed(q))
            {
                if (Widgets.ButtonText(new Rect(0, y, Math.Min(240, w), 36), q.rewards.Count == 0 && q.alternatives.Count == 0 ? "Завершить" : "Получить награду"))
                {
                    StartClaim(game, q);
                }
                y += 44;
            }
            if (game.Completed(q)) Label(ref y, w, "Выполнение зафиксировано в этой колонии.", 34);
            if (game.PendingCount > 0) Label(ref y, w, "В старом сохранении есть невыданные награды: " + game.PendingCount + " стопок. Они сохранены для переноса; автоматическая выдача отключена.", 60);
            Label(ref y, w, "Наличие: видимые предметы на домашних картах и вещи своих пешек на картах и в караванах. Постройки: готовые здания игрока. Добавление и слияние предметов проверяются сразу; контрольная сверка идёт раз в 250 тиков.", 90);
            detailHeight = y + 15; Widgets.EndScrollView();
        }
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
                    status = result ?? "Получено заданий: " + plan.questIds.Count + ". Предметы целей остаются у вас.";
                }
            }
            catch (Exception e) { Fail(e); }
        }
        static string DefLabel(string name) { var d = DefDatabase<ThingDef>.GetNamedSilentFail(name); return d == null ? "[нет: " + name + "]" : d.LabelCap.ToString(); }
        static string QualityLabel(string quality)
        {
            switch (quality)
            {
                case "Awful": return "ужасное";
                case "Poor": return "плохое";
                case "Normal": return "обычное";
                case "Good": return "хорошее";
                case "Excellent": return "отличное";
                case "Masterwork": return "шедевр";
                case "Legendary": return "легендарное";
                default: return quality;
            }
        }
        static string ObjectiveTitle(Objective o)
        {
            string prefix = o.type == "manual" ? "Ручная цель" : o.type == "build" ? "Построить: " : o.type == "produce" ? "Произвести: " : o.type == "mine" ? "Добыть: " : o.type == "harvest" ? "Собрать урожай: " : "Иметь: ";
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
            if (Widgets.ButtonText(new Rect(r.x, r.y, half, 32), "Применить"))
            {
                try
                {
                    ValidateNumberBuffers();
                    if (!SaveDraft(true)) return;
                    QuestPack candidate = game.ValidateApply(draft);
                    int pendingEventGoals = candidate.quests.Sum(item => item.objectives.Count(QuestGame.IsEventObjective));
                    string summary = BookHistory.DescribeChange(game.Pack, candidate) + "\n" + CompatibilitySummary(candidate)
                        + (pendingEventGoals == 0 ? "" : "\nСобытийных целей: " + pendingEventGoals + ". Стандартные источники подключены; нестандартным процессам нужны адаптеры.");
                    Find.WindowStack.Add(new ConfirmWindow(summary, () => {
                        try
                        {
                            game.Apply(candidate); bookSelection = game.Pack.quests.FirstOrDefault()?.id;
                            bookChapter = ChooseChapter(game.Pack, game.LastChapter(game.Pack.id));
                            status = "Применено. Сохраните игру для записи прогресса; экспортируйте JSON для переноса. " + CompatibilitySummary(candidate);
                        }
                        catch (Exception e) { Fail(e); }
                    }));
                }
                catch (Exception e) { Fail(e); }
            }
            if (Widgets.ButtonText(new Rect(r.x + half + 8, r.y, half, 32), "Из активной книги"))
            {
                Find.WindowStack.Add(new ConfirmWindow("Начать черновик заново из активной книги? Текущие неприменённые правки сохраняются в резервной копии, повреждённый файл — отдельно.", () => ResetDraft(game)));
            }
            if (Widgets.ButtonText(new Rect(r.x, r.y + 38, half, 30), "← Отменить")) EditHistory(false);
            if (Widgets.ButtonText(new Rect(r.x + half + 8, r.y + 38, half, 30), "Повторить →")) EditHistory(true);
            if (Widgets.ButtonText(new Rect(r.x, r.y + 76, half, 30), "Проверить книгу")) CheckDraft(game);
            if (Widgets.ButtonText(new Rect(r.x + half + 8, r.y + 76, half, 30), "Предпросмотр"))
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
                catch (Exception e) { choices.Add(new Choice(e.Message, "Числовые поля", () => { })); }
                List<string> structural = PackCodec.Validate(draft);
                foreach (string issue in structural)
                {
                    Quest target = draft.quests?.FirstOrDefault(q => q != null && !string.IsNullOrEmpty(q.id) && issue.Contains(q.id));
                    choices.Add(new Choice(issue, target == null ? "Книга" : target.title, () => { if (target != null) SelectQuest(draft, game, target); }));
                }
                if (structural.Count == 0)
                    foreach (Quest item in draft.quests)
                    {
                        foreach (Objective goal in item.objectives)
                        {
                            string issue = QuestGame.ObjectiveProblem(goal);
                            if (issue != null) choices.Add(new Choice(issue, item.title + " / " + goal.id, () => SelectQuest(draft, game, item)));
                            if (QuestGame.IsEventObjective(goal)) choices.Add(new Choice("Проверьте источник события для модовой машины: " + goal.type, item.title + " / " + goal.id, () => SelectQuest(draft, game, item)));
                        }
                        foreach (Reward reward in item.rewards.Concat(item.alternatives.SelectMany(option => option.items)))
                        {
                            string issue = QuestGame.RewardProblem(reward);
                            if (issue != null) choices.Add(new Choice(issue, item.title, () => SelectQuest(draft, game, item)));
                        }
                    }
                if (choices.Count == 0) choices.Add(new Choice("Ошибок не найдено", "Проверены формат и доступные определения", () => { }));
                Find.WindowStack.Add(new ChoiceWindow("Проверка книги", choices));
            }
            catch (Exception e) { Fail(e); }
        }
        void EditHistory(bool redo)
        {
            try
            {
                if (draftHistory == null) { status = "История правок недоступна."; return; }
                draftHistory.Record(CurrentDraft());
                DraftDocument restored = redo ? draftHistory.Redo() : draftHistory.Undo();
                if (restored == null) { status = redo ? "Нечего повторять." : "Нечего отменять."; return; }
                SetDraft(restored); status = redo ? "Правка повторена в черновике." : "Правка отменена в черновике.";
            }
            catch (Exception e) { Fail(e); }
        }
        static string CompatibilitySummary(QuestPack p)
        {
            var issues = p.quests.SelectMany(QuestGame.Problems).Distinct().ToList();
            return issues.Count == 0 ? "Ссылки на предметы доступны." : "Проблемы совместимости: " + string.Join("; ", issues.Take(5));
        }
        void EditPack(ref float y, float w)
        {
            draft.title = Field(ref y, w, "Название книги", draft.title);
            Label(ref y, w, "ID пакета: " + draft.id, 34);
            Label(ref y, w, "Постоянные ID сохраняют историю выполнения и наград при повторном импорте. Для независимой книги используйте «Новая книга» во вкладке файлов.", 70);
            Chapter remove = null;
            foreach (var c in draft.chapters)
            {
                c.title = Field(ref y, Math.Max(120, w - 180), "Глава · " + c.id, c.title);
                if (Widgets.ButtonText(new Rect(w - 170, y - 38, 80, 30), "Копия"))
                {
                    try { QuestEditor.CopyChapter(draft, c); status = "Глава скопирована с новыми ID и внутренними связями."; break; }
                    catch (Exception e) { Fail(e); }
                }
                if (Widgets.ButtonText(new Rect(w - 80, y - 38, 80, 30), "Удалить")) remove = c;
                c.description = Field(ref y, w, "Описание главы", c.description ?? "", 1000);
                if (Widgets.ButtonText(new Rect(0, y, Math.Max(100, w - 150), 30), "Иконка: " + (string.IsNullOrEmpty(c.icon) ? "не выбрана" : DefLabel(c.icon))))
                    PickIcon(d => c.icon = d.defName);
                int index = draft.chapters.IndexOf(c);
                if (Widgets.ButtonText(new Rect(w - 145, y, 70, 30), "Вверх") && index > 0)
                { draft.chapters.RemoveAt(index); draft.chapters.Insert(index - 1, c); break; }
                if (Widgets.ButtonText(new Rect(w - 70, y, 70, 30), "Вниз") && index + 1 < draft.chapters.Count)
                { draft.chapters.RemoveAt(index); draft.chapters.Insert(index + 1, c); break; }
                y += 42;
            }
            if (remove != null) ConfirmRemoveChapter(remove);
            if (draft.chapters.Count < 100 && Widgets.ButtonText(new Rect(0, y, 170, 30), "+ Глава")) draft.chapters.Add(new Chapter { id = PackCodec.NewId("chapter."), title = "Новая глава" });
            y += 40;
        }
        void EditQuest(ref float y, float w, Quest q)
        {
            q.title = Field(ref y, w, "Название задания", q.title);
            Label(ref y, w, "ID: " + q.id, 30);
            if (Widgets.ButtonText(new Rect(0, y, w, 30), "Иконка: " + (string.IsNullOrEmpty(q.icon) ? "автоматическая" : DefLabel(q.icon))))
                PickIcon(d => q.icon = d.defName);
            y += 40;
            string chapter = draft.chapters.FirstOrDefault(c => c.id == q.chapter)?.title ?? "Выбрать главу";
            if (Widgets.ButtonText(new Rect(0, y, w, 30), "Глава: " + chapter)) Find.WindowStack.Add(new FloatMenu(draft.chapters.Select(c => new FloatMenuOption(c.title, () => q.chapter = c.id)).ToList()));
            y += 40;
            Label(ref y, w, "Описание", 24);
            q.description = Widgets.TextArea(new Rect(0, y, w, 100), q.description); if (q.description.Length > 12000) q.description = q.description.Substring(0, 12000); y += 110;
            Label(ref y, w, "Предыдущие задания (все перечисленные)", 28);
            string removeDep = null;
            foreach (string id in q.requires)
            {
                Label(ref y, w - 90, draft.quests.FirstOrDefault(x => x.id == id)?.title ?? id, 32);
                if (Widgets.ButtonText(new Rect(w - 80, y - 36, 80, 30), "Убрать")) removeDep = id;
            }
            if (removeDep != null) q.requires.Remove(removeDep);
            if (Widgets.ButtonText(new Rect(0, y, 220, 30), "+ Предыдущее задание"))
            {
                var candidates = draft.quests.Where(x => x.id != q.id && !q.requires.Contains(x.id)).ToList();
                if (candidates.Count > 0) Find.WindowStack.Add(new ChoiceWindow("Выберите задание", candidates.Select(x => new Choice(x.title, x.id, () => {
                    string error; if (!QuestEditor.TryAddDependency(draft, q, x, out error)) status = error;
                })).ToList()));
                else status = "Нет доступных заданий. Сначала создайте ещё одно.";
            }
            y += 44;
            Label(ref y, w, "Цели — ничего не расходуют", 30);
            Objective removeGoal = null;
            foreach (var o in q.objectives)
            {
                string typeLabel = o.type == "have" ? "Иметь предмет" : o.type == "build" ? "Иметь постройку" : o.type == "produce" ? "Произвести" : o.type == "mine" ? "Добыть" : o.type == "harvest" ? "Собрать урожай" : "Ручная отметка";
                if (Widgets.ButtonText(new Rect(0, y, 200, 30), typeLabel))
                    Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption> {
                        new FloatMenuOption("Иметь предмет", () => ChangeObjective(o, "have", o.type == "have" ? o.target : "Steel")),
                        new FloatMenuOption("Иметь постройку", () => ChangeObjective(o, "build", o.type == "build" ? o.target : "TableStonecutter")),
                        new FloatMenuOption("Произвести", () => ChangeObjective(o, "produce", o.type == "produce" ? o.target : "Steel")),
                        new FloatMenuOption("Добыть", () => ChangeObjective(o, "mine", o.type == "mine" ? o.target : "Steel")),
                        new FloatMenuOption("Собрать урожай", () => ChangeObjective(o, "harvest", o.type == "harvest" ? o.target : "RawRice")),
                        new FloatMenuOption("Ручная отметка", () => { ChangeObjective(o, "manual", ""); o.count = 1; }) }));
                if (Widgets.ButtonText(new Rect(w - 80, y, 80, 30), "Удалить")) removeGoal = o;
                y += 38;
                Widgets.Label(new Rect(0, y, Math.Max(80, w - 140), 30), "ID: " + o.id);
                if (Widgets.ButtonText(new Rect(w - 135, y, 135, 30), "Новый ID"))
                    Find.WindowStack.Add(new ConfirmWindow("Назначить цели новый ID? Это новое испытание: её прежняя отметка останется в истории, но не перейдёт на новую цель.", () => {
                        numberBuffers.Remove(o.id); o.id = PackCodec.NewId("goal.");
                    }));
                y += 38;
                if (o.type != "manual")
                {
                    if (Widgets.ButtonText(new Rect(0, y, w - 120, 30), DefLabel(o.target))) PickDef(o.type == "build", false, d => ChangeObjective(o, o.type, d.defName));
                    o.count = Number(new Rect(w - 110, y, 110, 30), o.id, o.count, 1000000); y += 38;
                    foreach (string variant in o.variants.ToList())
                    {
                        Label(ref y, w - 90, "или " + DefLabel(variant), 30);
                        if (Widgets.ButtonText(new Rect(w - 80, y - 34, 80, 30), "Убрать")) EditObjective(o, goal => goal.variants.Remove(variant));
                    }
                    if (o.variants.Count < 100 && Widgets.ButtonText(new Rect(0, y, w, 30), "+ Допустимый вариант"))
                        PickDef(o.type == "build", false, d => {
                            if (d.defName == o.target || o.variants.Contains(d.defName)) { status = "Этот вариант уже добавлен."; return; }
                            EditObjective(o, goal => goal.variants.Add(d.defName));
                        });
                    y += 38;
                    var defs = new[] { o.target }.Concat(o.variants).Select(DefDatabase<ThingDef>.GetNamedSilentFail).ToList();
                    bool materialApplicable = defs.All(d => d != null && d.MadeFromStuff);
                    bool qualityApplicable = defs.All(d => d != null && d.comps != null && d.comps.Any(c => c.compClass != null && typeof(CompQuality).IsAssignableFrom(c.compClass)));
                    if (materialApplicable || !string.IsNullOrEmpty(o.stuff))
                    {
                        if (Widgets.ButtonText(new Rect(0, y, w, 30), "Материал: " + (string.IsNullOrEmpty(o.stuff) ? "любой" : DefLabel(o.stuff))))
                        {
                            var materials = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => materialApplicable && d.IsStuff && defs.All(item => d.stuffProps.CanMake(item)))
                                .Select(d => new Choice(d.LabelCap.ToString(), d.defName, () => EditObjective(o, goal => goal.stuff = d.defName))).ToList();
                            materials.Insert(0, new Choice("Любой", "", () => EditObjective(o, goal => goal.stuff = null)));
                            Find.WindowStack.Add(new ChoiceWindow("Материал цели", materials));
                        }
                        y += 38;
                    }
                    if (qualityApplicable || !string.IsNullOrEmpty(o.minQuality))
                    {
                        if (Widgets.ButtonText(new Rect(0, y, w, 30), "Качество не ниже: " + (o.minQuality == null ? "любое" : QualityLabel(o.minQuality))))
                        {
                            var qualities = new List<FloatMenuOption> { new FloatMenuOption("Любое", () => EditObjective(o, goal => goal.minQuality = null)) };
                            if (qualityApplicable) qualities.AddRange(Enum.GetValues(typeof(QualityCategory)).Cast<QualityCategory>()
                                .Select(level => new FloatMenuOption(QualityLabel(level.ToString()), () => EditObjective(o, goal => goal.minQuality = level.ToString()))));
                            Find.WindowStack.Add(new FloatMenu(qualities));
                        }
                        y += 38;
                    }
                }
                o.text = Field(ref y, w, "Своя подпись (необязательно)", o.text, 1000);
                if (QuestGame.IsEventObjective(o)) Label(ref y, w, QuestGame.EventSourceSummary(o), 48);
                string issue = QuestGame.ObjectiveProblem(o); if (issue != null) Label(ref y, w, issue, 48);
                y += 8;
            }
            if (removeGoal != null) { q.objectives.Remove(removeGoal); numberBuffers.Clear(); }
            if (q.objectives.Count < 30 && Widgets.ButtonText(new Rect(0, y, 160, 30), "+ Цель")) q.objectives.Add(new Objective { id = PackCodec.NewId("goal.") });
            y += 42;
            Label(ref y, w, "Гарантированные предметные награды", 32);
            EditRewardRows(ref y, w, q.rewards, q.id + ".r");
            Label(ref y, w, "Наборы на выбор — игрок получит ровно один", 48);
            RewardOption removeOption = null;
            foreach (RewardOption option in q.alternatives)
            {
                option.title = Field(ref y, Math.Max(100, w - 90), "Название набора", option.title);
                if (Widgets.ButtonText(new Rect(w - 80, y - 38, 80, 30), "Удалить")) removeOption = option;
                EditRewardRows(ref y, w, option.items, q.id + ".a." + option.id);
            }
            if (removeOption != null) { q.alternatives.Remove(removeOption); numberBuffers.Clear(); }
            if (q.alternatives.Count < 30 && Widgets.ButtonText(new Rect(0, y, Math.Min(210, w), 30), "+ Набор на выбор"))
                q.alternatives.Add(new RewardOption { id = PackCodec.NewId("choice."), items = new List<Reward> { new Reward() } });
            y += 42;
            if (Widgets.ButtonText(new Rect(0, y, 200, 32), "Дублировать задание"))
            {
                try { selected = QuestEditor.CopySelected(draft, new[] { q.id }).Single().id; status = "Задание скопировано с новым ID, без связей и истории наград."; }
                catch (Exception e) { Fail(e); }
            }
            y += 44;
            if (Widgets.ButtonText(new Rect(0, y, 200, 32), "Удалить задание")) ConfirmRemoveQuest(q);
            y += 44;
        }
        void EditRewardRows(ref float y, float w, List<Reward> rewards, string keyPrefix)
        {
            Reward removeReward = null;
            foreach (var reward in rewards)
            {
                if (Widgets.ButtonText(new Rect(0, y, Math.Max(55, w - 210), 30), DefLabel(reward.def))) PickDef(false, true, d => { reward.def = d.defName; reward.stuff = ""; reward.quality = null; });
                reward.count = Number(new Rect(w - 200, y, 110, 30), keyPrefix + "." + rewards.IndexOf(reward), reward.count, 10000);
                if (Widgets.ButtonText(new Rect(w - 80, y, 80, 30), "Удалить")) removeReward = reward;
                y += 38;
                var def = DefDatabase<ThingDef>.GetNamedSilentFail(reward.def);
                if (def != null && def.MadeFromStuff)
                {
                    if (Widgets.ButtonText(new Rect(0, y, w, 30), "Материал: " + (string.IsNullOrEmpty(reward.stuff) ? "по умолчанию" : DefLabel(reward.stuff))))
                    {
                        var choices = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.IsStuff && d.stuffProps.CanMake(def)).Select(d => new Choice(d.LabelCap.ToString(), d.defName, () => reward.stuff = d.defName)).ToList();
                        choices.Insert(0, new Choice("По умолчанию", "", () => reward.stuff = "")); Find.WindowStack.Add(new ChoiceWindow("Материал награды", choices));
                    }
                    y += 38;
                }
                if (def != null && def.comps != null && def.comps.Any(c => c.compClass != null && typeof(CompQuality).IsAssignableFrom(c.compClass)))
                {
                    if (Widgets.ButtonText(new Rect(0, y, w, 30), "Качество: " + QualityLabel(reward.quality ?? "Normal")))
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
            if (rewards.Count < 30 && Widgets.ButtonText(new Rect(0, y, Math.Min(180, w), 30), "+ Награда")) rewards.Add(new Reward());
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
            var affected = draft.quests.Where(x => x.requires.Contains(q.id)).Select(x => x.title).ToList();
            string links = affected.Count == 0 ? "Связанных последующих заданий нет." : "Будут удалены связи с заданиями: " + string.Join(", ", affected) + ". Сами задания останутся.";
            Find.WindowStack.Add(new ConfirmWindow("Удалить «" + q.title + "» из черновика? " + links + " История наград останется в сохранении.", () => {
                QuestEditor.RemoveQuest(draft, q); if (selected == q.id) selected = null; numberBuffers.Clear();
            }));
        }
        void ConfirmRemoveChapter(Chapter chapter)
        {
            var inside = draft.quests.Where(q => q.chapter == chapter.id).ToList();
            if (inside.Count == 0)
            {
                Find.WindowStack.Add(new ConfirmWindow("Удалить пустую главу «" + chapter.title + "»?", () => draft.chapters.Remove(chapter)));
                return;
            }
            var choices = new List<FloatMenuOption>();
            foreach (Chapter destination in draft.chapters.Where(c => c != chapter))
                choices.Add(new FloatMenuOption("Перенести задания в «" + destination.title + "»", () =>
                    Find.WindowStack.Add(new ConfirmWindow("Перенести " + inside.Count + " заданий и удалить главу «" + chapter.title + "»? ID и связи сохранятся.", () => {
                        foreach (Quest quest in inside) quest.chapter = destination.id;
                        draft.chapters.Remove(chapter);
                    }))));
            var ids = new HashSet<string>(inside.Select(q => q.id));
            var affected = draft.quests.Where(q => !ids.Contains(q.id) && q.requires.Any(ids.Contains)).Select(q => q.title).ToList();
            choices.Add(new FloatMenuOption("Удалить главу вместе с заданиями", () =>
                Find.WindowStack.Add(new ConfirmWindow("Удалить главу «" + chapter.title + "» и заданий: " + inside.Count + "? " +
                    (affected.Count == 0 ? "Внешних связей нет." : "Будут удалены связи с: " + string.Join(", ", affected) + ". Эти задания останутся.") +
                    " История наград останется в сохранении.", () => {
                        foreach (Quest quest in inside) QuestEditor.RemoveQuest(draft, quest);
                        draft.chapters.Remove(chapter); selected = null; numberBuffers.Clear();
                    }))));
            Find.WindowStack.Add(new FloatMenu(choices));
        }
        static void PickIcon(Action<ThingDef> choose)
        {
            var defs = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.label != null &&
                (d.category == ThingCategory.Item || (d.category == ThingCategory.Building && d.designationCategory != null)));
            var choices = defs.OrderBy(d => d.label).Select(d => new Choice(d.LabelCap.ToString(), d.defName + " · " + (d.modContentPack?.Name ?? "Core"), () => choose(d))).ToList();
            Find.WindowStack.Add(new ChoiceWindow("Выберите иконку", choices));
        }
        static void PickDef(bool building, bool reward, Action<ThingDef> choose)
        {
            var defs = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => building ? d.category == ThingCategory.Building && d.designationCategory != null : d.category == ThingCategory.Item && !d.IsCorpse && d.thingClass != null && !typeof(MinifiedThing).IsAssignableFrom(d.thingClass) && !typeof(UnfinishedThing).IsAssignableFrom(d.thingClass));
            if (reward) defs = defs.Where(d => QuestGame.RewardProblem(new Reward { def = d.defName }) == null);
            var choices = defs.OrderBy(d => d.label).Select(d => new Choice(d.LabelCap.ToString(), d.defName + " · " + (d.modContentPack?.Name ?? "Core"), () => choose(d))).ToList();
            Find.WindowStack.Add(new ChoiceWindow("Выберите " + (building ? "постройку" : "предмет"), choices));
        }
        void Files(Rect r, QuestGame game)
        {
            float y = r.y;
            Widgets.Label(new Rect(r.x, y, r.width, 50), "Экспорт содержит черновик определений, без прогресса колонии. Импорт заменяет черновик; затем нажмите «Применить к этой колонии». Повторный импорт тех же ID не сбрасывает награды."); y += 58;
            if (Widgets.ButtonText(new Rect(r.x, y, 200, 32), "Экспортировать JSON"))
            { try { ValidateNumberBuffers(); if (SaveDraft(true)) { status = "Экспорт: " + PackFiles.Export(draft); ReloadFiles(); } } catch (Exception e) { Fail(e); } }
            if (Widgets.ButtonText(new Rect(r.x + 210, y, 180, 32), "Обновить файлы")) ReloadFiles();
            if (Widgets.ButtonText(new Rect(r.x + 400, y, 170, 32), "Новая книга"))
                Find.WindowStack.Add(new ConfirmWindow("Создать пустой черновик с новым ID? Текущий черновик сохранится отдельно; активная книга колонии не изменится.", () => ReplaceDraft(QuestGame.EmptyPack(), "Новая книга создана в черновике.")));
            y += 40;
            if (Widgets.ButtonText(new Rect(r.x, y, 250, 32), "Книги из этой колонии"))
            {
                try
                {
                    var saved = game.SavedBooks();
                    var choices = saved.Select(p => new Choice(p.title, p.id, () => ImportDraft(p))).ToList();
                    Find.WindowStack.Add(new ChoiceWindow("Сохранённые книги", choices));
                }
                catch (Exception e) { Fail(e); }
            }
            y += 40;
            if (draftSaveBlocked)
            {
                if (Widgets.ButtonText(new Rect(r.x, y, 240, 32), "Восстановить из копии"))
                    Find.WindowStack.Add(new ConfirmWindow("Восстановить последнюю исправную копию? Повреждённый файл сохранится отдельно.", RecoverDraft));
                if (Widgets.ButtonText(new Rect(r.x + 250, y, 270, 32), "Начать из активной книги"))
                    Find.WindowStack.Add(new ConfirmWindow("Сохранить повреждённый файл отдельно и начать черновик из активной книги?", () => ResetDraft(game)));
                y += 40;
            }
            Widgets.Label(new Rect(r.x, y, r.width, 54), "Папка обмена: " + PackFiles.Folder); y += 60;
            float fileHeight = Math.Max(80, (r.height - 280) * 0.55f);
            Rect view = new Rect(r.x, y, r.width, fileHeight);
            Widgets.BeginScrollView(view, ref fileScroll, new Rect(0, 0, r.width - 20, Math.Max(fileHeight, files.Count * 44)));
            float fy = 0;
            foreach (string file in files)
            {
                string suffix = file.StartsWith(PackFiles.Folder, StringComparison.Ordinal) ? "" : file.StartsWith(PackFiles.LegacyFolder, StringComparison.Ordinal) ? " · старый экспорт" : " · пример мода";
                Widgets.Label(new Rect(0, fy, r.width - 160, 38), Path.GetFileName(file) + suffix);
                if (Widgets.ButtonText(new Rect(r.width - 150, fy, 120, 32), "Импорт"))
                { try { ImportDraft(PackFiles.Read(file)); } catch (Exception e) { Fail(e); } }
                fy += 44;
            }
            Widgets.EndScrollView(); y += fileHeight + 10;
            Widgets.Label(new Rect(r.x, y, r.width, 26), "Или вставьте JSON сюда:"); y += 30;
            float textHeight = Math.Max(40, r.yMax - y - 42);
            jsonInput = Widgets.TextArea(new Rect(r.x, y, r.width, textHeight), jsonInput);
            if (jsonInput.Length > PackCodec.MaxBytes) jsonInput = jsonInput.Substring(0, PackCodec.MaxBytes);
            y += textHeight + 6;
            if (Widgets.ButtonText(new Rect(r.x, y, 220, 30), "Импортировать из текста"))
            { try { ImportDraft(PackCodec.Decode(jsonInput)); } catch (Exception e) { Fail(e); } }
        }
        void ImportDraft(QuestPack pack)
        {
            Find.WindowStack.Add(new ConfirmWindow("Заменить черновик пакетом «" + pack.title + "» (" + pack.quests.Count + " заданий)?", () =>
            {
                ReplaceDraft(pack, "Импортировано в черновик. " + CompatibilitySummary(pack) + " Для активации откройте редактор и примените.");
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
        public override Vector2 InitialSize { get { return new Vector2(680, 600); } }
        public RewardChoiceWindow(QuestGame game, QuestClaimPlan plan, Action<string> report)
        {
            this.game = game; this.plan = plan; this.report = report;
            quests = plan.questIds.Select(id => game.Pack.quests.First(q => q.id == id)).Where(q => q.alternatives.Count > 0).ToList();
            doCloseX = true; absorbInputAroundWindow = true; forcePause = false;
        }
        public override void DoWindowContents(Rect rect)
        {
            Widgets.Label(new Rect(0, 0, rect.width, 56), "Выберите по одному набору для каждого задания. До подтверждения награды не выдаются.");
            Rect area = new Rect(0, 60, rect.width, Math.Max(0, rect.height - 160));
            Widgets.BeginScrollView(area, ref scroll, new Rect(0, 0, area.width - 20, Math.Max(area.height, quests.Count * 100)));
            float y = 0;
            foreach (Quest q in quests)
            {
                Widgets.Label(new Rect(0, y, area.width - 24, 32), q.title); y += 34;
                string id;
                RewardOption current = choices.TryGetValue(q.id, out id) ? q.alternatives.FirstOrDefault(x => x.id == id) : null;
                string label = current == null ? "Выберите набор" : current.title + " — " + string.Join(", ", current.items.Select(DescribeReward));
                if (Widgets.ButtonText(new Rect(0, y, area.width - 24, 46), label))
                {
                    var menu = q.alternatives.Select(option => new FloatMenuOption(option.title + " — " + string.Join(", ", option.items.Select(DescribeReward)),
                        () => choices[q.id] = option.id)).ToList();
                    Find.WindowStack.Add(new FloatMenu(menu));
                }
                y += 58;
            }
            Widgets.EndScrollView();
            Widgets.Label(new Rect(0, rect.height - 95, rect.width, 48), error);
            if (Widgets.ButtonText(new Rect(0, rect.height - 42, 200, 36), "Получить выбранное"))
            {
                try
                {
                    string result = game.FinishClaim(plan, choices);
                    if (result != null) error = result;
                    else { report("Получено заданий: " + plan.questIds.Count + ". Предметы целей остаются у вас."); Close(); }
                }
                catch (Exception e) { error = "Ошибка выдачи: " + e.Message; Log.Error("[Grechka Quests] " + error); }
            }
            if (Widgets.ButtonText(new Rect(rect.width - 170, rect.height - 42, 170, 36), "Отмена")) Close();
        }
        static string DescribeReward(Reward reward)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(reward.def);
            return reward.count + " × " + (def == null ? reward.def : def.LabelCap.ToString());
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
            if (Widgets.ButtonText(new Rect(0, r.height - 40, 190, 36), "Подтвердить")) { action(); Close(); }
            if (Widgets.ButtonText(new Rect(r.width - 190, r.height - 40, 190, 36), "Отмена")) Close();
        }
    }
}
