using System;
using System.IO;

namespace OptionTradesParser
{
    public static class ReconciliationLog
    {
        private static readonly object Gate = new();

        public static string Path { get; } = ResolvePath();

        public static void Write(string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
            lock (Gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.AppendAllText(Path, line);
            }
        }

        private static string ResolvePath()
        {
            string? configured = Environment.GetEnvironmentVariable("OTP_RECON_LOG_FILE");
            if (!string.IsNullOrWhiteSpace(configured)) return configured;

            return System.IO.Path.Combine(AppContext.BaseDirectory, "logs", "reconciliation.log");
        }
    }
}