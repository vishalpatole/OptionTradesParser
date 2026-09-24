using System.Text.Json;

namespace OptionTradesParser.Dashboard.Api;

public sealed record AccountConfiguration(string AccountType)
{
    public static AccountConfiguration Load(string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        string accountType = document.RootElement.GetProperty("IBKRSettings").GetProperty("AccountType").GetString() ?? "PAPER";
        return new AccountConfiguration(accountType.ToUpperInvariant());
    }
}
