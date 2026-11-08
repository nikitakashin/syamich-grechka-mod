using Verse;

namespace Grechka.WorldIntegration
{
    internal static class WorldText
    {
        // Def parsing and isolated tools can run before an active language exists.
        internal static string Get(string key, string fallback)
            => LanguageDatabase.activeLanguage == null ? fallback : key.Translate().ToString();
        // Save keys for new faults and recognize both legacy language strings.
        internal static string Fault(string value)
        {
            switch (value)
            {
                case "GrechkaWorldIntegration_355c0d15c28d":
                case "Изменился тип фабрики; улучшение приостановлено":
                case "The factory type has changed. Upgrade paused":
                    return Get("GrechkaWorldIntegration_355c0d15c28d", "The factory type has changed. Upgrade paused");
                case "GrechkaWorldIntegration_4c5487fe9857":
                case "Ошибка улучшения; исходная фабрика и материалы сохранены":
                case "Upgrade failed. The original factory and materials were preserved":
                    return Get("GrechkaWorldIntegration_4c5487fe9857", "Upgrade failed. The original factory and materials were preserved");
                case "GrechkaWorldIntegration_d3cfbf414b70":
                case "Улучшение выполнено, расход материалов приостановлен":
                case "Upgrade completed. Material consumption paused":
                    return Get("GrechkaWorldIntegration_d3cfbf414b70", "Upgrade completed. Material consumption paused");
                default: return value;
            }
        }
    }
}
