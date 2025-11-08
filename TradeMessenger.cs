#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.StrategyGenerator;
using NinjaTrader.Data;

#endregion

//This namespace holds Strategies in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Strategies.AutoEdge
{
    public class TradeMessenger : Strategy
    {
        public TradeMessenger()
        {
            VendorLicense(338);
        }
        
        // Notifications
        [NinjaScriptProperty]
        [Display(Name = "Send Push Notifications", Description = "Enable push notifications for alerts", Order = 1, GroupName = "A. Notifications")]
        public bool SendPushNotifications { get; set; }
		
		[NinjaScriptProperty]
        [Display(Name = "Show Account Name", Description = "Show account name in message", Order = 2, GroupName = "A. Notifications")]
        public bool ShowAccountName { get; set; }
		
		[NinjaScriptProperty]
        [Display(Name = "Show Entry", Description = "Show when a new trade is being places", Order = 3, GroupName = "A. Notifications")]
        public bool ShowEntry{ get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Exit", Description = "Show when a new trade exit", Order = 4, GroupName = "A. Notifications")]
        public bool ShowExit{ get; set; }

        // Discord
        [NinjaScriptProperty]
        [Display(Name = "Send to Discord", Description = "Enable push notifications for Discord", Order = 1, GroupName = "B. Discord")]
        public bool SendToDiscord { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Discord Webhook URL", Order = 2, GroupName = "B. Discord")]
        public string DiscordWebhookUrl { get; set; }
	
        // Telegram
        [NinjaScriptProperty]
        [Display(Name = "Send to Telegram", Description = "Enable push notifications for Discord", Order = 1, GroupName = "C. Telegram")]
        public bool SendToTelegram { get; set; }
    
		[NinjaScriptProperty]
        [Display(Name = "Bot token", Description = "The token for telegram bot", Order = 2, GroupName = "C. Telegram")]
        public string BotToken { get; set; }
		
		[NinjaScriptProperty]
        [Display(Name = "Chat ID", Description = "The ID of the chat that messages will turn up in", Order = 3, GroupName = "C. Telegram")]
        public string ChatId { get; set; }

        // Alive monitoring
        [NinjaScriptProperty]
        [Display(Name = "Data feed Reporting", Order = 1, GroupName = "D. Alive monitoring")]
        public bool DataFeedReporting { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Data Timeout (Seconds)", Description = "Seconds without data update before warning", Order = 2, GroupName = "D. Alive monitoring")]
        public int WatchdogDataTimeoutSeconds { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Heartbeat Reporting", Order = 3, GroupName = "D. Alive monitoring")]
        public bool HeartbeatReporting { get; set; }

        // [NinjaScriptProperty]
        // [Display(Name = "Heartbeat File Path", Description = "Path to heartbeat file written by strategies", Order = 4 , GroupName = "D. Alive monitoring")]
        internal string HeartbeatFilePath { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Heartbeat Timeout (Seconds)", Description = "Seconds without update before warning", Order = 5, GroupName = "D. Alive monitoring")]
        public int HeartbeatTimeoutSeconds { get; set; }

        private bool debug = false;
        private double entryPrice = 0;
        private bool entryIsShort = false;
        private DateTime lastTickTime = DateTime.UtcNow;
        private DateTime lastCheckTime = DateTime.MinValue;
        private bool feedStalled = false;
        private System.Timers.Timer watchdogTimer;
        private Dictionary<string, DateTime> strategyHeartbeats = new Dictionary<string, DateTime>();
        private Dictionary<string, bool> strategyStalled = new Dictionary<string, bool>();
        private Dictionary<string, int> strategyStartupCount = new Dictionary<string, int>();

        protected override void OnStateChange()
        {
			if (State == State.SetDefaults)
            {
                Name = "TradeMessenger";
				ShowAccountName = false;
				ShowEntry = false;
                ShowExit = false;
				SendPushNotifications = true;
                SendToTelegram = false;
                SendToTelegram = false;
                DataFeedReporting = false;
                WatchdogDataTimeoutSeconds = 60;
                HeartbeatReporting = true;
                HeartbeatFilePath = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "TradeMessengerHeartbeats.csv");
                HeartbeatTimeoutSeconds = 60;
				BotToken = "";//"8179492057:AAEdPEbpXTrS0X_ksrnpSZZrlFHb57Om--U";
           		ChatId = "";//"5763704400";
                DiscordWebhookUrl = "";
			}
            else if (State == State.DataLoaded)
            {
                Account.ExecutionUpdate += OnAccountExecutionUpdate;

                watchdogTimer = new System.Timers.Timer(5000); // Check every 5 seconds
                watchdogTimer.Elapsed += WatchdogTimerElapsed;
                watchdogTimer.AutoReset = true;
                watchdogTimer.Start();
            }
            else if (State == State.Terminated)
            {
                Account.ExecutionUpdate -= OnAccountExecutionUpdate;

                if (watchdogTimer != null)
                {
                    watchdogTimer.Stop();
                    watchdogTimer.Dispose();
                    watchdogTimer = null;
                }
            }

        }

        private void OnAccountExecutionUpdate(object sender, ExecutionEventArgs e)
        {
            // Grab the Execution object
            Execution exec = e.Execution;
			DateTime filltime = e.Time;

            // Only care about our instrument
            if (exec.Instrument.FullName != Instrument.FullName)
                return;

            // Must have an Order and be fully filled
            if (exec.Order == null || exec.Order.OrderState != OrderState.Filled)
                return;

            // Determine type of fill
            switch (exec.Order.OrderAction)
            {
                case OrderAction.Buy:
                    // Long entry
                    entryPrice = exec.Price;
                    entryIsShort = false;
                   	if (ShowEntry) createPushNotification(filltime, "Long", 0);
                    break;

                case OrderAction.SellShort:
                    // Short entry
                    entryPrice = exec.Price;
                    entryIsShort = true;
                    if (ShowEntry) createPushNotification(filltime, "Short", 0);
                    break;

                case OrderAction.Sell:
                case OrderAction.BuyToCover:
                    // Exit – compute PnL using instrument multiplier
                    double exitPrice = exec.Price;
                    double qty = exec.Quantity;
                    double points = entryIsShort
                        ? entryPrice - exitPrice
                        : exitPrice - entryPrice;

                    // Multiply by point value (e.g. tick value * ticks per point)
                    double tradePnl = points * Instrument.MasterInstrument.PointValue * qty;
                    if (ShowExit) createPushNotification(filltime, "Exit", tradePnl);
                    break;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1)
                return;

            // Check only once per bar or use a timer
            if ((DateTime.UtcNow - lastCheckTime).TotalSeconds >= 5)
            {
                lastCheckTime = DateTime.UtcNow;

                double secondsSinceLastTick = (DateTime.UtcNow - lastTickTime).TotalSeconds;

                if (!feedStalled && secondsSinceLastTick >= WatchdogDataTimeoutSeconds)
                {
                    feedStalled = true;
                    LogToOutput2($"{Time[0]} - ⚠️ Data feed stalled: no ticks for {secondsSinceLastTick:F0} seconds.");
                    if (SendPushNotifications && DataFeedReporting)
                        sendPushNotification(Time[0], $"⚠️ WARNING: Data feed stalled on {Instrument.FullName}. No ticks for {secondsSinceLastTick:F0} seconds.");
                }
            }
        }

        protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
        {
            if (marketDataUpdate.MarketDataType != MarketDataType.Last)
                return;

            // Update the time of the last received tick
            lastTickTime = DateTime.UtcNow;

            // If feed was stalled and now we get data again
            if (feedStalled)
            {
                feedStalled = false;
                LogToOutput2($"{Time[0]} - ✅ Data feed has resumed.");
                if (SendPushNotifications)
                    sendPushNotification(Time[0], $"✅ Data feed has resumed on {Instrument.FullName}");
            }

            // Timer-like check: only check every 5 seconds
            if ((DateTime.UtcNow - lastCheckTime).TotalSeconds < 5)
                return;

            lastCheckTime = DateTime.UtcNow;

            double secondsSinceLastTick = (DateTime.UtcNow - lastTickTime).TotalSeconds;

            if (!feedStalled && secondsSinceLastTick >= WatchdogDataTimeoutSeconds)
            {
                feedStalled = true;
                LogToOutput2($"{Time[0]} - ⚠️ Data feed stalled: no ticks for {secondsSinceLastTick:F0} seconds.");
                if (SendPushNotifications)
                    sendPushNotification(Time[0], $"⚠️ WARNING: Data feed stalled on {Instrument.FullName}.");
            }
        }

        private void createPushNotification(DateTime filltime, string reason, double tradePnl)
        {
            // skip any zero-PnL Exit if undesired
            if (reason == "Exit" && tradePnl == 0)
                return;

			 // Get account name
            string acctName = ShowAccountName ? Account.Name : "";
			double cashValue = Account.Get(AccountItem.CashValue, Currency.UsDollar) + tradePnl;
			
            string message;
            if (reason == "Long" || reason == "Short")
            {
                message = $"{Instrument.FullName} {reason}";
            }
            else
            {
                // Format with en-US culture
                var usCulture = new CultureInfo("en-US");
                var pnlText = tradePnl.ToString("C", usCulture);
                var emoji = tradePnl >= 0 ? "💰" : "🔻";
                var cashText = cashValue.ToString("C0", usCulture); // "C0" = currency, 0 decimals
                message = $"{emoji} {pnlText} {Instrument.FullName} \n🏦 {cashText} \n{acctName}";

            }

            LogToOutput2($"{filltime} - {message}");
            if (SendPushNotifications)
                sendPushNotification(filltime, message);
        }

        private void sendPushNotification(DateTime filltime, string message)
        {
            if (SendToTelegram) {
                sendTelegramNotification(filltime, message);
            }
            if (SendToDiscord) {
                sendDiscordNotification(filltime, message);
            }
        }

        private void sendTelegramNotification(DateTime filltime, string message)
        {
            //string botToken = "8179492057:AAEdPEbpXTrS0X_ksrnpSZZrlFHb57Om--U";
            //string chatID = "5763704400";
            string url = $"https://api.telegram.org/bot{BotToken}/sendMessage";
            string postData = $"chat_id={ChatId}&text={Uri.EscapeDataString(message)}";

            try
            {
                using (var client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.ContentType] = "application/x-www-form-urlencoded";
                    client.UploadString(url, postData);
                }
            }
            catch (WebException ex)
            {
                LogToOutput2($"{filltime} - Failed to send push notification: " + ex.Message);
            }
        }

        private void sendDiscordNotification(DateTime filltime, string message)
        {
            if (string.IsNullOrWhiteSpace(DiscordWebhookUrl))
            {
                LogToOutput2($"{filltime} - ⚠️ No Discord webhook URL configured.");
                return;
            }

            try
            {
                // Build JSON safely (handles quotes, backslashes, emojis, etc.)
                var payload = new Dictionary<string, string>
                {
                    { "content", message }  // Discord supports \n in content; no need to escape manually
                };
                string jsonPayload = new JavaScriptSerializer().Serialize(payload);

                // Send as UTF-8 bytes with proper charset
                var request = (HttpWebRequest)WebRequest.Create(DiscordWebhookUrl);
                request.Method = "POST";
                request.ContentType = "application/json; charset=utf-8";

                byte[] body = Encoding.UTF8.GetBytes(jsonPayload);
                request.ContentLength = body.Length;

                using (var reqStream = request.GetRequestStream())
                    reqStream.Write(body, 0, body.Length);

                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    // Optional: read response to confirm success
                    // using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    //     LogToOutput2($"Discord response: {reader.ReadToEnd()}");
                }
            }
            catch (WebException ex)
            {
                string responseText = "";
                try
                {
                    using (var resp = (HttpWebResponse)ex.Response)
                    using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                        responseText = reader.ReadToEnd();
                }
                catch { /* ignore */ }

                LogToOutput2($"{filltime} - ❌ Discord send error: {ex.Message}, Response: {responseText}");
            }
            catch (Exception ex)
            {
                LogToOutput2($"{filltime} - 🚨 Unexpected error in sendPushNotification: {ex.Message}");
            }
        }

        private void LogToOutput2(string message)
        {
            if (debug)
                NinjaTrader.Code.Output.Process(message, PrintTo.OutputTab2);
        }

        private void WatchdogTimerElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            double secondsSinceLastTick = (DateTime.UtcNow - lastTickTime).TotalSeconds;

            if (secondsSinceLastTick > 5) LogToOutput2($"Watchdog timer fired. Seconds since last tick: {secondsSinceLastTick:F0}");

            if (!feedStalled && secondsSinceLastTick >= WatchdogDataTimeoutSeconds)
            {
                feedStalled = true;
                string logMessage = $"⚠️ Data feed stalled: no ticks for {secondsSinceLastTick:F0} seconds.";
                LogToOutput2($"{DateTime.UtcNow} - {logMessage}");
                if (SendPushNotifications)
                    sendPushNotification(DateTime.UtcNow, logMessage);
            }

            try
            {
                // --- Check heartbeat file dynamically
                if (!string.IsNullOrEmpty(HeartbeatFilePath))
                    CheckHeartbeatFileMulti();
            }
            catch (Exception ex)
            {
                LogToOutput2($"Heartbeat watchdog error: {ex.Message}");
            }
        }

        private void CheckHeartbeatFileMulti()
        {
            if (!HeartbeatReporting)
            {
                LogToOutput2("💤 Heartbeat reporting disabled.");
                return;
            }

            if (string.IsNullOrEmpty(HeartbeatFilePath))
            {
                LogToOutput2("⚠️ No heartbeat file path specified.");
                return;
            }

            if (!System.IO.File.Exists(HeartbeatFilePath))
            {
                LogToOutput2($"⚠️ Heartbeat file not found: {HeartbeatFilePath}");
                return;
            }

            //LogToOutput2($"🔍 Checking heartbeat file: {HeartbeatFilePath}");

            string[] lines = Array.Empty<string>();
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    lines = System.IO.File.ReadAllLines(HeartbeatFilePath);
                    //LogToOutput2($"✅ Read {lines.Length} lines from heartbeat file.");
                    break;
                }
                catch (System.IO.IOException ioex)
                {
                    LogToOutput2($"⚠️ File read attempt {i + 1} failed: {ioex.Message}");
                    System.Threading.Thread.Sleep(100);
                }
            }

            if (lines.Length == 0)
            {
                LogToOutput2("⚠️ Heartbeat file is empty — no strategies reporting yet.");
                return;
            }

            DateTime now = DateTime.Now; // 🕓 local time (must match Duo's DateTime.Now)

            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                string[] parts = line.Split(',');
                if (parts.Length < 2)
                    continue;

                string strategyName = parts[0].Trim();

                // --- Parse timestamp as local time (safe across all users)
                if (!DateTime.TryParse(parts[1], out DateTime heartbeatTime))
                {
                    LogToOutput2($"⚠️ Could not parse heartbeat time for {strategyName}: {parts[1]}");
                    continue;
                }

                double elapsed = (now - heartbeatTime).TotalSeconds;
                if (elapsed < 0) elapsed = 0; // clamp in case of small clock drift

                // Store heartbeat time
                strategyHeartbeats[strategyName] = heartbeatTime;

                // Initialize tracking
                if (!strategyStalled.ContainsKey(strategyName))
                    strategyStalled[strategyName] = false;
                if (!strategyStartupCount.ContainsKey(strategyName))
                    strategyStartupCount[strategyName] = 0;

                //LogToOutput2($"💓 {strategyName}: last update {elapsed:F1}s ago (at {heartbeatTime:HH:mm:ss}). StartupCount={strategyStartupCount[strategyName]}");

                // --- Smart startup logic ---
                if (elapsed > 300 && strategyStartupCount[strategyName] == 0)
                {
                    LogToOutput2($"⏸️ {strategyName}: Ignoring old heartbeat (stale from previous run, {elapsed:F0}s old).");
                    continue;
                }

                // Recent heartbeat within timeout
                if (elapsed < HeartbeatTimeoutSeconds)
                {
                    if (strategyStartupCount[strategyName] < 3)
                        strategyStartupCount[strategyName]++;

                    if (strategyStartupCount[strategyName] < 3)
                    {
                        LogToOutput2($"🕓 {strategyName}: Warming up ({strategyStartupCount[strategyName]}/3 heartbeats).");
                        continue;
                    }
                }

                // --- Regular monitoring after warm-up ---
                // --- Regular monitoring after warm-up ---
                if (!strategyStalled[strategyName] && elapsed >= HeartbeatTimeoutSeconds)
                {
                    strategyStalled[strategyName] = true;

                    // 🧠 Reset warm-up counter so it must send 3 fresh beats before "resumed"
                    strategyStartupCount[strategyName] = 0;

                    string msg = $"⚠️ Strategy '{strategyName}' has not updated for {elapsed:F0}s. It may have stopped or disabled itself.";
                    LogToOutput2(msg);
                    if (SendPushNotifications)
                        sendPushNotification(DateTime.Now, msg);
                }
                else if (strategyStalled[strategyName] && elapsed < HeartbeatTimeoutSeconds)
                {
                    // Only resume after enough new heartbeats since reactivation
                    if (strategyStartupCount[strategyName] < 3)
                    {
                        strategyStartupCount[strategyName]++;
                        LogToOutput2($"🕓 {strategyName}: Re-warming after stall ({strategyStartupCount[strategyName]}/3 heartbeats).");
                        continue;
                    }

                    strategyStalled[strategyName] = false;
                    string msg = $"✅ Strategy '{strategyName}' resumed updates ({elapsed:F0}s since last heartbeat).";
                    LogToOutput2(msg);
                    if (SendPushNotifications)
                        sendPushNotification(DateTime.Now, msg);
                }
            }

            // Cleanup: remove strategies no longer in file
            foreach (var key in new List<string>(strategyHeartbeats.Keys))
            {
                if (Array.Find(lines, l => l.StartsWith(key + ",")) == null)
                {
                    LogToOutput2($"🧹 Removing stale entry for {key} (no longer in file).");
                    strategyHeartbeats.Remove(key);
                    strategyStalled.Remove(key);
                    strategyStartupCount.Remove(key);
                }
            }
        }


    }
}