using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using Verse;

namespace Grechka.Quests
{
    // These two fields contain JSON. RimWorld's normal string reader converts
    // backslash-n to a line feed even when it is part of a JSON escape.
    internal static class QuestSaveText
    {
        internal static void Look(ref string value, string label)
        {
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                value = Read(Scribe.loader.curXmlParent[label]);
            else
                Scribe_Values.Look(ref value, label, null);
        }

        internal static void Look(ref List<string> values, string label)
        {
            if (Scribe.mode != LoadSaveMode.LoadingVars)
            {
                Scribe_Collections.Look(ref values, label, LookMode.Value);
                return;
            }

            XmlNode list = Scribe.loader.curXmlParent[label];
            if (IsNull(list)) { values = null; return; }
            values = new List<string>();
            foreach (XmlNode item in list.SelectNodes("li")) values.Add(Read(item));
        }

        static bool IsNull(XmlNode node) => node == null ||
            string.Equals(node.Attributes?["IsNull"]?.Value, "true", StringComparison.OrdinalIgnoreCase);

        static string Read(XmlNode node)
        {
            if (IsNull(node)) return null;
            string text = node.InnerText;
            if (text.Length > PackCodec.MaxBytes) return text; // The strict decoder reports the size limit.
            // An old build could re-save an already damaged snapshot on LoadError.
            // Reverse only literal LFs inside JSON strings. LF is illegal there,
            // so valid formatting, ordinary escapes and authored text are unchanged.
            bool quoted = false, escaped = false;
            StringBuilder repaired = null;
            int copied = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted && c == '\n')
                {
                    repaired ??= new StringBuilder(text.Length);
                    repaired.Append(text, copied, i - copied).Append("\\n");
                    copied = i + 1; escaped = false;
                }
                else if (escaped) escaped = false;
                else if (c == '\\' && quoted) escaped = true;
                else if (c == '"') quoted = !quoted;
            }
            return repaired == null ? text : repaired.Append(text, copied, text.Length - copied).ToString();
        }
    }
}
