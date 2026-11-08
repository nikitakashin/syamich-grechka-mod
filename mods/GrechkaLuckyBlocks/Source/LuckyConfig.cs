using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Grechka.LuckyBlocks
{
    public static class LuckyText
    {
        public static Func<string, string, string> Resolve = (key, fallback) => fallback;
        public static string Get(string key, string fallback) => Resolve(key, fallback);
    }

    public sealed class LuckyConfig
    {
        public int schemaVersion = 1;
        public double raiderChance = 0.02;
        public List<double> raiderTierWeights = new List<double> { 60, 25, 10, 4, 1 };
        public List<LuckyPool> pools = new List<LuckyPool>();
    }
    public sealed class LuckyPool
    {
        public string id = "";
        public List<LuckyOutcome> outcomes = new List<LuckyOutcome>();
    }
    public sealed class LuckyOutcome
    {
        public string id = "";
        public double weight = 1;
        public string effect = "items";
        public string pawnKind = "";
        public int pawnCount = 1;
        public List<string> requiredMods = new List<string>();
        public List<LuckyItem> items = new List<LuckyItem>();
    }
    public sealed class LuckyItem
    {
        public string def = "Steel";
        public int count = 1;
        public string stuff = "";
        public string quality = "";
    }
    public static class LuckyCodec
    {
        public const int MaxBytes = 2 * 1024 * 1024;
        static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true, MaxDepth = 24,
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
        public static string Encode(LuckyConfig config) => JsonSerializer.Serialize(config, Options);
        public static string EncodeOutcome(LuckyOutcome outcome) => JsonSerializer.Serialize(outcome, Options);
        public static LuckyOutcome DecodeOutcome(string json)
        {
            if (json == null || System.Text.Encoding.UTF8.GetByteCount(json) > MaxBytes) throw new FormatException(LuckyText.Get("GrechkaLuckyBlocks_Text001", "Сохранённый исход превышает 2 МБ."));
            LuckyOutcome outcome = JsonSerializer.Deserialize<LuckyOutcome>(json, Options);
            var errors = Validate(new LuckyConfig { pools = new List<LuckyPool> {
                new LuckyPool { id = "saved", outcomes = new List<LuckyOutcome> { outcome } } } });
            if (errors.Count > 0) throw new FormatException(string.Join("\n", errors.Take(15)));
            return outcome;
        }
        public static LuckyConfig Decode(string json)
        {
            if (json == null || System.Text.Encoding.UTF8.GetByteCount(json) > MaxBytes) throw new FormatException(LuckyText.Get("GrechkaLuckyBlocks_Text002", "Конфиг превышает 2 МБ."));
            var result = JsonSerializer.Deserialize<LuckyConfig>(json, Options);
            var errors = Validate(result);
            if (errors.Count > 0) throw new FormatException(string.Join("\n", errors.Take(15)));
            return result;
        }
        public static List<string> Validate(LuckyConfig config)
        {
            var errors = new List<string>();
            if (config == null || config.schemaVersion != 1) { errors.Add(LuckyText.Get("GrechkaLuckyBlocks_Text003", "Нужен schemaVersion 1.")); return errors; }
            if (!double.IsFinite(config.raiderChance) || config.raiderChance < 0 || config.raiderChance > 1) errors.Add(LuckyText.Get("GrechkaLuckyBlocks_Text004", "Шанс рейдера: 0–1."));
            if (config.raiderTierWeights == null || config.raiderTierWeights.Count != 5 || config.raiderTierWeights.Any(w => !double.IsFinite(w) || w < 0)
                || !double.IsFinite(config.raiderTierWeights.Sum()) || config.raiderTierWeights.Sum() <= 0) errors.Add(LuckyText.Get("GrechkaLuckyBlocks_Text005", "Нужны пять неотрицательных весов тиров с конечной положительной суммой."));
            if (config.pools == null || config.pools.Count == 0 || config.pools.Count > 200) { errors.Add(LuckyText.Get("GrechkaLuckyBlocks_Text006", "Нужно 1–200 пулов.")); return errors; }
            var poolIds = new HashSet<string>();
            foreach (var pool in config.pools)
            {
                if (pool == null || !ValidId(pool.id) || !poolIds.Add(pool.id)) { errors.Add(LuckyText.Get("GrechkaLuckyBlocks_Text007", "Повторный/некорректный ID пула.")); continue; }
                if (pool.outcomes == null || pool.outcomes.Count == 0 || pool.outcomes.Count > 200) { errors.Add(pool.id + LuckyText.Get("GrechkaLuckyBlocks_Text008", ": нужно 1–200 исходов.")); continue; }
                var ids = new HashSet<string>();
                foreach (var outcome in pool.outcomes)
                {
                    if (outcome == null || !ValidId(outcome.id) || !ids.Add(outcome.id)) { errors.Add(pool.id + LuckyText.Get("GrechkaLuckyBlocks_Text009", ": некорректный ID исхода.")); continue; }
                    if (!double.IsFinite(outcome.weight) || outcome.weight <= 0 || outcome.weight > 1000000) errors.Add(outcome.id + LuckyText.Get("GrechkaLuckyBlocks_Text010", ": неверный вес."));
                    if (outcome.effect != "items" && outcome.effect != "animals" && outcome.effect != "hostileMechs") errors.Add(outcome.id + LuckyText.Get("GrechkaLuckyBlocks_Text011", ": неизвестный эффект."));
                    if (outcome.requiredMods == null || outcome.requiredMods.Count > 30 || outcome.requiredMods.Any(m => !ValidId(m))) errors.Add(outcome.id + LuckyText.Get("GrechkaLuckyBlocks_Text012", ": неверные requiredMods."));
                    if (outcome.items == null || outcome.items.Count > 30) { errors.Add(outcome.id + LuckyText.Get("GrechkaLuckyBlocks_Text013", ": неверный список предметов.")); continue; }
                    if (outcome.effect == "items" && outcome.items.Count == 0) errors.Add(outcome.id + LuckyText.Get("GrechkaLuckyBlocks_Text014", ": пустая награда."));
                    if (outcome.effect != "items" && (!ValidId(outcome.pawnKind) || outcome.pawnCount < 1 || outcome.pawnCount > 100)) errors.Add(outcome.id + LuckyText.Get("GrechkaLuckyBlocks_Text015", ": нужно 1–100 пешек существующего вида."));
                    foreach (var item in outcome.items)
                        if (item == null || !ValidId(item.def) || item.count < 1 || item.count > 100000 || (!string.IsNullOrEmpty(item.stuff) && !ValidId(item.stuff))
                            || (!string.IsNullOrEmpty(item.quality) && !new[] { "Awful", "Poor", "Normal", "Good", "Excellent", "Masterwork", "Legendary" }.Contains(item.quality))) errors.Add(outcome.id + LuckyText.Get("GrechkaLuckyBlocks_Text016", ": неверный предмет/материал/качество."));
                }
            }
            return errors;
        }
        public static bool ValidId(string value) => !string.IsNullOrEmpty(value) && value.Length <= 120 && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '.' || c == '-');
        public static int PickIndex(IReadOnlyList<double> weights, double unit)
        {
            if (weights == null || weights.Count == 0 || !double.IsFinite(unit) || unit < 0 || unit >= 1) throw new ArgumentException(LuckyText.Get("GrechkaLuckyBlocks_Text017", "Некорректная выборка."));
            if (weights.Any(w => !double.IsFinite(w) || w < 0)) throw new ArgumentException(LuckyText.Get("GrechkaLuckyBlocks_Text018", "Некорректный вес."));
            double sum = weights.Sum();
            if (!double.IsFinite(sum) || sum <= 0) throw new ArgumentException(LuckyText.Get("GrechkaLuckyBlocks_Text019", "Пустой пул."));
            double target = unit * sum;
            for (int i = 0; i < weights.Count; i++) { target -= weights[i]; if (target < 0) return i; }
            for (int i = weights.Count - 1; i >= 0; i--) if (weights[i] > 0) return i;
            throw new InvalidOperationException();
        }
    }
}
