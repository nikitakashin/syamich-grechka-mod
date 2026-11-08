using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grechka.Quests
{
    // The player reads the graph and a focused quest card; authoring stays in the editor.
    public sealed partial class QuestWindow
    {
        static readonly Color BookGreen = new Color(.44f, .8f, .43f);
        bool questDetailOpen, chapterRailExpanded = true;
        QuestPack bundledUpdate;
        readonly HashSet<string> fittedChapters = new HashSet<string>();

        public override void OnCancelKeyPressed()
        {
            if (tab == 0 && questDetailOpen) { questDetailOpen = false; Event.current.Use(); return; }
            base.OnCancelKeyPressed();
        }

        void FindBundledUpdate(QuestGame game)
        {
            bundledUpdate = null;
            // Only the shipped book with the same ID is offered; unrelated custom books stay independent.
            if (game.Pack.id != "grechka.world" || string.IsNullOrEmpty(GrechkaMod.Root)) return;
            try
            {
                var next = PackFiles.Read(Path.Combine(GrechkaMod.Root, "QuestPacks", "grechka-world.json"));
                if (next.id == game.Pack.id && PackCodec.Encode(next) != PackCodec.Encode(game.Pack)) bundledUpdate = next;
            }
            catch (Exception e) { Log.Warning(QuestText.Get("GrechkaQuests_6aba5ed89654", "[Grechka Quests] Не удалось проверить обновление книги: ") + e.Message); }
        }
        void OfferBundledUpdate(QuestGame game)
        {
            if (bundledUpdate == null) return;
            QuestPack candidate = bundledUpdate;
            string summary = QuestText.Get("GrechkaQuests_40b8d82562c5", "Установить книгу из текущей версии мода? Личные правки активной книги будут заменены; её копия сохранится в экспортах. История полученных наград сохраняется.\n\n")
                + BookHistory.DescribeChange(game.Pack, candidate);
            Find.WindowStack.Add(new ConfirmWindow(summary, () => {
                try
                {
                    var valid = game.ValidateApply(candidate);
                    string backup = PackFiles.Export(game.Pack);
                    game.Apply(valid); bundledUpdate = null; fittedChapters.Clear();
                    bookChapter = ChooseChapter(game.Pack, bookChapter); bookSelection = null; questDetailOpen = false;
                    status = QuestText.Get("GrechkaQuests_78a628111356", "Книга обновлена. Сохраните игру. Предыдущая книга: ") + backup;
                }
                catch (Exception e) { Fail(e); }
            }));
        }

        void FullscreenBook(Rect r, QuestGame game, bool author)
        {
            if (!game.Pack.chapters.Any(c => c.id == currentChapter)) currentChapter = ChooseChapter(game.Pack, game.LastChapter(game.Pack.id));
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && !questDetailOpen;
            try
            {
                QuestArt.Draw(new Rect(2, 2, 30, 30), "QuestBook");
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(42, 0, Math.Max(100, r.width - 520), 36), QuestPackText.Content(game.Pack.title));
                Text.Font = GameFont.Small;
                if (author && Widgets.ButtonText(new Rect(r.width - 460, 0, 110, 32), QuestText.Get("GrechkaQuests_119d528f623e", "Редактор"))) { tab = 1; detailScroll = Vector2.zero; }
                if (author && Widgets.ButtonText(new Rect(r.width - 342, 0, 120, 32), QuestText.Get("GrechkaQuests_965587e9f297", "Импорт / экспорт"))) { tab = 2; ReloadFiles(); }
                if (QuestArt.Button(new Rect(r.width - 210, 0, 165, 32), QuestText.Get("GrechkaQuests_7445c6f3b8f7", "Забрать всё"), "QuestClaimAll")) StartClaim(game, null);
                if (Widgets.ButtonText(new Rect(r.width - 36, 0, 32, 32), "×")) Close();
                float railWidth = chapterRailExpanded ? Mathf.Clamp(r.width * .19f, 220, 280) : 62;
                Rect rail = new Rect(0, 44, railWidth, r.height - 80);
                ChapterRail(rail, game);
                Rect canvas = new Rect(rail.xMax + 12, 44, r.width - railWidth - 12, r.height - 80);
                Chapter chapter = game.Pack.chapters.FirstOrDefault(c => c.id == currentChapter);
                var quests = game.Pack.quests.Where(q => q.chapter == currentChapter).ToList();
                if (chapter != null)
                {
                    Text.Font = GameFont.Medium;
                    Widgets.Label(new Rect(canvas.x + 8, canvas.y, canvas.width - 150, 35), QuestPackText.Content(chapter.title));
                    Text.Font = GameFont.Small;
                    if (Widgets.ButtonText(new Rect(canvas.xMax - 142, canvas.y, 140, 30), QuestText.Get("GrechkaQuests_9ff41361456d", "Вся ветка"))) FitChapter(new Rect(canvas.x, canvas.y + 70, canvas.width, canvas.height - 70), quests, game);
                    string progress = quests.Count(game.Claimed) + " / " + quests.Count + QuestText.Get("GrechkaQuests_c0cf6ef79774", " наград получено");
                    Widgets.Label(new Rect(canvas.x + 8, canvas.y + 37, canvas.width - 16, 26), progress);
                    if (!string.IsNullOrWhiteSpace(QuestPackText.Content(chapter.description)))
                        TooltipHandler.TipRegion(new Rect(canvas.x, canvas.y, canvas.width - 150, 64), QuestPackText.Content(chapter.description));
                }
                canvas.y += 70; canvas.height -= 70;
                if (fittedChapters.Add(game.Pack.id + "/" + currentChapter) && !game.HasGraphView(game.Pack.id, currentChapter)) FitChapter(canvas, quests, game);
                DrawGraph(canvas, game.Pack, game);
                string hint = string.IsNullOrEmpty(status) ? QuestText.Get("GrechkaQuests_68bc148453cb", "ЛКМ по узлу — задание   ·   Потяните фон — перемещение   ·   Колесо — масштаб") : status;
                Widgets.Label(new Rect(0, r.height - 28, r.width, 28), hint);
                if (!game.HasColony) Widgets.Label(new Rect(canvas.x + 16, canvas.y + 12, canvas.width - 32, 35), QuestText.Get("GrechkaQuests_731c3711ff2a", "Создайте колонию"));
            }
            finally { GUI.enabled = enabled; }
            if (questDetailOpen)
            {
                Quest q = game.Pack.quests.FirstOrDefault(x => x.id == selected);
                if (q == null) { questDetailOpen = false; return; }
                Widgets.DrawBoxSolid(r, new Color(0, 0, 0, .68f));
                float width = Math.Min(900, r.width - 48), height = Math.Min(820, r.height - 56);
                Rect card = new Rect((r.width - width) / 2, (r.height - height) / 2, width, height);
                Widgets.DrawBoxSolid(card, new Color(.11f, .14f, .17f));
                Widgets.DrawBox(card);
                GUI.BeginGroup(card.ContractedBy(18));
                try
                {
                    Rect inner = new Rect(0, 0, card.width - 36, card.height - 36);
                    QuestVisuals.Icon(new Rect(0, 0, 44, 44), QuestVisuals.QuestIcon(q));
                    Text.Font = GameFont.Medium;
                    Widgets.Label(new Rect(56, 0, inner.width - 100, 58), QuestPackText.Content(q.title));
                    Text.Font = GameFont.Small;
                    if (Widgets.ButtonText(new Rect(inner.width - 32, 0, 32, 32), "×")) questDetailOpen = false;
                    QuestDetails(new Rect(0, 68, inner.width, inner.height - 68), game, q);
                }
                finally { GUI.EndGroup(); Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; }
                if (GUI.enabled && Event.current.type == EventType.MouseDown && !card.Contains(Event.current.mousePosition))
                { questDetailOpen = false; Event.current.Use(); }
            }
        }

        void FitChapter(Rect canvas, List<Quest> quests, QuestGame game)
        {
            if (quests.Count == 0 || currentChapter == null) return;
            var positions = quests.Select((q, i) => GraphPosition(q, i)).ToList();
            float minX = positions.Min(v => v.x), minY = positions.Min(v => v.y);
            float width = positions.Max(v => v.x) - minX + 64, height = positions.Max(v => v.y) - minY + 64;
            float zoom = Mathf.Clamp(Math.Min((canvas.width - 90) / width, (canvas.height - 90) / height), .45f, 1.2f);
            Vector2 pan = new Vector2((canvas.width - width * zoom) / 2 - minX * zoom,
                (canvas.height - height * zoom) / 2 - minY * zoom);
            game.SetGraphView(game.Pack.id, currentChapter, pan, zoom);
        }

        void ChapterRail(Rect area, QuestGame game)
        {
            Widgets.DrawBoxSolid(area, new Color(.12f, .15f, .18f));
            if (Widgets.ButtonText(new Rect(area.x + 6, area.y + 4, area.width - 12, 30), chapterRailExpanded ? QuestText.Get("GrechkaQuests_634f20ae7645", "‹  Главы") : "›"))
                chapterRailExpanded = !chapterRailExpanded;
            float top = 40;
            if (chapterRailExpanded)
            {
                searchText = Widgets.TextField(new Rect(area.x + 8, area.y + top, area.width - 16, 30), searchText ?? "", 120);
                TooltipHandler.TipRegion(new Rect(area.x + 8, area.y + top, area.width - 16, 30), QuestText.Get("GrechkaQuests_6d88f7b0f0b1", "Найти квест, предмет или машину"));
                top += 38;
            }
            float updateHeight = bundledUpdate != null && chapterRailExpanded ? 42 : 0;
            if (updateHeight > 0 && Widgets.ButtonText(new Rect(area.x + 6, area.yMax - 38, area.width - 12, 32), QuestText.Get("GrechkaQuests_4e5d5fa12f43", "Обновить книгу"))) OfferBundledUpdate(game);
            Rect view = new Rect(area.x + 4, area.y + top, area.width - 8, area.height - top - 4 - updateHeight);
            bool searching = chapterRailExpanded && !string.IsNullOrWhiteSpace(searchText);
            var matches = searching ? game.Pack.quests.Where(q => SearchableQuest(q).IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0).ToList() : null;
            float w = view.width - 18;
            var rows = game.Pack.chapters.Select(c => new { chapter = c, height = chapterRailExpanded ? Math.Max(54, Text.CalcHeight(QuestPackText.Content(c.title), w - 60) + 28) : 48 }).ToList();
            float height = searching ? matches.Sum(q => Math.Max(56, Text.CalcHeight(QuestPackText.Content(q.title), w - 48) + 24)) : rows.Sum(row => row.height);
            Widgets.BeginScrollView(view, ref listScroll, new Rect(0, 0, w, Math.Max(view.height, height)));
            try
            {
                float y = 0;
                if (searching)
                {
                    foreach (Quest q in matches)
                    {
                        float h = Math.Max(56, Text.CalcHeight(QuestPackText.Content(q.title), w - 48) + 24);
                        Rect row = new Rect(0, y, w, h);
                        Widgets.DrawHighlightIfMouseover(row);
                        QuestVisuals.Icon(new Rect(3, y + 8, 34, 34), QuestVisuals.QuestIcon(q));
                        Widgets.Label(new Rect(44, y + 5, w - 48, h - 10), QuestPackText.Content(q.title));
                        if (Widgets.ButtonInvisible(row)) { SelectQuest(game.Pack, game, q); questDetailOpen = true; }
                        y += h;
                    }
                    if (matches.Count == 0) Widgets.Label(new Rect(6, 4, w - 12, 70), QuestText.Get("GrechkaQuests_480fae1c4bc9", "Ничего не найдено"));
                }
                else foreach (var entry in rows)
                {
                    Chapter c = entry.chapter;
                    var quests = game.Pack.quests.Where(q => q.chapter == c.id).ToList();
                    Rect row = new Rect(0, y, w, entry.height);
                    if (c.id == currentChapter) Widgets.DrawBoxSolid(row, new Color(.23f, .29f, .32f));
                    Widgets.DrawHighlightIfMouseover(row);
                    QuestVisuals.Icon(new Rect(3, y + 7, 34, 34), c.icon);
                    if (chapterRailExpanded)
                    {
                        Widgets.Label(new Rect(44, y + 3, w - 60, entry.height - 23), QuestPackText.Content(c.title));
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(new Rect(44, y + entry.height - 22, w - 48, 21), quests.Count(game.Claimed) + " / " + quests.Count);
                        Text.Font = GameFont.Small;
                    }
                    if (quests.Any(game.CanClaim)) QuestBadge.Draw(new Rect(w - 19, y + 2, 19, 19));
                    TooltipHandler.TipRegion(row, QuestPackText.Content(c.title) + "\n" + QuestPackText.Content(c.description));
                    if (Widgets.ButtonInvisible(row))
                    {
                        currentChapter = c.id; game.SetLastChapter(game.Pack.id, c.id);
                        selected = null; questDetailOpen = false; detailScroll = Vector2.zero;
                    }
                    y += entry.height;
                }
            }
            finally { Widgets.EndScrollView(); Text.Font = GameFont.Small; }
        }

        static string SearchableQuest(Quest q) => QuestPackText.Content(q.title) + " " + QuestPackText.Content(q.description) + " " + string.Join(" ",
            q.objectives.Where(o => o.type != "manual").SelectMany(o => new[] { o.target }.Concat(o.variants))
                .Concat(q.rewards.Select(reward => reward.def)).Concat(q.alternatives.SelectMany(option => option.items).Select(reward => reward.def))
                .Distinct().Select(DefLabel));

        void QuestDetails(Rect area, QuestGame game, Quest q)
        {
            Widgets.BeginScrollView(area, ref detailScroll, new Rect(0, 0, area.width - 20, Math.Max(area.height, detailHeight)));
            try
            {
                float w = area.width - 24, gap = 24, col = (w - gap) / 2;
                float leftY = 0, rightY = 0;
                DetailHeading(new Rect(0, leftY, col, 28), QuestText.Get("GrechkaQuests_03cc1c2d5ac2", "Задачи")); leftY += 36;
                DetailHeading(new Rect(col + gap, rightY, col, 28), QuestText.Get("GrechkaQuests_8de848092baa", "Награды")); rightY += 36;
                foreach (Objective o in q.objectives)
                {
                    bool done = game.ObjectiveAchieved(q, o);
                    string name = o.type == "manual" ? (string.IsNullOrWhiteSpace(QuestPackText.Content(o.text)) ? QuestText.Get("GrechkaQuests_cab03bf682bb", "Прочитать и подтвердить") : QuestPackText.Content(o.text)) : DefLabel(o.target);
                    string verb = o.type == "manual" ? QuestText.Get("GrechkaQuests_21bb95294ce2", "Прочитать") : o.type == "build" ? QuestText.Get("GrechkaQuests_77ad8130c298", "Построить") : o.type == "produce" ? QuestText.Get("GrechkaQuests_b7404d6d9ba7", "Произвести") : o.type == "mine" ? QuestText.Get("GrechkaQuests_96fc5b936c1d", "Добыть") : o.type == "harvest" ? QuestText.Get("GrechkaQuests_906be2792204", "Собрать урожай") : o.type == "kill" ? QuestText.Get("GrechkaQuests_ec89da0c31c3", "Убить или забить") : QuestText.Get("GrechkaQuests_2a7a3d00e878", "Получить");
                    string count = o.type == "manual" ? (done ? QuestText.Get("GrechkaQuests_ef05d57959cf", "Готово") : QuestText.Get("GrechkaQuests_fad2f82e70fb", "Нажмите")) : (done ? o.count : Math.Min(game.Value(q, o), o.count)) + " / " + o.count;
                    float h = QuestVisuals.ItemRowHeight(col, name);
                    Rect row = new Rect(0, leftY, col, h);
                    QuestVisuals.ItemRow(row, o.type == "manual" ? "ObjectiveManual" : o.target, name, verb + " · " + count, done, o.stuff);
                    string tooltip = ObjectiveTitle(o) + (string.IsNullOrEmpty(QuestPackText.Content(o.text)) ? "" : "\n" + QuestPackText.Content(o.text))
                        + (string.IsNullOrEmpty(o.stuff) ? "" : QuestText.Get("GrechkaQuests_5117ea64c733", "\nМатериал: ") + DefLabel(o.stuff))
                        + (string.IsNullOrEmpty(o.minQuality) ? "" : QuestText.Get("GrechkaQuests_f694fc107254", "\nКачество: ") + QualityLabel(o.minQuality));
                    TooltipHandler.TipRegion(row, tooltip);
                    if (Widgets.ButtonInvisible(row))
                    {
                        if (o.type == "manual" && !done && game.HasColony && !game.Claimed(q)) game.MarkManual(q, o);
                        else Find.WindowStack.Add(new QuestObjectiveInfoWindow(game, q, o));
                    }
                    leftY += h + 8;
                }
                if (q.objectives.Count == 0) { Widgets.Label(new Rect(0, leftY, col, 48), QuestText.Get("GrechkaQuests_76ee8db52447", "Прочитайте описание и завершите задание.")); leftY += 56; }
                rightY = RewardRows(col + gap, rightY, col, q.rewards, game.Claimed(q));
                if (q.rewards.Count == 0 && q.alternatives.Count == 0)
                { Widgets.Label(new Rect(col + gap, rightY, col, 48), QuestText.Get("GrechkaQuests_7742302e10a7", "Учебная веха без предметной награды")); rightY += 56; }
                foreach (RewardOption option in q.alternatives)
                {
                    string text = QuestText.Get("GrechkaQuests_37602fe01312", "На выбор: ") + QuestPackText.Content(option.title);
                    float h = Text.CalcHeight(text, col) + 8;
                    Widgets.Label(new Rect(col + gap, rightY, col, h), text); rightY += h;
                    rightY = RewardRows(col + gap, rightY, col, option.items, game.Claimed(q) && game.ChosenReward(q) == option.id);
                }
                float y = Math.Max(leftY, rightY) + 12;
                Widgets.DrawLineHorizontal(0, y, w); y += 16;
                if (game.Claimed(q))
                { QuestVisuals.Check(new Rect(0, y + 4, 22, 22)); Widgets.Label(new Rect(32, y, w - 32, 32), QuestText.Get("GrechkaQuests_f27a15f94fd4", "Награда получена")); y += 40; }
                else if (game.CanClaim(q))
                {
                    if (QuestArt.Button(new Rect(0, y, Math.Min(300, w), 38), q.rewards.Count == 0 && q.alternatives.Count == 0 ? QuestText.Get("GrechkaQuests_8be2bbf39a83", "Завершить") : QuestText.Get("GrechkaQuests_ba790e7bdf84", "Получить награду"), "QuestClaimReward")) StartClaim(game, q);
                    y += 48;
                }
                else
                {
                    string state = !game.HasColony ? QuestText.Get("GrechkaQuests_731c3711ff2a", "Создайте колонию") : !game.Unlocked(q) ? QuestText.Get("GrechkaQuests_77ed2aa1801c", "Награда откроется после предыдущих заданий. Цели уже учитываются.") : QuestText.Get("GrechkaQuests_cba06045865f", "Выполните задачи выше. Предметы останутся у вас.");
                    float h = Text.CalcHeight(state, w) + 8; Widgets.Label(new Rect(0, y, w, h), state); y += h;
                }
                if (!string.IsNullOrWhiteSpace(QuestPackText.Content(q.description)))
                { float h = Text.CalcHeight(QuestPackText.Content(q.description), w) + 10; Widgets.Label(new Rect(0, y, w, h), QuestPackText.Content(q.description)); y += h + 10; }
                if (q.requires.Count > 0)
                {
                    DetailHeading(new Rect(0, y, w, 28), QuestText.Get("GrechkaQuests_848aa87a96e2", "Предыдущие задания")); y += 34;
                    foreach (string id in q.requires)
                    {
                        Quest parent = game.Pack.quests.FirstOrDefault(x => x.id == id); if (parent == null) continue;
                        float h = Math.Max(42, Text.CalcHeight(QuestPackText.Content(parent.title), w - 60) + 12);
                        Rect row = new Rect(0, y, w, h);
                        Widgets.DrawHighlightIfMouseover(row);
                        QuestVisuals.Icon(new Rect(4, y + 4, 32, 32), QuestVisuals.QuestIcon(parent));
                        Widgets.Label(new Rect(44, y + 6, w - 74, h - 10), QuestPackText.Content(parent.title));
                        if (game.Claimed(parent)) QuestVisuals.Check(new Rect(w - 24, y + 10, 20, 20));
                        if (Widgets.ButtonInvisible(row)) { SelectQuest(game.Pack, game, parent); detailScroll = Vector2.zero; }
                        y += h + 4;
                    }
                }
                foreach (string problem in QuestGame.Problems(q))
                { float h = Text.CalcHeight(problem, w) + 8; Widgets.Label(new Rect(0, y, w, h), problem); y += h; }
                if (game.PendingCount > 0)
                {
                    string legacy = QuestText.Get("GrechkaQuests_7c2850eb8763", "В старом сохранении остались невыданные награды: ") + game.PendingCount + QuestText.Get("GrechkaQuests_4bbb2394051c", " стопок. История сохранена для переноса; автоматическая выдача отключена.");
                    float h = Text.CalcHeight(legacy, w) + 8; Widgets.Label(new Rect(0, y, w, h), legacy); y += h;
                }
                detailHeight = y + 12;
            }
            finally { Widgets.EndScrollView(); }
        }
        static void DetailHeading(Rect r, string label)
        {
            Color old = GUI.color; GUI.color = new Color(.83f, .75f, .53f);
            Widgets.Label(r, label); GUI.color = old;
        }
        static float RewardRows(float x, float y, float width, List<Reward> rewards, bool claimed)
        {
            foreach (Reward reward in rewards)
            {
                string name = DefLabel(reward.def);
                float h = QuestVisuals.ItemRowHeight(width, name);
                Rect row = new Rect(x, y, width, h);
                QuestVisuals.ItemRow(row, reward.def, name, "× " + reward.count, claimed, reward.stuff);
                TooltipHandler.TipRegion(row, name + " × " + reward.count + (string.IsNullOrEmpty(reward.quality) ? "" : QuestText.Get("GrechkaQuests_f694fc107254", "\nКачество: ") + QualityLabel(reward.quality)));
                y += h + 8;
            }
            return y;
        }
    }

    [StaticConstructorOnStartup]
    internal static class QuestVisuals
    {
        static Texture2D disc;
        public static string QuestIcon(Quest q) => !string.IsNullOrEmpty(q.icon) ? q.icon : q.objectives.FirstOrDefault(o => o.type != "manual")?.target ?? "QuestBook";
        public static void Circle(Rect r, Color color)
        {
            if (disc == null)
            {
                const int side = 96;
                disc = new Texture2D(side, side);
                var pixels = new Color[side * side];
                for (int y = 0; y < side; y++) for (int x = 0; x < side; x++)
                {
                    float dx = x - (side - 1) / 2f, dy = y - (side - 1) / 2f;
                    pixels[y * side + x] = new Color(1, 1, 1, Mathf.Clamp01(side / 2f - Mathf.Sqrt(dx * dx + dy * dy)));
                }
                disc.SetPixels(pixels); disc.Apply();
            }
            Color old = GUI.color;
            try { GUI.color = color; GUI.DrawTexture(r, disc); }
            finally { GUI.color = old; }
        }
        public static void Check(Rect r)
        {
            // Use the authored bitmap as a single image. Separate rotated GUI lines
            // visibly detach at their joint in the current game renderer.
            QuestArt.Draw(r, "QuestCompleted");
        }
        public static void Icon(Rect r, string id, string stuff = null)
        {
            ThingDef def = string.IsNullOrEmpty(id) ? null : QuestGame.ResolveThingDef(id);
            if (def != null) Widgets.ThingIcon(r, def, string.IsNullOrEmpty(stuff) ? null : QuestGame.ResolveThingDef(stuff));
            else if (!QuestArt.Draw(r, id)) QuestArt.Draw(r, "QuestBook");
        }
        public static float ItemRowHeight(float width, string name) => Math.Max(84, Text.CalcHeight(name, Math.Max(40, width - 92)) + 43);
        public static void ItemRow(Rect r, string id, string name, string count, bool completed, string stuff = null)
        {
            Widgets.DrawBoxSolid(r, new Color(.17f, .21f, .24f));
            Icon(new Rect(r.x + 8, r.y + 10, 54, 54), id, stuff);
            float textWidth = r.width - 92;
            float nameHeight = Text.CalcHeight(name, textWidth);
            Widgets.Label(new Rect(r.x + 72, r.y + 8, textWidth, nameHeight + 2), name);
            Widgets.Label(new Rect(r.x + 72, r.y + nameHeight + 12, r.width - 78, 36), count);
            if (completed) Check(new Rect(r.xMax - 23, r.y + 5, 20, 20));
        }
    }

    internal sealed class QuestObjectiveInfoWindow : Window
    {
        readonly QuestGame game; readonly Quest quest; readonly Objective objective;
        Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(650, 500);
        public QuestObjectiveInfoWindow(QuestGame game, Quest quest, Objective objective)
        { this.game = game; this.quest = quest; this.objective = objective; doCloseX = true; forcePause = false; absorbInputAroundWindow = true; }
        public override void DoWindowContents(Rect r)
        {
            Objective o = objective;
            string scope = QuestGame.IsEventObjective(o) ? QuestGame.EventSourceSummary(o) : o.type == "build" ? QuestText.Get("GrechkaQuests_5de224f7f08a", "Учитываются готовые свои постройки на домашних картах.") : QuestText.Get("GrechkaQuests_2ec7caeafb1f", "Учитываются вещи на домашних картах и у своих пешек, включая караваны.");
            string text = scope + QuestText.Get("GrechkaQuests_e707b8c74130", "\n\nДопустимые варианты:\n") + string.Join("\n", new[] { o.target }.Concat(o.variants).Select(id => QuestGame.ResolveThingDef(id)?.LabelCap.ToString() ?? id));
            if (QuestGame.IsEventObjective(o)) text += QuestText.Get("GrechkaQuests_eb694fd4f929", "\n\nУчтено: ") + game.Value(quest, o) + QuestText.Get("GrechkaQuests_476d5c9a669e", ". Старые события до подключения цели не восстанавливаются по текущим запасам.");
            float h = Text.CalcHeight(text, r.width - 24) + 16;
            Widgets.BeginScrollView(r, ref scroll, new Rect(0, 0, r.width - 20, h));
            try { Widgets.Label(new Rect(0, 0, r.width - 24, h), text); }
            finally { Widgets.EndScrollView(); }
        }
    }
}
