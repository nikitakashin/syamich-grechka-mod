using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Grechka.Quests
{
    public sealed class QuestPack
    {
        public int schemaVersion = 1;
        public string id = "grechka.example";
        public string title = "Моя книга квестов";
        public List<Chapter> chapters = new List<Chapter>();
        public List<Quest> quests = new List<Quest>();
    }
    public sealed class Chapter { public string id = "main"; public string title = "Начало"; }
    public sealed class Quest
    {
        public string id = "";
        public string chapter = "main";
        public string title = "Новое задание";
        public string description = "";
        public List<string> requires = new List<string>();
        public List<Objective> objectives = new List<Objective>();
        public List<Reward> rewards = new List<Reward>();
    }
    public sealed class Objective
    {
        public string id = "";
        public string type = "have";
        public string target = "Steel";
        public int count = 10;
        public string text = "";
    }
    public sealed class Reward { public string def = "Steel"; public int count = 10; public string stuff = ""; }
    public static class PackCodec
    {
        public const int MaxBytes = 2 * 1024 * 1024;
        static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true, PropertyNameCaseInsensitive = false, MaxDepth = 32, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
        static readonly Regex IdPattern = new Regex(@"^[A-Za-z0-9_.-]{1,100}$");
        public static string NewId(string prefix) { return prefix + Guid.NewGuid().ToString("N"); }
        public static string Encode(QuestPack p) { return JsonSerializer.Serialize(p, Options); }
        public static QuestPack Decode(string json)
        {
            if (json == null || System.Text.Encoding.UTF8.GetByteCount(json) > MaxBytes) throw new FormatException("Размер JSON превышает 2 МБ.");
            QuestPack p = JsonSerializer.Deserialize<QuestPack>(json, Options);
            List<string> errors = Validate(p);
            if (errors.Count > 0) throw new FormatException(string.Join("\n", errors.Take(12)));
            return p;
        }
        public static QuestPack Clone(QuestPack p) { return JsonSerializer.Deserialize<QuestPack>(Encode(p), Options); }
        public static bool ValidId(string s) { return s != null && IdPattern.IsMatch(s); }
        public static string Key(string pack, string quest) { return pack + "/" + quest; }
        public static string ObjectiveKey(string pack, string quest, string objective) { return Key(pack, quest) + "/" + objective; }
        public static List<string> Validate(QuestPack p)
        {
            var e = new List<string>();
            if (p == null) { e.Add("Пакет пуст."); return e; }
            if (p.schemaVersion != 1) e.Add("Поддерживается schemaVersion = 1.");
            if (!ValidId(p.id)) e.Add("Некорректный ID пакета: латиница, цифры, точка, дефис, подчёркивание.");
            if (string.IsNullOrWhiteSpace(p.title) || p.title.Length > 200) e.Add("Название пакета: от 1 до 200 символов.");
            if (p.chapters == null || p.quests == null) { e.Add("chapters и quests должны быть массивами."); return e; }
            if (p.chapters.Count > 100 || p.quests.Count > 500) { e.Add("Максимум 100 глав и 500 заданий."); return e; }
            var chapters = new HashSet<string>();
            foreach (var c in p.chapters)
            {
                if (c == null || !ValidId(c.id) || !chapters.Add(c.id)) e.Add("Некорректный или повторяющийся ID главы.");
                if (c == null || string.IsNullOrWhiteSpace(c.title) || c.title.Length > 200) e.Add("Название главы: 1–200 символов.");
            }
            var quests = new Dictionary<string, Quest>();
            foreach (var q in p.quests)
            {
                if (q == null) { e.Add("Пустое задание."); continue; }
                if (!ValidId(q.id) || quests.ContainsKey(q.id)) e.Add("Некорректный или повторный ID задания: " + q.id);
                else quests.Add(q.id, q);
                if (string.IsNullOrWhiteSpace(q.title) || q.title.Length > 200) e.Add("Название задания: 1–200 символов.");
                if (q.description == null || q.description.Length > 12000) e.Add("Описание слишком длинное: " + q.id);
                if (q.chapter == null || !chapters.Contains(q.chapter)) e.Add("Не найдена глава: " + q.id);
                if (q.objectives == null || q.rewards == null || q.requires == null) { e.Add("Массивы задания не могут быть null: " + q.id); continue; }
                if (q.objectives.Count < 1 || q.objectives.Count > 30 || q.rewards.Count > 30 || q.requires.Count > 100) e.Add("Лимиты задания: 1–30 целей, до 30 наград, до 100 зависимостей.");
                var ids = new HashSet<string>();
                foreach (var o in q.objectives)
                {
                    if (o == null) { e.Add("Пустая цель."); continue; }
                    if (!ValidId(o.id) || !ids.Add(o.id)) e.Add("Повторный или некорректный ID цели: " + q.id);
                    if (o.type != "have" && o.type != "build" && o.type != "manual") e.Add("Неизвестный тип цели: " + o.type + ". Сдача предметов не поддерживается.");
                    if (o.type != "manual" && !ValidId(o.target)) e.Add("Укажите defName цели: " + q.id);
                    if (o.count < 1 || o.count > 1000000) e.Add("Количество цели: 1–1000000.");
                    if (o.text == null || o.text.Length > 1000) e.Add("Подпись цели слишком длинная.");
                }
                foreach (var r in q.rewards)
                    if (r == null || !ValidId(r.def) || r.count < 1 || r.count > 10000 || (r.stuff != "" && !ValidId(r.stuff))) e.Add("Некорректная награда: " + q.id);
                if (q.requires.Distinct().Count() != q.requires.Count) e.Add("Повторяющиеся зависимости: " + q.id);
            }
            if (e.Count > 0) return e;
            foreach (var q in quests.Values)
                foreach (var id in q.requires)
                    if (id == null || !quests.ContainsKey(id)) e.Add("Не найдена зависимость " + id + " у " + q.id);
            if (e.Count > 0) return e;
            var visited = new HashSet<string>(); var visiting = new HashSet<string>();
            foreach (var id in quests.Keys) if (Cycle(id, quests, visiting, visited)) { e.Add("Цикл зависимостей заданий."); break; }
            return e;
        }
        static bool Cycle(string id, Dictionary<string, Quest> qs, HashSet<string> visiting, HashSet<string> visited)
        {
            if (visited.Contains(id)) return false;
            if (!visiting.Add(id)) return true;
            foreach (var dep in qs[id].requires) if (Cycle(dep, qs, visiting, visited)) return true;
            visiting.Remove(id); visited.Add(id); return false;
        }
    }
}
