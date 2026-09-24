using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using IBApi; 

namespace OptionTradesParser
{
    public class OrderExecutionService : DefaultEWrapper
    {
        private static readonly TimeSpan BrokerReplyTimeout = TimeSpan.FromSeconds(10);

        // Escalating backoff: TWS often just needs a few seconds after a forced close before it accepts again,
        // but a restart can take minutes, so later attempts wait longer.
        private static readonly TimeSpan[] ReconnectDelays =
            { TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2) };

        // The quote is advisory only, so it must never delay a time-sensitive confirmation prompt for long.
        private static readonly TimeSpan QuoteTimeout = TimeSpan.FromSeconds(3);

        private static readonly TimeSpan DashboardProbeTimeout = TimeSpan.FromMilliseconds(300);
        private static readonly TimeSpan OrderStatusRefreshInterval = TimeSpan.FromSeconds(15);
        private const string DashboardUiHost = "127.0.0.1";
        private const int DashboardUiPort = 5173;

        private readonly EClientSocket _clientSocket;
        private readonly EReaderSignal _readerSignal;
        private readonly string _host;
        private readonly int _port;
        private int _currentOrderId = 0;
        private int _clientId; // 🔥 Tracker handle
        private readonly int _clientIdMin;
        private readonly int _clientIdMax;
        private readonly IReadOnlyList<double> _orderBudgets;
        private readonly string _accountType;
        private readonly TimeSpan _browserApprovalTimeout;
        private int _reconnectLoopActive;
        private DatabaseService? _dbService;
        private ulong _activeDiscordMessageId = 0;
        private readonly ManualResetEventSlim _orderIdReady = new(false);
        private readonly ContractRequestBook _contractRequests = new();
        private readonly MarketRuleRequestBook _marketRuleRequests = new();
        private readonly PositionBook _positions = new();
        private readonly QuoteBook _quotes = new();
        private readonly ConcurrentDictionary<int, PendingEntryOrder> _pendingEntryOrders = new();
        private readonly ConcurrentDictionary<int, ActiveTakeProfitOrder> _activeTakeProfitOrders = new();
        private readonly ConcurrentDictionary<int, ulong> _orderMessageIds = new();
        private readonly ConcurrentDictionary<int, decimal> _executionFilledQuantities = new();
        private readonly ConcurrentDictionary<string, decimal> _latestOptionPositions = new();
        private readonly ConcurrentDictionary<int, DatabaseService.ManualExitPriceCandidate> _manualExitExecutionRequests = new();
        private readonly ConcurrentDictionary<string, DatabaseService.ManualExitPriceCandidate> _manualExitPriceLookups = new();
        private readonly CancellationTokenSource _shutdown = new();
        private int _orderStatusMonitorStarted;
        private int _statusRequestId = 900000;
        private int _plannedDisconnect;
        private Thread? _readerThread;

        public bool IsReady => _clientSocket.IsConnected() && _orderIdReady.IsSet;

        // Serializes the console confirmation prompt so concurrent alerts cannot steal each other's keystrokes.
        private readonly object _promptGate = new();

        private sealed class PendingEntryOrder
        {
            public PendingEntryOrder(Contract contract, ParsedTrade trade, int quantity, ulong discordMessageId, IReadOnlyList<PriceIncrementRule> priceRules)
            {
                Contract = contract;
                Trade = trade;
                Quantity = quantity;
                DiscordMessageId = discordMessageId;
                PriceRules = priceRules;
            }

            public Contract Contract { get; }
            public ParsedTrade Trade { get; }
            public int Quantity { get; }
            public ulong DiscordMessageId { get; }
            public IReadOnlyList<PriceIncrementRule> PriceRules { get; }
            public object SyncRoot { get; } = new();
            public int LastEntryFilled { get; set; }
            public double LastAverageFillPrice { get; set; }
            public Dictionary<int, TargetLevelState> Targets { get; } = new();
        }

        private sealed class TargetLevelState
        {
            public int CompletedQuantity { get; set; }
            public int? WorkingOrderId { get; set; }
            public int WorkingOrderFilled { get; set; }
            public int SubmittedQuantity { get; set; }
            public double SubmittedPrice { get; set; }
        }

        private sealed record ActiveTakeProfitOrder(int ContractId, int ParentEntryOrderId, int Level, ManualResetEventSlim Completed);

        private sealed record ResolvedContract(Contract Contract, IReadOnlyList<PriceIncrementRule> PriceRules);

        public OrderExecutionService(string host, int port, int clientId, int clientIdMin, int clientIdMax, IReadOnlyList<double> orderBudgets, string accountType, TimeSpan browserApprovalTimeout)
        {
            _readerSignal = new EReaderMonitorSignal();
            _clientSocket = new EClientSocket(this, _readerSignal);
            _host = host;
            _port = port;
            _clientId = clientId;
            _clientIdMin = clientIdMin;
            _clientIdMax = clientIdMax;
            _orderBudgets = orderBudgets;
            _accountType = string.IsNullOrWhiteSpace(accountType) ? "PAPER" : accountType.Trim().ToUpperInvariant();
            _browserApprovalTimeout = browserApprovalTimeout;

            ConnectAndWaitForHandshake("initial connection");
        }

        public async System.Threading.Tasks.Task<bool> WaitUntilReadyAsync()
        {
            if (IsReady) return true;
            if (Interlocked.CompareExchange(ref _reconnectLoopActive, 1, 0) != 0) return false;

            return await ReconnectAsync();
        }

        private bool ConnectAndWaitForHandshake(string attemptDescription)
        {
            bool socketOpened = Connect();
            if (!socketOpened) return false;

            if (_orderIdReady.Wait(BrokerReplyTimeout) && _clientSocket.IsConnected())
            {
                // Falls back to delayed quotes when the account has no live option data subscription.
                _clientSocket.reqMarketDataType(4);
                StartOrderStatusMonitor();
                return true;
            }

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"❌ [IBKR] {attemptDescription} opened a socket but did not receive nextValidId within {BrokerReplyTimeout.TotalSeconds:0}s.");
            Console.ResetColor();

            if (_clientSocket.IsConnected()) _clientSocket.eDisconnect();
            return false;
        }

        private bool Connect()
        {
            // TWS can force-close a socket that still looks "connected" to this client; disconnecting first
            // guarantees a clean slate instead of retrying eConnect against stale internal state.
            if (_clientSocket.IsConnected())
            {
                Interlocked.Exchange(ref _plannedDisconnect, 1);
                _clientSocket.eDisconnect();
                Interlocked.Exchange(ref _plannedDisconnect, 0);
            }

            // Two reader threads racing on the same shared signal (old + new generation) can desync the
            // protocol stream and looks exactly like TWS forcibly resetting the connection. Only one may run.
            if (_readerThread is { IsAlive: true } stale && !stale.Join(TimeSpan.FromSeconds(2)))
            {
                Console.WriteLine("⚠️ [IBKR] Previous reader thread did not exit in time; proceeding anyway.");
            }

            _orderIdReady.Reset();
            Console.WriteLine($"🔌 [IBKR] Connecting to TWS Gateway at {_host}:{_port} with ClientId {_clientId}...");
            _clientSocket.eConnect(_host, _port, _clientId);

            if (_clientSocket.IsConnected())
            {
                Console.WriteLine("🔌 [IBKR] Socket opened; awaiting nextValidId handshake...");

                var reader = new EReader(_clientSocket, _readerSignal);
                reader.Start();
                var thread = new Thread(() => {
                    while (_clientSocket.IsConnected()) {
                        _readerSignal.waitForSignal();
                        reader.processMsgs();
                    }
                }) { IsBackground = true };
                _readerThread = thread;
                thread.Start();

                return true;
            }

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"❌ [IBKR] Could not connect to {_host}:{_port} (clientId {_clientId}). Check TWS API settings: 'Enable ActiveX and Socket Clients' on, 'Read-Only API' OFF, port matches, and clientId is not already in use.");
            Console.ResetColor();
            return false;
        }

        public void SetDatabaseService(DatabaseService dbService)
        {
            _dbService = dbService;
        }

        /// Cleanly logs off the TWS API session; call on process exit so a crashed/killed run doesn't leave a
        /// ghost session that can count against TWS's connected-client limit and cause future connects to reset.
        public void Disconnect()
        {
            _shutdown.Cancel();
            Interlocked.Exchange(ref _plannedDisconnect, 1);
            if (_clientSocket.IsConnected()) _clientSocket.eDisconnect();
            if (_readerThread is { IsAlive: true } reader && !reader.Join(TimeSpan.FromSeconds(2)))
            {
                Console.WriteLine("⚠️ [IBKR] Reader thread did not exit within 2s during shutdown.");
            }
        }

        private void StartOrderStatusMonitor()
        {
            if (Interlocked.CompareExchange(ref _orderStatusMonitorStarted, 1, 0) != 0) return;

            var thread = new Thread(MonitorOrderStatuses) { IsBackground = true };
            thread.Start();
        }

        private void MonitorOrderStatuses()
        {
            while (!_shutdown.IsCancellationRequested)
            {
                try
                {
                    if (_clientSocket.IsConnected() && _orderIdReady.IsSet)
                    {
                        int executionRequestId = Interlocked.Increment(ref _statusRequestId);
                        ReconciliationLog.Write($"[IBKR STATUS SYNC] Requesting open/completed orders, executions reqId {executionRequestId}, and positions...");
                        _clientSocket.reqAllOpenOrders();
                        _clientSocket.reqCompletedOrders(apiOnly: false);
                        _clientSocket.reqExecutions(executionRequestId, new ExecutionFilter());
                        _latestOptionPositions.Clear();
                        _clientSocket.reqPositions();
                        RequestMissingManualExitPrices();
                    }
                }
                catch (Exception ex)
                {
                    ReconciliationLog.Write($"[IBKR STATUS SYNC] Refresh failed ({ex.GetType().Name}: {ex.Message}).");
                }

                _shutdown.Token.WaitHandle.WaitOne(OrderStatusRefreshInterval);
            }
        }

        private void RequestMissingManualExitPrices()
        {
            IReadOnlyList<DatabaseService.ManualExitPriceCandidate> candidates = _dbService?.GetManualExitsMissingPrice()
                ?? Array.Empty<DatabaseService.ManualExitPriceCandidate>();
            if (candidates.Count == 0) return;

            ReconciliationLog.Write($"[MANUAL EXIT RECON] {candidates.Count} manual exit(s) missing price. Requesting contract-level IBKR executions...");
            foreach (DatabaseService.ManualExitPriceCandidate candidate in candidates)
            {
                string contractKey = DatabaseService.FormatOptionPositionKey(candidate.Ticker, candidate.OptionType, candidate.Strike, candidate.Expiration);
                _manualExitPriceLookups[contractKey] = candidate;

                int reqId = Interlocked.Increment(ref _statusRequestId);
                _manualExitExecutionRequests[reqId] = candidate;
                var filter = new ExecutionFilter
                {
                    Symbol = candidate.Ticker,
                    SecType = "OPT"
                };

                ReconciliationLog.Write($"[MANUAL EXIT RECON] reqId {reqId}: looking for SELL execution matching {candidate.Ticker} {candidate.Expiration} {candidate.Strike}{(candidate.OptionType == "CALL" ? "C" : "P")} qty {candidate.Quantity}.");
                _clientSocket.reqExecutions(reqId, filter);
            }
        }

        public override void nextValidId(int orderId)
        {
            _currentOrderId = orderId;
            _orderIdReady.Set();
            Console.WriteLine($"✅ [IBKR READY] Trading session synchronized. ClientId {_clientId}; next order ID #{_currentOrderId}.");
        }

        public override void managedAccounts(string accountsList)
        {
            Console.WriteLine($"🏦 [IBKR ACCOUNTS] Linked account(s): {accountsList}");
        }

        public override void error(int id, long errorTime, int errorCode, string errorMsg, string advancedOrderRejectJson)
        {
            // 2104/2106/2107/2158/2119 are data-farm status notices, not failures.
            bool informational = errorCode is 2104 or 2106 or 2107 or 2158 or 2119;

            string errorLine = $"{(informational ? "i" : "X")} [IBKR {(informational ? "INFO" : "ERROR")}] id={id} code={errorCode} :: {errorMsg}";
            if (informational || errorCode is 202)
            {
                ReconciliationLog.Write(errorLine);
                if (!string.IsNullOrWhiteSpace(advancedOrderRejectJson)) ReconciliationLog.Write($"   └── Reject detail: {advancedOrderRejectJson}");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(errorLine);
                if (!string.IsNullOrWhiteSpace(advancedOrderRejectJson))
                {
                    Console.WriteLine($"   └── Reject detail: {advancedOrderRejectJson}");
                }
                Console.ResetColor();
            }

            if (!informational)
            {
                _dbService?.LogAudit(_activeDiscordMessageId, "-", "IBKR_API_ERROR", errorCode.ToString(), $"id={id} :: {errorMsg}");
            }

            if (id > 0 && IsOrderRejection(errorCode, advancedOrderRejectJson)
                && _activeTakeProfitOrders.TryRemove(id, out ActiveTakeProfitOrder? rejectedTarget))
            {
                if (_pendingEntryOrders.TryGetValue(rejectedTarget.ParentEntryOrderId, out PendingEntryOrder? entry))
                {
                    lock (entry.SyncRoot)
                    {
                        TargetLevelState levelState = GetTargetState(entry, rejectedTarget.Level);
                        if (levelState.WorkingOrderId == id)
                        {
                            levelState.CompletedQuantity += levelState.WorkingOrderFilled;
                            levelState.WorkingOrderId = null;
                            levelState.WorkingOrderFilled = 0;
                            levelState.SubmittedQuantity = 0;
                        }
                    }

                    _dbService?.UpdateOrderStatus(id, _clientId, "REJECTED");
                    _dbService?.LogAudit(entry.DiscordMessageId, entry.Trade.Ticker, "TAKE_PROFIT_ROUTING", "REJECTED",
                        $"TP{rejectedTarget.Level} order #{id} rejected by IBKR ({errorCode}): {errorMsg}");
                }

                rejectedTarget.Completed.Set();
            }

            // Releases any caller blocked on a contract lookup that TWS just rejected (e.g. code 200).
            if (id > 0)
            {
                _contractRequests.Complete(id);
                _marketRuleRequests.Complete(id);
                _quotes.Complete(id);
            }
        }

        public override void error(string str) => ReconciliationLog.Write($"X [IBKR ERROR] {str}");

        public override void error(Exception e) => ReconciliationLog.Write($"X [IBKR EXCEPTION] {e.Message}");

        private static bool IsOrderRejection(int errorCode, string advancedOrderRejectJson)
            => errorCode is 110 or 201 or 399 or 463 || !string.IsNullOrWhiteSpace(advancedOrderRejectJson);

        public override void openOrder(int orderId, Contract contract, Order order, OrderState orderState)
        {
            bool tracked = UpdateTrackedOrderStatus(orderId, order.ClientId, orderState.Status, "OPEN_ORDER_SNAPSHOT");
            if (tracked)
            {
                ReconciliationLog.Write($"[IBKR ACK] Order #{orderId} {order.Action} {order.TotalQuantity} {contract.LocalSymbol ?? contract.Symbol} :: state={orderState.Status}");
            }
        }

        public override void completedOrder(Contract contract, Order order, OrderState orderState)
        {
            if (UpdateTrackedOrderStatus(order.OrderId, order.ClientId, orderState.Status, "COMPLETED_ORDER_SNAPSHOT"))
            {
                string symbol = contract.LocalSymbol ?? contract.Symbol ?? "UNKNOWN";
                ReconciliationLog.Write($"[IBKR COMPLETED] Order #{order.OrderId} {order.Action} {order.TotalQuantity} {symbol} :: state={orderState.Status}");
            }
        }

        public override void execDetails(int reqId, Contract contract, Execution execution)
        {
            if (_manualExitExecutionRequests.TryGetValue(reqId, out DatabaseService.ManualExitPriceCandidate? lookup))
            {
                ReconciliationLog.Write($"[MANUAL EXIT RECON] reqId {reqId}: received execution {execution.ExecId} order #{execution.OrderId} {execution.Side} {execution.Shares} @ ${execution.Price:F2} for {contract.Symbol} {contract.LastTradeDateOrContractMonth} {contract.Strike}{contract.Right}.");
                if (IsManualExitMatch(lookup, contract, execution))
                {
                    ImportExternalExecution(contract, execution);
                    return;
                }
            }

            string executionAction = execution.Side.Equals("BOT", StringComparison.OrdinalIgnoreCase) ? "BUY" : "SELL";
            if (executionAction == "SELL" && TryGetManualExitLookup(contract, out _))
            {
                ImportExternalExecution(contract, execution);
                return;
            }

            DatabaseService.TrackedOrderContext? trackedOrder = _dbService?.FindTrackedOrder(execution.OrderId, execution.ClientId);
            if (!_orderMessageIds.TryGetValue(execution.OrderId, out ulong messageId))
            {
                if (trackedOrder == null)
                {
                    ImportExternalExecution(contract, execution);
                    return;
                }
                messageId = trackedOrder.DiscordMessageId;
                _orderMessageIds[execution.OrderId] = messageId;
            }

            DateTimeOffset executedAt = ParseBrokerExecutionTime(execution.Time);
            bool newlyLogged = _dbService?.LogExecution(execution.ExecId, execution.OrderId, execution.ClientId, messageId,
                executionAction, execution.Shares, execution.Price, executedAt) == true;

            if (!newlyLogged || executionAction != "BUY") return;

            PendingEntryOrder? entry = GetOrRestorePendingEntry(execution.OrderId, contract, trackedOrder);
            if (entry == null) return;

            decimal filled = _executionFilledQuantities.AddOrUpdate(
                execution.OrderId,
                execution.Shares,
                (_, existing) => Math.Min(entry.Quantity, existing + execution.Shares));
            UpdateTrackedOrderStatus(execution.OrderId, execution.ClientId,
                filled >= entry.Quantity ? "FILLED" : "PARTIALLY_FILLED", "EXECUTION_CALLBACK");
            ReconcileTakeProfitOrders(execution.OrderId, entry, (int)filled, execution.Price);
        }

        private static bool IsManualExitMatch(DatabaseService.ManualExitPriceCandidate lookup, Contract contract, Execution execution)
        {
            if (!execution.Side.Equals("SLD", StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(contract.Symbol, lookup.Ticker, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(contract.LastTradeDateOrContractMonth, lookup.Expiration, StringComparison.OrdinalIgnoreCase)) return false;
            if (Math.Abs(contract.Strike - lookup.Strike) > 0.0001) return false;

            string optionType = contract.Right?.Equals("P", StringComparison.OrdinalIgnoreCase) == true ? "PUT" : "CALL";
            return string.Equals(optionType, lookup.OptionType, StringComparison.OrdinalIgnoreCase);
        }

        private bool TryGetManualExitLookup(Contract contract, out DatabaseService.ManualExitPriceCandidate? lookup)
        {
            lookup = null;
            if (string.IsNullOrWhiteSpace(contract.Symbol) || string.IsNullOrWhiteSpace(contract.LastTradeDateOrContractMonth) || contract.Strike <= 0)
            {
                return false;
            }

            string optionType = contract.Right?.Equals("P", StringComparison.OrdinalIgnoreCase) == true ? "PUT" : "CALL";
            string key = DatabaseService.FormatOptionPositionKey(contract.Symbol, optionType, contract.Strike, contract.LastTradeDateOrContractMonth);
            return _manualExitPriceLookups.TryGetValue(key, out lookup);
        }

        private void ImportExternalExecution(Contract contract, Execution execution)
        {
            string action = execution.Side.Equals("BOT", StringComparison.OrdinalIgnoreCase) ? "BUY" : "SELL";
            if (action != "SELL") return;

            string ticker = contract.Symbol?.Trim().ToUpperInvariant() ?? string.Empty;
            string optionType = contract.Right?.Equals("P", StringComparison.OrdinalIgnoreCase) == true ? "PUT" : "CALL";
            string expiration = contract.LastTradeDateOrContractMonth?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(ticker) || string.IsNullOrWhiteSpace(expiration) || contract.Strike <= 0)
            {
                ReconciliationLog.Write($"[OMS STATUS SYNC] Ignored external SELL execution {execution.ExecId}: missing option contract identity.");
                return;
            }

            DateTimeOffset executedAt = ParseBrokerExecutionTime(execution.Time);
            if (_dbService?.TryImportExternalSellExecution(execution.ExecId, execution.OrderId, execution.ClientId,
                ticker, optionType, contract.Strike, expiration, execution.Shares, execution.Price, executedAt) == true)
            {
                ReconciliationLog.Write($"[OMS STATUS SYNC] Imported/backfilled manual TWS close for {ticker} {expiration} {contract.Strike}{contract.Right}: SELL {execution.Shares} @ ${execution.Price:F2}.");
            }
            else
            {
                ReconciliationLog.Write($"[OMS STATUS SYNC] External SELL execution {execution.ExecId} for {ticker} {expiration} {contract.Strike}{contract.Right} @ ${execution.Price:F2} did not match an open trade or missing-price manual exit.");
            }
        }

        public override void execDetailsEnd(int reqId)
        {
            if (_manualExitExecutionRequests.TryRemove(reqId, out DatabaseService.ManualExitPriceCandidate? lookup))
            {
                ReconciliationLog.Write($"[MANUAL EXIT RECON] reqId {reqId}: completed execution search for {lookup.Ticker} {lookup.Expiration} {lookup.Strike}{(lookup.OptionType == "CALL" ? "C" : "P")}.");
            }
        }

        private static DateTimeOffset ParseBrokerExecutionTime(string value)
        {
            if (DateTimeOffset.TryParse(value, out DateTimeOffset parsed)) return parsed;

            string[] parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3
                && DateTime.TryParseExact($"{parts[0]} {parts[1]}", "yyyyMMdd HH:mm:ss", null,
                    System.Globalization.DateTimeStyles.None, out DateTime localTime))
            {
                string zoneId = parts[2] switch
                {
                    "US/Eastern" => "Eastern Standard Time",
                    "US/Central" => "Central Standard Time",
                    "US/Mountain" => "Mountain Standard Time",
                    "US/Pacific" => "Pacific Standard Time",
                    _ => TimeZoneInfo.Local.Id
                };

                try
                {
                    TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
                    return new DateTimeOffset(localTime, zone.GetUtcOffset(localTime)).ToUniversalTime();
                }
                catch (TimeZoneNotFoundException)
                {
                    return new DateTimeOffset(localTime, TimeZoneInfo.Local.GetUtcOffset(localTime)).ToUniversalTime();
                }
            }

            return DateTimeOffset.UtcNow;
        }

        private PendingEntryOrder? GetOrRestorePendingEntry(int orderId, Contract contract, DatabaseService.TrackedOrderContext? trackedOrder)
        {
            if (_pendingEntryOrders.TryGetValue(orderId, out PendingEntryOrder? entry)) return entry;

            trackedOrder ??= _dbService?.FindTrackedOrder(orderId, _clientId);
            if (trackedOrder == null || !trackedOrder.OrderRole.Equals("ENTRY", StringComparison.OrdinalIgnoreCase)) return null;
            if (!trackedOrder.ActionType.Equals("BUY", StringComparison.OrdinalIgnoreCase)) return null;

            var trade = new ParsedTrade(
                trackedOrder.TraderName,
                trackedOrder.ActionType,
                trackedOrder.Ticker,
                trackedOrder.OptionType,
                trackedOrder.Strike,
                trackedOrder.Expiration,
                trackedOrder.EntryPrice,
                trackedOrder.RiskCategory,
                TrimFraction: 1,
                SizingAmbiguous: false,
                ContractInferred: false);

            entry = new PendingEntryOrder(contract, trade, trackedOrder.Quantity, trackedOrder.DiscordMessageId,
                new[] { new PriceIncrementRule(0, 0.01) });
            _pendingEntryOrders[orderId] = entry;
            return entry;
        }

        public override void commissionAndFeesReport(CommissionAndFeesReport report)
        {
            _dbService?.UpdateExecutionCommission(report.ExecId, report.CommissionAndFees, report.RealizedPNL);
        }

        public override void connectionClosed()
        {
            Console.WriteLine("🔌 [IBKR] Socket connection closed by TWS.");

            if (Volatile.Read(ref _plannedDisconnect) == 1 || _shutdown.IsCancellationRequested)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _reconnectLoopActive, 1, 0) == 0)
            {
                _ = ReconnectAsync();
            }
        }

        private async System.Threading.Tasks.Task<bool> ReconnectAsync()
        {
            int maxAttempts = ReconnectDelays.Length;
            try
            {
                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    TimeSpan delay = ReconnectDelays[attempt - 1];
                    Console.WriteLine($"🔄 [IBKR] Reconnect attempt {attempt}/{maxAttempts} starts in {delay.TotalSeconds:0}s.");
                    await System.Threading.Tasks.Task.Delay(delay);

                    RotateClientId();

                    if (ConnectAndWaitForHandshake($"Reconnect attempt {attempt}/{maxAttempts}"))
                    {
                        Console.WriteLine($"✅ [IBKR] Reconnected on attempt {attempt}/{maxAttempts}.");
                        return true;
                    }

                    Console.WriteLine($"❌ [IBKR] Reconnect attempt {attempt}/{maxAttempts} did not complete the broker handshake.");
                }

                Console.WriteLine($"❌ [IBKR] Reconnect stopped after {maxAttempts} unsuccessful attempts. If TWS just restarted, open");
                Console.WriteLine("   Global Configuration → API → Settings and click Apply once, then restart this app.");
                return false;
            }
            finally
            {
                Interlocked.Exchange(ref _reconnectLoopActive, 0);
            }
        }

        private void RotateClientId()
        {
            if (_clientIdMin <= 0 || _clientIdMax < _clientIdMin) return;

            int nextClientId = _clientId >= _clientIdMax ? _clientIdMin : _clientId + 1;
            if (nextClientId == _clientId) return;

            Console.WriteLine($"🔄 [IBKR] Rotating ClientId {_clientId} -> {nextClientId} for reconnect attempt.");
            _clientId = nextClientId;
        }

        public override void contractDetails(int reqId, ContractDetails contractDetails)
        {
            _contractRequests.Add(reqId, contractDetails);
        }

        public override void contractDetailsEnd(int reqId)
        {
            _contractRequests.Complete(reqId);
        }

        public override void marketRule(int marketRuleId, PriceIncrement[] priceIncrements)
        {
            _marketRuleRequests.Complete(marketRuleId, priceIncrements);
        }

        public override void position(string account, Contract contract, decimal pos, double avgCost)
        {
            _positions.Record(contract.ConId, pos);

            if (contract.SecType?.Equals("OPT", StringComparison.OrdinalIgnoreCase) == true
                && !string.IsNullOrWhiteSpace(contract.Symbol)
                && !string.IsNullOrWhiteSpace(contract.LastTradeDateOrContractMonth)
                && contract.Strike > 0)
            {
                string optionType = contract.Right?.Equals("P", StringComparison.OrdinalIgnoreCase) == true ? "PUT" : "CALL";
                string key = DatabaseService.FormatOptionPositionKey(contract.Symbol, optionType, contract.Strike, contract.LastTradeDateOrContractMonth);
                _latestOptionPositions[key] = pos;
            }
        }

        public override void positionEnd()
        {
            _positions.CompleteSnapshot();
            int closed = _dbService?.ReconcileClosedPositions(_latestOptionPositions) ?? 0;
            if (closed > 0)
            {
                ReconciliationLog.Write($"[OMS POSITION SYNC] Marked {closed} trade(s) closed from IBKR position snapshot.");
            }
            else
            {
                ReconciliationLog.Write($"[OMS POSITION SYNC] Position snapshot received; no additional tracked trades needed closure.");
            }
        }

        public override void tickPrice(int tickerId, int field, double price, TickAttrib attribs)
        {
            _quotes.Record(tickerId, field, price);
        }

        public override void tickSnapshotEnd(int tickerId)
        {
            _quotes.Complete(tickerId);
        }

        public override void marketDataType(int reqId, int marketDataType)
        {
            _quotes.MarkDelayed(reqId, marketDataType >= 3);
        }

        public override void orderStatus(int orderId, string status, decimal filled, decimal remaining, double avgFillPrice, long permId, int parentId, double lastFillPrice, int clientId, string whyHeld, double mktCapPrice)
        {
            bool isFilled = status.Equals("Filled", StringComparison.OrdinalIgnoreCase);
            bool isTerminal = isFilled || status is "Cancelled" or "ApiCancelled" or "Inactive";
            string normalizedStatus = NormalizeBrokerOrderStatus(status, filled, remaining);

            ReconciliationLog.Write($"[IBKR STATUS] Order #{orderId} :: {status} | filled {filled} | remaining {remaining}"
                + (avgFillPrice > 0 ? $" | avg ${avgFillPrice}" : string.Empty)
                + (string.IsNullOrWhiteSpace(whyHeld) ? string.Empty : $" | held: {whyHeld}"));

            UpdateTrackedOrderStatus(orderId, clientId, normalizedStatus, "ORDER_STATUS_CALLBACK");

            if (_activeTakeProfitOrders.TryGetValue(orderId, out ActiveTakeProfitOrder? activeTarget)
                && _pendingEntryOrders.TryGetValue(activeTarget.ParentEntryOrderId, out PendingEntryOrder? targetEntry))
            {
                lock (targetEntry.SyncRoot)
                {
                    TargetLevelState levelState = GetTargetState(targetEntry, activeTarget.Level);
                    levelState.WorkingOrderFilled = Math.Max(levelState.WorkingOrderFilled, (int)filled);
                    if (isTerminal)
                    {
                        levelState.CompletedQuantity += levelState.WorkingOrderFilled;
                        levelState.WorkingOrderId = null;
                        levelState.WorkingOrderFilled = 0;
                        levelState.SubmittedQuantity = 0;
                    }
                }
            }

            if (isTerminal)
            {
                if (_activeTakeProfitOrders.TryRemove(orderId, out ActiveTakeProfitOrder? target))
                {
                    target.Completed.Set();
                }
            }

            if (filled > 0 && avgFillPrice > 0 && _pendingEntryOrders.TryGetValue(orderId, out PendingEntryOrder? entry))
            {
                ReconcileTakeProfitOrders(orderId, entry, (int)filled, avgFillPrice);
            }
            else if (isTerminal && filled == 0)
            {
                _pendingEntryOrders.TryRemove(orderId, out _);
            }
        }

        private bool UpdateTrackedOrderStatus(int orderId, int brokerClientId, string? status, string source)
        {
            if (string.IsNullOrWhiteSpace(status)) return false;

            int effectiveClientId = brokerClientId > 0 ? brokerClientId : _clientId;
            string normalizedStatus = NormalizeBrokerOrderStatus(status, filled: 0, remaining: 0);
            if (_dbService?.UpdateKnownOrderStatus(orderId, effectiveClientId, normalizedStatus) == true)
            {
                ReconciliationLog.Write($"[OMS STATUS SYNC] {source}: order #{orderId} -> {normalizedStatus}");
                return true;
            }

            return false;
        }

        private static string NormalizeBrokerOrderStatus(string status, decimal filled, decimal remaining)
        {
            if (filled > 0 && remaining > 0) return "PARTIALLY_FILLED";

            return status.Trim().ToUpperInvariant() switch
            {
                "PRESUBMITTED" => "PRESUBMITTED",
                "PRE-SUBMITTED" => "PRESUBMITTED",
                "SUBMITTED" => "SUBMITTED",
                "PENDINGSUBMIT" => "PENDING_SUBMIT",
                "PENDINGCANCEL" => "PENDING_CANCEL",
                "APICANCELLED" => "API_CANCELLED",
                "CANCELLED" => "CANCELLED",
                "FILLED" => "FILLED",
                "INACTIVE" => "INACTIVE",
                var value => value.Replace(" ", "_")
            };
        }

        public void ProcessIncomingAlert(ulong discordMsgId, ParsedTrade trade)
        {
            lock (_promptGate)
            {
                _activeDiscordMessageId = discordMsgId;
                EvaluateAndPrompt(discordMsgId, trade);
            }
        }

        private void EvaluateAndPrompt(ulong discordMsgId, ParsedTrade trade)
        {
            string action = trade.ActionType;
            string ticker = trade.Ticker;

            if (!_clientSocket.IsConnected())
            {
                Block(discordMsgId, ticker, "EXCHANGE_ROUTING", "CRASH", "IBKR socket stream connection is offline.");
                return;
            }

            // TWS silently drops orders sent before it publishes the next valid order id.
            if (!_orderIdReady.Wait(BrokerReplyTimeout))
            {
                Block(discordMsgId, ticker, "EXCHANGE_ROUTING", "BLOCKED", "nextValidId handshake never completed.");
                return;
            }

            ResolvedContract? resolvedContract = ResolveContract(ticker, trade.OptionType, trade.Strike, trade.Expiration);
            if (resolvedContract == null)
            {
                Block(discordMsgId, ticker, "CONTRACT_VALIDATION", "BLOCKED",
                    $"IBKR returned no tradable contract for {ticker} {trade.Expiration} {trade.Strike} {trade.OptionType}.");
                return;
            }
            Contract contract = resolvedContract.Contract;

            bool isExit = action is "SELL" or "TRIM" or "SOLD_ALL";
            string orderAction = isExit ? "SELL" : "BUY";
            int orderQty;
            string? sizingWarning = null;
            IReadOnlyList<TradeBudgetOption> budgetOptions = Array.Empty<TradeBudgetOption>();

            if (isExit)
            {
                decimal? held = GetHeldQuantity(contract.ConId);
                if (held == null)
                {
                    Block(discordMsgId, ticker, "POSITION_VALIDATION", "BLOCKED",
                        "IBKR never delivered a position snapshot, so the held quantity could not be verified.");
                    return;
                }

                if (held.Value <= 0)
                {
                    Block(discordMsgId, ticker, "POSITION_VALIDATION", "BLOCKED",
                        $"Rejected {action}: IBKR reports {held.Value} contracts held for {contract.LocalSymbol}.");
                    return;
                }

                orderQty = action == "TRIM"
                    ? (int)Math.Clamp(Math.Round(held.Value * (decimal)trade.TrimFraction, MidpointRounding.AwayFromZero), 1m, held.Value)
                    : (int)held.Value;

                Console.ForegroundColor = action == "TRIM" ? ConsoleColor.DarkYellow : ConsoleColor.DarkRed;
                Console.WriteLine($"\n📊 [OMS SIZING] IBKR reports {held.Value} held. {action}"
                    + (action == "TRIM" ? $" at {trade.TrimFraction:P0}" : string.Empty)
                    + $" will sell {orderQty}.");
                Console.ResetColor();

                if (trade.SizingAmbiguous)
                {
                    sizingWarning = $"The alert asked to trim but never stated a size. Defaulting to {trade.TrimFraction:P0} ({orderQty} contracts).";
                }
            }
            else
            {
                orderQty = 0;
            }

            double limitPrice = OrderSizing.SnapPrice(trade.PricePaid, resolvedContract.PriceRules, roundUp: false);
            if (limitPrice < 0.01)
            {
                Block(discordMsgId, ticker, "PRICE_VALIDATION", "BLOCKED", $"Alert price {trade.PricePaid} is not a usable limit price.");
                return;
            }


            if (!isExit)
            {
                budgetOptions = _orderBudgets
                    .Select(budget => OrderSizing.ForBudget(budget, limitPrice))
                    .ToArray();
            }

            var quote = RequestQuote(contract);
            string? marketabilityWarning = DescribeMarketability(orderAction, limitPrice, quote);

            Console.Beep(); 
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n=======================================================");
            Console.WriteLine("🚨 [PRE-TRADE CONFIRMATION GATEWAY]");
            Console.WriteLine($"   ├── Source Trader: {trade.TraderName}");
            Console.WriteLine($"   ├── Intent Action: {action}");
            Console.WriteLine($"   ├── Parsed Alert:  {trade.Ticker} {trade.Expiration} {trade.Strike}{(trade.OptionType == "CALL" ? "C" : "P")} @ ${trade.PricePaid:F2}{(trade.ContractInferred ? " [INFERRED CONTRACT]" : string.Empty)}");
            Console.WriteLine($"   ├── Account Type:  {_accountType}");
            Console.WriteLine($"   ├── IBKR Contract: {contract.LocalSymbol} (conId {contract.ConId}, {contract.TradingClass} @ {contract.Exchange})");
            Console.WriteLine($"   ├── Market Quote:  {DescribeQuote(quote)}");
            Console.WriteLine(isExit
                ? $"   └── Working Order: {orderAction} {orderQty} @ LMT ${limitPrice}"
                : $"   └── Working Order: {orderAction} @ LMT ${limitPrice} — budget selected in dialog");
            Console.WriteLine("=======================================================");
            Console.ResetColor();

            if (marketabilityWarning != null)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"⚠️ [NOT MARKETABLE] {marketabilityWarning}");
                Console.ResetColor();
            }

            if (sizingWarning != null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"⚠️ [AMBIGUOUS EXIT SIZE] {sizingWarning}");
                Console.ResetColor();
            }

            Console.WriteLine($"⏳ Awaiting dashboard approval for {_browserApprovalTimeout.TotalSeconds:0}s. Desktop dialog remains available as fallback...");

            var warnings = new System.Collections.Generic.List<string>();
            if (sizingWarning != null) warnings.Add(sizingWarning);
            if (marketabilityWarning != null) warnings.Add(marketabilityWarning);
            if (trade.ContractInferred) warnings.Add("Source: prior matching trader alert (contract inferred, not stated in this message).");

            var details = new TradeConfirmationDetails(
                Trader: trade.TraderName,
                Intent: action,
                OrderAction: orderAction,
                Ticker: trade.Ticker,
                OptionType: trade.OptionType,
                Strike: trade.Strike,
                Expiry: trade.Expiration,
                Quantity: orderQty,
                OrderType: "LMT",
                LimitPrice: limitPrice,
                MarketQuote: DescribeQuote(quote),
                ContractSymbol: $"{contract.LocalSymbol} (conId {contract.ConId}, {contract.TradingClass} @ {contract.Exchange})",
                RiskCategory: trade.RiskCategory,
                AccountType: _accountType,
                ContractInferred: trade.ContractInferred,
                Warnings: warnings)
            {
                BudgetOptions = budgetOptions
            };

            TradeConfirmationResult confirmation = RequestConfirmation(discordMsgId, ticker, details);

            if (confirmation.Approved)
            {
                if (!isExit)
                {
                    if (confirmation.SelectedBudget == null)
                    {
                        Block(discordMsgId, ticker, "PRE_TRADE_GATEWAY", "BLOCKED", "No entry budget was selected.");
                        return;
                    }

                    orderQty = confirmation.SelectedBudget.Quantity;
                }
                else
                {
                    if (!CancelWorkingTakeProfits(contract.ConId))
                    {
                        Block(discordMsgId, ticker, "TAKE_PROFIT_CANCELLATION", "BLOCKED",
                            "Working take-profit orders did not reach a terminal state before the timeout.");
                        return;
                    }

                    decimal? refreshedHeld = GetHeldQuantity(contract.ConId);
                    if (refreshedHeld == null || refreshedHeld.Value <= 0)
                    {
                        Block(discordMsgId, ticker, "POSITION_REVALIDATION", "BLOCKED",
                            "No position remained after cancelling working take-profit orders.");
                        return;
                    }

                    orderQty = action == "TRIM"
                        ? (int)Math.Clamp(Math.Round(refreshedHeld.Value * (decimal)trade.TrimFraction, MidpointRounding.AwayFromZero), 1m, refreshedHeld.Value)
                        : (int)refreshedHeld.Value;
                }

                string approvalDetails = confirmation.SelectedBudget == null
                    ? "User confirmed execution in the pre-trade dialog."
                    : $"User selected ${confirmation.SelectedBudget.Budget:F2}; {orderQty} contracts estimated at ${confirmation.SelectedBudget.EstimatedValue:F2}.";
                _dbService?.LogAudit(discordMsgId, ticker, "PRE_TRADE_GATEWAY", "USER_APPROVED", approvalDetails);
                PlaceOrder(contract, resolvedContract.PriceRules, orderAction, orderQty, limitPrice, trade);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("🛡️ [ORDER BYPASSED] Pre-trade gate dropped manually. No capital deployed.\n");
                Console.ResetColor();

                _dbService?.LogAudit(discordMsgId, ticker, "PRE_TRADE_GATEWAY", "USER_DECLINED", "User declined the pre-trade confirmation.");
            }
        }

        private TradeConfirmationResult RequestConfirmation(ulong discordMsgId, string ticker, TradeConfirmationDetails details)
        {
            if (_dbService == null) return ConfirmationDialog.Confirm(details);

            try
            {
                _dbService.CreatePendingApproval(discordMsgId, details);

                if (!IsDashboardUiListening())
                {
                    _dbService.LogAudit(discordMsgId, ticker, "PRE_TRADE_GATEWAY", "DASHBOARD_OFFLINE",
                        $"React dashboard was not listening on {DashboardUiHost}:{DashboardUiPort}; opening desktop confirmation immediately.");
                    Console.WriteLine("🖥️ [DIALOG FALLBACK] React dashboard is not running. Opening desktop confirmation immediately.");
                    return ConfirmationDialog.Confirm(details);
                }

                _dbService.LogAudit(discordMsgId, ticker, "PRE_TRADE_GATEWAY", "PENDING_BROWSER",
                    $"Waiting up to {_browserApprovalTimeout.TotalSeconds:0}s for dashboard approval.");

                BrowserApprovalDecision decision = _dbService.WaitForBrowserApproval(discordMsgId, _browserApprovalTimeout);
                if (decision.Status == "APPROVED")
                {
                    TradeBudgetOption? selectedBudget = decision.SelectedBudget.HasValue
                        ? details.BudgetOptions.FirstOrDefault(option => Math.Abs(option.Budget - decision.SelectedBudget.Value) < 0.001)
                        : null;
                    if (details.BudgetOptions.Count == 0 || selectedBudget != null)
                    {
                        Console.WriteLine("✅ [DASHBOARD APPROVAL] Order approved from React dashboard.");
                        return new TradeConfirmationResult(true, selectedBudget);
                    }

                    Console.WriteLine("⚠️ [DASHBOARD APPROVAL] Dashboard returned an invalid budget. Opening fallback dialog.");
                }
                else if (decision.Status == "REJECTED")
                {
                    Console.WriteLine("🛡️ [DASHBOARD REJECTION] Order rejected from React dashboard.");
                    return new TradeConfirmationResult(false, null);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ [DASHBOARD FALLBACK] Browser approval failed ({ex.GetType().Name}: {ex.Message}).");
            }

            Console.WriteLine("🖥️ [DIALOG FALLBACK] Browser approval timed out or was unavailable. Opening desktop confirmation.");
            return ConfirmationDialog.Confirm(details);
        }

        private static bool IsDashboardUiListening()
        {
            try
            {
                using var client = new TcpClient();
                IAsyncResult pendingConnection = client.BeginConnect(DashboardUiHost, DashboardUiPort, null, null);
                if (!pendingConnection.AsyncWaitHandle.WaitOne(DashboardProbeTimeout)) return false;

                client.EndConnect(pendingConnection);
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        /// Asks TWS to resolve the alert into a real, tradable contract before any order is built.
        private ResolvedContract? ResolveContract(string ticker, string type, double strike, string expiry)
        {
            var query = new Contract {
                Symbol = ticker,
                SecType = "OPT",
                Exchange = "SMART",
                Currency = "USD",
                Multiplier = "100",
                LastTradeDateOrContractMonth = expiry,
                Strike = strike,
                Right = (type == "CALL") ? "C" : "P"
            };

            int reqId = _contractRequests.OpenRequest();
            _clientSocket.reqContractDetails(reqId, query);

            var matches = _contractRequests.WaitForResults(reqId, BrokerReplyTimeout)
                .Where(d => d.Contract.LastTradeDateOrContractMonth == expiry)
                .ToList();

            if (matches.Count == 0) return null;

            if (matches.Select(d => d.Contract.TradingClass).Distinct().Count() > 1)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"⚠️ [CONTRACT AMBIGUITY] {matches.Count} contracts matched: {string.Join(", ", matches.Select(d => d.Contract.LocalSymbol))}. Verify the selection below before approving.");
                Console.ResetColor();
            }

            ContractDetails chosen = matches.FirstOrDefault(d => d.Contract.TradingClass == ticker) ?? matches[0];
            return new ResolvedContract(chosen.Contract, ResolvePriceRules(chosen));
        }

        private IReadOnlyList<PriceIncrementRule> ResolvePriceRules(ContractDetails details)
        {
            string[] exchanges = (details.ValidExchanges ?? string.Empty).Split(',', StringSplitOptions.TrimEntries);
            string[] ruleIds = (details.MarketRuleIds ?? string.Empty).Split(',', StringSplitOptions.TrimEntries);
            int smartIndex = Array.FindIndex(exchanges, exchange => exchange.Equals("SMART", StringComparison.OrdinalIgnoreCase));

            if (smartIndex >= 0 && smartIndex < ruleIds.Length && int.TryParse(ruleIds[smartIndex], out int marketRuleId))
            {
                _marketRuleRequests.OpenRequest(marketRuleId);
                _clientSocket.reqMarketRule(marketRuleId);
                IReadOnlyList<PriceIncrementRule> rules = _marketRuleRequests.WaitForResults(marketRuleId, BrokerReplyTimeout);
                if (rules.Count > 0) return rules;
            }

            double fallbackIncrement = details.MinTick > 0 ? details.MinTick : 0.01;
            Console.WriteLine($"⚠️ [MARKET RULE] Using contract minimum tick ${fallbackIncrement} because SMART price bands were unavailable.");
            return new[] { new PriceIncrementRule(0, fallbackIncrement) };
        }

        /// Returns the quantity IBKR itself reports for the contract, or null if the snapshot never arrived.
        private decimal? GetHeldQuantity(int conId)
        {
            _positions.BeginSnapshot();
            _clientSocket.reqPositions();

            bool delivered = _positions.WaitForSnapshot(BrokerReplyTimeout);
            _clientSocket.cancelPositions();

            return delivered ? _positions.GetQuantity(conId) : null;
        }

        private QuoteBook.Quote? RequestQuote(Contract contract)
        {
            // Streaming rather than snapshot: IB snapshots require a live subscription and return nothing for delayed data.
            int reqId = _quotes.OpenRequest();
            _clientSocket.reqMktData(reqId, contract, string.Empty, false, false, null);

            var quote = _quotes.WaitForQuote(reqId, QuoteTimeout);
            _clientSocket.cancelMktData(reqId);

            return quote;
        }

        private static string DescribeQuote(QuoteBook.Quote? quote)
        {
            if (quote == null || !quote.HasMarket)
                return "unavailable (no option market data subscription, or market closed)";

            string bid = quote.Bid > 0 ? $"${quote.Bid:F2}" : "n/a";
            string ask = quote.Ask > 0 ? $"${quote.Ask:F2}" : "n/a";
            string last = quote.Last > 0 ? $"  Last ${quote.Last:F2}" : string.Empty;

            return $"Bid {bid} / Ask {ask}{last}{(quote.Delayed ? "  [DELAYED]" : string.Empty)}";
        }

        /// A limit that does not cross the spread rests unfilled, which is the usual reason an order sits at Submitted.
        private static string? DescribeMarketability(string orderAction, double limitPrice, QuoteBook.Quote? quote)
        {
            if (quote == null || !quote.HasMarket) return null;

            if (orderAction == "BUY" && quote.Ask > 0 && limitPrice < quote.Ask)
                return $"BUY limit ${limitPrice} is below the ask ${quote.Ask:F2}. It will rest unfilled until the ask drops to your price.";

            if (orderAction == "SELL" && quote.Bid > 0 && limitPrice > quote.Bid)
                return $"SELL limit ${limitPrice} is above the bid ${quote.Bid:F2}. It will rest unfilled until the bid rises to your price.";

            return null;
        }

        private void PlaceOrder(Contract contract, IReadOnlyList<PriceIncrementRule> priceRules, string orderAction, int orderQty, double limitPrice, ParsedTrade trade)
        {
            var order = new Order {
                Action = orderAction,
                OrderType = "LMT",
                LmtPrice = limitPrice,
                TotalQuantity = orderQty,
                Tif = "DAY",
                Transmit = true
            };

            int orderId = Interlocked.Increment(ref _currentOrderId) - 1;
            _orderMessageIds[orderId] = _activeDiscordMessageId;

            if (orderAction == "BUY")
            {
                _pendingEntryOrders[orderId] = new PendingEntryOrder(contract, trade, orderQty, _activeDiscordMessageId, priceRules);
            }

            _clientSocket.placeOrder(orderId, contract, order);
            Console.WriteLine("📬 [IBKR ROUTED]");
            Console.WriteLine($"   ├── Order ID: #{orderId}");
            Console.WriteLine($"   ├── Trader:   {trade.TraderName}");
            Console.WriteLine($"   ├── Parsed:   {trade.ActionType} {trade.Ticker} {trade.Expiration} {trade.Strike}{(trade.OptionType == "CALL" ? "C" : "P")} @ ${trade.PricePaid:F2}");
            Console.WriteLine($"   └── Submitted: {orderAction} {orderQty}x {contract.LocalSymbol} @ LMT ${limitPrice:F2}\n");

            // 🔥 UPDATED: Passes ClientId, type, and strike into the unique logging function
            _dbService?.LogExecutedOrder(orderId, _clientId, _activeDiscordMessageId, trade.Ticker, trade.OptionType, trade.Strike, trade.Expiration, orderAction, orderQty, "SUBMITTED", null, orderAction == "BUY" ? "ENTRY" : "EXIT", limitPrice);
            _dbService?.LogAudit(_activeDiscordMessageId, trade.Ticker, "EXCHANGE_ROUTING", "SUBMITTED", $"Order ID #{orderId} successfully dispatched onto the market exchange.");
        }

        private void ReconcileTakeProfitOrders(int parentOrderId, PendingEntryOrder entry, int filledQuantity, double averageFillPrice)
        {
            lock (entry.SyncRoot)
            {
                if (filledQuantity == entry.LastEntryFilled && Math.Abs(averageFillPrice - entry.LastAverageFillPrice) < 0.000001) return;

                TakeProfitPlan plan = OrderSizing.CreateTakeProfitPlan(
                    entry.Quantity, filledQuantity, averageFillPrice, entry.PriceRules);

                foreach (TakeProfitAllocation target in plan.Targets)
                {
                    TargetLevelState levelState = GetTargetState(entry, target.Level);
                    int requiredOrderQuantity = target.Quantity - levelState.CompletedQuantity;
                    if (requiredOrderQuantity <= 0) continue;

                    bool needsNewOrder = levelState.WorkingOrderId == null;
                    bool needsModification = levelState.SubmittedQuantity != requiredOrderQuantity
                        || Math.Abs(levelState.SubmittedPrice - target.TargetPrice) > 0.000001;
                    if (!needsNewOrder && !needsModification) continue;

                    int targetOrderId = levelState.WorkingOrderId
                        ?? (Interlocked.Increment(ref _currentOrderId) - 1);
                    var targetOrder = new Order
                    {
                        Action = "SELL",
                        OrderType = "LMT",
                        LmtPrice = target.TargetPrice,
                        TotalQuantity = requiredOrderQuantity,
                        Tif = "DAY",
                        OrderRef = $"OTP-{entry.DiscordMessageId}-TP{target.Level}",
                        Transmit = true
                    };

                    if (needsNewOrder)
                    {
                        levelState.WorkingOrderId = targetOrderId;
                        levelState.WorkingOrderFilled = 0;
                        _activeTakeProfitOrders[targetOrderId] = new ActiveTakeProfitOrder(
                            entry.Contract.ConId, parentOrderId, target.Level, new ManualResetEventSlim(false));
                        _orderMessageIds[targetOrderId] = entry.DiscordMessageId;
                    }

                    levelState.SubmittedQuantity = requiredOrderQuantity;
                    levelState.SubmittedPrice = target.TargetPrice;
                    _clientSocket.placeOrder(targetOrderId, entry.Contract, targetOrder);
                    _dbService?.LogExecutedOrder(targetOrderId, _clientId, entry.DiscordMessageId, entry.Trade.Ticker,
                        entry.Trade.OptionType, entry.Trade.Strike, entry.Trade.Expiration, "SELL", requiredOrderQuantity, "SUBMITTED",
                        parentOrderId, $"TP{target.Level}", target.TargetPrice);
                    _dbService?.LogAudit(entry.DiscordMessageId, entry.Trade.Ticker, "TAKE_PROFIT_ROUTING",
                        needsNewOrder ? "SUBMITTED" : "MODIFIED",
                        $"TP{target.Level} order #{targetOrderId}: SELL {requiredOrderQuantity} @ ${target.TargetPrice:F2}, based on {filledQuantity}/{entry.Quantity} filled @ ${averageFillPrice:F2} average.");
                }

                entry.LastEntryFilled = filledQuantity;
                entry.LastAverageFillPrice = averageFillPrice;
                Console.WriteLine($"🎯 [TAKE PROFITS] Entry #{parentOrderId} filled {filledQuantity}/{entry.Quantity} @ ${averageFillPrice:F2}. "
                    + $"Active plan: {plan.Targets.Count} target(s); runner filled allocation: {plan.RunnerQuantity}.");
                _dbService?.LogAudit(entry.DiscordMessageId, entry.Trade.Ticker, "TAKE_PROFIT_PLAN", "ACTIVE",
                    $"Entry #{parentOrderId}; filled {filledQuantity}/{entry.Quantity}; {plan.Targets.Count} fixed targets; runner allocation {plan.RunnerQuantity}.");
            }
        }

        private static TargetLevelState GetTargetState(PendingEntryOrder entry, int level)
        {
            if (!entry.Targets.TryGetValue(level, out TargetLevelState? state))
            {
                state = new TargetLevelState();
                entry.Targets[level] = state;
            }

            return state;
        }

        private bool CancelWorkingTakeProfits(int contractId)
        {
            KeyValuePair<int, ActiveTakeProfitOrder>[] targets = _activeTakeProfitOrders
                .Where(item => item.Value.ContractId == contractId)
                .ToArray();
            if (targets.Length == 0) return true;

            foreach (KeyValuePair<int, ActiveTakeProfitOrder> target in targets)
            {
                _clientSocket.cancelOrder(target.Key, new OrderCancel());
            }

            var stopwatch = Stopwatch.StartNew();
            foreach (KeyValuePair<int, ActiveTakeProfitOrder> target in targets)
            {
                TimeSpan remaining = BrokerReplyTimeout - stopwatch.Elapsed;
                if (remaining <= TimeSpan.Zero || !target.Value.Completed.Wait(remaining))
                {
                    return false;
                }
            }

            return true;
        }

        private void Block(ulong discordMsgId, string ticker, string stage, string outcome, string reason)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n🛡️ [{stage} {outcome}] {reason}");
            Console.WriteLine("   └── [AUDIT TRACK] Order blocked automatically. No prompt initiated.\n");
            Console.ResetColor();

            _dbService?.LogAudit(discordMsgId, ticker, stage, outcome, reason);
        }
    }
}
