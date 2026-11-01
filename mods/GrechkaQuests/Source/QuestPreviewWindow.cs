using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grechka.Quests
{
    public sealed class QuestPreviewWindow : Window
    {
        readonly QuestPreview preview;
        readonly Dictionary<string, string> choices = new Dictionary<string, string>();
        string chapter, selected, status = "";
        Vector2 chapterScroll, graphScroll, detailScroll;
        float detailHeight = 600;
        public override Vector2 InitialSize { get { return new Vector2(1100, 760); } }

        public QuestPreviewWindow(QuestPack draft)
        {
            preview = new QuestPreview(draft);
            chapter = preview.Pack.chapters.FirstOrDefault()?.id;
            selected = preview.Pack.quests.FirstOrDefault(q => q.chapter == chapter)?.id;
            doCloseX = true; absorbInputAroundWindow = true; forcePause = false;
        }
        public override void DoWindowContents(Rect rect)
        {
            Widgets.Label(new Rect(0, 0, rect.width - 380, 45), "Предпросмотр: тестовое прохождение без предметов и изменений колонии");
            if (Widgets.ButtonText(new Rect(rect.width - 370, 0, 180, 32), "Забрать всё в тесте")) StartClaimAll();
            if (Widgets.ButtonText(new Rect(rect.width - 180, 0, 170, 32), "Сбросить тест"))
            { preview.Reset(); choices.Clear(); status = "Тестовое прохождение сброшено."; }
            Rect area = new Rect(0, 50, rect.width, rect.height - 100);
            Rect chapters = new Rect(area.x, area.y, 170, area.height);
            Rect graph = new Rect(chapters.xMax + 8, area.y, Math.Max(200, area.width - 550), area.height);
            Rect details = new Rect(graph.xMax + 8, area.y, area.xMax - graph.xMax - 8, area.height);
            DrawChapters(chapters); DrawGraph(graph); DrawDetails(details);
            Widgets.Label(new Rect(0, rect.height - 45, rect.width, 45), status);
        }
        void StartClaimAll()
        {
            QuestClaimPlan plan = preview.PrepareClaimAll();
            if (plan.questIds.Count == 0) { status = "Нет готовых тестовых наград."; return; }
            if (plan.questIds.Any(id => preview.Pack.quests.First(q => q.id == id).alternatives.Count > 0))
                Find.WindowStack.Add(new PreviewRewardChoiceWindow(preview, plan, message => status = message));
            else
            {
                string error = preview.FinishClaimAll(plan, null);
                status = error ?? "Получено в тесте заданий: " + plan.questIds.Count + ". Предметы не созданы.";
            }
        }
        void DrawChapters(Rect rect)
        {
            var chapters = preview.Pack.chapters;
            Widgets.BeginScrollView(rect, ref chapterScroll, new Rect(0, 0, rect.width - 18, Math.Max(rect.height, chapters.Count * 44)));
            float y = 0;
            foreach (Chapter item in chapters)
            {
                var quests = preview.Pack.quests.Where(q => q.chapter == item.id).ToList();
                if (Widgets.ButtonText(new Rect(0, y, rect.width - 20, 40), item.title + " (" + quests.Count(preview.Claimed) + "/" + quests.Count + ")"))
                { chapter = item.id; selected = quests.FirstOrDefault()?.id; graphScroll = Vector2.zero; detailScroll = Vector2.zero; }
                if (quests.Any(preview.Ready)) QuestBadge.Draw(new Rect(rect.width - 42, y, 20, 20));
                y += 44;
            }
            Widgets.EndScrollView();
        }
        void DrawGraph(Rect rect)
        {
            var quests = preview.Pack.quests.Where(q => q.chapter == chapter).ToList();
            var positions = quests.Select((q, i) => q.graphPositioned ? new Vector2(q.graphX, q.graphY) : new Vector2((i % 3) * 170, (i / 3) * 100)).ToList();
            float minX = positions.Count == 0 ? 0 : Math.Min(0, positions.Min(p => p.x));
            float minY = positions.Count == 0 ? 0 : Math.Min(0, positions.Min(p => p.y));
            var nodes = quests.Select((q, i) => new { quest = q, rect = new Rect(positions[i].x - minX + 20, positions[i].y - minY + 20, 150, 62) }).ToList();
            float width = nodes.Count == 0 ? rect.width : Math.Max(rect.width, nodes.Max(n => n.rect.xMax) + 30);
            float height = nodes.Count == 0 ? rect.height : Math.Max(rect.height, nodes.Max(n => n.rect.yMax) + 30);
            Widgets.BeginScrollView(rect, ref graphScroll, new Rect(0, 0, width, height));
            foreach (var node in nodes)
                foreach (string id in node.quest.requires)
                {
                    var parent = nodes.FirstOrDefault(n => n.quest.id == id);
                    if (parent != null) Widgets.DrawLine(new Vector2(parent.rect.xMax, parent.rect.center.y), new Vector2(node.rect.x, node.rect.center.y), Color.gray, 2f);
                }
            foreach (var node in nodes)
            {
                string state = preview.Claimed(node.quest) ? "✓ " : preview.Unlocked(node.quest) ? "" : "× ";
                if (Widgets.ButtonText(node.rect, state + node.quest.title)) { selected = node.quest.id; detailScroll = Vector2.zero; }
                if (preview.Ready(node.quest)) QuestBadge.Draw(new Rect(node.rect.xMax - 21, node.rect.y + 1, 20, 20));
            }
            Widgets.EndScrollView();
        }
        void DrawDetails(Rect rect)
        {
            Quest q = preview.Pack.quests.FirstOrDefault(item => item.id == selected);
            if (q == null) { Widgets.Label(rect, "Выберите задание на графе."); return; }
            Widgets.BeginScrollView(rect, ref detailScroll, new Rect(0, 0, rect.width - 20, detailHeight));
            float y = 0, w = rect.width - 24;
            Label(ref y, w, q.title, 36);
            Label(ref y, w, q.description, Math.Max(44, Text.CalcHeight(q.description, w) + 8));
            if (!preview.Unlocked(q)) Label(ref y, w, "Награда закрыта до получения наград предшественников.", 55);
            foreach (string id in q.requires)
            {
                Quest parent = preview.Pack.quests.FirstOrDefault(item => item.id == id); if (parent == null) continue;
                if (Widgets.ButtonText(new Rect(0, y, w, 30), (preview.Claimed(parent) ? "✓ " : "→ ") + parent.title))
                { chapter = parent.chapter; selected = parent.id; graphScroll = Vector2.zero; detailScroll = Vector2.zero; }
                y += 36;
            }
            foreach (Objective goal in q.objectives)
            {
                string label = string.IsNullOrWhiteSpace(goal.text) ? GoalLabel(goal) : goal.text;
                Label(ref y, w, (preview.Achieved(q, goal) ? "✓ " : "○ ") + label, 42);
                if (!preview.Achieved(q, goal))
                {
                    if (Widgets.ButtonText(new Rect(0, y, Math.Min(w, 220), 30), "Выполнить в тесте")) preview.Mark(q, goal);
                    y += 36;
                }
            }
            Label(ref y, w, "Гарантированные награды: " + (q.rewards.Count == 0 ? "нет" : string.Join(", ", q.rewards.Select(RewardLabel))), 60);
            string choice = null;
            if (q.alternatives.Count > 0)
            {
                choices.TryGetValue(q.id, out choice);
                RewardOption chosen = q.alternatives.FirstOrDefault(option => option.id == choice);
                if (Widgets.ButtonText(new Rect(0, y, w, 40), chosen == null ? "Выберите тестовую награду" : DescribeOption(chosen)))
                    Find.WindowStack.Add(new FloatMenu(q.alternatives.Select(option => new FloatMenuOption(DescribeOption(option), () => choices[q.id] = option.id)).ToList()));
                y += 46;
            }
            if (preview.Claimed(q)) Label(ref y, w, "Награда получена в тесте.", 32);
            else if (preview.Ready(q) && Widgets.ButtonText(new Rect(0, y, w, 36), "Получить в тесте"))
            { string error = preview.Claim(q, choice); status = error ?? "Тестовая награда отмечена. Предметы не созданы."; }
            y += 42;
            detailHeight = y + 20;
            Widgets.EndScrollView();
        }
        static string GoalLabel(Objective goal)
        {
            if (goal.type == "manual") return "Ручная цель";
            string type = goal.type == "have" ? "Иметь" : goal.type == "build" ? "Построить" : goal.type == "produce" ? "Произвести" : goal.type == "mine" ? "Добыть" : "Собрать";
            return type + ": " + string.Join(" / ", new[] { goal.target }.Concat(goal.variants).Select(DefLabel)) + " × " + goal.count;
        }
        static string RewardLabel(Reward reward) { return reward.count + " × " + DefLabel(reward.def); }
        internal static string DescribeOption(RewardOption option) { return option.title + " — " + string.Join(", ", option.items.Select(RewardLabel)); }
        static string DefLabel(string name)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(name);
            return def == null ? "[нет: " + name + "]" : def.LabelCap.ToString();
        }
        static void Label(ref float y, float width, string text, float height)
        { Widgets.Label(new Rect(0, y, width, height), text); y += height + 4; }
    }

    // Choice state and completion are isolated from the real reward dialog and game component.
    public sealed class PreviewRewardChoiceWindow : Window
    {
        readonly QuestPreview preview;
        readonly QuestClaimPlan plan;
        readonly Action<string> report;
        readonly List<Quest> quests;
        readonly Dictionary<string, string> choices = new Dictionary<string, string>();
        Vector2 scroll;
        string error = "";
        public override Vector2 InitialSize { get { return new Vector2(680, 600); } }
        public PreviewRewardChoiceWindow(QuestPreview preview, QuestClaimPlan plan, Action<string> report)
        {
            this.preview = preview; this.plan = plan; this.report = report;
            quests = plan.questIds.Select(id => preview.Pack.quests.First(q => q.id == id)).Where(q => q.alternatives.Count > 0).ToList();
            doCloseX = true; absorbInputAroundWindow = true; forcePause = false;
        }
        public override void DoWindowContents(Rect rect)
        {
            Widgets.Label(new Rect(0, 0, rect.width, 55), "Выберите тестовые награды. До подтверждения ничего не отмечается; предметы не создаются.");
            Rect area = new Rect(0, 60, rect.width, Math.Max(0, rect.height - 160));
            Widgets.BeginScrollView(area, ref scroll, new Rect(0, 0, area.width - 20, Math.Max(area.height, quests.Count * 100)));
            float y = 0;
            foreach (Quest q in quests)
            {
                Widgets.Label(new Rect(0, y, area.width - 24, 32), q.title); y += 34;
                string id; choices.TryGetValue(q.id, out id);
                RewardOption selected = q.alternatives.FirstOrDefault(option => option.id == id);
                if (Widgets.ButtonText(new Rect(0, y, area.width - 24, 46), selected == null ? "Выберите набор" : QuestPreviewWindow.DescribeOption(selected)))
                    Find.WindowStack.Add(new FloatMenu(q.alternatives.Select(option => new FloatMenuOption(QuestPreviewWindow.DescribeOption(option), () => choices[q.id] = option.id)).ToList()));
                y += 58;
            }
            Widgets.EndScrollView();
            Widgets.Label(new Rect(0, rect.height - 95, rect.width, 48), error);
            if (Widgets.ButtonText(new Rect(0, rect.height - 42, 200, 36), "Получить в тесте"))
            {
                error = preview.FinishClaimAll(plan, choices);
                if (error == null) { report("Получено в тесте заданий: " + plan.questIds.Count + ". Предметы не созданы."); Close(); }
            }
            if (Widgets.ButtonText(new Rect(rect.width - 170, rect.height - 42, 170, 36), "Отмена")) Close();
        }
    }
}
