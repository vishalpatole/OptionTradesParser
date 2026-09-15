using System;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

namespace OptionTradesParser
{
    public class DiscordBotService
    {
        private readonly DiscordSocketClient _client;
        private readonly string _botToken;
        private readonly ulong _targetChannelId;
        private readonly MessageParser _parser;
        private readonly DatabaseService _dbService;
        private readonly OrderExecutionService _executionService; 

        public DiscordBotService(
            string token, 
            ulong channelId, 
            MessageParser parser, 
            DatabaseService dbService, 
            OrderExecutionService executionService)
        {
            _botToken = token;
            _targetChannelId = channelId;
            _parser = parser;
            _dbService = dbService;
            _executionService = executionService;

            var config = new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent
            };
            _client = new DiscordSocketClient(config);

            _client.Log += LogAsync;
            _client.MessageReceived += MessageReceivedAsync;
        }

        public async Task StartAsync()
        {
            await _client.LoginAsync(TokenType.Bot, _botToken);
            await _client.StartAsync();
        }

        private Task LogAsync(LogMessage log)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Discord Systems] {log.Message}");
            return Task.CompletedTask;
        }

        private async Task MessageReceivedAsync(SocketMessage message)
        {
            if (message.Author.IsBot) return;
            if (message.Channel.Id != _targetChannelId) return;

            string content = message.Content;
            
            Console.WriteLine("\n📥 [NEW ACTIVITY DETECTED]");
            Console.WriteLine($"   ├── Message ID: {message.Id}");
            Console.WriteLine($"   ├── From User:  {message.Author.Username}");
            Console.WriteLine("   └── Content Analysis Step Commenced...");

            var result = _parser.TryParse(content);

            if (result != null)
            {
                // 1. Offload database tracking routines to distinct task threads
                _ = Task.Run(() => _dbService.SaveTrade(
                    message.Id, result.TraderName, result.ActionType, result.Ticker, result.OptionType, result.Strike, result.Expiration, result.PricePaid, result.RiskCategory, content));

                // 2. 🔥 THE FIX: Position parameters mapped exactly to match OrderExecutionService signature definitions
                _ = Task.Run(() => _executionService.ProcessIncomingAlert(
                    message.Id, 
                    result.TraderName, 
                    result.ActionType, 
                    result.Ticker, 
                    result.OptionType, 
                    result.Strike, 
                    result.Expiration, 
                    result.PricePaid));
            }
            else
            {
                Console.WriteLine("❌ [PIPELINE OUTPUT] Message marked as UNHANDLED or CHATTER. Data dropped cleanly.\n");
            }

            await Task.CompletedTask;
        }
    }
}
