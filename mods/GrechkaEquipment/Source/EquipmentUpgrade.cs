using Verse;

namespace Grechka.Equipment
{
    // Marks a one-for-one conversion so Industry/World Integration do not treat
    // the already paid item as fresh raw materials for bonus production.
    public sealed class RecipeWorker_EquipmentUpgrade : RecipeWorker { }
}
