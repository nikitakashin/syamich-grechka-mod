using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Xml.Linq;
using HarmonyLib;
using Verse;

namespace Grechka.Translations.Rus
{
    public sealed class TranslationsMod : Mod
    {
        internal static string Directory;
        public TranslationsMod(ModContentPack content) : base(content)
        {
            Directory = content.RootDir;
        }
    }

    [StaticConstructorOnStartup]
    internal static class Startup
    {
        static Startup()
        {
            if (string.IsNullOrEmpty(TranslationsMod.Directory)) return;
            var mapping = Path.Combine(TranslationsMod.Directory, "Translations", "UiMappings.xml");
            if (!File.Exists(mapping)) return;
            var harmony = new Harmony("grechka.translations.rus.ui");
            foreach (var method in UiLocalization.ConfigureMappings(mapping))
            {
                try
                {
                    harmony.Patch(method, transpiler: new HarmonyMethod(typeof(UiLocalization), nameof(UiLocalization.Transpiler)));
                }
                catch (Exception ex)
                {
                    Log.Warning("[Grechka Translations Rus] UI translation skipped for " + method.DeclaringType.FullName + "." + method.Name + ": " + ex.Message);
                }
            }
        }
    }

    // Only literal UI strings in explicitly listed methods can be changed.
    // No gameplay types, saved values, settings identifiers, or enum values are patched.
    public static class UiLocalization
    {
        private static readonly Dictionary<MethodBase, Dictionary<string, string>> Mappings = new Dictionary<MethodBase, Dictionary<string, string>>();
        private static readonly MethodInfo TranslateMethod = typeof(UiLocalization).GetMethod(nameof(TranslateKey));

        public static List<MethodBase> ConfigureMappings(string path)
        {
            Mappings.Clear();
            foreach (var target in XDocument.Load(path).Root.Elements("Target"))
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == (string)target.Attribute("assembly"));
                var type = assembly == null ? null : assembly.GetType((string)target.Attribute("type"), false);
                if (type == null) continue;
                var method = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .SingleOrDefault(m => m.Name == (string)target.Attribute("method"));
                if (method == null) continue;
                var texts = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var text in target.Elements("Text")) texts[Normalize(text.Value)] = (string)text.Attribute("key");
                Mappings[method] = texts;
            }
            return Mappings.Keys.ToList();
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            Dictionary<string, string> texts;
            Mappings.TryGetValue(__originalMethod, out texts);
            foreach (var instruction in instructions)
            {
                string key;
                if (texts != null && instruction.opcode == OpCodes.Ldstr && instruction.operand is string && texts.TryGetValue(Normalize((string)instruction.operand), out key))
                {
                    // Keep the original instruction object, branch labels and exception metadata.
                    instruction.operand = key;
                    yield return instruction;
                    yield return new CodeInstruction(OpCodes.Call, TranslateMethod);
                }
                else yield return instruction;
            }
        }

        public static string TranslateKey(string key)
        {
            // Resolve on each draw so language reloads also update these settings windows.
            return key.Translate().ToString();
        }

        private static string Normalize(string value)
        {
            // XML normalizes CRLF; the original assembly may retain Windows newlines.
            return value.Replace("\r\n", "\n").Replace("\r", "\n");
        }
    }
}
