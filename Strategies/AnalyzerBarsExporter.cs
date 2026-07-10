#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Text;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.AutoEdge
{
    public class AnalyzerBarsExporter : Strategy
    {
        private StreamWriter writer;
        private string tempFilePath = string.Empty;
        private string finalFilePath = string.Empty;
        private string metadataFilePath = string.Empty;
        private DateTime firstBarTime = DateTime.MinValue;
        private DateTime lastBarTime = DateTime.MinValue;
        private int barsWritten;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "AnalyzerBarsExporter";
                Description = "Exports the exact primary bars seen by this strategy/analyzer run in Trader-compatible minute CSV format.";
                Calculate = Calculate.OnBarClose;
                IsOverlay = false;
                BarsRequiredToTrade = 0;
                IsInstantiatedOnEachOptimizationIteration = false;

                ExportRoot = string.Empty;
                ExportFileName = string.Empty;
                OverwriteExisting = true;
                WriteMetadataFile = true;
            }
            else if (State == State.DataLoaded)
            {
                PrepareOutputFiles();
            }
            else if (State == State.Terminated)
            {
                FinalizeOutputFiles();
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
            {
                return;
            }

            if (writer is null)
            {
                return;
            }

            var barTime = Time[0];
            if (firstBarTime == DateTime.MinValue)
            {
                firstBarTime = barTime;
            }

            lastBarTime = barTime;
            writer.Write(barTime.ToString("yyyyMMdd HHmmss", CultureInfo.InvariantCulture));
            writer.Write(';');
            writer.Write(Open[0].ToString("0.########", CultureInfo.InvariantCulture));
            writer.Write(';');
            writer.Write(High[0].ToString("0.########", CultureInfo.InvariantCulture));
            writer.Write(';');
            writer.Write(Low[0].ToString("0.########", CultureInfo.InvariantCulture));
            writer.Write(';');
            writer.Write(Close[0].ToString("0.########", CultureInfo.InvariantCulture));
            writer.Write(';');
            writer.WriteLine(Volume[0].ToString(CultureInfo.InvariantCulture));

            barsWritten++;
        }

        private void PrepareOutputFiles()
        {
            var root = ResolveExportRoot();
            var contractDirectory = Path.Combine(root, SanitizePathComponent(Instrument.FullName));
            Directory.CreateDirectory(contractDirectory);

            var stem = ResolveFileStem();
            finalFilePath = Path.Combine(contractDirectory, stem + ".csv");
            metadataFilePath = Path.Combine(contractDirectory, stem + ".meta.txt");
            tempFilePath = Path.Combine(contractDirectory, stem + ".tmp");

            if (OverwriteExisting)
            {
                TryDelete(finalFilePath);
                TryDelete(metadataFilePath);
                TryDelete(tempFilePath);
            }
            else if (File.Exists(finalFilePath))
            {
                throw new InvalidOperationException("AnalyzerBarsExporter output file already exists: " + finalFilePath);
            }

            writer = new StreamWriter(
                new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.Read),
                new UTF8Encoding(false));
        }

        private void FinalizeOutputFiles()
        {
            if (writer is null)
            {
                return;
            }

            try
            {
                writer.Flush();
                writer.Dispose();
                writer = null;

                if (barsWritten == 0)
                {
                    TryDelete(tempFilePath);
                    return;
                }

                File.Move(tempFilePath, finalFilePath);

                if (WriteMetadataFile)
                {
                    File.WriteAllText(metadataFilePath, BuildMetadata(), new UTF8Encoding(false));
                }

                Print("AnalyzerBarsExporter wrote " + barsWritten + " bars to " + finalFilePath);
            }
            catch (Exception ex)
            {
                Print("AnalyzerBarsExporter failed to finalize export: " + ex);
                throw;
            }
        }

        private string ResolveExportRoot()
        {
            if (string.IsNullOrWhiteSpace(ExportRoot))
            {
                return Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "db", "analyzer-bars");
            }

            return ExportRoot.Trim();
        }

        private string ResolveFileStem()
        {
            if (string.IsNullOrWhiteSpace(ExportFileName) is false)
            {
                return Path.GetFileNameWithoutExtension(ExportFileName.Trim());
            }

            var periodType = BarsPeriod.BarsPeriodType.ToString();
            var periodValue = BarsPeriod.Value.ToString(CultureInfo.InvariantCulture);
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}-{1}-{2}",
                SanitizePathComponent(periodType + periodValue),
                SanitizePathComponent(Bars?.TradingHours?.Name ?? "UnknownHours"),
                timestamp);
        }

        private string BuildMetadata()
        {
            var builder = new StringBuilder();
            builder.AppendLine("Name=" + Name);
            builder.AppendLine("Instrument=" + Instrument.FullName);
            builder.AppendLine("MasterInstrument=" + Instrument.MasterInstrument.Name);
            builder.AppendLine("BarsPeriodType=" + BarsPeriod.BarsPeriodType);
            builder.AppendLine("BarsPeriodValue=" + BarsPeriod.Value.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("TradingHours=" + (Bars?.TradingHours?.Name ?? string.Empty));
            builder.AppendLine("BarsType=" + (BarsArray != null && BarsArray.Length > 0 && BarsArray[0]?.BarsType != null ? BarsArray[0].BarsType.GetType().FullName : string.Empty));
            builder.AppendLine("Calculate=" + Calculate);
            builder.AppendLine("BarsRequiredToTrade=" + BarsRequiredToTrade.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("BarsWritten=" + barsWritten.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("FirstBarTime=" + (firstBarTime == DateTime.MinValue ? string.Empty : firstBarTime.ToString("O", CultureInfo.InvariantCulture)));
            builder.AppendLine("LastBarTime=" + (lastBarTime == DateTime.MinValue ? string.Empty : lastBarTime.ToString("O", CultureInfo.InvariantCulture)));
            return builder.ToString();
        }

        private static string SanitizePathComponent(string value)
        {
            var sanitized = value;
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                sanitized = sanitized.Replace(invalid, '_');
            }

            return sanitized.Replace(':', '_');
        }

        private static void TryDelete(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || File.Exists(path) is false)
            {
                return;
            }

            File.Delete(path);
        }

        [NinjaScriptProperty]
        [Display(Name = "Export Root", Order = 1, GroupName = "Export")]
        public string ExportRoot
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Export File Name", Order = 2, GroupName = "Export")]
        public string ExportFileName
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Overwrite Existing", Order = 3, GroupName = "Export")]
        public bool OverwriteExisting
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Write Metadata File", Order = 4, GroupName = "Export")]
        public bool WriteMetadataFile
        { get; set; }
    }
}
