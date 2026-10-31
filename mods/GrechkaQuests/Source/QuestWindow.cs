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
        string selected;
        Vector2 listScroll, detailScroll, fileScroll;
        float detailHeight = 1000;
        string status = "";
        string jsonInput = "";
        List<string> files = new List<string>();
        readonly Dictionary<string, string> numberBuffers = new Dictionary<string, string>();
        public override Vector2 RequestedTabSize { get { return new Vector2(1100, 760); } }
        public override void PostOpen()
        {
            base.PostOpen(); var game = QuestGame.Current; if (game == null) return;
            game.Refresh(); draft = PackCodec.Clone(game.Pack); selected = draft.quests.FirstOrDefault()?.id;
            ReloadFiles(); status = game.LoadError ?? "";
        }
        void ReloadFiles() { try { files = PackFiles.List(); } catch (Exception e) { status = e.Message; } }
        void Fail(Exception e) { status = "Ошибка: " + e.Message; Log.Warning("[Grechka Quests] " + e); }
        public override void DoWindowContents(Rect rect)
        {
            var game = QuestGame.Current; if (game == null || draft == null) { Widgets.Label(rect, "Откройте колонию, чтобы работать с квестами."); return; }
            Text.Font = GameFont.Small;
            if (Widgets.ButtonText(new Rect(0, 0, 140, 32), "Книга")) { tab = 0; game.Refresh(); detailScroll = Vector2.zero; }
            if (Widgets.ButtonText(new Rect(148, 0, 140, 32), "Редактор")) { tab = 1; detailScroll = Vector2.zero; }
            if (Widgets.ButtonText(new Rect(296, 0, 180, 32), "Импорт / экспорт")) { tab = 2; ReloadFiles(); }
            Widgets.Label(new Rect(490, 3, rect.width - 490, 30), "Grechka Quests · " + (tab == 0 ? game.Pack.title : draft.title));
            float footer = 72;
            Rect body = new Rect(0, 42, rect.width, rect.height - 42 - footer);
            if (tab == 2) Files(body, game); else QuestPane(body, game);
            Widgets.DrawLineHorizontal(0, rect.height - footer, rect.width);
            Widgets.Label(new Rect(0, rect.height - footer + 6, rect.width, footer - 6), status);
        }
        void QuestPane(Rect r, QuestGame game)
        {
            QuestPack p = tab == 0 ? game.Pack : draft;
            float leftWidth = 260;
            Rect left = new Rect(r.x, r.y, leftWidth, r.height);
            float top = 0;
            if (tab == 1)
            {
                if (Widgets.ButtonText(new Rect(left.x, left.y, 125, 30), "+ Задание"))
                {
                    if (draft.quests.Count >= 500) { status = "Лимит: 500 заданий."; return; }
                    if (draft.chapters.Count == 0) draft.chapters.Add(new Chapter());
                    var q = new Quest { id = PackCodec.NewId("quest."), chapter = draft.chapters[0].id };
                    q.objectives.Add(new Objective { id = PackCodec.NewId("goal.") }); draft.quests.Add(q); selected = q.id; detailScroll = Vector2.zero;
                }
                if (Widgets.ButtonText(new Rect(left.x + 132, left.y, 128, 30), "Главы / пакет")) selected = null;
                top = 36;
            }
            float total = p.chapters.Count * 32 + p.quests.Count * 54 + 20;
            Rect viewport = new Rect(left.x, left.y + top, left.width, left.height - top);
            Widgets.BeginScrollView(viewport, ref listScroll, new Rect(0, 0, left.width - 20, Math.Max(total, viewport.height)));
            float y = 0;
            foreach (var c in p.chapters)
            {
                Widgets.Label(new Rect(0, y, left.width - 20, 28), c.title); y += 32;
                foreach (var q in p.quests.Where(q => q.chapter == c.id))
                {
                    string marker = tab != 0 ? "" : game.Claimed(q) ? "✓ " : game.Completed(q) ? "★ " : !game.Unlocked(q) ? "… " : "";
                    if (Widgets.ButtonText(new Rect(0, y, left.width - 22, 48), marker + q.title)) { selected = q.id; detailScroll = Vector2.zero; }
                    y += 54;
                }
            }
            Widgets.EndScrollView();
            Rect right = new Rect(r.x + leftWidth + 18, r.y, r.width - leftWidth - 18, r.height);
            Quest current = p.quests.FirstOrDefault(q => q.id == selected);
            if (tab == 0) Book(right, game, current); else Editor(right, current, game);
        }
        void Book(Rect r, QuestGame game, Quest q)
        {
            if (q == null) { Widgets.Label(r, game.Pack.quests.Count == 0 ? "Книга пока пуста. Создайте задания в редакторе или импортируйте пример во вкладке «Импорт / экспорт»." : "Выберите задание слева."); return; }
            Widgets.BeginScrollView(r, ref detailScroll, new Rect(0, 0, r.width - 20, detailHeight));
            float w = r.width - 24, y = 0;
            Label(ref y, w, q.title, 42);
            Label(ref y, w, q.description, Math.Max(60, Text.CalcHeight(q.description, w) + 8));
            if (!game.Unlocked(q)) Label(ref y, w, "Сначала выполните: " + string.Join(", ", q.requires.Select(id => game.Pack.quests.FirstOrDefault(x => x.id == id)?.title ?? id)), 64);
            foreach (var problem in QuestGame.Problems(q)) Label(ref y, w, "⚠ " + problem, 48);
            foreach (var o in q.objectives)
            {
                string text = string.IsNullOrWhiteSpace(o.text) ? ObjectiveTitle(o) : o.text;
                Label(ref y, w, text + (o.type == "manual" ? (game.Manual(q, o) ? " — отмечено" : "") : " — " + game.Value(q, o) + " / " + o.count), 48);
                if (o.type == "manual" && !game.Manual(q, o) && game.Unlocked(q) && !game.Completed(q))
                {
                    if (Widgets.ButtonText(new Rect(0, y, 200, 30), "Отметить выполнение")) game.MarkManual(q, o);
                    y += 38;
                }
            }
            Label(ref y, w, "Награды: " + (q.rewards.Count == 0 ? "нет предметных наград" : string.Join(", ", q.rewards.Select(x => x.count + " × " + DefLabel(x.def)))), 70);
            if (game.Claimed(q)) Label(ref y, w, "Награда уже получена.", 34);
            else if (game.Completed(q))
            {
                if (Widgets.ButtonText(new Rect(0, y, 240, 36), "Получить награду"))
                {
                    try { status = game.Claim(q) ?? "Награда выдана. Предметы задания остаются у вас."; } catch (Exception e) { Fail(e); }
                }
                y += 44;
            }
            if (game.Completed(q)) Label(ref y, w, "Выполнение зафиксировано в этой колонии.", 34);
            if (game.PendingCount > 0) Label(ref y, w, "Ожидают размещения: " + game.PendingCount + " стопок. Они сохранены и будут выданы на домашней карте.", 60);
            Label(ref y, w, "Наличие: открытые незапрещённые предметы на домашних картах и инвентарь колонистов на них. Постройки: готовые здания игрока. Проверка примерно раз в 4 игровых секунды; это не счётчик производства.", 90);
            detailHeight = y + 15; Widgets.EndScrollView();
        }
        static string DefLabel(string name) { var d = DefDatabase<ThingDef>.GetNamedSilentFail(name); return d == null ? "[нет: " + name + "]" : d.LabelCap.ToString(); }
        static string ObjectiveTitle(Objective o) { return o.type == "manual" ? "Ручная цель" : (o.type == "build" ? "Построить: " : "Иметь: ") + DefLabel(o.target); }
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
            if (Widgets.ButtonText(new Rect(r.x, r.y, 220, 32), "Применить к этой колонии"))
            {
                try
                {
                    if (numberBuffers.Values.Any(v => !int.TryParse(v, out int n) || n < 1 || n > 1000000)) throw new FormatException("Проверьте числовые поля: нужны положительные целые числа.");
                    game.Apply(draft);
                    status = "Применено. Сохраните игру для записи прогресса; экспортируйте JSON для переноса. " + CompatibilitySummary(draft);
                }
                catch (Exception e) { Fail(e); }
            }
            if (Widgets.ButtonText(new Rect(r.x + 230, r.y, 170, 32), "Отменить правки"))
            {
                Find.WindowStack.Add(new ConfirmWindow("Отменить все неприменённые правки редактора?", () => { draft = PackCodec.Clone(game.Pack); numberBuffers.Clear(); status = "Черновик восстановлен из колонии."; }));
            }
            Rect view = new Rect(r.x, r.y + 42, r.width, r.height - 42);
            Widgets.BeginScrollView(view, ref detailScroll, new Rect(0, 0, view.width - 20, detailHeight));
            float y = 0, w = view.width - 24;
            if (q == null) EditPack(ref y, w);
            else EditQuest(ref y, w, q);
            detailHeight = y + 20; Widgets.EndScrollView();
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
                c.title = Field(ref y, w - 90, "Глава · " + c.id, c.title);
                if (Widgets.ButtonText(new Rect(w - 80, y - 38, 80, 30), "Удалить")) remove = c;
            }
            if (remove != null)
            {
                if (draft.quests.Any(q => q.chapter == remove.id)) status = "Сначала перенесите задания из главы или удалите их.";
                else draft.chapters.Remove(remove);
            }
            if (draft.chapters.Count < 100 && Widgets.ButtonText(new Rect(0, y, 170, 30), "+ Глава")) draft.chapters.Add(new Chapter { id = PackCodec.NewId("chapter."), title = "Новая глава" });
            y += 40;
        }
        void EditQuest(ref float y, float w, Quest q)
        {
            q.title = Field(ref y, w, "Название задания", q.title);
            Label(ref y, w, "ID: " + q.id, 30);
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
                if (candidates.Count > 0) Find.WindowStack.Add(new ChoiceWindow("Выберите задание", candidates.Select(x => new Choice(x.title, x.id, () => q.requires.Add(x.id))).ToList()));
                else status = "Нет доступных заданий. Сначала создайте ещё одно.";
            }
            y += 44;
            Label(ref y, w, "Цели — ничего не расходуют", 30);
            Objective removeGoal = null;
            foreach (var o in q.objectives)
            {
                string typeLabel = o.type == "have" ? "Иметь предмет" : o.type == "build" ? "Иметь постройку" : "Ручная отметка";
                if (Widgets.ButtonText(new Rect(0, y, 200, 30), typeLabel))
                    Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption> {
                        new FloatMenuOption("Иметь предмет", () => { o.type = "have"; o.target = "Steel"; }),
                        new FloatMenuOption("Иметь постройку", () => { o.type = "build"; o.target = "TableStonecutter"; }),
                        new FloatMenuOption("Ручная отметка", () => { o.type = "manual"; o.count = 1; }) }));
                if (Widgets.ButtonText(new Rect(w - 80, y, 80, 30), "Удалить")) removeGoal = o;
                y += 38;
                if (o.type != "manual")
                {
                    if (Widgets.ButtonText(new Rect(0, y, w - 120, 30), DefLabel(o.target))) PickDef(o.type == "build", false, d => o.target = d.defName);
                    o.count = Number(new Rect(w - 110, y, 110, 30), o.id, o.count, 1000000); y += 38;
                }
                o.text = Field(ref y, w, "Своя подпись (необязательно)", o.text, 1000);
                string issue = QuestGame.ObjectiveProblem(o); if (issue != null) Label(ref y, w, issue, 48);
                y += 8;
            }
            if (removeGoal != null) { q.objectives.Remove(removeGoal); numberBuffers.Clear(); }
            if (q.objectives.Count < 30 && Widgets.ButtonText(new Rect(0, y, 160, 30), "+ Цель")) q.objectives.Add(new Objective { id = PackCodec.NewId("goal.") });
            y += 42;
            Label(ref y, w, "Предметные награды — добавляются один раз", 32);
            Reward removeReward = null;
            foreach (var reward in q.rewards)
            {
                if (Widgets.ButtonText(new Rect(0, y, w - 210, 30), DefLabel(reward.def))) PickDef(false, true, d => { reward.def = d.defName; reward.stuff = ""; });
                reward.count = Number(new Rect(w - 200, y, 110, 30), q.id + ".r." + q.rewards.IndexOf(reward), reward.count, 10000);
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
                string issue = QuestGame.RewardProblem(reward); if (issue != null) Label(ref y, w, issue, 48);
            }
            if (removeReward != null) { q.rewards.Remove(removeReward); numberBuffers.Clear(); }
            if (q.rewards.Count < 30 && Widgets.ButtonText(new Rect(0, y, 180, 30), "+ Награда")) q.rewards.Add(new Reward());
            y += 50;
            if (Widgets.ButtonText(new Rect(0, y, 200, 32), "Удалить задание"))
            {
                if (draft.quests.Any(x => x.requires.Contains(q.id))) status = "На задание ссылаются другие. Сначала уберите эти зависимости.";
                else Find.WindowStack.Add(new ConfirmWindow("Удалить задание «" + q.title + "» из черновика? История наград останется в сохранении.", () => { draft.quests.Remove(q); selected = null; }));
            }
            y += 44;
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
            { try { status = "Экспорт: " + PackFiles.Export(draft); ReloadFiles(); } catch (Exception e) { Fail(e); } }
            if (Widgets.ButtonText(new Rect(r.x + 210, y, 180, 32), "Обновить файлы")) ReloadFiles();
            if (Widgets.ButtonText(new Rect(r.x + 400, y, 170, 32), "Новая книга"))
                Find.WindowStack.Add(new ConfirmWindow("Создать пустой черновик с новым ID? Неприменённые правки будут потеряны; активная книга колонии пока не изменится.", () => { draft = QuestGame.EmptyPack(); selected = null; numberBuffers.Clear(); status = "Новая книга создана в черновике."; }));
            y += 40;
            Widgets.Label(new Rect(r.x, y, r.width, 54), "Папка обмена: " + PackFiles.Folder); y += 60;
            float fileHeight = Math.Max(80, (r.height - 280) * 0.55f);
            Rect view = new Rect(r.x, y, r.width, fileHeight);
            Widgets.BeginScrollView(view, ref fileScroll, new Rect(0, 0, r.width - 20, Math.Max(fileHeight, files.Count * 44)));
            float fy = 0;
            foreach (string file in files)
            {
                Widgets.Label(new Rect(0, fy, r.width - 160, 38), Path.GetFileName(file) + (file.StartsWith(PackFiles.Folder, StringComparison.Ordinal) ? "" : " · пример мода"));
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
                draft = pack; selected = draft.quests.FirstOrDefault()?.id; numberBuffers.Clear(); detailScroll = Vector2.zero;
                status = "Импортировано в черновик. " + CompatibilitySummary(pack) + " Для активации откройте редактор и примените.";
            }));
        }
    }
    public sealed class Choice
    {
        public readonly string Title, Detail; public readonly Action Select;
        public Choice(string title, string detail, Action select) { Title = title; Detail = detail; Select = select; }
    }
    public sealed class ChoiceWindow : Window
    {
        readonly string title; readonly List<Choice> choices; string search = ""; Vector2 scroll;
        public override Vector2 InitialSize { get { return new Vector2(720, 600); } }
        public ChoiceWindow(string title, List<Choice> choices) { this.title = title; this.choices = choices; doCloseX = true; absorbInputAroundWindow = true; forcePause = true; }
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
        public override Vector2 InitialSize { get { return new Vector2(520, 240); } }
        public ConfirmWindow(string message, Action action) { this.message = message; this.action = action; doCloseX = true; absorbInputAroundWindow = true; forcePause = true; }
        public override void DoWindowContents(Rect r)
        {
            Widgets.Label(new Rect(0, 0, r.width, r.height - 50), message);
            if (Widgets.ButtonText(new Rect(0, r.height - 40, 190, 36), "Подтвердить")) { action(); Close(); }
            if (Widgets.ButtonText(new Rect(r.width - 190, r.height - 40, 190, 36), "Отмена")) Close();
        }
    }
}
