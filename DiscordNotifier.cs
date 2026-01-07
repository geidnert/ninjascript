#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.AutoEdge
{
    public class DiscordNotifier : Strategy
    {
        [NinjaScriptProperty]
        [Display(Name = "Discord Bot Token", Order = 1, GroupName = "Discord")]
        public string DiscordBotToken { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Discord Channel ID", Order = 2, GroupName = "Discord")]
        public string DiscordChannelId { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Instance ID", Order = 3, GroupName = "Discord")]
        public string InstanceId { get; set; } = "Default";

        private double entryPrice = 0;
        private bool entryIsShort = false;
        private DateTime entryTime = DateTime.MinValue;
        private string statsFilePath;
        private string messageIdFilePath;
        private string lastMessageId = "";
        private object fileLock = new object();
        private CultureInfo usCulture = new CultureInfo("en-US");
        private string currentPositionStatus = "";
        private bool positionActive = false;
        private Dictionary<DateTime, Dictionary<string, double>> dailyPnls = new Dictionary<DateTime, Dictionary<string, double>>();
        private DateTime currentWeekStart = DateTime.MinValue;
        private DateTime lastSavedWeekStart = DateTime.MinValue;
        private bool workingOrderActive = false;
        private string workingOrderSide = "";
        private double monthlyTotal = 0;
        private string currentMonth = "";
        private Dictionary<DateTime, double> monthlyDayTotals = new Dictionary<DateTime, double>();
        private DateTime lastMessageDay = DateTime.MinValue;
        // Internal toggle: when true, skip Discord posts and log the full message for debugging.
        private bool debugMode = false;
        private readonly object discordQueueLock = new object();
        private Queue<DiscordMessage> discordQueue = new Queue<DiscordMessage>();
        private bool isProcessingDiscordQueue = false;
        private bool stopDiscordQueue = false;
        private int discordMinDelayMs = 1000;

        private class DiscordMessage
        {
            public string Content { get; set; }
            public DateTime Day { get; set; }
            public int Attempts { get; set; }
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "DiscordNotifier";
                DiscordBotToken = "";
                DiscordChannelId = "";
                InstanceId = "Default";
            }
            else if (State == State.Configure)
            {
                string safeId = InstanceId.Replace(" ", "_").Replace("/", "_");
                string baseDir = NinjaTrader.Core.Globals.UserDataDir;
                statsFilePath = Path.Combine(baseDir, $"DiscordNotifierStats_{safeId}.json");
                messageIdFilePath = Path.Combine(baseDir, $"DiscordNotifierMessageId_{safeId}.txt");
            }
            else if (State == State.DataLoaded)
            {
                Account.ExecutionUpdate += OnAccountExecutionUpdate;
                Account.OrderUpdate += OnAccountOrderUpdate;

                // If we start while already in a position, seed the entry info so the first exit isn't mis-priced.
                var pos = Position;
                if (pos != null && pos.MarketPosition != MarketPosition.Flat)
                {
                    entryPrice = pos.AveragePrice;
                    entryIsShort = pos.MarketPosition == MarketPosition.Short;
                    positionActive = true;
                    currentPositionStatus = entryIsShort ? "Currently Short" : "Currently Long";
                    LogToOutput2($"ℹ️ Resuming with existing {pos.MarketPosition} position from {entryPrice}.");
                }

                LogToOutput2("🧹 Preparing DiscordNotifier state...");
                if (debugMode)
                    LogToOutput2("🐞 Debug mode is ON — Discord messages will be logged locally instead of posted.");

                // Load saved message ID
                if (File.Exists(messageIdFilePath))
                {
                    LoadMessageId();
                    LogToOutput2($"💾 Loaded saved message ID: '{lastMessageId}'");
                }
                else
                {
                    lastMessageId = "";
                    lastMessageDay = DateTime.MinValue;
                    LogToOutput2("ℹ️ No message ID file found — will create new Discord message on first update.");
                }

                // Load stats file
                if (File.Exists(statsFilePath))
                {
                    LoadPnLStats();
                    DateTime thisWeek;

                    if (dailyPnls.Count > 0)
                    {
                        DateTime latestTradeDate = DateTime.MinValue;
                        foreach (var d in dailyPnls.Keys)
                            if (d > latestTradeDate) latestTradeDate = d;

                        thisWeek = latestTradeDate.AddDays(-(int)latestTradeDate.DayOfWeek + (int)DayOfWeek.Monday);
                    }
                    else
                    {
                        // fallback: if no trades yet, use today's calendar
                        // DateTime now = Core.Globals.Now.Date;
                        // thisWeek = now.AddDays(-(int)now.DayOfWeek + (int)DayOfWeek.Monday);
                        thisWeek = DateTime.MinValue;
                    }

                    // ✅ Simple and direct rule
                    if (lastSavedWeekStart == thisWeek)
                    {
                        // Same week → keep stats, reuse message
                        currentWeekStart = thisWeek;
                        LogToOutput2($"🔁 Same week detected ({thisWeek:MMMM dd}), will reuse message ID '{lastMessageId}'.");
                    }
                    else if (lastSavedWeekStart < thisWeek)
                    {
                        // ✅ New week → reset stats and start fresh
                        LogToOutput2($"📅 New week detected (saved={lastSavedWeekStart:MMMM dd}, current={thisWeek:MMMM dd}). Starting new stats + new Discord message.");
                        dailyPnls.Clear();
                        currentWeekStart = thisWeek;
                        lastSavedWeekStart = thisWeek;
                        lastMessageId = "";
                        lastMessageDay = DateTime.MinValue;
                        SavePnLStats();    // reset stats file immediately
                        SaveMessageId();   // reset message ID immediately
                    }
                    else
                    {
                        // Future/invalid case → reset everything
                        LogToOutput2("⚠️ Invalid or future LastWeekStart detected — resetting.");
                        dailyPnls.Clear();
                        currentWeekStart = thisWeek;
                        lastSavedWeekStart = thisWeek;
                        lastMessageId = "";
                        lastMessageDay = DateTime.MinValue;
                        SavePnLStats();
                        SaveMessageId();
                    }

                }
                else
                {
                    LogToOutput2("ℹ️ No stats file found — starting from scratch.");
                    dailyPnls.Clear();
                    DateTime now = Core.Globals.Now.Date;
                    currentWeekStart = now.AddDays(-(int)now.DayOfWeek + (int)DayOfWeek.Monday);
                    lastSavedWeekStart = currentWeekStart;
                    lastMessageId = "";
                    lastMessageDay = DateTime.MinValue;
                    SaveMessageId();
                }
            }

            else if (State == State.Terminated)
            {
                Account.ExecutionUpdate -= OnAccountExecutionUpdate;
                Account.OrderUpdate -= OnAccountOrderUpdate;
                stopDiscordQueue = true;
                //SavePnLStats();
            }
        }

        private void OnAccountExecutionUpdate(object sender, ExecutionEventArgs e)
        {
            var exec = e.Execution;
            if (exec == null || exec.Order == null)
                return;
            if (exec.Instrument.FullName != Instrument.FullName)
                return;

            var order = exec.Order;

            // 🟠 2️⃣ Filled order becomes active position
            if (order.OrderState == OrderState.Filled)
            {
                switch (order.OrderAction)
                {
                    case OrderAction.Buy:
                        entryPrice = exec.Price;
                        entryIsShort = false;
                        entryTime = exec.Time;
                        positionActive = true;
                        workingOrderActive = false;
                        currentPositionStatus = "Currently Long";
                        PostPositionStatus(e.Time);
                        break;

                    case OrderAction.SellShort:
                        entryPrice = exec.Price;
                        entryIsShort = true;
                        entryTime = exec.Time;
                        positionActive = true;
                        workingOrderActive = false;
                        currentPositionStatus = "Currently Short";
                        PostPositionStatus(e.Time);
                        break;

                    case OrderAction.Sell:
                    case OrderAction.BuyToCover:
                        if (!positionActive)
                        {
                            LogToOutput2("⚠️ Ignoring exit fill because no entry was tracked (likely pre-existing position).");
                            return;
                        }

                        double exitPrice = exec.Price;
                        double qty = exec.Quantity;
                        double points = entryIsShort ? entryPrice - exitPrice : exitPrice - entryPrice;
                        double tradePnl = points * Instrument.MasterInstrument.PointValue * qty;

                        positionActive = false;
                        workingOrderActive = false;
                        currentPositionStatus = "";
                        AddTradeToWeek(entryTime, e.Time, tradePnl);
                        entryTime = DateTime.MinValue;
                        break;
                }
            }
        }

        private void OnAccountOrderUpdate(object sender, OrderEventArgs e)
        {
            var order = e.Order;
            if (order == null || order.Instrument.FullName != Instrument.FullName)
                return;

            // Ignore protective orders (TP/SL)
            string name = order.Name?.ToLowerInvariant() ?? "";
            bool isProtective = name.Contains("stop") || name.Contains("target") || name.Contains("tp") || name.Contains("sl");

            // Only show entry working status while FLAT
            if (Position.MarketPosition == MarketPosition.Flat && order.OrderState == OrderState.Working && !isProtective)
            {
                if (order.OrderAction == OrderAction.Buy || order.OrderAction == OrderAction.BuyToCover)
                {
                    workingOrderActive = true;
                    workingOrderSide = "Long";
                }
                else if (order.OrderAction == OrderAction.SellShort || order.OrderAction == OrderAction.Sell)
                {
                    workingOrderActive = true;
                    workingOrderSide = "Short";
                }
                else return;

                LogToOutput2($"📣 DiscordNotifier: Working {workingOrderSide} entry @ {order.LimitPrice}");
                PostPositionStatus(order.Time/*Core.Globals.Now*/);     // 🔁 re-render weekly summary (puts the line under today)
                return;
            }

            // If the entry is cancelled while flat → remove the line
            if (order.OrderState == OrderState.Cancelled && workingOrderActive && Position.MarketPosition == MarketPosition.Flat && !isProtective)
            {
                workingOrderActive = false;
                LogToOutput2("⚪ Working order cancelled");
                PostPositionStatus(order.Time/*Core.Globals.Now*/);     // 🔁 redraw without the line
                return;
            }
        }

        private void PostPositionStatus(DateTime filltime)
        {
            DateTime localDate = filltime.Date;
            DateTime weekStart = localDate.AddDays(-(int)localDate.DayOfWeek + (int)DayOfWeek.Monday);

            string monthKey = filltime.ToString("yyyy-MM");

            if (currentMonth == "")
            {
                // First load or fresh install
                currentMonth = monthKey;
                monthlyTotal = 0;
                monthlyDayTotals.Clear();
                SavePnLStats();
            }
            else if (monthKey != currentMonth)
            {
                // NEW MONTH
                LogToOutput2($"📆 New month detected! {currentMonth} → {monthKey}");

                currentMonth = monthKey;
                monthlyTotal = 0;
                monthlyDayTotals.Clear();
                SavePnLStats();
            }

             // 🧠 Check if we've moved into a new week
            if (lastSavedWeekStart != DateTime.MinValue && weekStart > lastSavedWeekStart)
            {
                LogToOutput2($"📅 New week detected! Old week was {lastSavedWeekStart:MMMM dd}, new week is {weekStart:MMMM dd}");

                // Reset all week-specific data
                dailyPnls.Clear();
                lastMessageId = "";
                SaveMessageId();

                currentWeekStart = weekStart;
                lastSavedWeekStart = weekStart;
                SavePnLStats();

            }
            else
            {
                currentWeekStart = weekStart;
                lastSavedWeekStart = weekStart;
            }
            PostDailySummary(filltime, includeStatus: true);
        }

        private void AddTradeToWeek(DateTime entryFillTime, DateTime exitFillTime, double tradePnl)
        {
            DateTime localDate = exitFillTime.Date;
            DateTime weekStart = localDate.AddDays(-(int)localDate.DayOfWeek + (int)DayOfWeek.Monday);

            string monthKey = exitFillTime.ToString("yyyy-MM");

            if (currentMonth == "")
            {
                // First load or fresh install
                currentMonth = monthKey;
                monthlyTotal = 0;
                monthlyDayTotals.Clear();
                SavePnLStats();
            }
            else if (monthKey != currentMonth)
            {
                // NEW MONTH
                LogToOutput2($"📆 New month detected! {currentMonth} → {monthKey}");

                currentMonth = monthKey;
                monthlyTotal = 0;
                monthlyDayTotals.Clear();
                SavePnLStats();
            }

            if (weekStart > lastSavedWeekStart)
            {
                currentWeekStart = weekStart;
                lastSavedWeekStart = weekStart;
                dailyPnls.Clear();
                lastMessageId = "";
                lastMessageDay = DateTime.MinValue;
                SaveMessageId();
                SavePnLStats();
                LogToOutput2($"📅 Starting new week: {currentWeekStart:MMMM dd, yyyy}");
            }

            if (!dailyPnls.ContainsKey(localDate))
                dailyPnls[localDate] = new Dictionary<string, double>();

            DateTime safeEntryTime = entryFillTime == DateTime.MinValue ? exitFillTime : entryFillTime;
            string tradeKey = BuildTradeKey(safeEntryTime, exitFillTime);
            if (dailyPnls[localDate].ContainsKey(tradeKey))
            {
                PostWeeklySummary(exitFillTime);
                return;
            }

            dailyPnls[localDate][tradeKey] = tradePnl;
            monthlyTotal += tradePnl;
            if (!monthlyDayTotals.ContainsKey(localDate))
                monthlyDayTotals[localDate] = 0;
            monthlyDayTotals[localDate] += tradePnl;

            SavePnLStats();
            PostWeeklySummary(exitFillTime);
        }

        private void PostWeeklySummary(DateTime filltime)
        {
            PostDailySummary(filltime, includeStatus: false);
        }

        private string FormatPnl(double pnl) =>
            pnl >= 0 ? pnl.ToString("C", usCulture) : "-" + Math.Abs(pnl).ToString("C", usCulture);

        private void PostDailySummary(DateTime filltime, bool includeStatus)
        {
            DateTime localDate = filltime.Date;

            if (!dailyPnls.ContainsKey(localDate))
                dailyPnls[localDate] = new Dictionary<string, double>();

            var sb = new StringBuilder();
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine($"📅 **{localDate:dddd MMMM dd, yyyy}**");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━\n");

            double dayTotal = 0;
            if (dailyPnls.TryGetValue(localDate, out var pnls))
            {
                foreach (var trade in pnls)
                {
                    double p = trade.Value;
                    if (TryFormatTradeTime(trade.Key, out string timeStr))
                        sb.AppendLine($"{FormatPnl(p)} — {timeStr}");
                    else
                        sb.AppendLine($"{FormatPnl(p)}");
                    dayTotal += p;
                }
            }

            if (includeStatus)
            {
                string timeStr = FormatTradeTime(filltime);
                if (positionActive)
                {
                    string coloredStatus = entryIsShort
                        ? $"🔴  **Currently Short — {timeStr}**"
                        : $"🟢  **Currently Long — {timeStr}**";
                    sb.AppendLine(coloredStatus);
                }
                else if (workingOrderActive)
                {
                    string workingLine = workingOrderSide == "Short"
                        ? $"🔴  **Working Order – Short — {timeStr}**"
                        : $"🟢  **Working Order – Long — {timeStr}**";
                    sb.AppendLine(workingLine);
                }
            }

            sb.AppendLine($"**{FormatPnl(dayTotal)} total**\n");

            double weeklyTotal = 0;
            foreach (var kvp in dailyPnls)
            {
                foreach (var trade in kvp.Value.Values)
                    weeklyTotal += trade;
            }

            sb.AppendLine("---");
            sb.AppendLine($"**Weekly Total: {FormatPnl(weeklyTotal)}**");
            int monthlyDayCount = monthlyDayTotals.Count;
            double monthlyDailyAverage = monthlyDayCount > 0 ? monthlyTotal / monthlyDayCount : 0;
            sb.AppendLine($"**Monthly Daily Average: {FormatPnl(monthlyDailyAverage)}**");
            sb.AppendLine($"**Monthly Total ({currentMonth}): {FormatPnl(monthlyTotal)}**");

            EnqueueDiscordMessage(sb.ToString(), localDate);
        }

        private void EnqueueDiscordMessage(string content, DateTime messageDay)
        {
            lock (discordQueueLock)
            {
                discordQueue.Enqueue(new DiscordMessage
                {
                    Content = content,
                    Day = messageDay.Date,
                    Attempts = 0
                });

                if (!isProcessingDiscordQueue)
                {
                    isProcessingDiscordQueue = true;
                    Task.Run(() => ProcessDiscordQueue());
                }
            }
        }

        private void ProcessDiscordQueue()
        {
            while (true)
            {
                if (stopDiscordQueue)
                    return;

                DiscordMessage message = null;
                lock (discordQueueLock)
                {
                    if (discordQueue.Count == 0)
                    {
                        isProcessingDiscordQueue = false;
                        return;
                    }
                    message = discordQueue.Dequeue();
                }

                bool success = SendOrUpdateDiscordMessageInternal(message.Content, message.Day);
                if (!success)
                {
                    message.Attempts++;
                    if (message.Attempts <= 10 && !stopDiscordQueue)
                    {
                        Thread.Sleep(discordMinDelayMs);
                        lock (discordQueueLock)
                        {
                            discordQueue.Enqueue(message);
                        }
                        continue;
                    }
                }

                Thread.Sleep(discordMinDelayMs);
            }
        }

        private bool SendOrUpdateDiscordMessageInternal(string content, DateTime messageDay)
        {
            // If somehow currentWeekStart is still uninitialized, use the saved one
            if (currentWeekStart == DateTime.MinValue && lastSavedWeekStart != DateTime.MinValue)
            {
                currentWeekStart = lastSavedWeekStart;
                LogToOutput2($"♻️ Restored currentWeekStart from saved file: {currentWeekStart:yyyy-MM-dd}");
            }

            LogToOutput2($"🧭 sendOrUpdateDiscordMessage: currentWeekStart={currentWeekStart:yyyy-MM-dd}, lastSavedWeekStart={lastSavedWeekStart:yyyy-MM-dd}, lastMessageId='{lastMessageId}'");
            if (currentWeekStart == DateTime.MinValue && lastSavedWeekStart != DateTime.MinValue)
                currentWeekStart = lastSavedWeekStart;

            if (debugMode)
            {
                LogToOutput2("🐞 Debug mode enabled — skipping Discord post. Message preview:");
                LogToOutput2(content);
                return true;
            }

            if (string.IsNullOrWhiteSpace(DiscordBotToken) || string.IsNullOrWhiteSpace(DiscordChannelId))
            {
                LogToOutput2("⚠️ Discord bot token or channel ID not set.");
                return true;
            }

            try
            {
                bool sameDay = lastMessageDay != DateTime.MinValue && lastMessageDay.Date == messageDay.Date;
                if (!string.IsNullOrEmpty(lastMessageId) && sameDay)
                {
                    try
                    {
                        var deleteReq = (HttpWebRequest)WebRequest.Create(
                            $"https://discord.com/api/v10/channels/{DiscordChannelId}/messages/{lastMessageId}");
                        deleteReq.Method = "DELETE";
                        deleteReq.Headers["Authorization"] = $"Bot {DiscordBotToken}";
                        using (var resp = (HttpWebResponse)deleteReq.GetResponse()) { }
                        LogToOutput2($"🗑️ Deleted old Discord message for current day ({lastMessageId}).");
                    }
                    catch (WebException delEx)
                    {
                        if (IsRateLimited(delEx))
                        {
                            LogToOutput2("⚠️ Rate limited while deleting message; will retry.");
                            return false;
                        }
                        LogToOutput2($"⚠️ Failed to delete old message: {delEx.Message}");
                    }
                }
                else if (!string.IsNullOrEmpty(lastMessageId))
                {
                    LogToOutput2($"🗓️ Keeping old message ({lastMessageId}) for previous day.");
                }
                else
                {
                    LogToOutput2("ℹ️ No previous Discord message ID found — will create a new one.");
                }

                var payload = new Dictionary<string, string> { { "content", content } };
                string jsonPayload = new JavaScriptSerializer().Serialize(payload);
                byte[] body = Encoding.UTF8.GetBytes(jsonPayload);

                var request = (HttpWebRequest)WebRequest.Create($"https://discord.com/api/v10/channels/{DiscordChannelId}/messages");
                request.Method = "POST";
                request.ContentType = "application/json; charset=utf-8";
                request.Headers["Authorization"] = $"Bot {DiscordBotToken}";
                request.ContentLength = body.Length;

                using (var reqStream = request.GetRequestStream())
                    reqStream.Write(body, 0, body.Length);

                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    string responseText = reader.ReadToEnd();
                    var serializer = new JavaScriptSerializer();
                    var result = serializer.Deserialize<Dictionary<string, object>>(responseText);

                    if (result.ContainsKey("id"))
                    {
                        lastMessageId = result["id"].ToString();
                        lastMessageDay = messageDay.Date;
                        SaveMessageId();
                        LogToOutput2($"✅ Posted new Discord message: {lastMessageId}");
                    }
                }
            }
            catch (WebException ex) when (IsRateLimited(ex))
            {
                LogToOutput2("⚠️ Rate limited while posting message; will retry.");
                return false;
            }
            catch (Exception ex)
            {
                LogToOutput2($"🚨 Discord update error: {ex.Message}");
            }
            return true;
        }

        private void SavePnLStats()
        {
            try
            {
                if (string.IsNullOrEmpty(statsFilePath)) return;

                lock (fileLock)
                {
                    var serializableDict = new Dictionary<string, Dictionary<string, double>>();
                    foreach (var kvp in dailyPnls)
                        serializableDict[kvp.Key.ToString("yyyy-MM-dd")] = new Dictionary<string, double>(kvp.Value);

                    var serializableMonthlyDayTotals = new Dictionary<string, double>();
                    foreach (var kvp in monthlyDayTotals)
                        serializableMonthlyDayTotals[kvp.Key.ToString("yyyy-MM-dd")] = kvp.Value;

                    var serializer = new JavaScriptSerializer();
                    var jsonObj = new 
                    {
                        LastWeekStart = lastSavedWeekStart.ToString("yyyy-MM-dd"),
                        CurrentMonth = currentMonth,
                        MonthlyTotal = monthlyTotal,
                        DailyPnLs = serializableDict,
                        MonthDailyTotals = serializableMonthlyDayTotals
                    };
                    string json = serializer.Serialize(jsonObj);
                    File.WriteAllText(statsFilePath, json, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                LogToOutput2($"⚠️ Failed to save stats: {ex.Message}");
            }
        }

        private void LoadPnLStats()
        {
            try
            {
                string json = File.ReadAllText(statsFilePath, Encoding.UTF8);
                var serializer = new JavaScriptSerializer();
                var data = serializer.Deserialize<Dictionary<string, object>>(json);
                if (data.ContainsKey("LastWeekStart"))
                    lastSavedWeekStart = DateTime.Parse(data["LastWeekStart"].ToString());

                dailyPnls.Clear();
                if (data.ContainsKey("DailyPnLs"))
                {
                    var raw = data["DailyPnLs"] as Dictionary<string, object>;
                    foreach (var kvp in raw)
                    {
                        if (DateTime.TryParse(kvp.Key, out DateTime keyDate))
                        {
                            var dict = new Dictionary<string, double>();
                            var inner = kvp.Value as Dictionary<string, object>;
                            foreach (var t in inner)
                                dict[t.Key] = Convert.ToDouble(t.Value);
                            dailyPnls[keyDate] = dict;
                        }
                    }
                }

                if (data.ContainsKey("CurrentMonth"))
                    currentMonth = data["CurrentMonth"].ToString();

                if (data.ContainsKey("MonthlyTotal"))
                    monthlyTotal = Convert.ToDouble(data["MonthlyTotal"]);
                else
                    monthlyTotal = 0;

                monthlyDayTotals.Clear();
                if (data.ContainsKey("MonthDailyTotals"))
                {
                    var rawMonthly = data["MonthDailyTotals"] as Dictionary<string, object>;
                    if (rawMonthly != null)
                    {
                        foreach (var kvp in rawMonthly)
                        {
                            if (DateTime.TryParse(kvp.Key, out DateTime keyDate))
                                monthlyDayTotals[keyDate] = Convert.ToDouble(kvp.Value);
                        }
                    }
                }

                LogToOutput2($"✅ Loaded PnL stats from {statsFilePath} (LastWeekStart={lastSavedWeekStart:MMMM dd})");
            }
            catch
            {
                dailyPnls.Clear();
                lastSavedWeekStart = DateTime.MinValue;
                monthlyDayTotals.Clear();
                LogToOutput2("ℹ️ No existing stats file found — starting fresh.");
            }
        }

        private void SaveMessageId()
        {
            try
            {
                if (string.IsNullOrEmpty(lastMessageId))
                {
                    File.WriteAllText(messageIdFilePath, "", Encoding.UTF8);
                    return;
                }

                string dayPart = lastMessageDay == DateTime.MinValue ? "" : lastMessageDay.ToString("yyyy-MM-dd");
                string content = string.IsNullOrEmpty(dayPart) ? lastMessageId : $"{dayPart}|{lastMessageId}";
                File.WriteAllText(messageIdFilePath, content, Encoding.UTF8);
            }
            catch (Exception ex) { LogToOutput2($"⚠️ Failed to save message ID: {ex.Message}"); }
        }

        private void LoadMessageId()
        {
            try
            {
                string content = File.ReadAllText(messageIdFilePath, Encoding.UTF8).Trim();
                if (string.IsNullOrEmpty(content))
                {
                    lastMessageId = "";
                    lastMessageDay = DateTime.MinValue;
                    return;
                }

                int separatorIndex = content.IndexOf("|", StringComparison.Ordinal);
                if (separatorIndex > 0)
                {
                    string datePart = content.Substring(0, separatorIndex);
                    string idPart = content.Substring(separatorIndex + 1);
                    if (DateTime.TryParse(datePart, out DateTime parsedDay))
                        lastMessageDay = parsedDay.Date;
                    lastMessageId = idPart;
                    return;
                }

                lastMessageId = content;
                lastMessageDay = DateTime.MinValue;
            }
            catch
            {
                lastMessageId = "";
                lastMessageDay = DateTime.MinValue;
            }
        }

        private string FormatTradeTime(DateTime time) =>
            $"{time.ToString("h:mmtt", CultureInfo.InvariantCulture).ToLower()} EST";

        private bool IsRateLimited(WebException ex)
        {
            var response = ex.Response as HttpWebResponse;
            return response != null && (int)response.StatusCode == 429;
        }

        private string FormatTradeTimeRange(DateTime entryTimeValue, DateTime exitTimeValue)
        {
            string entry = entryTimeValue.ToString("h:mm", CultureInfo.InvariantCulture);
            string exitWithMeridiem = exitTimeValue.ToString("h:mmtt", CultureInfo.InvariantCulture).ToLower();
            string entryMeridiem = entryTimeValue.ToString("tt", CultureInfo.InvariantCulture);
            string exitMeridiem = exitTimeValue.ToString("tt", CultureInfo.InvariantCulture);
            if (entryMeridiem == exitMeridiem)
                return $"{entry}-{exitWithMeridiem} EST";
            return $"{entryTimeValue.ToString("h:mmtt", CultureInfo.InvariantCulture).ToLower()}-{exitWithMeridiem} EST";
        }

        private string BuildTradeKey(DateTime entryTimeValue, DateTime exitTimeValue) =>
            $"{entryTimeValue.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture)}|{exitTimeValue.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture)}";

        private bool TryFormatTradeTime(string tradeKey, out string timeStr)
        {
            timeStr = "";
            if (string.IsNullOrEmpty(tradeKey))
                return false;

            string[] parts = tradeKey.Split('|');
            if (parts.Length == 2)
            {
                if (DateTime.TryParseExact(parts[0], "yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime entryTimeValue)
                    && DateTime.TryParseExact(parts[1], "yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime exitTimeValue))
                {
                    timeStr = FormatTradeTimeRange(entryTimeValue, exitTimeValue);
                    return true;
                }
            }

            if (DateTime.TryParseExact(tradeKey, "yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime singleTime))
            {
                timeStr = FormatTradeTime(singleTime);
                return true;
            }

            return false;
        }

        private void LogToOutput2(string message) =>
            NinjaTrader.Code.Output.Process(message, PrintTo.OutputTab2);

        private bool HasEntriesForCurrentWeek()
        {
            try
            {
                if (dailyPnls.Count == 0)
                    return false;

                DateTime now = Core.Globals.Now.Date;
                DateTime weekStart = now.AddDays(-(int)now.DayOfWeek + (int)DayOfWeek.Monday);
                foreach (var kvp in dailyPnls.Keys)
                {
                    DateTime dayWeekStart = kvp.AddDays(-(int)kvp.DayOfWeek + (int)DayOfWeek.Monday);
                    if (dayWeekStart == weekStart)
                        return true;
                }
            }
            catch { }
            return false;
        }
    }
}
