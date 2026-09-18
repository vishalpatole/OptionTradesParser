using System;
using System.IO;

namespace OptionTradesParser
{
    /// Persists raw Discord message dumps to disk so real alert formats (especially forwarded embeds) can be
    /// collected and used to tune the parser later. Temporary corpus collection, not part of the trading pipeline.
    public static class RawMessageDumpWriter
    {
        private const string DumpFolder = @"C:\Users\vi_pa\Projects\OptionTradesParser\tmp";

        public static void Save(ulong messageId, DateTimeOffset timestamp, string dumpText)
        {
            try
            {
                Directory.CreateDirectory(DumpFolder);
                string fileName = $"{timestamp.UtcDateTime:yyyyMMdd_HHmmss}_{messageId}.txt";
                File.WriteAllText(Path.Combine(DumpFolder, fileName), dumpText);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [RAW DUMP WRITE ERROR]: {ex.Message}");
            }
        }
    }
}
