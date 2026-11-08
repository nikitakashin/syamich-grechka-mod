using System.Linq;
using Verse;

namespace Grechka.Quests
{
    // This is an instant map designation, not a construction blueprint or a building.
    public sealed class Designator_RewardMarker : Designator
    {
        public Designator_RewardMarker()
        {
            defaultLabel = QuestText.Get("GrechkaQuests_45eaba2d918e", "Место выдачи наград");
            defaultDesc = QuestText.Get("GrechkaQuests_44f9ab839479", "Указать место прибытия квестовых наград. Новая метка перемещает предыдущую на этой карте.");
            icon = ContentFinder<UnityEngine.Texture2D>.Get("Designations/Haul");
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 cell)
        {
            if (Map == null || !Map.IsPlayerHome) return QuestText.Get("GrechkaQuests_a468f89a4520", "Метка доступна на карте колонии.");
            if (!cell.InBounds(Map)) return false;
            return true;
        }

        public override void DesignateSingleCell(IntVec3 cell)
        {
            var def = DefDatabase<DesignationDef>.GetNamed("Grechka_RewardMarker");
            foreach (Designation old in Map.designationManager.AllDesignations.Where(d => d.def == def).ToList())
                Map.designationManager.RemoveDesignation(old);
            Map.designationManager.AddDesignation(new Designation(cell, def));
        }
    }
}
