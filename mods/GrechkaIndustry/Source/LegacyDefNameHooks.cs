using System;
using System.Xml;
using HarmonyLib;
using Verse;

namespace Grechka.Industry
{
    [HarmonyPatch(typeof(BackCompatibility), nameof(BackCompatibility.BackCompatibleDefName),
        new Type[] { typeof(Type), typeof(string), typeof(bool), typeof(XmlNode) })]
    internal static class LegacySaveDefNameHook
    {
        private static void Prefix(Type __0, ref string __1)
        {
            if (__0 != null && __0.IsAssignableFrom(typeof(ThingDef)))
                __1 = LegacyDefNames.Canonicalize(__1);
        }
    }

}
