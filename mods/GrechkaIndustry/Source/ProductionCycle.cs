using System;

namespace Grechka.Industry
{
    // Game-independent state: a completed batch waits until every unit is placed.
    public sealed class ProductionCycle
    {
        public int WorkedTicks;
        public int PendingCount;

        public void Advance(bool powered, int ticks, int cycleTicks, int batchCount)
        {
            if (ticks < 0 || cycleTicks <= 0 || batchCount <= 0) throw new ArgumentOutOfRangeException();
            if (!powered || PendingCount > 0 || ticks == 0) return;
            WorkedTicks = (int)Math.Min(cycleTicks, (long)WorkedTicks + ticks);
            if (WorkedTicks < cycleTicks) return;
            WorkedTicks = 0;
            PendingCount = batchCount;
        }

        public void RecordDelivery(int delivered)
        {
            if (delivered < 0 || delivered > PendingCount) throw new ArgumentOutOfRangeException(nameof(delivered));
            PendingCount -= delivered;
        }
    }
}
