using System;
using Microsoft.Data.Sqlite;

namespace OptionTradesParser
{
    public class DatabaseService
    {
        private readonly string _connectionString;

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
                        RawMessage TEXT
                    );";
                command.ExecuteNonQuery();

                AddColumnIfMissing(command, "TradeAlerts", "AlertTimestamp");
                AddColumnIfMissing(command, "TradeAlerts", "ContractSymbol");

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
            }
        }

        /// Every timestamp in this database is UTC ISO-8601 so rows stay comparable regardless of the poster's local zone.
        private static string UtcStamp(DateTimeOffset moment) => moment.UtcDateTime.ToString("o");

        /// Denormalized display/analytics form of a contract, e.g. "INTC 20260917 97C", stored alongside the
        /// individual Ticker/Expiration/Strike/OptionType columns so reports don't need to reassemble it each time.
        public static string FormatContractSymbol(string ticker, string expiry, double strike, string optionType)
            => $"{ticker} {expiry} {strike}{(optionType == "CALL" ? "C" : "P")}";

        private static void AddColumnIfMissing(SqliteCommand command, string table, string column)
        {
            command.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}';";
            if (Convert.ToInt64(command.ExecuteScalar() ?? 0L) > 0) return;

            command.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} TEXT;";
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
        public void LogExecutedOrder(int ibOrderId, int clientId, ulong discordMessageId, string ticker, string optionType, double strike, string expiry, string action, int quantity, string status)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    
                    command.CommandText = @"
                        INSERT OR REPLACE INTO ExecutedOrders (IbOrderId, ClientId, DiscordMessageId, Ticker, OptionType, Strike, Expiration, ContractSymbol, ActionType, Quantity, Status, Timestamp)
                        VALUES ($ibId, $clientId, $discordId, $ticker, $optType, $strike, $expiry, $symbol, $action, $qty, $status, $time);";
                    
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

                    command.ExecuteNonQuery();
                    Console.WriteLine($"📝 [OMS TRACKER] Multi-Composite Order Rule Locked: ID #{ibOrderId} | Client {clientId} | {ticker} {strike}{optionType} saved successfully.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [OMS DB ERROR]: {ex.Message}");
            }
        }

        public void UpdateOrderStatus(int ibOrderId, int clientId, string status)
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
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [ORDER STATUS DB ERROR]: {ex.Message}");
            }
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
