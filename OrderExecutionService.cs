using System;
using System.Linq;
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

        private readonly EClientSocket _clientSocket;
        private readonly EReaderSignal _readerSignal;
        private readonly string _host;
        private readonly int _port;
        private int _currentOrderId = 0;
        private readonly int _clientId; // 🔥 Tracker handle
        private readonly int _defaultQty;
        private int _reconnectLoopActive;
        private DatabaseService? _dbService;
        private ulong _activeDiscordMessageId = 0;
        private readonly ManualResetEventSlim _orderIdReady = new(false);
        private readonly ContractRequestBook _contractRequests = new();
        private readonly PositionBook _positions = new();
        private readonly QuoteBook _quotes = new();
        private Thread? _readerThread;

        // Serializes the console confirmation prompt so concurrent alerts cannot steal each other's keystrokes.
        private readonly object _promptGate = new();

        public OrderExecutionService(string host, int port, int clientId, int defaultQty)
        {
            _readerSignal = new EReaderMonitorSignal();
            _clientSocket = new EClientSocket(this, _readerSignal);
            _host = host;
            _port = port;
            _clientId = clientId;
            _defaultQty = defaultQty;

            // "Connection refused" at startup usually means TWS hasn't finished its own boot/login yet
            // (this is routine right after TWS's nightly restart), so the first attempt gets retried too.
            if (!Connect() && Interlocked.CompareExchange(ref _reconnectLoopActive, 1, 0) == 0)
            {
                _ = ReconnectAsync();
            }
        }

        private bool Connect()
        {
            // TWS can force-close a socket that still looks "connected" to this client; disconnecting first
            // guarantees a clean slate instead of retrying eConnect against stale internal state.
            if (_clientSocket.IsConnected()) _clientSocket.eDisconnect();

            // Two reader threads racing on the same shared signal (old + new generation) can desync the
            // protocol stream and looks exactly like TWS forcibly resetting the connection. Only one may run.
            if (_readerThread is { IsAlive: true } stale && !stale.Join(TimeSpan.FromSeconds(2)))
            {
                Console.WriteLine("⚠️ [IBKR] Previous reader thread did not exit in time; proceeding anyway.");
            }

            _orderIdReady.Reset();
            Console.WriteLine($"🔌 [IBKR] Connecting to TWS Gateway at {_host}:{_port}...");
            _clientSocket.eConnect(_host, _port, _clientId);

            if (_clientSocket.IsConnected())
            {
                Console.WriteLine("USA ✅ [IBKR] Successfully linked to active trading session socket.");

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

                // Falls back to delayed quotes when the account has no live option data subscription.
                _clientSocket.reqMarketDataType(4);
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
            if (_clientSocket.IsConnected()) _clientSocket.eDisconnect();
        }

        public override void nextValidId(int orderId)
        {
            _currentOrderId = orderId;
            _orderIdReady.Set();
            Console.WriteLine($"🔄 [IBKR SYNC] Order ID baseline synchronized dynamically with broker servers. Next ID: #{_currentOrderId}");
        }

        public override void managedAccounts(string accountsList)
        {
            Console.WriteLine($"🏦 [IBKR ACCOUNTS] Linked account(s): {accountsList}");
        }

        public override void error(int id, long errorTime, int errorCode, string errorMsg, string advancedOrderRejectJson)
        {
            // 2104/2106/2107/2158/2119 are data-farm status notices, not failures.
            bool informational = errorCode is 2104 or 2106 or 2107 or 2158 or 2119;

            Console.ForegroundColor = informational ? ConsoleColor.DarkGray : ConsoleColor.Red;
            Console.WriteLine($"{(informational ? "i" : "X")} [IBKR {(informational ? "INFO" : "ERROR")}] id={id} code={errorCode} :: {errorMsg}");
            if (!string.IsNullOrWhiteSpace(advancedOrderRejectJson))
            {
                Console.WriteLine($"   └── Reject detail: {advancedOrderRejectJson}");
            }
            Console.ResetColor();

            if (!informational)
            {
                _dbService?.LogAudit(_activeDiscordMessageId, "-", "IBKR_API_ERROR", errorCode.ToString(), $"id={id} :: {errorMsg}");
            }

            // Releases any caller blocked on a contract lookup that TWS just rejected (e.g. code 200).
            if (id > 0)
            {
                _contractRequests.Complete(id);
                _quotes.Complete(id);
            }
        }

        public override void error(string str) => Console.WriteLine($"X [IBKR ERROR] {str}");

        public override void error(Exception e) => Console.WriteLine($"X [IBKR EXCEPTION] {e.Message}");

        public override void openOrder(int orderId, Contract contract, Order order, OrderState orderState)
        {
            Console.WriteLine($"📗 [IBKR ACK] Order #{orderId} {order.Action} {order.TotalQuantity} {contract.LocalSymbol ?? contract.Symbol} :: state={orderState.Status}");
        }

        public override void connectionClosed()
        {
            Console.WriteLine("🔌 [IBKR] Socket connection closed by TWS.");

            if (Interlocked.CompareExchange(ref _reconnectLoopActive, 1, 0) == 0)
            {
                _ = ReconnectAsync();
            }
        }

        private async System.Threading.Tasks.Task ReconnectAsync()
        {
            int maxAttempts = ReconnectDelays.Length;
            try
            {
                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    TimeSpan delay = ReconnectDelays[attempt - 1];
                    Console.WriteLine($"🔄 [IBKR] Reconnect attempt {attempt}/{maxAttempts} starts in {delay.TotalSeconds:0}s.");
                    await System.Threading.Tasks.Task.Delay(delay);

                    bool socketOpened = Connect();
                    bool handshakeCompleted = socketOpened && _orderIdReady.Wait(BrokerReplyTimeout);
                    if (handshakeCompleted && _clientSocket.IsConnected())
                    {
                        Console.WriteLine($"✅ [IBKR] Reconnected on attempt {attempt}/{maxAttempts}.");
                        return;
                    }

                    Console.WriteLine($"❌ [IBKR] Reconnect attempt {attempt}/{maxAttempts} did not complete the broker handshake.");
                }

                Console.WriteLine($"❌ [IBKR] Reconnect stopped after {maxAttempts} unsuccessful attempts. If TWS just restarted, open");
                Console.WriteLine("   Global Configuration → API → Settings and click Apply once, then restart this app.");
            }
            finally
            {
                Interlocked.Exchange(ref _reconnectLoopActive, 0);
            }
        }

        public override void contractDetails(int reqId, ContractDetails contractDetails)
        {
            _contractRequests.Add(reqId, contractDetails);
        }

        public override void contractDetailsEnd(int reqId)
        {
            _contractRequests.Complete(reqId);
        }

        public override void position(string account, Contract contract, decimal pos, double avgCost)
        {
            _positions.Record(contract.ConId, pos);
        }

        public override void positionEnd()
        {
            _positions.CompleteSnapshot();
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

            Console.ForegroundColor = isFilled ? ConsoleColor.Green : isTerminal ? ConsoleColor.Red : ConsoleColor.DarkGray;
            Console.WriteLine($"📊 [IBKR STATUS] Order #{orderId} :: {status} | filled {filled} | remaining {remaining}"
                + (avgFillPrice > 0 ? $" | avg ${avgFillPrice}" : string.Empty)
                + (string.IsNullOrWhiteSpace(whyHeld) ? string.Empty : $" | held: {whyHeld}"));
            Console.ResetColor();

            if (isTerminal)
            {
                _dbService?.UpdateOrderStatus(orderId, clientId, status.ToUpperInvariant());
            }
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

            Contract? contract = ResolveContract(ticker, trade.OptionType, trade.Strike, trade.Expiration);
            if (contract == null)
            {
                Block(discordMsgId, ticker, "CONTRACT_VALIDATION", "BLOCKED",
                    $"IBKR returned no tradable contract for {ticker} {trade.Expiration} {trade.Strike} {trade.OptionType}.");
                return;
            }

            bool isExit = action is "SELL" or "TRIM" or "SOLD_ALL";
            string orderAction = isExit ? "SELL" : "BUY";
            int orderQty;
            string? sizingWarning = null;

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
                orderQty = _defaultQty;
            }

            double limitPrice = Math.Round(trade.PricePaid, 2, MidpointRounding.AwayFromZero);
            if (limitPrice < 0.01)
            {
                Block(discordMsgId, ticker, "PRICE_VALIDATION", "BLOCKED", $"Alert price {trade.PricePaid} is not a usable limit price.");
                return;
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
            Console.WriteLine($"   ├── IBKR Contract: {contract.LocalSymbol} (conId {contract.ConId}, {contract.TradingClass} @ {contract.Exchange})");
            Console.WriteLine($"   ├── Market Quote:  {DescribeQuote(quote)}");
            Console.WriteLine($"   └── Working Order: {orderAction} {orderQty} @ LMT ${limitPrice}");
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

            Console.WriteLine("⏳ Awaiting confirmation in the pop-up dialog...");

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
                ContractInferred: trade.ContractInferred,
                Warnings: warnings);

            bool approved = ConfirmationDialog.Confirm(details);

            if (approved)
            {
                _dbService?.LogAudit(discordMsgId, ticker, "PRE_TRADE_GATEWAY", "USER_APPROVED", $"User confirmed execution in the pre-trade dialog.");
                PlaceOrder(contract, orderAction, orderQty, limitPrice, trade);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("🛡️ [ORDER BYPASSED] Pre-trade gate dropped manually. No capital deployed.\n");
                Console.ResetColor();

                _dbService?.LogAudit(discordMsgId, ticker, "PRE_TRADE_GATEWAY", "USER_DECLINED", "User selected No in the pre-trade dialog.");
            }
        }

        /// Asks TWS to resolve the alert into a real, tradable contract before any order is built.
        private Contract? ResolveContract(string ticker, string type, double strike, string expiry)
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

            var chosen = matches.FirstOrDefault(d => d.Contract.TradingClass == ticker) ?? matches[0];
            return chosen.Contract;
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

        private void PlaceOrder(Contract contract, string orderAction, int orderQty, double limitPrice, ParsedTrade trade)
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

            _clientSocket.placeOrder(orderId, contract, order);
            Console.WriteLine("📬 [IBKR ROUTED]");
            Console.WriteLine($"   ├── Order ID: #{orderId}");
            Console.WriteLine($"   ├── Trader:   {trade.TraderName}");
            Console.WriteLine($"   ├── Parsed:   {trade.ActionType} {trade.Ticker} {trade.Expiration} {trade.Strike}{(trade.OptionType == "CALL" ? "C" : "P")} @ ${trade.PricePaid:F2}");
            Console.WriteLine($"   └── Submitted: {orderAction} {orderQty}x {contract.LocalSymbol} @ LMT ${limitPrice:F2}\n");

            // 🔥 UPDATED: Passes ClientId, type, and strike into the unique logging function
            _dbService?.LogExecutedOrder(orderId, _clientId, _activeDiscordMessageId, trade.Ticker, trade.OptionType, trade.Strike, trade.Expiration, orderAction, orderQty, "SUBMITTED");
            _dbService?.LogAudit(_activeDiscordMessageId, trade.Ticker, "EXCHANGE_ROUTING", "SUBMITTED", $"Order ID #{orderId} successfully dispatched onto the market exchange.");
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
