using System;
using System.Collections.Generic;
using System.Linq;
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
            // 🔥 ENHANCED TRACE: Expose hidden network connection errors
            if (log.Exception != null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Discord ERROR] {log.Message} | Exception Type: {log.Exception.GetType().Name} | Msg: {log.Exception.Message}");
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Discord Systems] {log.Message}");
            }
            return Task.CompletedTask;
        }


        private async Task MessageReceivedAsync(SocketMessage message)
        {
            if (message.Author.IsBot) return;
            if (message.Channel.Id != _targetChannelId) return;

            Console.WriteLine("\n📥 [NEW ACTIVITY DETECTED]");
            Console.WriteLine($"   ├── Message ID: {message.Id}");
            Console.WriteLine($"   ├── From User:  {message.Author.Username}");
            Console.WriteLine("   └── Content Analysis Step Commenced...");

            string content = ExtractDiagnosticText(message, out bool usedForwardedContent);

            var result = _parser.TryParse(content, _dbService.FindSingleCurrentEntry);

            if (result != null)
            {
                string optionCode = result.OptionType == "CALL" ? "C" : "P";
                Console.WriteLine("✅ [PARSED TRADE]");
                Console.WriteLine($"   ├── Trader:   {result.TraderName}");
                Console.WriteLine($"   ├── Action:   {result.ActionType}{(result.SizingAmbiguous ? " (size requires confirmation)" : string.Empty)}");
                Console.WriteLine($"   ├── Contract: {result.Ticker} {result.Expiration} {result.Strike}{optionCode}{(result.ContractInferred ? " (inferred from prior alert)" : string.Empty)}");
                Console.WriteLine($"   ├── Price:    ${result.PricePaid:F2}");
                Console.WriteLine($"   └── Risk:     {result.RiskCategory}");

                // Persist before returning so an immediate follow-up trim can resolve this contract.
                _dbService.SaveTrade(
                    message.Id, message.Timestamp, result.TraderName, result.ActionType, result.Ticker, result.OptionType, result.Strike, result.Expiration, result.PricePaid, result.RiskCategory, content);

                // Keep IBKR validation and confirmation off the Discord event thread.
                _ = Task.Run(() => _executionService.ProcessIncomingAlert(message.Id, result));
            }
            else
            {
                Console.WriteLine("❌ [PIPELINE OUTPUT] Message marked as UNHANDLED or CHATTER.");
                DumpRawMessage(message, usedForwardedContent);
                Console.WriteLine("🔎 [PARSER DIAGNOSTICS] Best-effort read of whatever the parser could still find:");
                foreach (var line in MessageParser.Diagnose(content).Split('\n'))
                    Console.WriteLine($"   │ {line}");
                Console.WriteLine("   └── Data dropped cleanly. Nothing above was acted on.\n");
            }

            await Task.CompletedTask;
        }

        /// Forwarded Discord messages arrive with an empty Content; the real text lives in ForwardedMessages instead.
        private static string ExtractDiagnosticText(SocketMessage message, out bool usedForwardedContent)
        {
            usedForwardedContent = false;
            if (!string.IsNullOrWhiteSpace(message.Content)) return message.Content;

            if (message is IUserMessage userMessage && userMessage.ForwardedMessages.Count > 0)
            {
                usedForwardedContent = true;
                return string.Join("\n\n", userMessage.ForwardedMessages.Select(s => s.Message.Content));
            }

            return message.Content;
        }

        /// Prints everything Discord actually delivered so a failed parse can be diagnosed from the console alone.
        private static void DumpRawMessage(SocketMessage message, bool usedForwardedContent)
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("📄 [RAW MESSAGE DUMP]");
            Console.WriteLine($"   ├── Type:        {message.Type}{(usedForwardedContent ? " (forwarded)" : string.Empty)}");
            Console.WriteLine($"   ├── Content:     {(string.IsNullOrWhiteSpace(message.Content) ? "(empty)" : message.Content)}");

            if (message is IUserMessage userMessage && userMessage.ForwardedMessages.Count > 0)
            {
                int i = 0;
                foreach (var snapshot in userMessage.ForwardedMessages)
                {
                    i++;
                    Console.WriteLine($"   ├── Forwarded #{i} Content: {(string.IsNullOrWhiteSpace(snapshot.Message.Content) ? "(empty)" : snapshot.Message.Content)}");
                    DumpEmbedsAndAttachments(snapshot.Message.Embeds, snapshot.Message.Attachments, $"Forwarded #{i} ");
                }
            }

            DumpEmbedsAndAttachments(message.Embeds, message.Attachments, string.Empty);
            Console.ResetColor();
        }

        private static void DumpEmbedsAndAttachments(IReadOnlyCollection<IEmbed> embeds, IReadOnlyCollection<IAttachment> attachments, string label)
        {
            int i = 0;
            foreach (var embed in embeds)
            {
                i++;
                Console.WriteLine($"   ├── {label}Embed #{i}: Title=\"{embed.Title}\" Description=\"{embed.Description}\"");
                foreach (var field in embed.Fields)
                    Console.WriteLine($"   │      Field: {field.Name} = {field.Value}");
            }

            foreach (var attachment in attachments)
                Console.WriteLine($"   ├── {label}Attachment: {attachment.Filename} ({attachment.Url})");
        }
    }
}
