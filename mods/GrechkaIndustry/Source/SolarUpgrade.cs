using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grechka.Industry
{
    public sealed class CompProperties_SolarUpgrade : CompProperties
    {
        public CompProperties_SolarUpgrade() { compClass = typeof(CompSolarUpgrade); }
    }

    // The old building is packaged whole. The normal S1 construction blueprint
    // then consumes that package along with the new parts and builder work.
    public sealed class CompSolarUpgrade : ThingComp
    {
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            Building panel = parent as Building;
            if (panel == null || !panel.Spawned || panel.Faction != Faction.OfPlayer) yield break;

            yield return new Command_Action
            {
                defaultLabel = "Улучшить до усиленной панели",
                defaultDesc = "Снимет эту панель целиком и поставит чертёж S1 на том же месте. Строитель использует снятую панель, корпус T2, 4 продвинутых компонента и 8 фотоэлементов. Пока строится новая панель, электричества от старой не будет.",
                icon = TexCommand.Install,
                action = StartUpgrade
            };
        }

        private void StartUpgrade()
        {
            Building oldPanel = parent as Building;
            ThingDef targetDef = DefDatabase<ThingDef>.GetNamedSilentFail("Grechka_SolarS1");
            if (oldPanel == null || !oldPanel.Spawned || targetDef == null) return;

            Map map = oldPanel.Map;
            IntVec3 position = oldPanel.Position;
            Rot4 rotation = oldPanel.Rotation;
            Faction faction = oldPanel.Faction;
            AcceptanceReport placement = GenConstruct.CanPlaceBlueprintAt(
                targetDef, position, rotation, map, false, oldPanel);
            if (!placement.Accepted)
            {
                Messages.Message("Здесь нельзя поставить улучшенную панель: " + placement.Reason,
                    MessageTypeDefOf.RejectInput);
                return;
            }

            MinifiedThing packed = MinifyUtility.Uninstall(oldPanel);
            if (packed == null)
            {
                Messages.Message("Не удалось снять солнечную панель целиком.", MessageTypeDefOf.RejectInput);
                return;
            }

            try
            {
                Blueprint_Build blueprint = GenConstruct.PlaceBlueprintForBuild(
                    targetDef, position, map, rotation, faction, null);
                if (blueprint == null) throw new InvalidOperationException("Чертёж S1 не был создан.");
            }
            catch (Exception error)
            {
                Log.Error("[Grechka Industry] Не удалось поставить чертёж улучшения панели: " + error);
                try { GenConstruct.PlaceBlueprintForInstall(packed, position, map, rotation, faction); }
                catch (Exception restoreError)
                {
                    Log.Error("[Grechka Industry] Не удалось поставить чертёж возврата S0: " + restoreError);
                }
                Messages.Message("Чертёж S1 не создан. Снятая панель сохранена для повторной установки.",
                    MessageTypeDefOf.RejectInput);
            }
        }
    }
}
