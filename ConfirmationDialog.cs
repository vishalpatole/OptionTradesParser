using System;
using System.Runtime.InteropServices;

namespace OptionTradesParser
{
    /// Raises the pre-trade confirmation in a top-most Windows dialog so it cannot be lost in the console log stream.
    public static class ConfirmationDialog
    {
        private const uint MbYesNo = 0x00000004;
        private const uint MbIconQuestion = 0x00000020;
        private const uint MbDefaultButton2 = 0x00000100;
        private const uint MbSystemModal = 0x00001000;
        private const uint MbSetForeground = 0x00010000;
        private const uint MbTopMost = 0x00040000;
        private const int IdYes = 6;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

        public static bool Confirm(string caption, string message)
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    // Defaults to "No" so an accidental Enter never sends an order.
                    int result = MessageBoxW(IntPtr.Zero, message, caption,
                        MbYesNo | MbIconQuestion | MbDefaultButton2 | MbSystemModal | MbSetForeground | MbTopMost);

                    if (result != 0) return result == IdYes;
                }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }

                Console.WriteLine("⚠️ [DIALOG FALLBACK] Windows dialog unavailable. Falling back to the console prompt.");
            }

            return ConfirmOnConsole();
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
