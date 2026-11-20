using System.Collections.Generic;
using System.Linq;
using System.Xml;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.Treasures
{
    [HarmonyPatch(typeof(ScribeLoader), nameof(ScribeLoader.InitLoading))]
    internal static class RoyalThroneSave
    {
        private static readonly HashSet<string> throneNames = new HashSet<string>(
            TreasureLoot.Gems.Select(gem => "GrechkaTreasures_" + gem + "Throne"));

        private static void Postfix(XmlNode ___curXmlParent)
        {
            if (___curXmlParent?.Name == "savegame" && ModsConfig.RoyaltyActive)
                UpgradeSavedThrones(___curXmlParent);
        }

        private static void UpgradeSavedThrones(XmlNode root)
        {
            foreach (XmlElement thing in root.SelectNodes("descendant::*[(@Class='Building' or @Class='Verse.Building') and def]"))
            {
                string defName = thing["def"].InnerText;
                if (throneNames.Contains(defName) && DefDatabase<ThingDef>.GetNamedSilentFail(defName)?.thingClass == typeof(Building_Throne))
                    thing.SetAttribute("Class", typeof(Building_Throne).FullName);
            }
        }
    }
}
