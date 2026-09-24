using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace OptionTradesParser.Dashboard.Api;

public sealed class DashboardRepository(string connectionString)
{
    public DashboardSnapshot GetDashboard(string accountType)
        => GetDashboard(accountType, DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month);

    public DashboardSnapshot GetDashboard(string accountType, int year, int month)
    {
        DateOnly start = new(year, month, 1);
        DateOnly end = start.AddMonths(1);
        IReadOnlyList<TradeSummary> trades = GetTradesForRange(start.ToString("yyyy-MM-dd"), end.ToString("yyyy-MM-dd"));
        IReadOnlyList<OrderSummary> orders = trades.SelectMany(trade => trade.Orders).ToArray();
        return new DashboardSnapshot(accountType, trades.Count, trades.Count(trade => trade.Executions.Count > 0), orders.Count,
            orders.Count(order => IsWorking(order.Status)),
            orders.Count(order => order.Status is "REJECTED" or "INACTIVE"),
            DateTimeOffset.UtcNow, trades.Take(8).ToArray());
    }

    public AccountSnapshot GetAccount(string accountType)
    {
        IReadOnlyList<TradeSummary> trades = GetTrades(1);
        return new AccountSnapshot(accountType,
            trades.SelectMany(trade => trade.Orders).Count(order => IsWorking(order.Status)),
            trades.Count, DateTimeOffset.UtcNow);
    }

    public IReadOnlyList<TradeSummary> GetTrades(int days)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT DiscordMessageId, COALESCE(TraderName, 'UNKNOWN'), COALESCE(ActionType, ''),
                   COALESCE(Ticker, ''), COALESCE(OptionType, ''), COALESCE(Strike, 0),
                   COALESCE(Expiration, ''), COALESCE(ExecutionPrice, 0), COALESCE(RiskCategory, 'STANDARD'),
                   COALESCE(AlertTimestamp, Timestamp), COALESCE(RawMessage, '')
            FROM TradeAlerts
            WHERE datetime(COALESCE(AlertTimestamp, Timestamp)) >= datetime('now', $window)
            ORDER BY datetime(COALESCE(AlertTimestamp, Timestamp)) DESC;";
        command.Parameters.AddWithValue("$window", $"-{days} days");

        var trades = new List<TradeSummary>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            long id = reader.GetInt64(0);
            IReadOnlyList<OrderSummary> orders = CollapseSupersededOrders(GetOrders(id));
            IReadOnlyList<ExecutionSummary> executions = GetExecutions(id);
            trades.Add(new TradeSummary(id.ToString(), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.GetDouble(5), reader.GetString(6), reader.GetDouble(7), reader.GetString(8), ResolveStatus(orders, executions),
                ParseTimestamp(reader.GetString(9)), reader.GetString(10), orders, executions, Array.Empty<AuditSummary>()));
        }
        return trades;
    }

    public IReadOnlyList<TradeSummary> GetTradesForDate(string date)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT DiscordMessageId, COALESCE(TraderName, 'UNKNOWN'), COALESCE(ActionType, ''),
                   COALESCE(Ticker, ''), COALESCE(OptionType, ''), COALESCE(Strike, 0),
                   COALESCE(Expiration, ''), COALESCE(ExecutionPrice, 0), COALESCE(RiskCategory, 'STANDARD'),
                   COALESCE(AlertTimestamp, Timestamp), COALESCE(RawMessage, '')
            FROM TradeAlerts
                WHERE EXISTS (
                         SELECT 1
                         FROM OrderExecutions e
                         WHERE e.DiscordMessageId = TradeAlerts.DiscordMessageId
                                         AND e.ActionType = 'SELL'
                            AND date(e.ExecutedAt) = date($date)
                    )
                    OR (
                         NOT EXISTS (SELECT 1 FROM OrderExecutions e WHERE e.DiscordMessageId = TradeAlerts.DiscordMessageId)
                         AND date(COALESCE(AlertTimestamp, Timestamp)) = date($date)
                    )
            ORDER BY datetime(COALESCE(AlertTimestamp, Timestamp)) DESC;";
        command.Parameters.AddWithValue("$date", date);

        var trades = new List<TradeSummary>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            long id = reader.GetInt64(0);
            IReadOnlyList<OrderSummary> orders = CollapseSupersededOrders(GetOrders(id));
            IReadOnlyList<ExecutionSummary> executions = GetExecutions(id);
            trades.Add(new TradeSummary(id.ToString(), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.GetDouble(5), reader.GetString(6), reader.GetDouble(7), reader.GetString(8), ResolveStatus(orders, executions),
                ParseTimestamp(reader.GetString(9)), reader.GetString(10), orders, executions, Array.Empty<AuditSummary>()));
        }
        return trades;
    }

    public IReadOnlyList<CalendarDaySummary> GetCalendar(int year, int month)
    {
        int days = DateTime.DaysInMonth(year, month);
        var summaries = Enumerable.Range(1, days)
            .ToDictionary(day => new DateOnly(year, month, day).ToString("yyyy-MM-dd"), _ => (Pnl: 0d, Closed: 0, Open: 0));

        DateOnly start = new(year, month, 1);
        DateOnly end = start.AddMonths(1);
        IReadOnlyList<CalendarExecutionPnl> executionPnl = GetCalendarExecutionPnl(start.ToString("yyyy-MM-dd"), end.ToString("yyyy-MM-dd"));
        foreach (CalendarExecutionPnl item in executionPnl.Where(item => Math.Abs(item.Pnl) > 0.000001 || item.HasSell))
        {
            string key = item.ExecutionDate;
            if (!summaries.TryGetValue(key, out var current)) continue;

            summaries[key] = (current.Pnl + item.Pnl, current.Closed + 1, current.Open);
        }

        return summaries.Select(item => new CalendarDaySummary(item.Key, item.Value.Pnl, item.Value.Closed, item.Value.Open)).ToArray();
    }

    private sealed record CalendarExecutionPnl(string ExecutionDate, long DiscordMessageId, double Pnl, bool HasSell);

    private IReadOnlyList<CalendarExecutionPnl> GetCalendarExecutionPnl(string startDate, string endDate)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            WITH buy_basis AS (
                SELECT DiscordMessageId,
                       SUM(Quantity) AS BuyQty,
                       SUM(Quantity * Price) / SUM(Quantity) AS AvgBuy
                FROM OrderExecutions
                WHERE ActionType = 'BUY'
                GROUP BY DiscordMessageId
            )
                 SELECT date(e.ExecutedAt) AS ExecutionDate,
                     e.DiscordMessageId,
                     ROUND(SUM(CASE
                      WHEN e.RealizedPnl IS NOT NULL THEN e.RealizedPnl
                      WHEN e.ActionType = 'SELL' THEN (e.Price - b.AvgBuy) * e.Quantity * 100
                      ELSE 0
                       END), 2) AS Pnl,
                       SUM(CASE WHEN e.ActionType = 'SELL' THEN 1 ELSE 0 END) AS SellExecutions
            FROM OrderExecutions e
            JOIN buy_basis b ON b.DiscordMessageId = e.DiscordMessageId
                 WHERE b.BuyQty > 0
              AND date(e.ExecutedAt) >= date($startDate)
              AND date(e.ExecutedAt) < date($endDate)
            GROUP BY date(e.ExecutedAt), e.DiscordMessageId;";
        command.Parameters.AddWithValue("$startDate", startDate);
        command.Parameters.AddWithValue("$endDate", endDate);

        var result = new List<CalendarExecutionPnl>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new CalendarExecutionPnl(reader.GetString(0), reader.GetInt64(1), reader.GetDouble(2), reader.GetInt64(3) > 0));
        }

        return result;
    }

    public string GetSelectedTradeDate()
    {
        using SqliteConnection connection = Open();
        EnsureSettingsSchema(connection);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM AppSettings WHERE Key = 'SelectedTradeDate';";
        return command.ExecuteScalar() as string ?? DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
    }

    public void SetSelectedTradeDate(string date)
    {
        if (!DateOnly.TryParse(date, out _)) throw new ArgumentException("Date must be yyyy-MM-dd.", nameof(date));

        using SqliteConnection connection = Open();
        EnsureSettingsSchema(connection);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO AppSettings (Key, Value, UpdatedAt)
            VALUES ('SelectedTradeDate', $date, $updatedAt)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value, UpdatedAt = excluded.UpdatedAt;";
        command.Parameters.AddWithValue("$date", date);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.UtcDateTime.ToString("o"));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<TradeSummary> GetTradesForRange(string startDate, string endDate)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT DiscordMessageId, COALESCE(TraderName, 'UNKNOWN'), COALESCE(ActionType, ''),
                   COALESCE(Ticker, ''), COALESCE(OptionType, ''), COALESCE(Strike, 0),
                   COALESCE(Expiration, ''), COALESCE(ExecutionPrice, 0), COALESCE(RiskCategory, 'STANDARD'),
                   COALESCE(AlertTimestamp, Timestamp), COALESCE(RawMessage, '')
            FROM TradeAlerts
            WHERE date(COALESCE(AlertTimestamp, Timestamp)) >= date($startDate)
              AND date(COALESCE(AlertTimestamp, Timestamp)) < date($endDate)
            ORDER BY datetime(COALESCE(AlertTimestamp, Timestamp)) DESC;";
        command.Parameters.AddWithValue("$startDate", startDate);
        command.Parameters.AddWithValue("$endDate", endDate);

        var trades = new List<TradeSummary>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            long id = reader.GetInt64(0);
            IReadOnlyList<OrderSummary> orders = CollapseSupersededOrders(GetOrders(id));
            IReadOnlyList<ExecutionSummary> executions = GetExecutions(id);
            trades.Add(new TradeSummary(id.ToString(), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.GetDouble(5), reader.GetString(6), reader.GetDouble(7), reader.GetString(8), ResolveStatus(orders, executions),
                ParseTimestamp(reader.GetString(9)), reader.GetString(10), orders, executions, Array.Empty<AuditSummary>()));
        }
        return trades;
    }

    public TradeSummary? GetTrade(long id)
    {
        TradeSummary? trade = GetTrades(3650).FirstOrDefault(item => item.Id == id.ToString());
        return trade == null ? null : trade with { Audit = GetAudit(id) };
    }

    public long GetDataVersion()
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT COALESCE((SELECT MAX(LogId) FROM ExecutionAuditLogs), 0)
                 + COALESCE((SELECT COUNT(*) FROM ExecutedOrders), 0)
                 + COALESCE((
                    SELECT SUM(
                        IbOrderId * 31
                        + ClientId * 17
                        + CASE COALESCE(Status, '')
                            WHEN 'PENDING_SUBMIT' THEN 1
                            WHEN 'PRESUBMITTED' THEN 2
                            WHEN 'SUBMITTED' THEN 3
                            WHEN 'PARTIALLY_FILLED' THEN 4
                            WHEN 'FILLED' THEN 5
                            WHEN 'PENDING_CANCEL' THEN 6
                            WHEN 'CANCELLED' THEN 7
                            WHEN 'API_CANCELLED' THEN 8
                            WHEN 'INACTIVE' THEN 9
                            WHEN 'REJECTED' THEN 10
                            ELSE 99
                          END)
                    FROM ExecutedOrders), 0);";
        long version = Convert.ToInt64(command.ExecuteScalar() ?? 0L);
        if (TableExists(connection, "OrderExecutions"))
        {
            command.CommandText = "SELECT COUNT(*) FROM OrderExecutions;";
            version += Convert.ToInt64(command.ExecuteScalar() ?? 0L);
        }
        if (TableExists(connection, "PendingApprovals"))
        {
            command.CommandText = "SELECT COALESCE(SUM(CASE Status WHEN 'PENDING' THEN 1 WHEN 'APPROVED' THEN 10 WHEN 'REJECTED' THEN 100 ELSE 1000 END), 0) FROM PendingApprovals;";
            version += Convert.ToInt64(command.ExecuteScalar() ?? 0L);
        }
        return version;
    }

    public IReadOnlyList<PendingApprovalSummary> GetPendingApprovals()
    {
        using SqliteConnection connection = Open();
        EnsureApprovalSchema(connection);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT DiscordMessageId, DetailsJson, CreatedAt FROM PendingApprovals WHERE Status = 'PENDING' ORDER BY CreatedAt;";
        var result = new List<PendingApprovalSummary>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read()) result.Add(ParseApproval(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
        return result;
    }

    public ApprovalActionResult Approve(long id, double? budget)
    {
        using SqliteConnection connection = Open();
        EnsureApprovalSchema(connection);
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "SELECT DetailsJson FROM PendingApprovals WHERE DiscordMessageId = $id AND Status = 'PENDING';";
        read.Parameters.AddWithValue("$id", id);
        string? detailsJson = read.ExecuteScalar() as string;
        if (detailsJson == null) return new ApprovalActionResult(false, "Approval is no longer pending.");

        using JsonDocument document = JsonDocument.Parse(detailsJson);
        JsonElement budgets = document.RootElement.GetProperty("BudgetOptions");
        double? selectedBudget = null;
        if (budgets.GetArrayLength() > 0)
        {
            if (!budget.HasValue) return new ApprovalActionResult(false, "Select an order budget.");
            bool valid = budgets.EnumerateArray().Any(item => Math.Abs(item.GetProperty("Budget").GetDouble() - budget.Value) < 0.001);
            if (!valid) return new ApprovalActionResult(false, "The selected budget is not valid for this approval.");
            selectedBudget = budget.Value;
        }

        using SqliteCommand update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = @"
            UPDATE PendingApprovals SET Status = 'APPROVED', SelectedBudget = $budget, DecidedAt = $decidedAt
            WHERE DiscordMessageId = $id AND Status = 'PENDING';";
        update.Parameters.AddWithValue("$id", id);
        update.Parameters.AddWithValue("$budget", (object?)selectedBudget ?? DBNull.Value);
        update.Parameters.AddWithValue("$decidedAt", DateTimeOffset.UtcNow.UtcDateTime.ToString("o"));
        if (update.ExecuteNonQuery() != 1) return new ApprovalActionResult(false, "Approval was already decided.");
        transaction.Commit();
        return new ApprovalActionResult(true, "Order approved.");
    }

    public ApprovalActionResult Reject(long id)
    {
        using SqliteConnection connection = Open();
        EnsureApprovalSchema(connection);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE PendingApprovals SET Status = 'REJECTED', DecidedAt = $decidedAt
            WHERE DiscordMessageId = $id AND Status = 'PENDING';";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$decidedAt", DateTimeOffset.UtcNow.UtcDateTime.ToString("o"));
        return command.ExecuteNonQuery() == 1
            ? new ApprovalActionResult(true, "Order rejected.")
            : new ApprovalActionResult(false, "Approval is no longer pending.");
    }

    private IReadOnlyList<AuditSummary> GetAudit(long id)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT LifecycleStep, DecisionStatus, Details, Timestamp FROM ExecutionAuditLogs WHERE DiscordMessageId = $id ORDER BY LogId;";
        command.Parameters.AddWithValue("$id", id);
        var result = new List<AuditSummary>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read()) result.Add(new AuditSummary(reader.GetString(0), reader.GetString(1), reader.GetString(2), ParseTimestamp(reader.GetString(3))));
        return result;
    }

    private IReadOnlyList<OrderSummary> GetOrders(long id)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT IbOrderId, ClientId, COALESCE(OrderRole, 'ORDER'), COALESCE(ActionType, ''),
                   COALESCE(Quantity, 0), COALESCE(LimitPrice, 0), COALESCE(Status, 'UNKNOWN'), ParentOrderId, Timestamp
            FROM ExecutedOrders WHERE DiscordMessageId = $id ORDER BY Timestamp, IbOrderId;";
        command.Parameters.AddWithValue("$id", id);
        var result = new List<OrderSummary>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read()) result.Add(new OrderSummary(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetDouble(5), reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetInt32(7), ParseTimestamp(reader.GetString(8))));
        return result;
    }

    private IReadOnlyList<ExecutionSummary> GetExecutions(long id)
    {
        using SqliteConnection connection = Open();
        if (!TableExists(connection, "OrderExecutions")) return Array.Empty<ExecutionSummary>();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT ExecutionId, IbOrderId, ActionType, Quantity, Price, ExecutedAt, Commission, RealizedPnl FROM OrderExecutions WHERE DiscordMessageId = $id ORDER BY ExecutedAt;";
        command.Parameters.AddWithValue("$id", id);
        var result = new List<ExecutionSummary>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read()) result.Add(new ExecutionSummary(reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetDouble(3), reader.GetDouble(4), ParseTimestamp(reader.GetString(5)), reader.IsDBNull(6) ? null : reader.GetDouble(6), reader.IsDBNull(7) ? null : reader.GetDouble(7)));
        return result;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout=5000;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static bool TableExists(SqliteConnection connection, string table)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$table;";
        command.Parameters.AddWithValue("$table", table);
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) > 0;
    }

    private static void EnsureApprovalSchema(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS PendingApprovals (
                DiscordMessageId INTEGER PRIMARY KEY,
                DetailsJson TEXT NOT NULL,
                Status TEXT NOT NULL,
                SelectedBudget REAL,
                CreatedAt TEXT NOT NULL,
                DecidedAt TEXT
            );";
        command.ExecuteNonQuery();
    }

    private static void EnsureSettingsSchema(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS AppSettings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );";
        command.ExecuteNonQuery();
    }

    private static PendingApprovalSummary ParseApproval(long id, string json, string createdAt)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        IReadOnlyList<string> warnings = root.GetProperty("Warnings").EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray();
        IReadOnlyList<ApprovalBudget> budgets = root.GetProperty("BudgetOptions").EnumerateArray()
            .Select(item => new ApprovalBudget(item.GetProperty("Budget").GetDouble(), item.GetProperty("Quantity").GetInt32(), item.GetProperty("EstimatedValue").GetDouble()))
            .ToArray();
        return new PendingApprovalSummary(id.ToString(), root.GetProperty("Trader").GetString() ?? "UNKNOWN",
            root.GetProperty("Intent").GetString() ?? string.Empty, root.GetProperty("OrderAction").GetString() ?? string.Empty,
            root.GetProperty("Ticker").GetString() ?? string.Empty, root.GetProperty("OptionType").GetString() ?? string.Empty,
            root.GetProperty("Strike").GetDouble(), root.GetProperty("Expiry").GetString() ?? string.Empty,
            root.GetProperty("Quantity").GetInt32(), root.GetProperty("OrderType").GetString() ?? "LMT",
            root.GetProperty("LimitPrice").GetDouble(), root.GetProperty("MarketQuote").GetString() ?? string.Empty,
            root.GetProperty("ContractSymbol").GetString() ?? string.Empty, root.GetProperty("RiskCategory").GetString() ?? "STANDARD",
            root.GetProperty("AccountType").GetString() ?? "PAPER", root.GetProperty("ContractInferred").GetBoolean(),
            warnings, budgets, ParseTimestamp(createdAt));
    }

    private static bool IsWorking(string status) => status is "SUBMITTED" or "PRESUBMITTED" or "PARTIALLY_FILLED";
    private static IReadOnlyList<OrderSummary> CollapseSupersededOrders(IReadOnlyList<OrderSummary> orders)
    {
        return orders
            .GroupBy(order => order.Role, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(order => order.Status == "FILLED")
                .ThenByDescending(order => !order.Role.Equals("MANUAL_EXIT", StringComparison.OrdinalIgnoreCase) || order.OrderId >= 0)
                .ThenByDescending(order => order.SubmittedAt)
                .ThenByDescending(order => order.OrderId)
                .First())
            .OrderBy(order => order.Role == "ENTRY" ? 0 : 1)
            .ThenBy(order => order.Role)
            .ToArray();
    }

    private static DateTimeOffset ParseTimestamp(string value) => DateTimeOffset.TryParse(value, out DateTimeOffset parsed) ? parsed : DateTimeOffset.MinValue;
    private static double EstimatePnl(IReadOnlyList<ExecutionSummary> executions)
    {
        double bought = executions.Where(execution => execution.Action == "BUY").Sum(execution => execution.Quantity);
        double sold = executions.Where(execution => execution.Action == "SELL").Sum(execution => execution.Quantity);
        if (bought <= 0 || sold <= 0) return 0;

        double avgEntry = executions.Where(execution => execution.Action == "BUY").Sum(execution => execution.Quantity * execution.Price) / bought;
        double avgExit = executions.Where(execution => execution.Action == "SELL").Sum(execution => execution.Quantity * execution.Price) / sold;
        return (avgExit - avgEntry) * Math.Min(bought, sold) * 100;
    }

    private static string ResolveStatus(IReadOnlyList<OrderSummary> orders, IReadOnlyList<ExecutionSummary> executions)
    {
        double bought = executions.Where(execution => execution.Action == "BUY").Sum(execution => execution.Quantity);
        double sold = executions.Where(execution => execution.Action == "SELL").Sum(execution => execution.Quantity);
        if (bought > 0 && sold >= bought) return "CLOSED";

        double filledBuyOrders = orders.Where(order => order.Action == "BUY" && order.Status is "FILLED" or "PARTIALLY_FILLED").Sum(order => order.Quantity);
        double filledSellOrders = orders.Where(order => order.Action == "SELL" && order.Status == "FILLED").Sum(order => order.Quantity);
        if (filledBuyOrders > 0 && filledSellOrders >= filledBuyOrders) return "CLOSED";

        if (orders.Any(order => order.Status is "REJECTED" or "INACTIVE")) return "ATTENTION";
        if (orders.Any(order => IsWorking(order.Status))) return executions.Count > 0 ? "OPEN" : "WORKING";
        if (executions.Any(execution => execution.Action == "SELL")) return "CLOSED";
        if (executions.Count > 0) return "OPEN";
        return orders.Count > 0 ? orders[^1].Status : "CAPTURED";
    }
}
