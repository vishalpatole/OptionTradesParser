using System;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace OptionTradesParser
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Build Application Configuration System
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("AppConfig.json", optional: false, reloadOnChange: true)
                .Build();

            // Extraction Parsing Coordinates
            string dbConnectionString = config["DatabaseSettings:ConnectionString"] ?? "Data Source=trade_alerts.db";
            string botToken = config["DiscordSettings:BotToken"] ?? "";
            string rawChannelId = config["DiscordSettings:TargetChannelId"] ?? "0";

            // IBKR API Parsing Coordinates
            string ibHost = config["IBKRSettings:Host"] ?? "127.0.0.1";
            int ibPort = int.Parse(config["IBKRSettings:Port"] ?? "7497");
            (int ibClientId, int ibClientIdMin, int ibClientIdMax) = ResolveClientId(config);
            string ibAccountType = (config["IBKRSettings:AccountType"] ?? "PAPER").Trim();
            int browserApprovalTimeoutSeconds = int.TryParse(config["ApprovalSettings:BrowserTimeoutSeconds"], out int configuredTimeout)
                ? Math.Clamp(configuredTimeout, 5, 300)
                : 20;
            double[] orderBudgets = config.GetSection("IBKRSettings:OrderBudgets")
                .GetChildren()
                .Select(item => double.TryParse(item.Value, out double budget) ? budget : 0)
                .Where(budget => budget > 0)
                .ToArray();
            if (orderBudgets.Length != 3)
            {
                Console.WriteLine("❌ ERROR: IBKRSettings:OrderBudgets must contain exactly three positive dollar amounts.");
                return;
            }
            Console.WriteLine($"🔑 [IBKR] Using dedicated ClientId: {ibClientId}");

            if (!ulong.TryParse(rawChannelId, out ulong targetChannelId) || string.IsNullOrEmpty(botToken) || botToken == "YOUR_ACTUAL_BOT_TOKEN_HERE" || targetChannelId == 0)
            {
                Console.WriteLine("❌ ERROR: Configuration settings in AppConfig.json are missing or invalid. Shutting down.");
                return;
            }

            // 1. Initialize DB Storage Environment
            var databaseService = new DatabaseService(dbConnectionString);
            databaseService.InitializeDatabase();

            // 2. Initialize Parsing Processor
            var parser = new MessageParser();

            // 3. Initialize High-Performance Pre-Trade Confirmation Engine & Connection Socket
            var executionService = new OrderExecutionService(ibHost, ibPort, ibClientId, ibClientIdMin, ibClientIdMax, orderBudgets, ibAccountType, TimeSpan.FromSeconds(browserApprovalTimeoutSeconds));

            // Pass execution service back over to map relational constraints cleanly
            executionService.SetDatabaseService(databaseService);

            if (!executionService.IsReady)
            {
                Console.WriteLine("⛔ [STARTUP BLOCKED] IBKR did not complete the nextValidId handshake. Discord listener will not start until broker routing is safe.");
                bool ready = await executionService.WaitUntilReadyAsync();
                if (!ready)
                {
                    Console.WriteLine("❌ [STARTUP FAILED] IBKR API handshake never completed. Fix TWS API connection, then restart this app.");
                    WriteStartupMarker("OTP_STARTUP_FAIL_FILE", "IBKR API handshake never completed.");
                    executionService.Disconnect();
                    return;
                }
            }

            WriteStartupMarker("OTP_STARTUP_READY_FILE", $"IBKR ready with ClientId {ibClientId}.");

            // Ctrl+C, window close, or an unhandled crash should still log the TWS session off cleanly,
            // otherwise it lingers as a ghost session that can contribute to future connection resets.
            AppDomain.CurrentDomain.ProcessExit += (_, _) => executionService.Disconnect();
            Console.CancelKeyPress += (_, _) => executionService.Disconnect();

            // 4. Initialize & Start the Connection Listener Engine (Passing exactly 5 parameters)
            var botService = new DiscordBotService(botToken, targetChannelId, parser, databaseService, executionService);

            
            Console.WriteLine("🚀 High-Speed Multi-Threaded Trading Pipeline Online.");
            await botService.StartAsync();

            // Keep execution pool context alive indefinitely
            await Task.Delay(-1);
        }

        private static (int ClientId, int MinClientId, int MaxClientId) ResolveClientId(IConfiguration config)
        {
            bool hasMin = int.TryParse(config["IBKRSettings:ClientIdMin"], out int minClientId);
            bool hasMax = int.TryParse(config["IBKRSettings:ClientIdMax"], out int maxClientId);
            if (hasMin && hasMax)
            {
                if (minClientId <= 0 || maxClientId < minClientId)
                    throw new InvalidOperationException("IBKRSettings:ClientIdMin/ClientIdMax must define a positive range.");

                int selectedClientId = Random.Shared.Next(minClientId, maxClientId + 1);
                Console.WriteLine($"🎲 [IBKR] Selected random ClientId {selectedClientId} from configured range {minClientId}-{maxClientId}.");
                return (selectedClientId, minClientId, maxClientId);
            }

            int fixedClientId = int.Parse(config["IBKRSettings:ClientId"] ?? "17");
            return (fixedClientId, fixedClientId, fixedClientId);
        }

        private static void WriteStartupMarker(string environmentVariable, string message)
        {
            string? path = Environment.GetEnvironmentVariable(environmentVariable);
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                File.WriteAllText(path, $"{DateTimeOffset.UtcNow:o} {message}{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ [STARTUP MARKER] Could not write {environmentVariable}: {ex.Message}");
            }
        }
    }
}
