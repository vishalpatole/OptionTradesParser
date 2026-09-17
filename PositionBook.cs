using System;
using System.Collections.Concurrent;
using System.Threading;

namespace OptionTradesParser
{
    /// Holds the latest reqPositions snapshot returned by IBKR, keyed by contract id.
    public class PositionBook
    {
        private readonly ConcurrentDictionary<int, decimal> _byConId = new();
        private readonly ManualResetEventSlim _snapshotReady = new(false);

        public void BeginSnapshot()
        {
            _snapshotReady.Reset();
            _byConId.Clear();
        }

        public void Record(int conId, decimal quantity) => _byConId[conId] = quantity;

        public void CompleteSnapshot() => _snapshotReady.Set();

        public bool WaitForSnapshot(TimeSpan timeout) => _snapshotReady.Wait(timeout);

        public decimal GetQuantity(int conId) => _byConId.TryGetValue(conId, out decimal qty) ? qty : 0m;
    }
}
