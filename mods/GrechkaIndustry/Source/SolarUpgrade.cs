using System;
using System.Linq;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grechka.Industry
{
    public sealed class CompProperties_SolarUpgrade : CompProperties
    {
        public string targetDefName;
        [MustTranslate] public string costDescription;
        public CompProperties_SolarUpgrade() { compClass = typeof(CompSolarUpgrade); }
    }

    // The old building is packaged whole. The normal S1 construction blueprint
    // then consumes that package along with the new parts and builder work.
    public sealed class CompSolarUpgrade : ThingComp
    {
        private CompProperties_SolarUpgrade Props { get { return (CompProperties_SolarUpgrade)props; } }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            Building panel = parent as Building;
            if (panel == null || !panel.Spawned || panel.Faction != Faction.OfPlayer) yield break;
            ThingDef targetDef = DefDatabase<ThingDef>.GetNamedSilentFail(Props.targetDefName);
            if (targetDef == null || targetDef.researchPrerequisites?.Any(r => !r.IsFinished) == true) yield break;

            yield return new Command_Action
            {
                defaultLabel = IndustryText.Get("GrechkaIndustry_7cc02bc655cd", "Upgrade to ") + targetDef.label,
                defaultDesc = IndustryText.Get("GrechkaIndustry_1f423d1791c2", "Uninstall this device intact and place a blueprint for the next stage at the same location. Cost: ")
                    + Props.costDescription + IndustryText.Get("GrechkaIndustry_f878119dcf1a", ". The old device will stop working while the new one is being built."),
                icon = TexCommand.Install,
                action = StartUpgrade
            };
        }

        private void StartUpgrade()
        {
            Building oldPanel = parent as Building;
            ThingDef targetDef = DefDatabase<ThingDef>.GetNamedSilentFail(Props.targetDefName);
            if (oldPanel == null || !oldPanel.Spawned || targetDef == null
                || targetDef.researchPrerequisites?.Any(r => !r.IsFinished) == true) return;

            Map map = oldPanel.Map;
            IntVec3 position = oldPanel.Position;
            Rot4 rotation = oldPanel.Rotation;
            Faction faction = oldPanel.Faction;
            AcceptanceReport placement = GenConstruct.CanPlaceBlueprintAt(
                targetDef, position, rotation, map, false, oldPanel);
            if (!placement.Accepted)
            {
                Messages.Message(IndustryText.Get("GrechkaIndustry_fa748223b51a", "Cannot place the next device here: ") + placement.Reason,
                    MessageTypeDefOf.RejectInput);
                return;
            }

            MinifiedThing packed = MinifyUtility.Uninstall(oldPanel);
            if (packed == null)
            {
                Messages.Message(IndustryText.Get("GrechkaIndustry_0535fa03e514", "Could not uninstall the device intact."), MessageTypeDefOf.RejectInput);
                return;
            }

            try
            {
                Blueprint_Build blueprint = GenConstruct.PlaceBlueprintForBuild(
                    targetDef, position, map, rotation, faction, null);
                if (blueprint == null) throw new InvalidOperationException(IndustryText.Get("GrechkaIndustry_1290fafdb3c2", "Blueprint ") + targetDef.defName + IndustryText.Get("GrechkaIndustry_62224fbdda5a", " was not created."));
            }
            catch (Exception error)
            {
                Log.Error(IndustryText.Get("GrechkaIndustry_b52c94091685", "[Grechka Industry] Failed to place panel upgrade blueprint: ") + error);
                try { GenConstruct.PlaceBlueprintForInstall(packed, position, map, rotation, faction); }
                catch (Exception restoreError)
                {
                    Log.Error(IndustryText.Get("GrechkaIndustry_31c93a2136e2", "[Grechka Industry] Failed to place S0 restoration blueprint: ") + restoreError);
                }
                Messages.Message(IndustryText.Get("GrechkaIndustry_a037b8b54564", "The upgrade blueprint was not created. The uninstalled device has been preserved for reinstallation."),
                    MessageTypeDefOf.RejectInput);
            }
        }
    }
}
