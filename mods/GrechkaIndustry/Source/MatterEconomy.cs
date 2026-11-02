using System;

namespace Grechka.Industry
{
    public static class MatterEconomy
    {
        public static int SynthesisCost(double value, double matterPerValue)
        {
            double result = Math.Ceiling(value * matterPerValue);
            if (!double.IsFinite(result) || result <= 0 || result > 100000000) return 0;
            return (int)result;
        }
        public static int RecycleYield(double value, double matterPerValue, double efficiency)
        {
            double result = Math.Floor(value * matterPerValue * efficiency);
            if (!double.IsFinite(result) || result <= 0 || result > 100000000) return 0;
            return (int)result;
        }
        public static float Work(double workPerUnit, double energyPerUnit, double units, double power, double speed)
        {
            if (units <= 0 || power <= 0 || speed <= 0) return float.MaxValue;
            // One in-game real second has 60 simulation ticks. A bill must satisfy
            // both the work budget and the joule budget at its fixed machine draw.
            return (float)Math.Min(1e12, Math.Max(workPerUnit * units, energyPerUnit * units / power * 60 * speed));
        }
    }
}
