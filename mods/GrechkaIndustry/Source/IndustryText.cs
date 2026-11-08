using Verse;

namespace Grechka.Industry
{
    internal static class IndustryText
    {
        // Def parsing and isolated tools can run before an active language exists.
        internal static string Get(string key, string fallback)
            => LanguageDatabase.activeLanguage == null ? fallback : key.Translate().ToString();
    }
}
