using RimWorld;
using Verse;

namespace Grechka.Industry
{
    public sealed class Building_RoomCooler : Building_TempControl
    {
        public override void TickRare()
        {
            if (!Spawned) return;
            base.TickRare();
            compTempControl.operatingAtHighPower = false;
            if (!compPowerTrader.PowerOn || this.IsBrokenDown() || !FlickUtility.WantsToBeOn(this)) return;

            float energy = compTempControl.Props.energyPerSecond * GenTicks.TickRareInterval / 60f;
            float change = GenTemperature.ControlTemperatureTempChange(Position, Map, energy, compTempControl.TargetTemperature);
            bool cooling = change < 0f;
            if (cooling) this.GetRoom().Temperature += change;
            compTempControl.operatingAtHighPower = cooling;
            compPowerTrader.PowerOutput = -compPowerTrader.Props.PowerConsumption
                * (cooling ? 1f : compTempControl.Props.lowPowerConsumptionFactor);
        }
    }
}
