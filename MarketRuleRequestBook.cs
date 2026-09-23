using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using IBApi;

namespace OptionTradesParser
{
    public sealed class MarketRuleRequestBook
    {
        private sealed class PendingRequest
        {
            public PriceIncrement[] Increments { get; set; } = Array.Empty<PriceIncrement>();
            public ManualResetEventSlim Done { get; } = new(false);
        }

        private readonly ConcurrentDictionary<int, PendingRequest> _pending = new();

        public void OpenRequest(int marketRuleId) => _pending[marketRuleId] = new PendingRequest();

        public void Complete(int marketRuleId, PriceIncrement[] increments)
        {
            if (_pending.TryGetValue(marketRuleId, out PendingRequest? request))
            {
                request.Increments = increments;
                request.Done.Set();
            }
        }

        public void Complete(int marketRuleId)
        {
            if (_pending.TryGetValue(marketRuleId, out PendingRequest? request)) request.Done.Set();
        }

        public IReadOnlyList<PriceIncrementRule> WaitForResults(int marketRuleId, TimeSpan timeout)
        {
            if (!_pending.TryGetValue(marketRuleId, out PendingRequest? request)) return Array.Empty<PriceIncrementRule>();

            request.Done.Wait(timeout);
            _pending.TryRemove(marketRuleId, out _);
            return request.Increments
                .Where(item => item.Increment > 0)
                .Select(item => new PriceIncrementRule(item.LowEdge, item.Increment))
                .OrderBy(item => item.LowEdge)
                .ToArray();
        }
    }
}