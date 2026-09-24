namespace OptionTradesParser.Dashboard.Api;

public sealed record DashboardSnapshot(
    string AccountType,
    int CapturedTrades,
    int ExecutedTrades,
    int RoutedOrders,
    int WorkingOrders,
    int RejectedOrders,
    DateTimeOffset AsOf,
    IReadOnlyList<TradeSummary> RecentTrades);

public sealed record AccountSnapshot(string AccountType, int WorkingOrders, int CapturedToday, DateTimeOffset AsOf);

public sealed record SelectedTradeDateRequest(string Date);
public sealed record SelectedTradeDateResponse(string Date);

public sealed record CalendarDaySummary(string Date, double Pnl, int ClosedTrades, int OpenTrades);

public sealed record TradeSummary(
    string Id,
    string Trader,
    string Action,
    string Ticker,
    string OptionType,
    double Strike,
    string Expiration,
    double AlertPrice,
    string Risk,
    string Status,
    DateTimeOffset AlertedAt,
    string RawMessage,
    IReadOnlyList<OrderSummary> Orders,
    IReadOnlyList<ExecutionSummary> Executions,
    IReadOnlyList<AuditSummary> Audit);

public sealed record OrderSummary(int OrderId, int ClientId, string Role, string Action, int Quantity, double LimitPrice, string Status, int? ParentOrderId, DateTimeOffset SubmittedAt);
public sealed record ExecutionSummary(string ExecutionId, int OrderId, string Action, double Quantity, double Price, DateTimeOffset ExecutedAt, double? Commission, double? RealizedPnl);
public sealed record AuditSummary(string Step, string Status, string Details, DateTimeOffset Timestamp);

public sealed record ApprovalBudget(double Budget, int Quantity, double EstimatedValue);

public sealed record PendingApprovalSummary(
    string Id,
    string Trader,
    string Intent,
    string OrderAction,
    string Ticker,
    string OptionType,
    double Strike,
    string Expiry,
    int Quantity,
    string OrderType,
    double LimitPrice,
    string MarketQuote,
    string ContractSymbol,
    string RiskCategory,
    string AccountType,
    bool ContractInferred,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ApprovalBudget> BudgetOptions,
    DateTimeOffset CreatedAt);

public sealed record ApprovalRequest(double? Budget);
public sealed record ApprovalActionResult(bool Success, string Message);
