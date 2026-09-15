using System;
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
            int ibClientId = int.Parse(config["IBKRSettings:ClientId"] ?? "1");
            int ibQty = int.Parse(config["IBKRSettings:DefaultQuantity"] ?? "5");

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
            var executionService = new OrderExecutionService(ibHost, ibPort, ibClientId, ibQty);

            // Pass execution service back over to map relational constraints cleanly
            executionService.SetDatabaseService(databaseService);

            // 4. Initialize & Start the Connection Listener Engine (Passing exactly 5 parameters)
            var botService = new DiscordBotService(botToken, targetChannelId, parser, databaseService, executionService);

            
            Console.WriteLine("🚀 High-Speed Multi-Threaded Trading Pipeline Online.");
            await botService.StartAsync();

            // Keep execution pool context alive indefinitely
            await Task.Delay(-1);
        }
    }
}
