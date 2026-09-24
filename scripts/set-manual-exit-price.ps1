param(
    [Parameter(Mandatory = $true)]
    [UInt64]$DiscordMessageId,

    [Parameter(Mandatory = $true)]
    [double]$ExitPrice,

    [Parameter(Mandatory = $false)]
    [string]$DatabasePath = (Join-Path $PSScriptRoot '..\trade_alerts.db')
)

$ErrorActionPreference = 'Stop'

if ($ExitPrice -le 0) {
    throw 'ExitPrice must be greater than zero.'
}

$databaseFullPath = [System.IO.Path]::GetFullPath($DatabasePath)
if (-not (Test-Path $databaseFullPath)) {
    throw "Database not found: $databaseFullPath"
}
$databaseLiteral = $databaseFullPath.Replace('\', '\\').Replace('"', '\"')

$temp = Join-Path $env:TEMP ('otp-manual-exit-price-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null

try {
    Push-Location $temp
    dotnet new console --framework net9.0 --no-restore | Out-Null
    dotnet add package Microsoft.Data.Sqlite --version 9.0.0 | Out-Null

    @"
using Microsoft.Data.Sqlite;

var db = "$databaseLiteral";
var messageId = $DiscordMessageId;
var exitPrice = $ExitPrice;

using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db }.ToString());
connection.Open();
using var transaction = connection.BeginTransaction();

using var read = connection.CreateCommand();
read.Transaction = transaction;
read.CommandText = """
SELECT o.DiscordMessageId, o.Ticker, o.OptionType, o.Strike, o.Expiration, o.Quantity
FROM ExecutedOrders o
WHERE o.DiscordMessageId = `$messageId
  AND o.ActionType = 'SELL'
  AND o.OrderRole = 'MANUAL_EXIT'
ORDER BY o.Timestamp DESC
LIMIT 1;
""";
read.Parameters.AddWithValue("`$messageId", (long)messageId);

using var reader = read.ExecuteReader();
if (!reader.Read())
{
    throw new InvalidOperationException(`$"No MANUAL_EXIT order found for DiscordMessageId {messageId}.");
}

string ticker = reader.GetString(1);
string optionType = reader.GetString(2);
double strike = reader.GetDouble(3);
string expiration = reader.GetString(4);
double quantity = reader.GetDouble(5);
reader.Close();

using var updateOrder = connection.CreateCommand();
updateOrder.Transaction = transaction;
updateOrder.CommandText = """
UPDATE ExecutedOrders
SET LimitPrice = `$exitPrice
WHERE DiscordMessageId = `$messageId
  AND ActionType = 'SELL'
  AND OrderRole = 'MANUAL_EXIT';
""";
updateOrder.Parameters.AddWithValue("`$exitPrice", exitPrice);
updateOrder.Parameters.AddWithValue("`$messageId", (long)messageId);
updateOrder.ExecuteNonQuery();

using var insertExecution = connection.CreateCommand();
insertExecution.Transaction = transaction;
insertExecution.CommandText = """
INSERT OR REPLACE INTO OrderExecutions
    (ExecutionId, IbOrderId, ClientId, DiscordMessageId, ActionType, Quantity, Price, ExecutedAt)
VALUES
    (`$executionId, `$orderId, 0, `$messageId, 'SELL', `$quantity, `$exitPrice, `$executedAt);
""";
insertExecution.Parameters.AddWithValue("`$executionId", `$"manual-exit-price-{messageId}");
insertExecution.Parameters.AddWithValue("`$orderId", -Math.Abs((int)(messageId % int.MaxValue)));
insertExecution.Parameters.AddWithValue("`$messageId", (long)messageId);
insertExecution.Parameters.AddWithValue("`$quantity", quantity);
insertExecution.Parameters.AddWithValue("`$exitPrice", exitPrice);
insertExecution.Parameters.AddWithValue("`$executedAt", DateTimeOffset.UtcNow.UtcDateTime.ToString("o"));
insertExecution.ExecuteNonQuery();

using var audit = connection.CreateCommand();
audit.Transaction = transaction;
audit.CommandText = """
INSERT INTO ExecutionAuditLogs (DiscordMessageId, Ticker, LifecycleStep, DecisionStatus, Details, Timestamp)
VALUES (`$messageId, `$ticker, 'MANUAL_EXIT_PRICE_BACKFILL', 'UPDATED', `$details, `$timestamp);
""";
audit.Parameters.AddWithValue("`$messageId", (long)messageId);
audit.Parameters.AddWithValue("`$ticker", ticker);
audit.Parameters.AddWithValue("`$details", `$"Manual exit price backfilled for {ticker} {expiration} {strike}{(optionType == "CALL" ? "C" : "P")}: {quantity} contract(s) @ `${exitPrice:F2}.");
audit.Parameters.AddWithValue("`$timestamp", DateTimeOffset.UtcNow.UtcDateTime.ToString("o"));
audit.ExecuteNonQuery();

transaction.Commit();
Console.WriteLine(`$"Updated manual exit price for {ticker} {expiration} {strike}{(optionType == "CALL" ? "C" : "P")}: {quantity} contract(s) @ `${exitPrice:F2}.");
"@ | Set-Content -Path Program.cs -Encoding UTF8

    dotnet run --no-restore
}
finally {
    Pop-Location
    Remove-Item -Path $temp -Recurse -Force -ErrorAction SilentlyContinue
}