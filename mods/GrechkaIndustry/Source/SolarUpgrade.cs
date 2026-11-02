using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grechka.Industry
{
    public sealed class CompProperties_SolarUpgrade : CompProperties
    {
        public string targetDefName;
        public string costDescription;
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
            if (targetDef == null) yield break;

            yield return new Command_Action
            {
                defaultLabel = "Улучшить до " + targetDef.label,
                defaultDesc = "Снимет это устройство целиком и поставит чертёж следующей ступени на том же месте. Стоимость: "
                    + Props.costDescription + ". Пока строится новое устройство, старое работать не будет.",
                icon = TexCommand.Install,
                action = StartUpgrade
            };
        }

        private void StartUpgrade()
        {
            Building oldPanel = parent as Building;
            ThingDef targetDef = DefDatabase<ThingDef>.GetNamedSilentFail(Props.targetDefName);
            if (oldPanel == null || !oldPanel.Spawned || targetDef == null) return;

            Map map = oldPanel.Map;
            IntVec3 position = oldPanel.Position;
            Rot4 rotation = oldPanel.Rotation;
            Faction faction = oldPanel.Faction;
            AcceptanceReport placement = GenConstruct.CanPlaceBlueprintAt(
                targetDef, position, rotation, map, false, oldPanel);
            if (!placement.Accepted)
            {
                Messages.Message("Здесь нельзя поставить следующее устройство: " + placement.Reason,
                    MessageTypeDefOf.RejectInput);
                return;
            }

            MinifiedThing packed = MinifyUtility.Uninstall(oldPanel);
            if (packed == null)
            {
                Messages.Message("Не удалось снять устройство целиком.", MessageTypeDefOf.RejectInput);
                return;
            }

            try
            {
                Blueprint_Build blueprint = GenConstruct.PlaceBlueprintForBuild(
                    targetDef, position, map, rotation, faction, null);
                if (blueprint == null) throw new InvalidOperationException("Чертёж " + targetDef.defName + " не был создан.");
            }
            catch (Exception error)
            {
                Log.Error("[Grechka Industry] Не удалось поставить чертёж улучшения панели: " + error);
                try { GenConstruct.PlaceBlueprintForInstall(packed, position, map, rotation, faction); }
                catch (Exception restoreError)
                {
                    Log.Error("[Grechka Industry] Не удалось поставить чертёж возврата S0: " + restoreError);
                }
                Messages.Message("Чертёж улучшения не создан. Снятое устройство сохранено для повторной установки.",
                    MessageTypeDefOf.RejectInput);
            }
        }
    }
}
