using System;

namespace Grechka.Industry
{
    public static class MatterEconomy
    {
        private static bool Positive(double value) => double.IsFinite(value) && value > 0;
        public static int SynthesisCost(double value, double matterPerValue)
        {
            if (!Positive(value) || !Positive(matterPerValue)) return 0;
            double result = Math.Ceiling(value * matterPerValue);
            if (!double.IsFinite(result) || result <= 0 || result > 100000000) return 0;
            return (int)result;
        }
        public static int RecycleYield(double value, double matterPerValue, double efficiency)
        {
            if (!Positive(value) || !Positive(matterPerValue) || !Positive(efficiency)) return 0;
            double result = Math.Floor(value * matterPerValue * efficiency);
            if (!double.IsFinite(result) || result <= 0 || result > 100000000) return 0;
            return (int)result;
        }
        public static float Work(double workPerUnit, double energyPerUnit, double units, double power, double speed)
        {
            if (!Positive(workPerUnit) || !Positive(energyPerUnit) || !Positive(units) || !Positive(power) || !Positive(speed)) return float.MaxValue;
            // One in-game real second has 60 simulation ticks. A bill must satisfy
            // both the work budget and the joule budget at its fixed machine draw.
            double required = Math.Max(workPerUnit * units, energyPerUnit * units / power * 60 * speed);
            if (!Positive(required) || required >= float.MaxValue) return float.MaxValue;
            float result = (float)required;
            // Never truncate an electrical budget merely to fit an arbitrary cap.
            return result < required ? MathF.BitIncrement(result) : result;
        }

        public static int RecycleBatch(double value, double factor, double efficiency, double discount)
        {
            if (!Positive(value) || !Positive(factor) || !Positive(efficiency)
                || !double.IsFinite(discount) || discount < 0 || discount >= 1) return 0;
            double count = Math.Max(Math.Min(100, Math.Max(1, Math.Ceiling(20 / value))),
                Math.Ceiling(Math.Ceiling(1 / (value * factor * efficiency)) / (1 - discount)));
            if (!double.IsFinite(count) || count > 100000000) return 0;
            int batch = (int)count;
            // Confirm the minimum yield after the real physical-unit rounding.
            for (int retry = 0; retry < 2; retry++)
            {
                if (RecycleYield(value * Math.Ceiling(batch * (1 - discount)), factor, efficiency) > 0) return batch;
                if (batch == 100000000) return 0;
                batch++;
            }
            return 0;
        }
    }
}
