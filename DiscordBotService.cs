using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

            bool isForwarded = message is IUserMessage forwardCheck && forwardCheck.ForwardedMessages.Count > 0;
            string dump = BuildRawMessageDump(message, isForwarded);
            string content = BuildParseableText(message);

            // Persisted for every message (not just forwarded) so a correct-looking parse can still be audited later.
            RawMessageDumpWriter.Save(message.Id, message.Timestamp, dump);

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write(dump);
            Console.WriteLine("   ├── Parseable Text: " + content.Replace("\n", " ↵ "));
            Console.ResetColor();

            var result = _parser.TryParse(content, _dbService.FindSingleCurrentEntry);

            if (result != null)
            {
                Console.WriteLine("✅ [PARSED TRADE]");
                Console.WriteLine($"   ├── Trader:   {result.TraderName}");
                Console.WriteLine($"   ├── Action:   {result.ActionType}{(result.SizingAmbiguous ? " (size requires confirmation)" : string.Empty)}");
                Console.WriteLine($"   ├── Ticker:   {result.Ticker}{(result.ContractInferred ? " (inferred from prior alert)" : string.Empty)}");
                Console.WriteLine($"   ├── Type:     {result.OptionType}");
                Console.WriteLine($"   ├── Strike:   {result.Strike}");
                Console.WriteLine($"   ├── Expiry:   {result.Expiration}");
                Console.WriteLine($"   ├── Price:    ${result.PricePaid:F2}");
                Console.WriteLine($"   └── Risk:     {result.RiskCategory}");

                // Persist before returning so an immediate follow-up trim can resolve this contract.
                _dbService.SaveTrade(
                    message.Id, message.Timestamp, result.TraderName, result.ActionType, result.Ticker, result.OptionType, result.Strike, result.Expiration, result.PricePaid, result.RiskCategory, content);

                // Keep IBKR validation and confirmation off the Discord event thread.
                _ = Task.Run(() => _executionService.ProcessIncomingAlert(message.Id, result));

                Console.WriteLine("🔎 [PARSER DIAGNOSTICS] Cross-check against the fields above — flag it if anything here looks off:");
                foreach (var line in MessageParser.Diagnose(content).Split('\n'))
                    Console.WriteLine($"   │ {line}");
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine("❌ [PIPELINE OUTPUT] Message marked as UNHANDLED or CHATTER.");
                Console.WriteLine("🔎 [PARSER DIAGNOSTICS] Best-effort read of whatever the parser could still find:");
                foreach (var line in MessageParser.Diagnose(content).Split('\n'))
                    Console.WriteLine($"   │ {line}");
                Console.WriteLine("   └── Data dropped cleanly. Nothing above was acted on.\n");
            }

            await Task.CompletedTask;
        }

        /// Builds the text actually handed to the parser: Content plus every embed's title, description and
        /// fields, for both the message itself and any forwarded snapshot. Forwarded alerts carry the trade
        /// details in the embed, not in Content, so skipping embeds here would leave the parser with nothing.
        private static string BuildParseableText(SocketMessage message)
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(message.Content)) parts.Add(message.Content);
            AppendEmbedText(parts, message.Embeds);

            if (message is IUserMessage userMessage)
            {
                foreach (var snapshot in userMessage.ForwardedMessages)
                {
                    if (!string.IsNullOrWhiteSpace(snapshot.Message.Content)) parts.Add(snapshot.Message.Content);
                    AppendEmbedText(parts, snapshot.Message.Embeds);
                }
            }

            return string.Join("\n", parts);
        }

        private static void AppendEmbedText(List<string> parts, IReadOnlyCollection<IEmbed> embeds)
        {
            foreach (var embed in embeds)
            {
                if (!string.IsNullOrWhiteSpace(embed.Title)) parts.Add(embed.Title);
                if (!string.IsNullOrWhiteSpace(embed.Description)) parts.Add(embed.Description);

                // "Name: Value" so the price/date regexes (which expect a colon or dash after a label) still match.
                foreach (var field in embed.Fields)
                    parts.Add($"{field.Name}: {field.Value}");
            }
        }

        /// Builds a text report of everything Discord actually delivered, for console display and disk persistence.
        private static string BuildRawMessageDump(SocketMessage message, bool isForwarded)
        {
            var sb = new StringBuilder();
            sb.AppendLine("📄 [RAW MESSAGE DUMP]");
            sb.AppendLine($"   ├── Type:        {message.Type}{(isForwarded ? " (forwarded)" : string.Empty)}");
            sb.AppendLine($"   ├── Content:     {(string.IsNullOrWhiteSpace(message.Content) ? "(empty)" : message.Content)}");

            if (message is IUserMessage userMessage && userMessage.ForwardedMessages.Count > 0)
            {
                int i = 0;
                foreach (var snapshot in userMessage.ForwardedMessages)
                {
                    i++;
                    sb.AppendLine($"   ├── Forwarded #{i} Content: {(string.IsNullOrWhiteSpace(snapshot.Message.Content) ? "(empty)" : snapshot.Message.Content)}");
                    AppendEmbedsAndAttachments(sb, snapshot.Message.Embeds, snapshot.Message.Attachments, $"Forwarded #{i} ");
                }
            }

            AppendEmbedsAndAttachments(sb, message.Embeds, message.Attachments, string.Empty);
            return sb.ToString();
        }

        private static void AppendEmbedsAndAttachments(StringBuilder sb, IReadOnlyCollection<IEmbed> embeds, IReadOnlyCollection<IAttachment> attachments, string label)
        {
            int i = 0;
            foreach (var embed in embeds)
            {
                i++;
                sb.AppendLine($"   ├── {label}Embed #{i}: Title=\"{embed.Title}\" Description=\"{embed.Description}\"");
                foreach (var field in embed.Fields)
                    sb.AppendLine($"   │      Field: {field.Name} = {field.Value}");
            }

            foreach (var attachment in attachments)
                sb.AppendLine($"   ├── {label}Attachment: {attachment.Filename} ({attachment.Url})");
        }
    }
}
