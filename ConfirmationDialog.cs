using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace OptionTradesParser
{
    public sealed record TradeBudgetOption(double Budget, int Quantity, double EstimatedValue);

    public sealed record TradeConfirmationResult(bool Approved, TradeBudgetOption? SelectedBudget);

    public sealed record BrowserApprovalDecision(string Status, double? SelectedBudget);

    /// Every labeled field shown on the pre-trade confirmation dialog.
    public sealed record TradeConfirmationDetails(
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
        IReadOnlyList<string> Warnings)
    {
        public IReadOnlyList<TradeBudgetOption> BudgetOptions { get; init; } = Array.Empty<TradeBudgetOption>();
    }

    /// Raises the pre-trade confirmation as a large, clearly labeled Windows dialog instead of a plain MessageBox.
    public static class ConfirmationDialog
    {
        public static TradeConfirmationResult Confirm(TradeConfirmationDetails details)
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    TradeConfirmationResult result = new(false, null);

                    // WinForms needs an STA thread; the caller here is a background Task.Run thread, so the
                    // dialog gets its own dedicated STA thread rather than requiring the whole app to be STA.
                    var thread = new Thread(() => result = ShowForm(details));
                    thread.SetApartmentState(ApartmentState.STA);
                    thread.Start();
                    thread.Join();
                    return result;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ [DIALOG FALLBACK] Windows dialog failed ({ex.GetType().Name}: {ex.Message}). Falling back to the console prompt.");
                }
            }

            return ConfirmOnConsole(details);
        }

        private static TradeConfirmationResult ShowForm(TradeConfirmationDetails d)
        {
            string optionCode = d.OptionType == "CALL" ? "C" : "P";
            TradeBudgetOption? selectedBudget = null;

            using var form = new Form
            {
                Text = $"Pre-Trade Confirmation — {d.OrderAction} {d.Ticker}",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterScreen,
                MaximizeBox = false,
                MinimizeBox = false,
                TopMost = true,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(20),
                Font = new Font("Segoe UI", 11.5F),
            };

            var root = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Dock = DockStyle.Fill,
            };

            var header = new Label
            {
                Text = $"{d.OrderAction}  {d.Ticker} {d.Expiry} {d.Strike}{optionCode}",
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = d.OrderAction == "BUY" ? Color.DarkGreen : Color.DarkRed,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 14),
            };
            root.Controls.Add(header);

            var fields = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Margin = new Padding(0, 0, 0, 14),
            };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            void AddRow(string label, string value, bool boldValue = false)
            {
                int row = fields.RowCount++;
                fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                fields.Controls.Add(new Label { Text = label, AutoSize = true, Font = new Font("Segoe UI", 11.5F, FontStyle.Bold), Margin = new Padding(0, 3, 20, 3) }, 0, row);
                fields.Controls.Add(new Label
                {
                    Text = value,
                    AutoSize = true,
                    Font = new Font("Segoe UI", 11.5F, boldValue ? FontStyle.Bold : FontStyle.Regular),
                    Margin = new Padding(0, 3, 0, 3),
                    ForeColor = boldValue && (value.Equals("PAPER", StringComparison.OrdinalIgnoreCase) || value.Equals("LIVE", StringComparison.OrdinalIgnoreCase))
                        ? (value.Equals("LIVE", StringComparison.OrdinalIgnoreCase) ? Color.DarkGreen : Color.DarkOrange)
                        : SystemColors.ControlText,
                }, 1, row);
            }

            AddRow("Trader:", d.Trader);
            AddRow("Intent:", d.Intent);
            AddRow("Ticker:", d.Ticker + (d.ContractInferred ? "  (inferred from prior alert)" : string.Empty));
            AddRow("Type:", d.OptionType);
            AddRow("Strike:", d.Strike.ToString());
            AddRow("Expiry:", d.Expiry);
            if (d.BudgetOptions.Count == 0)
                AddRow("Quantity:", d.Quantity.ToString());
            AddRow("Account Type:", d.AccountType, true);
            AddRow("Order:", d.BudgetOptions.Count > 0
                ? $"{d.OrderAction} at {d.OrderType} ${d.LimitPrice:F2} — select a budget below"
                : $"{d.OrderAction} {d.Quantity} @ {d.OrderType} ${d.LimitPrice:F2}");
            AddRow("Contract:", d.ContractSymbol);
            AddRow("Market Quote:", d.MarketQuote);
            AddRow("Risk:", d.RiskCategory);
            root.Controls.Add(fields);

            foreach (var warning in d.Warnings)
            {
                root.Controls.Add(new Label
                {
                    Text = "⚠ " + warning,
                    ForeColor = Color.DarkRed,
                    Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                    AutoSize = true,
                    MaximumSize = new Size(560, 0),
                    Margin = new Padding(0, 0, 0, 10),
                });
            }

            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 10, 0, 0),
            };

            var noButton = new Button
            {
                Text = "&No — Drop Order",
                DialogResult = DialogResult.No,
                AutoSize = true,
                Padding = new Padding(12, 6, 12, 6),
                Font = new Font("Segoe UI", 11.5F, FontStyle.Bold),
            };
            if (d.BudgetOptions.Count == 0)
            {
                var yesButton = new Button
                {
                    Text = "&Yes — Execute",
                    DialogResult = DialogResult.Yes,
                    AutoSize = true,
                    Padding = new Padding(12, 6, 12, 6),
                    Margin = new Padding(0, 0, 10, 0),
                };
                buttons.Controls.Add(yesButton);
            }
            else
            {
                foreach (var option in d.BudgetOptions.Reverse())
                {
                    var budgetButton = new Button
                    {
                        Text = $"${option.Budget:F0} — {option.Quantity} contract{(option.Quantity == 1 ? string.Empty : "s")} (${option.EstimatedValue:F0})",
                        AutoSize = true,
                        Padding = new Padding(12, 6, 12, 6),
                        Margin = new Padding(0, 0, 10, 0),
                    };
                    budgetButton.Click += (_, _) =>
                    {
                        selectedBudget = option;
                        form.DialogResult = DialogResult.Yes;
                        form.Close();
                    };
                    buttons.Controls.Add(budgetButton);
                }
            }
            buttons.Controls.Add(noButton);
            root.Controls.Add(buttons);

            form.Controls.Add(root);

            // Defaults to "No" so a stray Enter keypress can never send an order; Escape also maps to No.
            form.AcceptButton = noButton;
            form.CancelButton = noButton;

            bool approved = form.ShowDialog() == DialogResult.Yes;
            return new TradeConfirmationResult(approved, selectedBudget);
        }

        public static TradeConfirmationResult ConfirmOnConsole(TradeConfirmationDetails details)
        {
            while (Console.KeyAvailable)
            {
                Console.ReadKey(intercept: true);
            }

            if (details.BudgetOptions.Count > 0)
            {
                Console.WriteLine("Select an order budget:");
                for (int index = 0; index < details.BudgetOptions.Count; index++)
                {
                    TradeBudgetOption option = details.BudgetOptions[index];
                    Console.WriteLine($"  [{index + 1}] ${option.Budget:F0} — {option.Quantity} contract(s), estimated ${option.EstimatedValue:F2}");
                }
                Console.Write("👉 Select [1-3] or [N] to drop: ");
                ConsoleKeyInfo budgetKey = Console.ReadKey();
                Console.WriteLine();
                int selectedIndex = budgetKey.KeyChar - '1';
                return selectedIndex >= 0 && selectedIndex < details.BudgetOptions.Count
                    ? new TradeConfirmationResult(true, details.BudgetOptions[selectedIndex])
                    : new TradeConfirmationResult(false, null);
            }

            Console.Write("👉 Press [Y] to Execute on IBKR Account or [N] to Ignore/Drop Order: ");
            ConsoleKeyInfo key = Console.ReadKey();
            Console.WriteLine();

            return new TradeConfirmationResult(key.Key == ConsoleKey.Y, null);
        }
    }
}
