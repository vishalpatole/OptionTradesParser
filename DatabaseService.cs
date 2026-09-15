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
                        ExecutionPrice REAL,
                        RiskCategory TEXT,
                        Timestamp TEXT,
                        RawMessage TEXT
                    );";
                command.ExecuteNonQuery();

                // 2. 🔥 REFACTORED: Added ClientId, OptionType, Strike and converted to a 5-Column Composite Primary Key
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS ExecutedOrders (
                        IbOrderId INTEGER,
                        ClientId INTEGER,
                        DiscordMessageId INTEGER,
                        Ticker TEXT,
                        OptionType TEXT,
                        Strike REAL,
                        ActionType TEXT,
                        Quantity INTEGER,
                        Status TEXT,
                        Timestamp TEXT,
                        PRIMARY KEY (IbOrderId, ClientId, Ticker, OptionType, Strike), 
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
            }
        }

        public void SaveTrade(ulong messageId, string trader, string action, string ticker, string type, double strike, string expiry, double price, string risk, string raw)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        INSERT OR IGNORE INTO TradeAlerts (DiscordMessageId, TraderName, ActionType, Ticker, OptionType, Strike, Expiration, ExecutionPrice, RiskCategory, Timestamp, RawMessage)
                        VALUES ($msgId, $trader, $action, $ticker, $type, $strike, $expiry, $price, $risk, $time, $raw);";
                    
                    command.Parameters.AddWithValue("$msgId", (long)messageId);
                    command.Parameters.AddWithValue("$trader", trader);
                    command.Parameters.AddWithValue("$action", action);
                    command.Parameters.AddWithValue("$ticker", ticker);
                    command.Parameters.AddWithValue("$type", type);
                    command.Parameters.AddWithValue("$strike", strike);
                    command.Parameters.AddWithValue("$expiry", expiry);
                    command.Parameters.AddWithValue("$price", price);
                    command.Parameters.AddWithValue("$risk", risk);
                    command.Parameters.AddWithValue("$time", DateTime.UtcNow.ToString("o"));
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

        // 🔥 REFACTORED: Now accepts ClientId, OptionType, and Strike to satisfy the unique primary key constraint
        public void LogExecutedOrder(int ibOrderId, int clientId, ulong discordMessageId, string ticker, string optionType, double strike, string action, int quantity, string status)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    
                    command.CommandText = @"
                        INSERT OR REPLACE INTO ExecutedOrders (IbOrderId, ClientId, DiscordMessageId, Ticker, OptionType, Strike, ActionType, Quantity, Status, Timestamp)
                        VALUES ($ibId, $clientId, $discordId, $ticker, $optType, $strike, $action, $qty, $status, $time);";
                    
                    command.Parameters.AddWithValue("$ibId", ibOrderId);
                    command.Parameters.AddWithValue("$clientId", clientId);
                    command.Parameters.AddWithValue("$discordId", (long)discordMessageId);
                    command.Parameters.AddWithValue("$ticker", ticker);
                    command.Parameters.AddWithValue("$optType", optionType);
                    command.Parameters.AddWithValue("$strike", strike);
                    command.Parameters.AddWithValue("$action", action);
                    command.Parameters.AddWithValue("$qty", quantity);
                    command.Parameters.AddWithValue("$status", status);
                    command.Parameters.AddWithValue("$time", DateTime.UtcNow.ToString("o"));

                    command.ExecuteNonQuery();
                    Console.WriteLine($"📝 [OMS TRACKER] Multi-Composite Order Rule Locked: ID #{ibOrderId} | Client {clientId} | {ticker} {strike}{optionType} saved successfully.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [OMS DB ERROR]: {ex.Message}");
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
                    command.Parameters.AddWithValue("$time", DateTime.UtcNow.ToString("o"));

                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [AUDIT LOGGER ERROR]: {ex.Message}");
            }
        }

        public bool HasExistingOpenPosition(string ticker)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        SELECT COUNT(*) FROM ExecutedOrders 
                        WHERE Ticker = $ticker AND ActionType = 'BUY' AND Status = 'FILLED';";
                    command.Parameters.AddWithValue("$ticker", ticker);
                    
                    long count = (long)(command.ExecuteScalar() ?? 0L);
                    return count > 0;
                }
            }
            catch
            {
                return false; 
            }
        }
    }
}
