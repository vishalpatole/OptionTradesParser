using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using Microsoft.Data.Sqlite;

namespace OptionTradesParser
{
    public class DatabaseService
    {
        private readonly string _connectionString;

        public sealed record TrackedOrderContext(
            ulong DiscordMessageId,
            string TraderName,
            string ActionType,
            string Ticker,
            string OptionType,
            double Strike,
            string Expiration,
            double EntryPrice,
            string RiskCategory,
            int Quantity,
            string OrderRole);

        public sealed record OpenTradeContext(
            ulong DiscordMessageId,
            string Ticker,
            string OptionType,
            double Strike,
            string Expiration,
            int OpenQuantity);

        public sealed record ManualExitPriceCandidate(
            ulong DiscordMessageId,
            string Ticker,
            string OptionType,
            double Strike,
            string Expiration,
            int Quantity);

        public DatabaseService(string connectionString)
        {
            _connectionString = connectionString;
        }

        public void InitializeDatabase()
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                
                // 1. Core Trade Alerts Intake Log
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS TradeAlerts (
                        DiscordMessageId INTEGER PRIMARY KEY, 
                        TraderName TEXT,
                        ActionType TEXT,
                        Ticker TEXT,
                        OptionType TEXT,
                        Strike REAL,
                        Expiration TEXT,
                        ContractSymbol TEXT,
                        ExecutionPrice REAL,
                        RiskCategory TEXT,
                        AlertTimestamp TEXT,
                        Timestamp TEXT,
                        RawMessage TEXT,
                        TradeContextPollingComplete INTEGER DEFAULT 0
                    );";
                command.ExecuteNonQuery();

                AddColumnIfMissing(command, "TradeAlerts", "AlertTimestamp");
                AddColumnIfMissing(command, "TradeAlerts", "ContractSymbol");
                AddColumnIfMissing(command, "TradeAlerts", "TradeContextPollingComplete");

                // 2. 🔥 REFACTORED: Added ClientId, OptionType, Strike and converted to a 5-Column Composite Primary Key
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS ExecutedOrders (
                        IbOrderId INTEGER,
                        ClientId INTEGER,
                        DiscordMessageId INTEGER,
                        Ticker TEXT,
                        OptionType TEXT,
                        Strike REAL,
                        Expiration TEXT,
                        ContractSymbol TEXT,
                        ActionType TEXT,
                        Quantity INTEGER,
                        Status TEXT,
                        Timestamp TEXT,
                        PRIMARY KEY (IbOrderId, ClientId, Ticker, OptionType, Strike), 
                        FOREIGN KEY(DiscordMessageId) REFERENCES TradeAlerts(DiscordMessageId)
                    );";
                command.ExecuteNonQuery();

                AddColumnIfMissing(command, "ExecutedOrders", "Expiration");
                AddColumnIfMissing(command, "ExecutedOrders", "ContractSymbol");
                AddColumnIfMissing(command, "ExecutedOrders", "ParentOrderId", "INTEGER");
                AddColumnIfMissing(command, "ExecutedOrders", "OrderRole");
                AddColumnIfMissing(command, "ExecutedOrders", "LimitPrice", "REAL");

                // 2b. Market-condition snapshot captured once per trade by the separate MarketContextWorker process.
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS MarketContext (
                        DiscordMessageId INTEGER PRIMARY KEY,
                        CapturedAt TEXT,
                        UnderlyingPrice REAL,
                        DayHigh REAL,
                        DayLow REAL,
                        Vix REAL,
                        ImpliedVol REAL,
                        OpenInterest REAL,
                        SupportLevel REAL,
                        SupportTouches INTEGER,
                        ResistanceLevel REAL,
                        ResistanceTouches INTEGER,
                        PutWallStrike REAL,
                        PutWallOI REAL,
                        CallWallStrike REAL,
                        CallWallOI REAL,
                        Notes TEXT,
                        FOREIGN KEY(DiscordMessageId) REFERENCES TradeAlerts(DiscordMessageId)
                    );";
                command.ExecuteNonQuery();

                // 3. Central System Audit Log Table
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS ExecutionAuditLogs (
                        LogId INTEGER PRIMARY KEY AUTOINCREMENT,
                        DiscordMessageId INTEGER,
                        Ticker TEXT,
                        LifecycleStep TEXT,
                        DecisionStatus TEXT,
                        Details TEXT,
                        Timestamp TEXT
                    );";
                command.ExecuteNonQuery();

                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS OrderExecutions (
                        ExecutionId TEXT PRIMARY KEY,
                        IbOrderId INTEGER,
                        ClientId INTEGER,
                        DiscordMessageId INTEGER,
                        ActionType TEXT,
                        Quantity REAL,
                        Price REAL,
                        ExecutedAt TEXT,
                        Commission REAL,
                        RealizedPnl REAL,
                        FOREIGN KEY(DiscordMessageId) REFERENCES TradeAlerts(DiscordMessageId)
                    );";
                command.ExecuteNonQuery();

                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS PendingApprovals (
                        DiscordMessageId INTEGER PRIMARY KEY,
                        DetailsJson TEXT NOT NULL,
                        Status TEXT NOT NULL,
                        SelectedBudget REAL,
                        CreatedAt TEXT NOT NULL,
                        DecidedAt TEXT,
                        FOREIGN KEY(DiscordMessageId) REFERENCES TradeAlerts(DiscordMessageId)
                    );";
                command.ExecuteNonQuery();

                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS AppSettings (
                        Key TEXT PRIMARY KEY,
                        Value TEXT NOT NULL,
                        UpdatedAt TEXT NOT NULL
                    );";
                command.ExecuteNonQuery();

                command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
                command.ExecuteNonQuery();
            }
        }

        /// Every timestamp in this database is UTC ISO-8601 so rows stay comparable regardless of the poster's local zone.
        private static string UtcStamp(DateTimeOffset moment) => moment.UtcDateTime.ToString("o");

        /// Denormalized display/analytics form of a contract, e.g. "INTC 20260917 97C", stored alongside the
        /// individual Ticker/Expiration/Strike/OptionType columns so reports don't need to reassemble it each time.
        public static string FormatContractSymbol(string ticker, string expiry, double strike, string optionType)
            => $"{ticker} {expiry} {strike}{(optionType == "CALL" ? "C" : "P")}";

        public static string FormatOptionPositionKey(string ticker, string optionType, double strike, string expiration)
            => $"{ticker.Trim().ToUpperInvariant()}|{optionType.Trim().ToUpperInvariant()}|{strike.ToString("0.####", CultureInfo.InvariantCulture)}|{expiration.Trim()}";

        private static void AddColumnIfMissing(SqliteCommand command, string table, string column, string columnType = "TEXT")
        {
            command.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}';";
            if (Convert.ToInt64(command.ExecuteScalar() ?? 0L) > 0) return;

            command.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {columnType};";
            command.ExecuteNonQuery();
        }

        public void SaveTrade(ulong messageId, DateTimeOffset alertTime, string trader, string action, string ticker, string type, double strike, string expiry, double price, string risk, string raw)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        INSERT OR IGNORE INTO TradeAlerts (DiscordMessageId, TraderName, ActionType, Ticker, OptionType, Strike, Expiration, ContractSymbol, ExecutionPrice, RiskCategory, AlertTimestamp, Timestamp, RawMessage)
                        VALUES ($msgId, $trader, $action, $ticker, $type, $strike, $expiry, $symbol, $price, $risk, $alertTime, $time, $raw);";
                    
                    command.Parameters.AddWithValue("$msgId", (long)messageId);
                    command.Parameters.AddWithValue("$trader", trader);
                    command.Parameters.AddWithValue("$action", action);
                    command.Parameters.AddWithValue("$ticker", ticker);
                    command.Parameters.AddWithValue("$type", type);
                    command.Parameters.AddWithValue("$strike", strike);
                    command.Parameters.AddWithValue("$expiry", expiry);
                    command.Parameters.AddWithValue("$symbol", FormatContractSymbol(ticker, expiry, strike, type));
                    command.Parameters.AddWithValue("$price", price);
                    command.Parameters.AddWithValue("$risk", risk);
                    command.Parameters.AddWithValue("$alertTime", UtcStamp(alertTime));
                    command.Parameters.AddWithValue("$time", UtcStamp(DateTimeOffset.UtcNow));
                    command.Parameters.AddWithValue("$raw", raw);

                    int rowsAffected = command.ExecuteNonQuery();
                    if (rowsAffected > 0)
                    {
                        Console.WriteLine($"✅ [DATABASE WRITE SUCCESS]");
                        Console.WriteLine($"   └── Alert Tracked: {ticker} {expiry} {strike} {type} @ ${price}\n");
                        LogAudit(messageId, ticker, "INTAKE_PARSER", "SUCCESS", $"Parsed {action} from {trader} successfully.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [DB ERROR]: {ex.Message}");
            }
        }

        public TradeContractContext? FindSingleCurrentEntry(string trader, string ticker)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Ticker, OptionType, Strike, Expiration
                FROM TradeAlerts
                WHERE UPPER(TraderName) = UPPER($trader)
                  AND UPPER(Ticker) = UPPER($ticker)
                  AND ActionType IN ('BUY', 'AVERAGE_DOWN')
                  AND Expiration >= $today
                GROUP BY Ticker, OptionType, Strike, Expiration
                ORDER BY MAX(COALESCE(AlertTimestamp, Timestamp)) DESC
                LIMIT 2;";
            command.Parameters.AddWithValue("$trader", trader);
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$today", ExpirationResolver.EasternToday().ToString("yyyyMMdd"));

            using var reader = command.ExecuteReader();
            TradeContractContext? match = null;
            int count = 0;
            while (reader.Read())
            {
                count++;
                if (count == 1)
                {
                    match = new TradeContractContext(
                        reader.GetString(0), reader.GetString(1), reader.GetDouble(2), reader.GetString(3));
                }
            }

            if (count > 1)
            {
                Console.WriteLine($"⚠️ [PARSER CONTEXT] Multiple current {ticker} contracts found for {trader}; ticker-only exit was not inferred.");
                return null;
            }

            return match;
        }

        // 🔥 REFACTORED: Now accepts ClientId, OptionType, and Strike to satisfy the unique primary key constraint
        public void LogExecutedOrder(int ibOrderId, int clientId, ulong discordMessageId, string ticker, string optionType, double strike, string expiry, string action, int quantity, string status, int? parentOrderId, string orderRole, double limitPrice)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    
                    command.CommandText = @"
                        INSERT OR REPLACE INTO ExecutedOrders (IbOrderId, ClientId, DiscordMessageId, Ticker, OptionType, Strike, Expiration, ContractSymbol, ActionType, Quantity, Status, Timestamp, ParentOrderId, OrderRole, LimitPrice)
                        VALUES ($ibId, $clientId, $discordId, $ticker, $optType, $strike, $expiry, $symbol, $action, $qty, $status, $time, $parentOrderId, $orderRole, $limitPrice);";
                    
                    command.Parameters.AddWithValue("$ibId", ibOrderId);
                    command.Parameters.AddWithValue("$clientId", clientId);
                    command.Parameters.AddWithValue("$discordId", (long)discordMessageId);
                    command.Parameters.AddWithValue("$ticker", ticker);
                    command.Parameters.AddWithValue("$optType", optionType);
                    command.Parameters.AddWithValue("$strike", strike);
                    command.Parameters.AddWithValue("$expiry", expiry);
                    command.Parameters.AddWithValue("$symbol", FormatContractSymbol(ticker, expiry, strike, optionType));
                    command.Parameters.AddWithValue("$action", action);
                    command.Parameters.AddWithValue("$qty", quantity);
                    command.Parameters.AddWithValue("$status", status);
                    command.Parameters.AddWithValue("$time", UtcStamp(DateTimeOffset.UtcNow));
                    command.Parameters.AddWithValue("$parentOrderId", (object?)parentOrderId ?? DBNull.Value);
                    command.Parameters.AddWithValue("$orderRole", orderRole);
                    command.Parameters.AddWithValue("$limitPrice", limitPrice);

                    command.ExecuteNonQuery();
                    Console.WriteLine($"📝 [OMS TRACKER] Multi-Composite Order Rule Locked: ID #{ibOrderId} | Client {clientId} | {ticker} {strike}{optionType} saved successfully.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [OMS DB ERROR]: {ex.Message}");
            }
        }

        public bool UpdateOrderStatus(int ibOrderId, int clientId, string status)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    // Matches on OrderId and ClientId together so parallel API clients cannot cross-update rows.
                    command.CommandText = "UPDATE ExecutedOrders SET Status = $status WHERE IbOrderId = $id AND ClientId = $clientId;";
                    command.Parameters.AddWithValue("$status", status);
                    command.Parameters.AddWithValue("$id", ibOrderId);
                    command.Parameters.AddWithValue("$clientId", clientId);
                    return command.ExecuteNonQuery() > 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [ORDER STATUS DB ERROR]: {ex.Message}");
                return false;
            }
        }

        public bool UpdateKnownOrderStatus(int ibOrderId, int clientId, string status)
        {
            if (UpdateOrderStatus(ibOrderId, clientId, status)) return true;

            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = "UPDATE ExecutedOrders SET Status = $status WHERE IbOrderId = $id;";
                    command.Parameters.AddWithValue("$status", status);
                    command.Parameters.AddWithValue("$id", ibOrderId);
                    return command.ExecuteNonQuery() > 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [ORDER STATUS DB ERROR]: {ex.Message}");
                return false;
            }
        }

        public bool LogExecution(string executionId, int ibOrderId, int clientId, ulong discordMessageId, string action, decimal quantity, double price, DateTimeOffset executedAt)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT OR IGNORE INTO OrderExecutions
                    (ExecutionId, IbOrderId, ClientId, DiscordMessageId, ActionType, Quantity, Price, ExecutedAt)
                VALUES ($executionId, $orderId, $clientId, $messageId, $action, $quantity, $price, $executedAt);";
            command.Parameters.AddWithValue("$executionId", executionId);
            command.Parameters.AddWithValue("$orderId", ibOrderId);
            command.Parameters.AddWithValue("$clientId", clientId);
            command.Parameters.AddWithValue("$messageId", (long)discordMessageId);
            command.Parameters.AddWithValue("$action", action);
            command.Parameters.AddWithValue("$quantity", quantity);
            command.Parameters.AddWithValue("$price", price);
            command.Parameters.AddWithValue("$executedAt", UtcStamp(executedAt));
            return command.ExecuteNonQuery() > 0;
        }

        public TrackedOrderContext? FindTrackedOrder(int ibOrderId, int clientId)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT o.DiscordMessageId, t.TraderName, o.ActionType, o.Ticker, o.OptionType, o.Strike,
                       o.Expiration, o.LimitPrice, t.RiskCategory, o.Quantity, o.OrderRole
                FROM ExecutedOrders o
                JOIN TradeAlerts t ON t.DiscordMessageId = o.DiscordMessageId
                WHERE o.IbOrderId = $orderId
                ORDER BY CASE WHEN o.ClientId = $clientId THEN 0 ELSE 1 END
                LIMIT 1;";
            command.Parameters.AddWithValue("$orderId", ibOrderId);
            command.Parameters.AddWithValue("$clientId", clientId);

            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read()) return null;

            return new TrackedOrderContext(
                (ulong)reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetDouble(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? 0 : reader.GetDouble(7),
                reader.GetString(8),
                reader.GetInt32(9),
                reader.GetString(10));
        }

        public OpenTradeContext? FindOpenTrade(string ticker, string optionType, double strike, string expiration)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                WITH matching_trades AS (
                    SELECT DiscordMessageId, Ticker, OptionType, Strike, Expiration
                    FROM TradeAlerts
                    WHERE UPPER(Ticker) = UPPER($ticker)
                      AND UPPER(OptionType) = UPPER($optionType)
                      AND ABS(Strike - $strike) < 0.0001
                      AND Expiration = $expiration
                ), execution_totals AS (
                    SELECT DiscordMessageId,
                           SUM(CASE WHEN ActionType = 'BUY' THEN Quantity ELSE 0 END) AS Bought,
                           SUM(CASE WHEN ActionType = 'SELL' THEN Quantity ELSE 0 END) AS Sold
                    FROM OrderExecutions
                    GROUP BY DiscordMessageId
                ), order_totals AS (
                    SELECT DiscordMessageId,
                           SUM(CASE WHEN ActionType = 'BUY' AND Status IN ('FILLED', 'PARTIALLY_FILLED', 'SUBMITTED', 'PRESUBMITTED') THEN Quantity ELSE 0 END) AS BuyOrders
                    FROM ExecutedOrders
                    GROUP BY DiscordMessageId
                )
                SELECT t.DiscordMessageId, t.Ticker, t.OptionType, t.Strike, t.Expiration,
                       CAST(MAX(COALESCE(e.Bought, o.BuyOrders, 0) - COALESCE(e.Sold, 0), 0) AS INTEGER) AS OpenQuantity
                FROM matching_trades t
                LEFT JOIN execution_totals e ON e.DiscordMessageId = t.DiscordMessageId
                LEFT JOIN order_totals o ON o.DiscordMessageId = t.DiscordMessageId
                WHERE COALESCE(e.Bought, o.BuyOrders, 0) > COALESCE(e.Sold, 0)
                ORDER BY t.DiscordMessageId DESC
                LIMIT 1;";
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$optionType", optionType);
            command.Parameters.AddWithValue("$strike", strike);
            command.Parameters.AddWithValue("$expiration", expiration);

            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read()) return null;

            return new OpenTradeContext(
                (ulong)reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDouble(3),
                reader.GetString(4),
                reader.GetInt32(5));
        }

        public IReadOnlyList<ManualExitPriceCandidate> GetManualExitsMissingPrice()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT DiscordMessageId, Ticker, OptionType, Strike, Expiration, Quantity
                FROM ExecutedOrders
                WHERE ActionType = 'SELL'
                  AND OrderRole = 'MANUAL_EXIT'
                  AND Status = 'FILLED'
                  AND COALESCE(LimitPrice, 0) <= 0
                                    AND DiscordMessageId IN (
                                            SELECT DiscordMessageId
                                            FROM TradeAlerts
                                            WHERE date(COALESCE(AlertTimestamp, Timestamp)) = date(COALESCE((SELECT Value FROM AppSettings WHERE Key = 'SelectedTradeDate'), date('now')))
                                    )
                ORDER BY Timestamp DESC;";

            var result = new List<ManualExitPriceCandidate>();
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new ManualExitPriceCandidate(
                    (ulong)reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetDouble(3),
                    reader.GetString(4),
                    reader.GetInt32(5)));
            }

            return result;
        }

        public void LogManualExitOrder(int ibOrderId, int clientId, OpenTradeContext trade, decimal quantity, double price)
        {
            LogExecutedOrder(ibOrderId, clientId, trade.DiscordMessageId, trade.Ticker, trade.OptionType, trade.Strike,
                trade.Expiration, "SELL", (int)Math.Ceiling(quantity), "FILLED", null, "MANUAL_EXIT", price);
            LogAudit(trade.DiscordMessageId, trade.Ticker, "EXTERNAL_EXECUTION_SYNC", "FILLED",
                $"Imported manual/external SELL execution from TWS: order #{ibOrderId}, {quantity} contract(s) @ ${price:F2}.");
        }

        public int ReconcileClosedPositions(IReadOnlyDictionary<string, decimal> optionPositions)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
                WITH execution_totals AS (
                    SELECT DiscordMessageId,
                           SUM(CASE WHEN ActionType = 'BUY' THEN Quantity ELSE 0 END) AS Bought,
                           SUM(CASE WHEN ActionType = 'SELL' THEN Quantity ELSE 0 END) AS Sold
                    FROM OrderExecutions
                    GROUP BY DiscordMessageId
                ), order_totals AS (
                    SELECT DiscordMessageId,
                           SUM(CASE WHEN ActionType = 'BUY' AND Status IN ('FILLED', 'PARTIALLY_FILLED', 'SUBMITTED', 'PRESUBMITTED') THEN Quantity ELSE 0 END) AS BuyOrders,
                           SUM(CASE WHEN ActionType = 'SELL' AND Status = 'FILLED' THEN Quantity ELSE 0 END) AS SellOrders
                    FROM ExecutedOrders
                    GROUP BY DiscordMessageId
                )
                SELECT t.DiscordMessageId, t.Ticker, t.OptionType, t.Strike, t.Expiration,
                       COALESCE(e.Bought, o.BuyOrders, 0) - MAX(COALESCE(e.Sold, 0), COALESCE(o.SellOrders, 0)) AS OpenQuantity
                FROM TradeAlerts t
                LEFT JOIN execution_totals e ON e.DiscordMessageId = t.DiscordMessageId
                LEFT JOIN order_totals o ON o.DiscordMessageId = t.DiscordMessageId
                WHERE COALESCE(e.Bought, o.BuyOrders, 0) > MAX(COALESCE(e.Sold, 0), COALESCE(o.SellOrders, 0));";

                 command.CommandText = @"
                  WITH execution_totals AS (
                      SELECT DiscordMessageId,
                          SUM(CASE WHEN ActionType = 'BUY' THEN Quantity ELSE 0 END) AS Bought,
                          SUM(CASE WHEN ActionType = 'SELL' THEN Quantity ELSE 0 END) AS Sold
                      FROM OrderExecutions
                      GROUP BY DiscordMessageId
                  ), order_totals AS (
                      SELECT DiscordMessageId,
                          SUM(CASE WHEN ActionType = 'BUY' AND Status IN ('FILLED', 'PARTIALLY_FILLED', 'SUBMITTED', 'PRESUBMITTED') THEN Quantity ELSE 0 END) AS BuyOrders,
                          SUM(CASE WHEN ActionType = 'SELL' AND Status = 'FILLED' THEN Quantity ELSE 0 END) AS SellOrders
                      FROM ExecutedOrders
                      GROUP BY DiscordMessageId
                  )
                  SELECT t.DiscordMessageId, t.Ticker, t.OptionType, t.Strike, t.Expiration,
                      COALESCE(e.Bought, o.BuyOrders, 0) - MAX(COALESCE(e.Sold, 0), COALESCE(o.SellOrders, 0)) AS OpenQuantity
                  FROM TradeAlerts t
                  LEFT JOIN execution_totals e ON e.DiscordMessageId = t.DiscordMessageId
                  LEFT JOIN order_totals o ON o.DiscordMessageId = t.DiscordMessageId
                  WHERE COALESCE(e.Bought, o.BuyOrders, 0) > MAX(COALESCE(e.Sold, 0), COALESCE(o.SellOrders, 0))
                    AND date(COALESCE(t.AlertTimestamp, t.Timestamp)) = date(COALESCE((SELECT Value FROM AppSettings WHERE Key = 'SelectedTradeDate'), date('now')));";

            var closed = new List<OpenTradeContext>();
            using (SqliteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var trade = new OpenTradeContext(
                        (ulong)reader.GetInt64(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetDouble(3),
                        reader.GetString(4),
                        (int)Math.Ceiling(reader.GetDecimal(5)));

                    string key = FormatOptionPositionKey(trade.Ticker, trade.OptionType, trade.Strike, trade.Expiration);
                    decimal brokerPosition = optionPositions.TryGetValue(key, out decimal position) ? position : 0m;
                    if (brokerPosition <= 0) closed.Add(trade);
                }
            }

            foreach (OpenTradeContext trade in closed)
            {
                int syntheticOrderId = -Math.Abs((int)(trade.DiscordMessageId % int.MaxValue));
                using var upsertOrder = connection.CreateCommand();
                upsertOrder.Transaction = transaction;
                upsertOrder.CommandText = @"
                    INSERT OR REPLACE INTO ExecutedOrders
                        (IbOrderId, ClientId, DiscordMessageId, Ticker, OptionType, Strike, Expiration, ContractSymbol, ActionType, Quantity, Status, Timestamp, ParentOrderId, OrderRole, LimitPrice)
                    VALUES ($orderId, 0, $messageId, $ticker, $optionType, $strike, $expiration, $symbol, 'SELL', $quantity, 'FILLED', $timestamp, NULL, 'MANUAL_EXIT', NULL);";
                upsertOrder.Parameters.AddWithValue("$orderId", syntheticOrderId);
                upsertOrder.Parameters.AddWithValue("$messageId", (long)trade.DiscordMessageId);
                upsertOrder.Parameters.AddWithValue("$ticker", trade.Ticker);
                upsertOrder.Parameters.AddWithValue("$optionType", trade.OptionType);
                upsertOrder.Parameters.AddWithValue("$strike", trade.Strike);
                upsertOrder.Parameters.AddWithValue("$expiration", trade.Expiration);
                upsertOrder.Parameters.AddWithValue("$symbol", FormatContractSymbol(trade.Ticker, trade.Expiration, trade.Strike, trade.OptionType));
                upsertOrder.Parameters.AddWithValue("$quantity", trade.OpenQuantity);
                upsertOrder.Parameters.AddWithValue("$timestamp", UtcStamp(DateTimeOffset.UtcNow));
                upsertOrder.ExecuteNonQuery();

                using var closeStaleTargets = connection.CreateCommand();
                closeStaleTargets.Transaction = transaction;
                closeStaleTargets.CommandText = @"
                    UPDATE ExecutedOrders
                    SET Status = 'EXTERNALLY_CLOSED'
                    WHERE DiscordMessageId = $messageId
                      AND ActionType = 'SELL'
                      AND COALESCE(OrderRole, '') <> 'MANUAL_EXIT'
                      AND Status IN ('PENDING_SUBMIT', 'PRESUBMITTED', 'SUBMITTED', 'PARTIALLY_FILLED', 'PENDING_CANCEL', 'CANCELLED');";
                closeStaleTargets.Parameters.AddWithValue("$messageId", (long)trade.DiscordMessageId);
                closeStaleTargets.ExecuteNonQuery();

                using var audit = connection.CreateCommand();
                audit.Transaction = transaction;
                audit.CommandText = @"
                    INSERT INTO ExecutionAuditLogs (DiscordMessageId, Ticker, LifecycleStep, DecisionStatus, Details, Timestamp)
                    VALUES ($messageId, $ticker, 'POSITION_RECONCILIATION', 'CLOSED', $details, $timestamp);";
                audit.Parameters.AddWithValue("$messageId", (long)trade.DiscordMessageId);
                audit.Parameters.AddWithValue("$ticker", trade.Ticker);
                audit.Parameters.AddWithValue("$details", $"IBKR position snapshot reports zero contracts for {FormatContractSymbol(trade.Ticker, trade.Expiration, trade.Strike, trade.OptionType)}; marked trade closed and stale exit orders externally closed.");
                audit.Parameters.AddWithValue("$timestamp", UtcStamp(DateTimeOffset.UtcNow));
                audit.ExecuteNonQuery();
            }

            transaction.Commit();
            return closed.Count;
        }

        public bool TryImportExternalSellExecution(
            string executionId,
            int ibOrderId,
            int clientId,
            string ticker,
            string optionType,
            double strike,
            string expiration,
            decimal quantity,
            double price,
            DateTimeOffset executedAt)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            using var find = connection.CreateCommand();
            find.Transaction = transaction;
            find.CommandText = @"
                WITH matching_trades AS (
                    SELECT DiscordMessageId, Ticker, OptionType, Strike, Expiration
                    FROM TradeAlerts
                    WHERE UPPER(Ticker) = UPPER($ticker)
                      AND UPPER(OptionType) = UPPER($optionType)
                      AND ABS(Strike - $strike) < 0.0001
                      AND Expiration = $expiration
                ), execution_totals AS (
                    SELECT DiscordMessageId,
                           SUM(CASE WHEN ActionType = 'BUY' THEN Quantity ELSE 0 END) AS Bought,
                           SUM(CASE WHEN ActionType = 'SELL' THEN Quantity ELSE 0 END) AS Sold
                    FROM OrderExecutions
                    GROUP BY DiscordMessageId
                ), order_totals AS (
                    SELECT DiscordMessageId,
                           SUM(CASE WHEN ActionType = 'BUY' AND Status IN ('FILLED', 'PARTIALLY_FILLED', 'SUBMITTED', 'PRESUBMITTED') THEN Quantity ELSE 0 END) AS BuyOrders
                    FROM ExecutedOrders
                    GROUP BY DiscordMessageId
                )
                SELECT t.DiscordMessageId, t.Ticker, t.OptionType, t.Strike, t.Expiration,
                       COALESCE(e.Bought, o.BuyOrders, 0) - COALESCE(e.Sold, 0) AS OpenQuantity
                FROM matching_trades t
                LEFT JOIN execution_totals e ON e.DiscordMessageId = t.DiscordMessageId
                LEFT JOIN order_totals o ON o.DiscordMessageId = t.DiscordMessageId
                WHERE COALESCE(e.Bought, o.BuyOrders, 0) > COALESCE(e.Sold, 0)
                ORDER BY t.DiscordMessageId DESC
                LIMIT 1;";
            find.Parameters.AddWithValue("$ticker", ticker);
            find.Parameters.AddWithValue("$optionType", optionType);
            find.Parameters.AddWithValue("$strike", strike);
            find.Parameters.AddWithValue("$expiration", expiration);

            using SqliteDataReader reader = find.ExecuteReader();
            if (!reader.Read())
            {
                reader.Close();
                return TryBackfillClosedManualExit(connection, transaction, executionId, ibOrderId, clientId,
                    ticker, optionType, strike, expiration, quantity, price, executedAt);
            }

            ulong discordMessageId = (ulong)reader.GetInt64(0);
            string matchedTicker = reader.GetString(1);
            string matchedOptionType = reader.GetString(2);
            double matchedStrike = reader.GetDouble(3);
            string matchedExpiration = reader.GetString(4);
            decimal openQuantity = reader.GetDecimal(5);
            reader.Close();

            decimal importedQuantity = Math.Min(quantity, openQuantity);
            if (importedQuantity <= 0) return false;

            using var insertExecution = connection.CreateCommand();
            insertExecution.Transaction = transaction;
            insertExecution.CommandText = @"
                INSERT OR REPLACE INTO OrderExecutions
                    (ExecutionId, IbOrderId, ClientId, DiscordMessageId, ActionType, Quantity, Price, ExecutedAt)
                VALUES ($executionId, $orderId, $clientId, $messageId, 'SELL', $quantity, $price, $executedAt);";
            insertExecution.Parameters.AddWithValue("$executionId", executionId);
            insertExecution.Parameters.AddWithValue("$orderId", ibOrderId);
            insertExecution.Parameters.AddWithValue("$clientId", clientId);
            insertExecution.Parameters.AddWithValue("$messageId", (long)discordMessageId);
            insertExecution.Parameters.AddWithValue("$quantity", importedQuantity);
            insertExecution.Parameters.AddWithValue("$price", price);
            insertExecution.Parameters.AddWithValue("$executedAt", UtcStamp(executedAt));
            insertExecution.ExecuteNonQuery();

            using var upsertOrder = connection.CreateCommand();
            upsertOrder.Transaction = transaction;
            upsertOrder.CommandText = @"
                INSERT OR REPLACE INTO ExecutedOrders
                    (IbOrderId, ClientId, DiscordMessageId, Ticker, OptionType, Strike, Expiration, ContractSymbol, ActionType, Quantity, Status, Timestamp, ParentOrderId, OrderRole, LimitPrice)
                VALUES ($orderId, $clientId, $messageId, $ticker, $optionType, $strike, $expiration, $symbol, 'SELL', $quantity, 'FILLED', $timestamp, NULL, 'MANUAL_EXIT', $price);";
            upsertOrder.Parameters.AddWithValue("$orderId", ibOrderId);
            upsertOrder.Parameters.AddWithValue("$clientId", clientId);
            upsertOrder.Parameters.AddWithValue("$messageId", (long)discordMessageId);
            upsertOrder.Parameters.AddWithValue("$ticker", matchedTicker);
            upsertOrder.Parameters.AddWithValue("$optionType", matchedOptionType);
            upsertOrder.Parameters.AddWithValue("$strike", matchedStrike);
            upsertOrder.Parameters.AddWithValue("$expiration", matchedExpiration);
            upsertOrder.Parameters.AddWithValue("$symbol", FormatContractSymbol(matchedTicker, matchedExpiration, matchedStrike, matchedOptionType));
            upsertOrder.Parameters.AddWithValue("$quantity", (int)Math.Ceiling(importedQuantity));
            upsertOrder.Parameters.AddWithValue("$timestamp", UtcStamp(DateTimeOffset.UtcNow));
            upsertOrder.Parameters.AddWithValue("$price", price);
            upsertOrder.ExecuteNonQuery();

                        using var removeSyntheticManualExits = connection.CreateCommand();
                        removeSyntheticManualExits.Transaction = transaction;
                        removeSyntheticManualExits.CommandText = @"
                                DELETE FROM ExecutedOrders
                                WHERE DiscordMessageId = $messageId
                                    AND ActionType = 'SELL'
                                    AND OrderRole = 'MANUAL_EXIT'
                                    AND IbOrderId < 0;";
                        removeSyntheticManualExits.Parameters.AddWithValue("$messageId", (long)discordMessageId);
                        removeSyntheticManualExits.ExecuteNonQuery();

            if (importedQuantity >= openQuantity)
            {
                using var closeStaleTargets = connection.CreateCommand();
                closeStaleTargets.Transaction = transaction;
                closeStaleTargets.CommandText = @"
                    UPDATE ExecutedOrders
                    SET Status = 'EXTERNALLY_CLOSED'
                    WHERE DiscordMessageId = $messageId
                      AND ActionType = 'SELL'
                      AND COALESCE(OrderRole, '') <> 'MANUAL_EXIT'
                      AND Status IN ('PENDING_SUBMIT', 'PRESUBMITTED', 'SUBMITTED', 'PARTIALLY_FILLED', 'PENDING_CANCEL');";
                closeStaleTargets.Parameters.AddWithValue("$messageId", (long)discordMessageId);
                closeStaleTargets.ExecuteNonQuery();
            }

            using var audit = connection.CreateCommand();
            audit.Transaction = transaction;
            audit.CommandText = @"
                INSERT INTO ExecutionAuditLogs (DiscordMessageId, Ticker, LifecycleStep, DecisionStatus, Details, Timestamp)
                VALUES ($messageId, $ticker, 'EXTERNAL_EXECUTION_SYNC', 'FILLED', $details, $timestamp);";
            audit.Parameters.AddWithValue("$messageId", (long)discordMessageId);
            audit.Parameters.AddWithValue("$ticker", matchedTicker);
            audit.Parameters.AddWithValue("$details", $"Imported manual/external SELL execution from TWS: order #{ibOrderId}, {importedQuantity} contract(s) @ ${price:F2}.");
            audit.Parameters.AddWithValue("$timestamp", UtcStamp(DateTimeOffset.UtcNow));
            audit.ExecuteNonQuery();

            transaction.Commit();
            return true;
        }

        private static bool TryBackfillClosedManualExit(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string executionId,
            int ibOrderId,
            int clientId,
            string ticker,
            string optionType,
            double strike,
            string expiration,
            decimal quantity,
            double price,
            DateTimeOffset executedAt)
        {
            using var findClosed = connection.CreateCommand();
            findClosed.Transaction = transaction;
            findClosed.CommandText = @"
                SELECT t.DiscordMessageId, t.Ticker, t.OptionType, t.Strike, t.Expiration, o.Quantity
                FROM TradeAlerts t
                JOIN ExecutedOrders o ON o.DiscordMessageId = t.DiscordMessageId
                WHERE UPPER(t.Ticker) = UPPER($ticker)
                  AND UPPER(t.OptionType) = UPPER($optionType)
                  AND ABS(t.Strike - $strike) < 0.0001
                  AND t.Expiration = $expiration
                  AND o.ActionType = 'SELL'
                  AND o.OrderRole = 'MANUAL_EXIT'
                  AND o.Status = 'FILLED'
                  AND COALESCE(o.LimitPrice, 0) <= 0
                ORDER BY o.Timestamp DESC
                LIMIT 1;";
            findClosed.Parameters.AddWithValue("$ticker", ticker);
            findClosed.Parameters.AddWithValue("$optionType", optionType);
            findClosed.Parameters.AddWithValue("$strike", strike);
            findClosed.Parameters.AddWithValue("$expiration", expiration);

            using SqliteDataReader closedReader = findClosed.ExecuteReader();
            if (!closedReader.Read()) return false;

            ulong discordMessageId = (ulong)closedReader.GetInt64(0);
            string matchedTicker = closedReader.GetString(1);
            string matchedOptionType = closedReader.GetString(2);
            double matchedStrike = closedReader.GetDouble(3);
            string matchedExpiration = closedReader.GetString(4);
            decimal matchedQuantity = closedReader.GetDecimal(5);
            closedReader.Close();

            decimal importedQuantity = Math.Min(quantity, matchedQuantity);
            if (importedQuantity <= 0) return false;

            using var insertExecution = connection.CreateCommand();
            insertExecution.Transaction = transaction;
            insertExecution.CommandText = @"
                INSERT OR REPLACE INTO OrderExecutions
                    (ExecutionId, IbOrderId, ClientId, DiscordMessageId, ActionType, Quantity, Price, ExecutedAt)
                VALUES ($executionId, $orderId, $clientId, $messageId, 'SELL', $quantity, $price, $executedAt);";
            insertExecution.Parameters.AddWithValue("$executionId", executionId);
            insertExecution.Parameters.AddWithValue("$orderId", ibOrderId);
            insertExecution.Parameters.AddWithValue("$clientId", clientId);
            insertExecution.Parameters.AddWithValue("$messageId", (long)discordMessageId);
            insertExecution.Parameters.AddWithValue("$quantity", importedQuantity);
            insertExecution.Parameters.AddWithValue("$price", price);
            insertExecution.Parameters.AddWithValue("$executedAt", UtcStamp(executedAt));
            insertExecution.ExecuteNonQuery();

            using var updateManualExit = connection.CreateCommand();
            updateManualExit.Transaction = transaction;
            updateManualExit.CommandText = @"
                UPDATE ExecutedOrders
                SET LimitPrice = $price
                WHERE DiscordMessageId = $messageId
                  AND ActionType = 'SELL'
                  AND OrderRole = 'MANUAL_EXIT'
                  AND Status = 'FILLED'
                  AND COALESCE(LimitPrice, 0) <= 0;";
            updateManualExit.Parameters.AddWithValue("$price", price);
            updateManualExit.Parameters.AddWithValue("$messageId", (long)discordMessageId);
            updateManualExit.ExecuteNonQuery();

                        using var removeSyntheticManualExits = connection.CreateCommand();
                        removeSyntheticManualExits.Transaction = transaction;
                        removeSyntheticManualExits.CommandText = @"
                                DELETE FROM ExecutedOrders
                                WHERE DiscordMessageId = $messageId
                                    AND ActionType = 'SELL'
                                    AND OrderRole = 'MANUAL_EXIT'
                                    AND IbOrderId < 0;";
                        removeSyntheticManualExits.Parameters.AddWithValue("$messageId", (long)discordMessageId);
                        removeSyntheticManualExits.ExecuteNonQuery();

            using var audit = connection.CreateCommand();
            audit.Transaction = transaction;
            audit.CommandText = @"
                INSERT INTO ExecutionAuditLogs (DiscordMessageId, Ticker, LifecycleStep, DecisionStatus, Details, Timestamp)
                VALUES ($messageId, $ticker, 'EXTERNAL_EXECUTION_SYNC', 'PRICE_BACKFILLED', $details, $timestamp);";
            audit.Parameters.AddWithValue("$messageId", (long)discordMessageId);
            audit.Parameters.AddWithValue("$ticker", matchedTicker);
            audit.Parameters.AddWithValue("$details", $"Backfilled manual exit price from TWS execution history: {FormatContractSymbol(matchedTicker, matchedExpiration, matchedStrike, matchedOptionType)}, {importedQuantity} contract(s) @ ${price:F2}.");
            audit.Parameters.AddWithValue("$timestamp", UtcStamp(DateTimeOffset.UtcNow));
            audit.ExecuteNonQuery();

            transaction.Commit();
            return true;
        }

        public void UpdateExecutionCommission(string executionId, double commission, double realizedPnl)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE OrderExecutions
                SET Commission = $commission, RealizedPnl = $realizedPnl
                WHERE ExecutionId = $executionId;";
            command.Parameters.AddWithValue("$executionId", executionId);
            command.Parameters.AddWithValue("$commission", commission);
            command.Parameters.AddWithValue("$realizedPnl", realizedPnl);
            command.ExecuteNonQuery();
        }

        public void CreatePendingApproval(ulong discordMessageId, TradeConfirmationDetails details)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO PendingApprovals (DiscordMessageId, DetailsJson, Status, SelectedBudget, CreatedAt, DecidedAt)
                VALUES ($messageId, $details, 'PENDING', NULL, $createdAt, NULL)
                ON CONFLICT(DiscordMessageId) DO UPDATE SET
                    DetailsJson = excluded.DetailsJson,
                    Status = 'PENDING',
                    SelectedBudget = NULL,
                    CreatedAt = excluded.CreatedAt,
                    DecidedAt = NULL;";
            command.Parameters.AddWithValue("$messageId", (long)discordMessageId);
            command.Parameters.AddWithValue("$details", JsonSerializer.Serialize(details));
            command.Parameters.AddWithValue("$createdAt", UtcStamp(DateTimeOffset.UtcNow));
            command.ExecuteNonQuery();
        }

        public BrowserApprovalDecision WaitForBrowserApproval(ulong discordMessageId, TimeSpan timeout)
        {
            DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(timeout);
            while (DateTimeOffset.UtcNow < deadline)
            {
                BrowserApprovalDecision decision = ReadBrowserApproval(discordMessageId);
                if (decision.Status is "APPROVED" or "REJECTED") return decision;
                Thread.Sleep(250);
            }

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE PendingApprovals
                SET Status = 'FALLBACK', DecidedAt = $decidedAt
                WHERE DiscordMessageId = $messageId AND Status = 'PENDING';";
            command.Parameters.AddWithValue("$messageId", (long)discordMessageId);
            command.Parameters.AddWithValue("$decidedAt", UtcStamp(DateTimeOffset.UtcNow));
            if (command.ExecuteNonQuery() > 0) return new BrowserApprovalDecision("FALLBACK", null);

            return ReadBrowserApproval(discordMessageId);
        }

        private BrowserApprovalDecision ReadBrowserApproval(ulong discordMessageId)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Status, SelectedBudget FROM PendingApprovals WHERE DiscordMessageId = $messageId;";
            command.Parameters.AddWithValue("$messageId", (long)discordMessageId);
            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read()) return new BrowserApprovalDecision("MISSING", null);
            return new BrowserApprovalDecision(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetDouble(1));
        }

        public void LogAudit(ulong discordMessageId, string ticker, string lifecycleStep, string status, string details)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        INSERT INTO ExecutionAuditLogs (DiscordMessageId, Ticker, LifecycleStep, DecisionStatus, Details, Timestamp)
                        VALUES ($msgId, $ticker, $step, $status, $details, $time);";
                    
                    command.Parameters.AddWithValue("$msgId", (long)discordMessageId);
                    command.Parameters.AddWithValue("$ticker", ticker);
                    command.Parameters.AddWithValue("$step", lifecycleStep);
                    command.Parameters.AddWithValue("$status", status);
                    command.Parameters.AddWithValue("$details", details);
                    command.Parameters.AddWithValue("$time", UtcStamp(DateTimeOffset.UtcNow));

                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [AUDIT LOGGER ERROR]: {ex.Message}");
            }
        }
    }
}
