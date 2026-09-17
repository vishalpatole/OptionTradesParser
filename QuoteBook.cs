using System;
using System.Collections.Concurrent;
using System.Threading;

namespace OptionTradesParser
{
    /// Collects a one-shot NBBO snapshot from IBKR so the limit price can be judged against the live market.
    public class QuoteBook
    {
        public sealed class Quote
        {
            public double Bid = -1;
            public double Ask = -1;
            public double Last = -1;
            public bool Delayed;

            public bool HasMarket => Bid > 0 || Ask > 0;
        }

        private sealed class PendingQuote
        {
            public readonly Quote Value = new();
            public readonly ManualResetEventSlim Done = new(false);
        }

        private readonly ConcurrentDictionary<int, PendingQuote> _pending = new();

        // Kept in its own band so ticker ids never collide with order or contract request ids.
        private int _nextRequestId = 8_000_000;

        public int OpenRequest()
        {
            int reqId = Interlocked.Increment(ref _nextRequestId);
            _pending[reqId] = new PendingQuote();
            return reqId;
        }

        public void Record(int reqId, int field, double price)
        {
            if (price <= 0 || !_pending.TryGetValue(reqId, out var pending)) return;

            switch (field)
            {
                case 1 or 66: pending.Value.Bid = price; break;   // BID / DELAYED_BID
                case 2 or 67: pending.Value.Ask = price; break;   // ASK / DELAYED_ASK
                case 4 or 68: pending.Value.Last = price; break;  // LAST / DELAYED_LAST
            }

            // Streaming requests have no end-of-snapshot marker, so a complete two-sided quote ends the wait.
            if (pending.Value.Bid > 0 && pending.Value.Ask > 0) pending.Done.Set();
        }

        public void MarkDelayed(int reqId, bool delayed)
        {
            if (_pending.TryGetValue(reqId, out var pending)) pending.Value.Delayed = delayed;
        }

        public void Complete(int reqId)
        {
            if (_pending.TryGetValue(reqId, out var pending)) pending.Done.Set();
        }

        public Quote? WaitForQuote(int reqId, TimeSpan timeout)
        {
            if (!_pending.TryGetValue(reqId, out var pending)) return null;

            pending.Done.Wait(timeout);
            _pending.TryRemove(reqId, out _);

            return pending.Value;
        }
    }
}
