using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace OptionTradesParser
{
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
        bool ContractInferred,
        IReadOnlyList<string> Warnings);

    /// Raises the pre-trade confirmation as a large, clearly labeled Windows dialog instead of a plain MessageBox.
    public static class ConfirmationDialog
    {
        public static bool Confirm(TradeConfirmationDetails details)
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    bool result = false;

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

            return ConfirmOnConsole();
        }

        private static bool ShowForm(TradeConfirmationDetails d)
        {
            string optionCode = d.OptionType == "CALL" ? "C" : "P";

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

            void AddRow(string label, string value)
            {
                int row = fields.RowCount++;
                fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                fields.Controls.Add(new Label { Text = label, AutoSize = true, Font = new Font("Segoe UI", 11.5F, FontStyle.Bold), Margin = new Padding(0, 3, 20, 3) }, 0, row);
                fields.Controls.Add(new Label { Text = value, AutoSize = true, Margin = new Padding(0, 3, 0, 3) }, 1, row);
            }

            AddRow("Trader:", d.Trader);
            AddRow("Intent:", d.Intent);
            AddRow("Ticker:", d.Ticker + (d.ContractInferred ? "  (inferred from prior alert)" : string.Empty));
            AddRow("Type:", d.OptionType);
            AddRow("Strike:", d.Strike.ToString());
            AddRow("Expiry:", d.Expiry);
            AddRow("Quantity:", d.Quantity.ToString());
            AddRow("Order:", $"{d.OrderAction} {d.Quantity} @ {d.OrderType} ${d.LimitPrice:F2}");
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
            var yesButton = new Button
            {
                Text = "&Yes — Execute",
                DialogResult = DialogResult.Yes,
                AutoSize = true,
                Padding = new Padding(12, 6, 12, 6),
                Margin = new Padding(0, 0, 10, 0),
            };
            buttons.Controls.Add(noButton);
            buttons.Controls.Add(yesButton);
            root.Controls.Add(buttons);

            form.Controls.Add(root);

            // Defaults to "No" so a stray Enter keypress can never send an order; Escape also maps to No.
            form.AcceptButton = noButton;
            form.CancelButton = noButton;

            return form.ShowDialog() == DialogResult.Yes;
        }

        private static bool ConfirmOnConsole()
        {
            while (Console.KeyAvailable)
            {
                Console.ReadKey(intercept: true);
            }

            Console.Write("👉 Press [Y] to Execute on IBKR Account or [N] to Ignore/Drop Order: ");
            ConsoleKeyInfo key = Console.ReadKey();
            Console.WriteLine();

            return key.Key == ConsoleKey.Y;
        }
    }
}
