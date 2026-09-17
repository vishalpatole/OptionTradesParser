using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using IBApi;

namespace OptionTradesParser
{
    /// Correlates asynchronous reqContractDetails responses back to the thread waiting on them.
    public class ContractRequestBook
    {
        private sealed class PendingRequest
        {
            public readonly List<ContractDetails> Results = new();
            public readonly ManualResetEventSlim Done = new(false);
        }

        private readonly ConcurrentDictionary<int, PendingRequest> _pending = new();

        // Kept far above the TWS order id range so request ids can never collide with order ids.
        private int _nextRequestId = 9_000_000;

        public int OpenRequest()
        {
            int reqId = Interlocked.Increment(ref _nextRequestId);
            _pending[reqId] = new PendingRequest();
            return reqId;
        }

        public void Add(int reqId, ContractDetails details)
        {
            if (_pending.TryGetValue(reqId, out var request))
            {
                lock (request.Results) request.Results.Add(details);
            }
        }

        public void Complete(int reqId)
        {
            if (_pending.TryGetValue(reqId, out var request)) request.Done.Set();
        }

        public IReadOnlyList<ContractDetails> WaitForResults(int reqId, TimeSpan timeout)
        {
            if (!_pending.TryGetValue(reqId, out var request)) return Array.Empty<ContractDetails>();

            request.Done.Wait(timeout);
            _pending.TryRemove(reqId, out _);

            lock (request.Results) return request.Results.ToArray();
        }
    }
}
