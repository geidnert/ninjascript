using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Timers;

namespace NinjaTrader.NinjaScript.Strategies.AutoEdge
{
    internal sealed class StrategyHeartbeatReporter : IDisposable
    {
        private readonly string strategyName;
        private readonly string heartbeatFilePath;
        private readonly Timer timer;

        internal StrategyHeartbeatReporter(string strategyName, string heartbeatFilePath, int intervalSeconds = 10)
        {
            this.strategyName = strategyName ?? string.Empty;
            this.heartbeatFilePath = heartbeatFilePath ?? string.Empty;

            timer = new Timer(Math.Max(1, intervalSeconds) * 1000.0);
            timer.AutoReset = true;
            timer.Elapsed += OnTimerElapsed;
        }

        internal void Start()
        {
            if (timer.Enabled)
                return;

            WriteHeartbeat();
            timer.Start();
        }

        internal void Stop()
        {
            if (!timer.Enabled)
                return;

            timer.Stop();
        }

        private void OnTimerElapsed(object sender, ElapsedEventArgs e)
        {
            WriteHeartbeat();
        }

        private void WriteHeartbeat()
        {
            if (string.IsNullOrWhiteSpace(strategyName) || string.IsNullOrWhiteSpace(heartbeatFilePath))
                return;

            string heartbeatLine = string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1}",
                strategyName,
                DateTime.Now.ToString("o", CultureInfo.InvariantCulture));

            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(heartbeatFilePath) ?? string.Empty);

                    List<string> lines = File.Exists(heartbeatFilePath)
                        ? File.ReadAllLines(heartbeatFilePath).Where(line => !string.IsNullOrWhiteSpace(line)).ToList()
                        : new List<string>();

                    bool updated = false;
                    string prefix = strategyName + ",";
                    for (int i = 0; i < lines.Count; i++)
                    {
                        if (!lines[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            continue;

                        lines[i] = heartbeatLine;
                        updated = true;
                        break;
                    }

                    if (!updated)
                        lines.Add(heartbeatLine);

                    File.WriteAllLines(heartbeatFilePath, lines);
                    return;
                }
                catch (IOException)
                {
                    System.Threading.Thread.Sleep(50);
                }
                catch (UnauthorizedAccessException)
                {
                    System.Threading.Thread.Sleep(50);
                }
            }
        }

        public void Dispose()
        {
            Stop();
            timer.Elapsed -= OnTimerElapsed;
            timer.Dispose();
        }
    }
}
