using System;
using System.Threading;
using IBApi; 

namespace OptionTradesParser
{
    public class OrderExecutionService : DefaultEWrapper
    {
        private readonly EClientSocket _clientSocket;
        private readonly EReaderSignal _readerSignal;
        private int _currentOrderId = 0;
        private readonly int _clientId; // 🔥 Tracker handle
        private readonly int _defaultQty;
        private DatabaseService? _dbService;
        private ulong _activeDiscordMessageId = 0;

        public OrderExecutionService(string host, int port, int clientId, int defaultQty)
        {
            _readerSignal = new EReaderMonitorSignal();
            _clientSocket = new EClientSocket(this, _readerSignal);
            _clientId = clientId;
            _defaultQty = defaultQty;

            Console.WriteLine($"🔌 [IBKR] Connecting to TWS Gateway at {host}:{port}...");
            _clientSocket.eConnect(host, port, clientId);

            if (_clientSocket.IsConnected())
            {
                Console.WriteLine("USA ✅ [IBKR] Successfully linked to active trading session socket.");
                
                var reader = new EReader(_clientSocket, _readerSignal);
                reader.Start();
                new Thread(() => {
                    while (_clientSocket.IsConnected()) {
                        _readerSignal.waitForSignal();
                        reader.processMsgs();
                    }
                }) { IsBackground = true }.Start();
            }
        }

        public void SetDatabaseService(DatabaseService dbService)
        {
            _dbService = dbService;
        }

        public override void nextValidId(int orderId)
        {
            _currentOrderId = orderId;
            Console.WriteLine($"🔄 [IBKR SYNC] Order ID baseline synchronized dynamically with broker servers. Next ID: #{_currentOrderId}");
        }

        public override void orderStatus(int orderId, string status, decimal filled, decimal remaining, double avgFillPrice, long permId, int parentId, double lastFillPrice, int clientId, string whyHeld, double mktCapPrice)
        {
            if (status.Equals("Filled", StringComparison.OrdinalIgnoreCase))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✨ [IBKR FILL CONFIRMATION] Order #{orderId} completely filled at ${avgFillPrice}!");
                Console.ResetColor();

                try
                {
                    using (var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=trade_alerts.db"))
                    {
                        connection.Open();
                        var command = connection.CreateCommand();
                        // 🔥 THE MAXIMUM SAFETY FILTER: Targets the row matching both OrderId and ClientId
                        command.CommandText = "UPDATE ExecutedOrders SET Status = 'FILLED' WHERE IbOrderId = $id AND ClientId = $clientId;";
                        command.Parameters.AddWithValue("$id", orderId);
                        command.Parameters.AddWithValue("$clientId", clientId);
                        command.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ [FILL DB UPDATE ERROR]: {ex.Message}");
                }
            }
        }

        public void ProcessIncomingAlert(ulong discordMsgId, string trader, string action, string ticker, string type, double strike, string expiry, double price)
        {
            _activeDiscordMessageId = discordMsgId;

            if ((action == "SELL" || action == "TRIM" || action == "SOLD_ALL") && _dbService != null)
            {
                bool ownsPosition = _dbService.HasExistingOpenPosition(ticker);
                if (!ownsPosition)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\n🛡️ [ORPHAN-SAFETY BLOCK] Intercepted exit signal ({action}) for {ticker}. No active open position found in database logs.");
                    Console.WriteLine("   └── [AUDIT TRACK] Order blocked automatically. No prompt initiated.\n");
                    Console.ResetColor();

                    _dbService.LogAudit(discordMsgId, ticker, "POSITION_VALIDATION", "BLOCKED", $"Rejected {action} alert because no open position exists for ticker {ticker}.");
                    return; 
                }
            }

            Console.Beep(); 
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n=======================================================");
            Console.WriteLine("🚨 [PRE-TRADE CONFIRMATION GATEWAY]");
            Console.WriteLine($"   ├── Source Trader: {trader}");
            Console.WriteLine($"   ├── Intent Action: {action}");
            Console.WriteLine($"   └── Target Asset:  {ticker} Exp:{expiry} Strike:${strike} {type}");
            Console.WriteLine("=======================================================");
            Console.ResetColor();

            Console.Write("👉 Press [Y] to Execute on IBKR Account or [N] to Ignore/Drop Order: ");
            ConsoleKeyInfo key = Console.ReadKey();
            Console.WriteLine(); 

            if (key.Key == ConsoleKey.Y)
            {
                _dbService?.LogAudit(discordMsgId, ticker, "PRE_TRADE_GATEWAY", "USER_APPROVED", $"User pressed Y to confirm order execution.");
                ExecuteOptionOrder(action, ticker, type, strike, expiry);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("🛡️ [ORDER BYPASSED] Pre-trade gate dropped manually. No capital deployed.\n");
                Console.ResetColor();

                _dbService?.LogAudit(discordMsgId, ticker, "PRE_TRADE_GATEWAY", "USER_DECLINED", "User pressed N or bypassed the confirmation gate prompt.");
            }
        }

        private void ExecuteOptionOrder(string action, string ticker, string type, double strike, string expiry)
        {
            if (!_clientSocket.IsConnected())
            {
                Console.WriteLine("❌ [EXECUTION CRASH] Cannot execute. IBKR socket stream connection is offline.");
                _dbService?.LogAudit(_activeDiscordMessageId, ticker, "EXCHANGE_ROUTING", "CRASH", "IBKR socket was disconnected at the moment of order placement.");
                return;
            }

            Contract contract = new Contract {
                Symbol = ticker,
                SecType = "OPT",
                Exchange = "SMART",
                Currency = "USD",
                LastTradeDateOrContractMonth = expiry,
                Strike = strike,
                Right = (type == "CALL") ? "C" : "P"
            };

            int orderQty = _defaultQty; 
            string orderAction = "BUY";

            if (action == "BUY" || action == "AVERAGE_DOWN")
            {
                orderAction = "BUY";
                orderQty = _defaultQty;
                _dbService?.LogAudit(_activeDiscordMessageId, ticker, "OMS_STRATEGY_ALLOCATION", "EXECUTE_BUY", $"Sizing standard initialization allocation: {orderQty} contracts.");
            }
            else if (action == "TRIM")
            {
                orderAction = "SELL";
                orderQty = (int)Math.Max(1, Math.Floor(_defaultQty * 0.5));
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine($"📊 [OMS AUTO-SCALE] Trim alert captured. Scale-Out Allocation applied (50% size): Selling {orderQty} contracts.");
                Console.ResetColor();
                _dbService?.LogAudit(_activeDiscordMessageId, ticker, "OMS_STRATEGY_ALLOCATION", "EXECUTE_TRIM", $"Scale-Out Allocation applied: Trimming {orderQty} contracts.");
            }
            else if (action == "SOLD_ALL")
            {
                orderAction = "SELL";
                orderQty = _defaultQty; 
                Console.ForegroundColor = ConsoleColor.DarkRed;
                Console.WriteLine($"🏁 [OMS LIQUIDATION] Full exit signal encountered. Nuclear Exit route initialized for all contracts.");
                Console.ResetColor();
                _dbService?.LogAudit(_activeDiscordMessageId, ticker, "OMS_STRATEGY_ALLOCATION", "EXECUTE_NUCLEAR_EXIT", "Nuclear Exit rule triggered. Routing market close sequence.");
            }

            Order order = new Order {
                Action = orderAction,
                OrderType = "MKT",
                TotalQuantity = orderQty
            };

            int orderId = Interlocked.Increment(ref _currentOrderId);
            
            _clientSocket.placeOrder(orderId, contract, order);
            Console.WriteLine($"📬 [IBKR ROUTED] Dispatched Order ID #{orderId} for {orderQty} contracts.\n");

            // 🔥 UPDATED: Passes ClientId, type, and strike into the unique logging function
            _dbService?.LogExecutedOrder(orderId, _clientId, _activeDiscordMessageId, ticker, type, strike, orderAction, orderQty, "SUBMITTED");
            _dbService?.LogAudit(_activeDiscordMessageId, ticker, "EXCHANGE_ROUTING", "SUBMITTED", $"Order ID #{orderId} successfully dispatched onto the market exchange.");
        }
    }
}
