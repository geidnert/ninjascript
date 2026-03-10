#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Timers;
using System.Web.Script.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
#endregion

namespace NinjaTrader.NinjaScript.AddOns
{
    public class TradeMessengerAddOn : AddOnBase
    {
        internal static TradeMessengerAddOn Instance { get; private set; }

        private sealed class MarketDataSubscription
        {
            public Instrument Instrument { get; set; }
            public object MarketData { get; set; }
            public EventInfo UpdateEvent { get; set; }
            public Delegate Handler { get; set; }
        }

        private sealed class TrackedPositionState
        {
            public double SignedQuantity { get; set; }
            public double AverageEntryPrice { get; set; }
        }

        private sealed class ExecutionMessageResult
        {
            public bool IsEntryNotification { get; set; }
            public bool IsExitNotification { get; set; }
            public string BotName { get; set; }
            public string Message { get; set; }
        }

        private readonly object reconnectLock = new object();
        private readonly Dictionary<string, DateTime> strategyHeartbeats = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> strategyStalled = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> strategyStartupCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> lastTickTimeByInstrument = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> feedStalledByInstrument = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ConnectionStatus> lastConnectionStatusByName = new Dictionary<string, ConnectionStatus>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TrackedPositionState> trackedPositionsByKey = new Dictionary<string, TrackedPositionState>(StringComparer.OrdinalIgnoreCase);
        private readonly List<MarketDataSubscription> marketDataSubscriptions = new List<MarketDataSubscription>();
        private readonly HashSet<string> subscribedAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Timer watchdogTimer;
        private string configFilePath;
        private string heartbeatFilePath;
        private bool debug;
        private bool sendPushNotifications;
        private bool showEntry;
        private bool showExit;
        private bool sendToDiscord;
        private string discordWebhookUrl;
        private bool sendToTelegram;
        private string botToken;
        private string chatId;
        private bool dataFeedReporting;
        private int watchdogDataTimeoutSeconds;
        private bool heartbeatReporting;
        private int heartbeatTimeoutSeconds;
        private bool runHeadlessWhenWindowClosed;
        private bool autoReconnectEnabled;
        private int reconnectInitialDelaySeconds;
        private int reconnectMaxDelaySeconds;
        private int reconnectMaxAttempts;
        private string monitoredConnectionName;
        private string monitoredAccountName;
        private string monitoredInstrumentsCsv;

        private bool reconnectLoopActive;
        private bool reconnectInProgress;
        private int reconnectAttemptCount;
        private DateTime nextReconnectAttemptUtc = DateTime.MinValue;
        private string lastReconnectFailure = string.Empty;
        private bool connectionIssueActive;
        private bool monitoringActive;
        private NTMenuItem newMenuItem;
        private NTMenuItem autoEdgeMenuItem;
        private NTMenuItem launcherMenuItem;
        private TradeMessengerAddOnWindow settingsWindow;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "TradeMessengerAddOn";
            }
            else if (State == State.Active)
            {
                Instance = this;
                configFilePath = Path.Combine(Core.Globals.UserDataDir, "TradeMessengerAddOn.config");
                heartbeatFilePath = Path.Combine(Core.Globals.UserDataDir, "TradeMessengerHeartbeats.csv");
                ApplyDefaultSettings();
                LoadSettings();
                RefreshMonitoringState();
            }
            else if (State == State.Terminated)
            {
                CloseSettingsWindow();
                StopMonitoring();
                RemoveMenuItem();
                Instance = null;
            }
        }

        protected override void OnWindowCreated(Window window)
        {
            ControlCenter controlCenter = window as ControlCenter;
            if (controlCenter == null)
                return;

            controlCenter.Dispatcher.BeginInvoke(new Action(() => EnsureMenuItem(controlCenter)));
        }

        protected override void OnWindowDestroyed(Window window)
        {
            if (!(window is ControlCenter))
                return;

            RemoveMenuItem();
        }

        private void ApplyDefaultSettings()
        {
            debug = false;
            sendPushNotifications = true;
            showEntry = false;
            showExit = false;
            sendToDiscord = false;
            discordWebhookUrl = string.Empty;
            sendToTelegram = false;
            botToken = string.Empty;
            chatId = string.Empty;
            dataFeedReporting = true;
            watchdogDataTimeoutSeconds = 60;
            heartbeatReporting = true;
            heartbeatTimeoutSeconds = 60;
            runHeadlessWhenWindowClosed = true;
            autoReconnectEnabled = true;
            reconnectInitialDelaySeconds = 10;
            reconnectMaxDelaySeconds = 120;
            reconnectMaxAttempts = 0;
            monitoredConnectionName = string.Empty;
            monitoredAccountName = string.Empty;
            monitoredInstrumentsCsv = string.Empty;
        }

        internal TradeMessengerAddOnSettings GetSettingsSnapshot()
        {
            return new TradeMessengerAddOnSettings
            {
                Debug = debug,
                SendPushNotifications = sendPushNotifications,
                ShowEntry = showEntry,
                ShowExit = showExit,
                SendToDiscord = sendToDiscord,
                DiscordWebhookUrl = discordWebhookUrl,
                SendToTelegram = sendToTelegram,
                BotToken = botToken,
                ChatId = chatId,
                DataFeedReporting = dataFeedReporting,
                WatchdogDataTimeoutSeconds = watchdogDataTimeoutSeconds,
                HeartbeatReporting = heartbeatReporting,
                HeartbeatFilePath = heartbeatFilePath,
                HeartbeatTimeoutSeconds = heartbeatTimeoutSeconds,
                RunHeadlessWhenWindowClosed = runHeadlessWhenWindowClosed,
                AutoReconnectEnabled = autoReconnectEnabled,
                ReconnectInitialDelaySeconds = reconnectInitialDelaySeconds,
                ReconnectMaxDelaySeconds = reconnectMaxDelaySeconds,
                ReconnectMaxAttempts = reconnectMaxAttempts,
                MonitoredConnectionName = monitoredConnectionName,
                MonitoredAccountName = monitoredAccountName,
                MonitoredInstrumentsCsv = monitoredInstrumentsCsv
            };
        }

        internal string GetStatusSnapshot()
        {
            int subscribedInstrumentCount = marketDataSubscriptions.Count;
            int subscribedAccountCount;
            lock (subscribedAccounts)
                subscribedAccountCount = subscribedAccounts.Count;

            return string.Format(
                CultureInfo.InvariantCulture,
                "Reconnect loop: {0} | Feed stalled: {1} | Connection issue: {2} | Accounts: {3} | Instruments: {4}",
                monitoringActive ? (reconnectLoopActive ? "armed" : "idle") : "stopped",
                AnyFeedCurrentlyStalled() ? "yes" : "no",
                connectionIssueActive ? "yes" : "no",
                subscribedAccountCount,
                subscribedInstrumentCount);
        }

        internal void ApplySettings(TradeMessengerAddOnSettings settings)
        {
            if (settings == null)
                return;

            debug = settings.Debug;
            sendPushNotifications = settings.SendPushNotifications;
            showEntry = settings.ShowEntry;
            showExit = settings.ShowExit;
            sendToDiscord = settings.SendToDiscord;
            discordWebhookUrl = settings.DiscordWebhookUrl ?? string.Empty;
            sendToTelegram = settings.SendToTelegram;
            botToken = settings.BotToken ?? string.Empty;
            chatId = settings.ChatId ?? string.Empty;
            dataFeedReporting = settings.DataFeedReporting;
            watchdogDataTimeoutSeconds = Math.Max(1, settings.WatchdogDataTimeoutSeconds);
            heartbeatReporting = settings.HeartbeatReporting;
            heartbeatFilePath = string.IsNullOrWhiteSpace(settings.HeartbeatFilePath)
                ? Path.Combine(Core.Globals.UserDataDir, "TradeMessengerHeartbeats.csv")
                : settings.HeartbeatFilePath;
            heartbeatTimeoutSeconds = Math.Max(1, settings.HeartbeatTimeoutSeconds);
            runHeadlessWhenWindowClosed = settings.RunHeadlessWhenWindowClosed;
            autoReconnectEnabled = settings.AutoReconnectEnabled;
            reconnectInitialDelaySeconds = Math.Max(1, settings.ReconnectInitialDelaySeconds);
            reconnectMaxDelaySeconds = Math.Max(reconnectInitialDelaySeconds, settings.ReconnectMaxDelaySeconds);
            reconnectMaxAttempts = Math.Max(0, settings.ReconnectMaxAttempts);
            monitoredConnectionName = (settings.MonitoredConnectionName ?? string.Empty).Trim();
            monitoredAccountName = (settings.MonitoredAccountName ?? string.Empty).Trim();
            monitoredInstrumentsCsv = (settings.MonitoredInstrumentsCsv ?? string.Empty).Trim();

            if (monitoringActive)
                StopMonitoring();

            PersistSettings();
            RefreshMonitoringState();
        }

        private void LoadSettings()
        {
            if (!File.Exists(configFilePath))
            {
                WriteDefaultConfig();
                return;
            }

            foreach (string rawLine in File.ReadAllLines(configFilePath))
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                    continue;

                string line = rawLine.Trim();
                if (line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                int separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0)
                    continue;

                string key = line.Substring(0, separatorIndex).Trim();
                string value = line.Substring(separatorIndex + 1).Trim();

                switch (key)
                {
                    case "Debug":
                        debug = ParseBool(value, debug);
                        break;
                    case "SendPushNotifications":
                        sendPushNotifications = ParseBool(value, sendPushNotifications);
                        break;
                    case "ShowEntry":
                        showEntry = ParseBool(value, showEntry);
                        break;
                    case "ShowExit":
                        showExit = ParseBool(value, showExit);
                        break;
                    case "SendToDiscord":
                        sendToDiscord = ParseBool(value, sendToDiscord);
                        break;
                    case "DiscordWebhookUrl":
                        discordWebhookUrl = value;
                        break;
                    case "SendToTelegram":
                        sendToTelegram = ParseBool(value, sendToTelegram);
                        break;
                    case "BotToken":
                        botToken = value;
                        break;
                    case "ChatId":
                        chatId = value;
                        break;
                    case "DataFeedReporting":
                        dataFeedReporting = ParseBool(value, dataFeedReporting);
                        break;
                    case "WatchdogDataTimeoutSeconds":
                        watchdogDataTimeoutSeconds = ParseInt(value, watchdogDataTimeoutSeconds);
                        break;
                    case "HeartbeatReporting":
                        heartbeatReporting = ParseBool(value, heartbeatReporting);
                        break;
                    case "HeartbeatFilePath":
                        heartbeatFilePath = value;
                        break;
                    case "HeartbeatTimeoutSeconds":
                        heartbeatTimeoutSeconds = ParseInt(value, heartbeatTimeoutSeconds);
                        break;
                    case "RunHeadlessWhenWindowClosed":
                        runHeadlessWhenWindowClosed = ParseBool(value, runHeadlessWhenWindowClosed);
                        break;
                    case "AutoReconnectEnabled":
                        autoReconnectEnabled = ParseBool(value, autoReconnectEnabled);
                        break;
                    case "ReconnectInitialDelaySeconds":
                        reconnectInitialDelaySeconds = ParseInt(value, reconnectInitialDelaySeconds);
                        break;
                    case "ReconnectMaxDelaySeconds":
                        reconnectMaxDelaySeconds = ParseInt(value, reconnectMaxDelaySeconds);
                        break;
                    case "ReconnectMaxAttempts":
                        reconnectMaxAttempts = ParseInt(value, reconnectMaxAttempts);
                        break;
                    case "MonitoredConnectionName":
                        monitoredConnectionName = value;
                        break;
                    case "MonitoredAccountName":
                        monitoredAccountName = value;
                        break;
                    case "MonitoredInstrumentsCsv":
                        monitoredInstrumentsCsv = value;
                        break;
                }
            }
        }

        private void WriteDefaultConfig()
        {
            string[] lines = new[]
            {
                "# TradeMessengerAddOn settings",
                "Debug=false",
                "SendPushNotifications=true",
                "ShowEntry=false",
                "ShowExit=false",
                "SendToDiscord=false",
                "DiscordWebhookUrl=",
                "SendToTelegram=false",
                "BotToken=",
                "ChatId=",
                "DataFeedReporting=true",
                "WatchdogDataTimeoutSeconds=60",
                "HeartbeatReporting=true",
                "HeartbeatFilePath=" + heartbeatFilePath,
                "HeartbeatTimeoutSeconds=60",
                "RunHeadlessWhenWindowClosed=true",
                "AutoReconnectEnabled=true",
                "ReconnectInitialDelaySeconds=10",
                "ReconnectMaxDelaySeconds=120",
                "ReconnectMaxAttempts=0",
                "MonitoredConnectionName=",
                "MonitoredAccountName=",
                "MonitoredInstrumentsCsv="
            };

            File.WriteAllLines(configFilePath, lines);
        }

        private void PersistSettings()
        {
            TradeMessengerAddOnSettings settings = GetSettingsSnapshot();
            string[] lines = new[]
            {
                "# TradeMessengerAddOn settings",
                "Debug=" + settings.Debug.ToString().ToLowerInvariant(),
                "SendPushNotifications=" + settings.SendPushNotifications.ToString().ToLowerInvariant(),
                "ShowEntry=" + settings.ShowEntry.ToString().ToLowerInvariant(),
                "ShowExit=" + settings.ShowExit.ToString().ToLowerInvariant(),
                "SendToDiscord=" + settings.SendToDiscord.ToString().ToLowerInvariant(),
                "DiscordWebhookUrl=" + settings.DiscordWebhookUrl,
                "SendToTelegram=" + settings.SendToTelegram.ToString().ToLowerInvariant(),
                "BotToken=" + settings.BotToken,
                "ChatId=" + settings.ChatId,
                "DataFeedReporting=" + settings.DataFeedReporting.ToString().ToLowerInvariant(),
                "WatchdogDataTimeoutSeconds=" + settings.WatchdogDataTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
                "HeartbeatReporting=" + settings.HeartbeatReporting.ToString().ToLowerInvariant(),
                "HeartbeatFilePath=" + settings.HeartbeatFilePath,
                "HeartbeatTimeoutSeconds=" + settings.HeartbeatTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
                "RunHeadlessWhenWindowClosed=" + settings.RunHeadlessWhenWindowClosed.ToString().ToLowerInvariant(),
                "AutoReconnectEnabled=" + settings.AutoReconnectEnabled.ToString().ToLowerInvariant(),
                "ReconnectInitialDelaySeconds=" + settings.ReconnectInitialDelaySeconds.ToString(CultureInfo.InvariantCulture),
                "ReconnectMaxDelaySeconds=" + settings.ReconnectMaxDelaySeconds.ToString(CultureInfo.InvariantCulture),
                "ReconnectMaxAttempts=" + settings.ReconnectMaxAttempts.ToString(CultureInfo.InvariantCulture),
                "MonitoredConnectionName=" + settings.MonitoredConnectionName,
                "MonitoredAccountName=" + settings.MonitoredAccountName,
                "MonitoredInstrumentsCsv=" + settings.MonitoredInstrumentsCsv
            };

            File.WriteAllLines(configFilePath, lines);
        }

        private void RefreshMonitoringState()
        {
            if (runHeadlessWhenWindowClosed || settingsWindow != null)
                StartMonitoring();
            else
                StopMonitoring();
        }

        private void StartMonitoring()
        {
            if (monitoringActive)
                return;

            monitoringActive = true;
            SubscribeToAccountExecutions();
            SubscribeToMarketData();
            StartWatchdog();
        }

        private void StopMonitoring()
        {
            if (!monitoringActive)
                return;

            monitoringActive = false;
            StopWatchdog();
            UnsubscribeFromMarketData();
            UnsubscribeFromAccountExecutions();
            StopReconnectLoop();

            lock (trackedPositionsByKey)
                trackedPositionsByKey.Clear();

            strategyHeartbeats.Clear();
            strategyStalled.Clear();
            strategyStartupCount.Clear();
            lastConnectionStatusByName.Clear();
            connectionIssueActive = false;
            reconnectInProgress = false;
            reconnectAttemptCount = 0;
            nextReconnectAttemptUtc = DateTime.MinValue;
            lastReconnectFailure = string.Empty;
        }

        private void StartWatchdog()
        {
            if (watchdogTimer != null)
                return;

            watchdogTimer = new Timer(5000);
            watchdogTimer.Elapsed += WatchdogTimerElapsed;
            watchdogTimer.AutoReset = true;
            watchdogTimer.Start();
        }

        private void StopWatchdog()
        {
            if (watchdogTimer == null)
                return;

            watchdogTimer.Stop();
            watchdogTimer.Elapsed -= WatchdogTimerElapsed;
            watchdogTimer.Dispose();
            watchdogTimer = null;
        }

        private void SubscribeToAccountExecutions()
        {
            lock (Account.All)
            {
                foreach (Account account in Account.All)
                    SubscribeToAccountExecution(account);
            }
        }

        private void SubscribeToConnectionAccounts(Connection connection)
        {
            if (connection == null)
                return;

            lock (connection.Accounts)
            {
                foreach (Account account in connection.Accounts)
                    SubscribeToAccountExecution(account);
            }
        }

        private void SubscribeToAccountExecution(Account account)
        {
            if (account == null || !IsMatchingAccount(account))
                return;

            lock (subscribedAccounts)
            {
                if (!subscribedAccounts.Add(account.Name))
                    return;
            }

            account.ExecutionUpdate -= OnAccountExecutionUpdate;
            account.ExecutionUpdate += OnAccountExecutionUpdate;
        }

        private void UnsubscribeFromAccountExecutions()
        {
            lock (Account.All)
            {
                foreach (Account account in Account.All)
                {
                    if (account == null)
                        continue;

                    account.ExecutionUpdate -= OnAccountExecutionUpdate;
                }
            }

            lock (subscribedAccounts)
                subscribedAccounts.Clear();
        }

        private void OnAccountExecutionUpdate(object sender, ExecutionEventArgs e)
        {
            if (e == null || e.Execution == null || e.Execution.Order == null)
                return;

            if (e.Execution.Order.OrderState != OrderState.Filled)
                return;

            string instrumentName = e.Execution.Instrument != null ? e.Execution.Instrument.FullName : "Unknown";
            Account account = sender as Account;
            string accountName = account != null ? account.Name : "Unknown";
            ExecutionMessageResult result = BuildExecutionMessage(account, e.Execution, accountName, instrumentName);
            if (result == null || string.IsNullOrEmpty(result.Message))
                return;

            if ((result.IsEntryNotification && !showEntry) || (result.IsExitNotification && !showExit))
                return;

            string message = result.Message;

            LogToOutput(message);
            if (sendPushNotifications)
                SendPushNotification(e.Time, message);
        }

        private ExecutionMessageResult BuildExecutionMessage(Account account, Execution execution, string accountName, string instrumentName)
        {
            if (execution == null)
                return new ExecutionMessageResult
                {
                    Message = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} {1} filled @ unknown price",
                        accountName,
                        instrumentName)
                };

            double signedFillQuantity = GetSignedFillQuantity(execution.Order.OrderAction, execution.Quantity);
            if (Math.Abs(signedFillQuantity) < 0.0000001)
                return null;

            string botName = ResolveBotName(execution);
            bool isEntryNotification = false;
            bool isExitNotification = false;
            double realizedPnl = 0;

            lock (trackedPositionsByKey)
            {
                if (string.IsNullOrWhiteSpace(botName))
                    botName = FindTrackedBotName(accountName, instrumentName);

                string key = GetTrackedPositionKey(accountName, instrumentName, botName);
                TrackedPositionState state;
                if (!trackedPositionsByKey.TryGetValue(key, out state))
                {
                    state = new TrackedPositionState();
                    trackedPositionsByKey[key] = state;
                }

                if (Math.Abs(state.SignedQuantity) < 0.0000001)
                {
                    state.SignedQuantity = signedFillQuantity;
                    state.AverageEntryPrice = execution.Price;
                    isEntryNotification = true;
                }
                else if (Math.Sign(state.SignedQuantity) == Math.Sign(signedFillQuantity))
                {
                    double existingAbsoluteQuantity = Math.Abs(state.SignedQuantity);
                    double fillAbsoluteQuantity = Math.Abs(signedFillQuantity);
                    double combinedAbsoluteQuantity = existingAbsoluteQuantity + fillAbsoluteQuantity;
                    state.AverageEntryPrice =
                        ((state.AverageEntryPrice * existingAbsoluteQuantity) + (execution.Price * fillAbsoluteQuantity)) /
                        combinedAbsoluteQuantity;
                    state.SignedQuantity += signedFillQuantity;
                    isEntryNotification = true;
                }
                else
                {
                    double closeQuantity = Math.Min(Math.Abs(state.SignedQuantity), Math.Abs(signedFillQuantity));
                    double points = state.SignedQuantity > 0
                        ? execution.Price - state.AverageEntryPrice
                        : state.AverageEntryPrice - execution.Price;
                    realizedPnl = points * execution.Instrument.MasterInstrument.PointValue * closeQuantity;
                    isExitNotification = true;

                    double newSignedQuantity = state.SignedQuantity + signedFillQuantity;
                    if (Math.Abs(newSignedQuantity) < 0.0000001)
                    {
                        trackedPositionsByKey.Remove(key);
                    }
                    else if (Math.Sign(newSignedQuantity) == Math.Sign(state.SignedQuantity))
                    {
                        state.SignedQuantity = newSignedQuantity;
                    }
                    else
                    {
                        state.SignedQuantity = newSignedQuantity;
                        state.AverageEntryPrice = execution.Price;
                    }
                }
            }

            if (isExitNotification)
                return new ExecutionMessageResult
                {
                    IsExitNotification = true,
                    BotName = botName,
                    Message = FormatExitMessage(account, accountName, instrumentName, realizedPnl, botName)
                };

            if (isEntryNotification)
                return new ExecutionMessageResult
                {
                    IsEntryNotification = true,
                    BotName = botName,
                    Message = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}\n{1}",
                        FormatMessageTitle(botName, instrumentName),
                        signedFillQuantity > 0 ? "Long" : "Short")
                };

            return new ExecutionMessageResult
            {
                BotName = botName,
                Message = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}\n{1} filled {2} {3} @ {4}",
                    FormatMessageTitle(botName, instrumentName),
                    accountName,
                    execution.Quantity,
                    execution.Order.OrderAction,
                    execution.Price)
            };
        }

        private string FormatExitMessage(Account account, string accountName, string instrumentName, double realizedPnl, string botName)
        {
            CultureInfo usCulture = new CultureInfo("en-US");
            string pnlText = realizedPnl.ToString("C", usCulture);
            string emoji = realizedPnl >= 0 ? "💰" : "🔻";
            string cashText = string.Empty;

            if (account != null)
            {
                double cashValue = account.Get(AccountItem.CashValue, Currency.UsDollar) + realizedPnl;
                cashText = cashValue.ToString("C0", usCulture);
            }

            if (string.IsNullOrEmpty(cashText))
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}\n{1} {2}",
                    FormatMessageTitle(botName, instrumentName),
                    emoji,
                    pnlText);

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}\n{1} {2}\n🏦 {3} | {4}",
                FormatMessageTitle(botName, instrumentName),
                emoji,
                pnlText,
                cashText,
                accountName);
        }

        private static string GetTrackedPositionKey(string accountName, string instrumentName, string botName)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}|{1}|{2}",
                accountName ?? string.Empty,
                instrumentName ?? string.Empty,
                botName ?? string.Empty);
        }

        private static string ResolveBotName(Execution execution)
        {
            if (execution == null || execution.Order == null)
                return string.Empty;

            string orderName = execution.Order.Name ?? string.Empty;
            string fromEntrySignal = execution.Order.FromEntrySignal ?? string.Empty;

            string botName = ExtractBotName(fromEntrySignal);
            if (!string.IsNullOrWhiteSpace(botName))
                return botName;

            return ExtractBotName(orderName);
        }

        private string FindTrackedBotName(string accountName, string instrumentName)
        {
            string prefix = string.Format(
                CultureInfo.InvariantCulture,
                "{0}|{1}|",
                accountName ?? string.Empty,
                instrumentName ?? string.Empty);

            string matchedBotName = string.Empty;
            foreach (KeyValuePair<string, TrackedPositionState> item in trackedPositionsByKey)
            {
                if (!item.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                string candidate = item.Key.Substring(prefix.Length);
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                if (!string.IsNullOrWhiteSpace(matchedBotName) && !string.Equals(matchedBotName, candidate, StringComparison.OrdinalIgnoreCase))
                    return string.Empty;

                matchedBotName = candidate;
            }

            return matchedBotName;
        }

        private static string FormatMessageTitle(string botName, string instrumentName)
        {
            if (string.IsNullOrWhiteSpace(botName))
                return instrumentName ?? string.Empty;

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} | {1}",
                botName,
                instrumentName ?? string.Empty);
        }

        private static string ExtractBotName(string signalName)
        {
            if (string.IsNullOrWhiteSpace(signalName))
                return string.Empty;

            string normalized = signalName.Trim();
            int cutoffIndex = normalized.Length;
            bool foundBoundary = false;

            foundBoundary |= UpdateCutoffIndex(normalized, "Long", ref cutoffIndex);
            foundBoundary |= UpdateCutoffIndex(normalized, "Short", ref cutoffIndex);
            foundBoundary |= UpdateCutoffIndex(normalized, "Exit", ref cutoffIndex);
            foundBoundary |= UpdateCutoffIndex(normalized, "TP", ref cutoffIndex);
            foundBoundary |= UpdateCutoffIndex(normalized, "SL", ref cutoffIndex);

            if (!foundBoundary)
                return string.Empty;

            string candidate = cutoffIndex > 0
                ? normalized.Substring(0, cutoffIndex)
                : normalized;

            candidate = candidate.TrimEnd('_', '-', ' ');

            if (string.IsNullOrWhiteSpace(candidate))
                candidate = normalized;

            int trailingSeparator = candidate.IndexOfAny(new[] { '_', '-' });
            if (trailingSeparator > 0)
                candidate = candidate.Substring(0, trailingSeparator);

            candidate = candidate.Trim();
            if (candidate.Length == 0)
                return string.Empty;

            int trailingDigitsStart = candidate.Length;
            while (trailingDigitsStart > 0 && char.IsDigit(candidate[trailingDigitsStart - 1]))
                trailingDigitsStart--;

            if (trailingDigitsStart > 0 && trailingDigitsStart < candidate.Length)
                candidate = candidate.Substring(0, trailingDigitsStart);

            return candidate;
        }

        private static bool UpdateCutoffIndex(string value, string token, ref int cutoffIndex)
        {
            int tokenIndex = value.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (tokenIndex > 0 && tokenIndex < cutoffIndex)
            {
                cutoffIndex = tokenIndex;
                return true;
            }

            return false;
        }

        private static double GetSignedFillQuantity(OrderAction orderAction, double quantity)
        {
            switch (orderAction)
            {
                case OrderAction.Buy:
                case OrderAction.BuyToCover:
                    return quantity;
                case OrderAction.Sell:
                case OrderAction.SellShort:
                    return -quantity;
                default:
                    return 0;
            }
        }

        private void SubscribeToMarketData()
        {
            foreach (string instrumentName in ParseCsv(monitoredInstrumentsCsv))
            {
                try
                {
                    Instrument instrument = Instrument.GetInstrument(instrumentName, true);
                    if (instrument == null || instrument.MarketData == null)
                    {
                        LogToOutput($"⚠️ Unable to resolve market data instrument '{instrumentName}'.");
                        continue;
                    }

                    EventInfo updateEvent = instrument.MarketData.GetType().GetEvent("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (updateEvent == null)
                    {
                        LogToOutput($"⚠️ Market data update event not found for '{instrumentName}'.");
                        continue;
                    }

                    Delegate handler = CreateMarketDataHandler(updateEvent.EventHandlerType);
                    if (handler == null)
                    {
                        LogToOutput($"⚠️ Unable to bind market data handler for '{instrumentName}'.");
                        continue;
                    }

                    updateEvent.AddEventHandler(instrument.MarketData, handler);
                    marketDataSubscriptions.Add(new MarketDataSubscription
                    {
                        Instrument = instrument,
                        MarketData = instrument.MarketData,
                        UpdateEvent = updateEvent,
                        Handler = handler
                    });

                    lastTickTimeByInstrument[instrument.FullName] = DateTime.UtcNow;
                    feedStalledByInstrument[instrument.FullName] = false;
                    LogToOutput($"📡 Monitoring market data for {instrument.FullName}.");
                }
                catch (Exception ex)
                {
                    LogToOutput($"⚠️ Failed to subscribe market data for '{instrumentName}': {ex.Message}");
                }
            }
        }

        private void UnsubscribeFromMarketData()
        {
            foreach (MarketDataSubscription subscription in marketDataSubscriptions)
            {
                try
                {
                    subscription.UpdateEvent.RemoveEventHandler(subscription.MarketData, subscription.Handler);
                }
                catch
                {
                }
            }

            marketDataSubscriptions.Clear();
            lastTickTimeByInstrument.Clear();
            feedStalledByInstrument.Clear();
        }

        private Delegate CreateMarketDataHandler(Type eventHandlerType)
        {
            MethodInfo twoArgHandler = GetType().GetMethod("OnInstrumentMarketData", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(object), typeof(MarketDataEventArgs) }, null);
            if (twoArgHandler != null)
            {
                Delegate handler = Delegate.CreateDelegate(eventHandlerType, this, twoArgHandler, false);
                if (handler != null)
                    return handler;
            }

            MethodInfo oneArgHandler = GetType().GetMethod("OnInstrumentMarketData", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(MarketDataEventArgs) }, null);
            if (oneArgHandler != null)
                return Delegate.CreateDelegate(eventHandlerType, this, oneArgHandler, false);

            return null;
        }

        private void OnInstrumentMarketData(object sender, MarketDataEventArgs e)
        {
            HandleMarketDataUpdate(e);
        }

        private void OnInstrumentMarketData(MarketDataEventArgs e)
        {
            HandleMarketDataUpdate(e);
        }

        private void HandleMarketDataUpdate(MarketDataEventArgs e)
        {
            if (e == null || e.MarketDataType != MarketDataType.Last || e.Instrument == null)
                return;

            string instrumentName = e.Instrument.FullName;
            lastTickTimeByInstrument[instrumentName] = DateTime.UtcNow;

            if (feedStalledByInstrument.ContainsKey(instrumentName) && feedStalledByInstrument[instrumentName])
            {
                feedStalledByInstrument[instrumentName] = false;
                LogToOutput($"✅ Data feed resumed on {instrumentName}.");
                if (sendPushNotifications && dataFeedReporting)
                    SendPushNotification(DateTime.UtcNow, $"✅ Data feed resumed on {instrumentName}.");
                if (!connectionIssueActive && !AnyFeedCurrentlyStalled())
                    StopReconnectLoop();
            }
        }

        private void WatchdogTimerElapsed(object sender, ElapsedEventArgs e)
        {
            CheckConnectionStatuses(DateTime.UtcNow);

            if (dataFeedReporting)
                CheckFeedStalls(DateTime.UtcNow);

            if (heartbeatReporting)
                CheckHeartbeatFileMulti();

            TryReconnectIfDue(DateTime.UtcNow);
        }

        private void CheckConnectionStatuses(DateTime nowUtc)
        {
            bool anyConnectionIssue = false;

            lock (Connection.Connections)
            {
                foreach (Connection connection in Connection.Connections)
                {
                    if (connection == null || !IsMatchingConnection(connection))
                        continue;

                    SubscribeToConnectionAccounts(connection);

                    string connectionName = GetConnectionName(connection);
                    ConnectionStatus currentStatus = connection.Status;
                    ConnectionStatus previousStatus = lastConnectionStatusByName.ContainsKey(connectionName)
                        ? lastConnectionStatusByName[connectionName]
                        : currentStatus;

                    bool isDisconnected = currentStatus == ConnectionStatus.Disconnected || currentStatus == ConnectionStatus.ConnectionLost;
                    bool wasDisconnected = previousStatus == ConnectionStatus.Disconnected || previousStatus == ConnectionStatus.ConnectionLost;

                    if (isDisconnected)
                    {
                        anyConnectionIssue = true;

                        if (!wasDisconnected)
                        {
                            string message = string.Format(
                                CultureInfo.InvariantCulture,
                                "⚠️ Connection issue on {0}: status={1}, previous={2}.",
                                connectionName,
                                currentStatus,
                                previousStatus);
                            LogToOutput(message);
                            if (sendPushNotifications)
                                SendPushNotification(nowUtc, message);
                        }

                        StartReconnectLoop(nowUtc);
                    }
                    else if (currentStatus == ConnectionStatus.Connected && wasDisconnected)
                    {
                        LogToOutput($"✅ Connection restored on {connectionName}.");
                        if (sendPushNotifications)
                            SendPushNotification(nowUtc, $"✅ Connection restored on {connectionName}.");
                    }

                    lastConnectionStatusByName[connectionName] = currentStatus;
                }
            }

            connectionIssueActive = anyConnectionIssue;
            if (!connectionIssueActive && !AnyFeedCurrentlyStalled())
                StopReconnectLoop();
        }

        private void CheckFeedStalls(DateTime nowUtc)
        {
            foreach (string instrumentName in lastTickTimeByInstrument.Keys.ToList())
            {
                double secondsSinceLastTick = (nowUtc - lastTickTimeByInstrument[instrumentName]).TotalSeconds;
                if (secondsSinceLastTick < watchdogDataTimeoutSeconds)
                    continue;

                bool wasStalled = feedStalledByInstrument.ContainsKey(instrumentName) && feedStalledByInstrument[instrumentName];
                if (wasStalled)
                    continue;

                feedStalledByInstrument[instrumentName] = true;
                string message = $"⚠️ Data feed stalled on {instrumentName}: no ticks for {secondsSinceLastTick:F0} seconds.";
                LogToOutput(message);
                if (sendPushNotifications)
                    SendPushNotification(nowUtc, message);
                StartReconnectLoop(nowUtc);
            }
        }

        private bool AnyFeedCurrentlyStalled()
        {
            return feedStalledByInstrument.Values.Any(stalled => stalled);
        }

        private void StartReconnectLoop(DateTime nowUtc)
        {
            if (!autoReconnectEnabled)
                return;

            lock (reconnectLock)
            {
                if (reconnectLoopActive)
                    return;

                reconnectLoopActive = true;
                reconnectInProgress = false;
                reconnectAttemptCount = 0;
                lastReconnectFailure = string.Empty;
                int initialDelaySeconds = Math.Max(1, reconnectInitialDelaySeconds);
                nextReconnectAttemptUtc = nowUtc.AddSeconds(initialDelaySeconds);
                LogToOutput($"🔁 Auto-reconnect armed. First attempt in {initialDelaySeconds}s.");
            }
        }

        private void StopReconnectLoop()
        {
            lock (reconnectLock)
            {
                reconnectLoopActive = false;
                reconnectInProgress = false;
                reconnectAttemptCount = 0;
                nextReconnectAttemptUtc = DateTime.MinValue;
                lastReconnectFailure = string.Empty;
            }
        }

        private void TryReconnectIfDue(DateTime nowUtc)
        {
            if (!autoReconnectEnabled)
                return;

            lock (reconnectLock)
            {
                if (!reconnectLoopActive || reconnectInProgress || nowUtc < nextReconnectAttemptUtc)
                    return;

                if (reconnectMaxAttempts > 0 && reconnectAttemptCount >= reconnectMaxAttempts)
                {
                    LogToOutput($"🛑 Auto-reconnect stopped: reached max attempts ({reconnectMaxAttempts}).");
                    reconnectLoopActive = false;
                    return;
                }

                reconnectInProgress = true;
                reconnectAttemptCount++;
            }

            bool issued = AttemptReconnectOnce(out string details);

            lock (reconnectLock)
            {
                reconnectInProgress = false;
                int delaySeconds = ComputeReconnectDelaySeconds(reconnectAttemptCount);
                nextReconnectAttemptUtc = nowUtc.AddSeconds(delaySeconds);

                if (issued)
                {
                    LogToOutput($"🔁 Reconnect attempt #{reconnectAttemptCount} issued. Next retry in {delaySeconds}s if needed.");
                }
                else if (!string.Equals(lastReconnectFailure, details, StringComparison.Ordinal))
                {
                    LogToOutput($"⚠️ Reconnect attempt #{reconnectAttemptCount} failed: {details}");
                    lastReconnectFailure = details;
                }
            }
        }

        private int ComputeReconnectDelaySeconds(int attemptNumber)
        {
            int initialDelay = Math.Max(1, reconnectInitialDelaySeconds);
            int maxDelay = Math.Max(initialDelay, reconnectMaxDelaySeconds);
            if (attemptNumber <= 1)
                return initialDelay;

            double scaled = initialDelay * Math.Pow(2.0, attemptNumber - 1);
            if (scaled > int.MaxValue)
                scaled = int.MaxValue;

            return Math.Min(maxDelay, (int)scaled);
        }

        private bool AttemptReconnectOnce(out string details)
        {
            details = string.Empty;

            List<Connection> connections = GetTargetConnections();
            if (connections.Count == 0)
            {
                details = "No matching connections found.";
                return false;
            }

            bool anyIssued = false;
            List<string> connectionResults = new List<string>();

            foreach (Connection connection in connections)
            {
                try
                {
                    bool connectIssued;
                    bool disconnectIssued = false;

                    if (connection.Status == ConnectionStatus.Connected && AnyFeedCurrentlyStalled())
                    {
                        disconnectIssued = TryInvokeParameterless(connection, "Disconnect");
                        System.Threading.Thread.Sleep(300);
                    }

                    connectIssued = TryInvokeParameterless(connection, "Connect");
                    anyIssued |= disconnectIssued || connectIssued;
                    connectionResults.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}(Disconnect={1}, Connect={2})",
                        GetConnectionName(connection),
                        disconnectIssued ? "yes" : "no",
                        connectIssued ? "yes" : "no"));
                }
                catch (Exception ex)
                {
                    connectionResults.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}(error={1})",
                        GetConnectionName(connection),
                        ex.Message));
                }
            }

            details = string.Join("; ", connectionResults);
            return anyIssued;
        }

        private List<Connection> GetTargetConnections()
        {
            List<Connection> connections = new List<Connection>();
            lock (Connection.Connections)
            {
                foreach (Connection connection in Connection.Connections)
                {
                    if (connection != null && IsMatchingConnection(connection))
                        connections.Add(connection);
                }
            }

            return connections;
        }

        private bool IsMatchingConnection(Connection connection)
        {
            if (connection == null)
                return false;

            if (string.IsNullOrWhiteSpace(monitoredConnectionName))
                return true;

            return string.Equals(GetConnectionName(connection), monitoredConnectionName, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsMatchingAccount(Account account)
        {
            if (account == null)
                return false;

            if (string.IsNullOrWhiteSpace(monitoredAccountName))
                return true;

            foreach (string accountFilter in ParseCsv(monitoredAccountName))
            {
                if (string.Equals(account.Name, accountFilter, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(account.DisplayName, accountFilter, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private string GetConnectionName(Connection connection)
        {
            if (connection == null)
                return "Unknown";

            return connection.Options != null && !string.IsNullOrWhiteSpace(connection.Options.Name)
                ? connection.Options.Name
                : connection.ToString();
        }

        private bool TryInvokeParameterless(object target, string methodName)
        {
            if (target == null || string.IsNullOrEmpty(methodName))
                return false;

            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);

            if (method == null)
                return false;

            method.Invoke(target, null);
            return true;
        }

        private void CheckHeartbeatFileMulti()
        {
            if (string.IsNullOrWhiteSpace(heartbeatFilePath))
                return;

            if (!File.Exists(heartbeatFilePath))
            {
                LogToOutput($"⚠️ Heartbeat file not found: {heartbeatFilePath}");
                return;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(heartbeatFilePath);
            }
            catch (Exception ex)
            {
                LogToOutput($"Heartbeat watchdog error: {ex.Message}");
                return;
            }

            if (lines.Length == 0)
                return;

            DateTime now = DateTime.Now;

            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                string[] parts = line.Split(',');
                if (parts.Length < 2)
                    continue;

                string strategyName = parts[0].Trim();
                if (!DateTime.TryParse(parts[1], out DateTime heartbeatTime))
                    continue;

                double elapsed = (now - heartbeatTime).TotalSeconds;
                if (elapsed < 0)
                    elapsed = 0;

                strategyHeartbeats[strategyName] = heartbeatTime;

                if (!strategyStalled.ContainsKey(strategyName))
                    strategyStalled[strategyName] = false;
                if (!strategyStartupCount.ContainsKey(strategyName))
                    strategyStartupCount[strategyName] = 0;

                if (elapsed > 300 && strategyStartupCount[strategyName] == 0)
                    continue;

                if (elapsed < heartbeatTimeoutSeconds)
                {
                    if (strategyStartupCount[strategyName] < 3)
                        strategyStartupCount[strategyName]++;

                    if (strategyStartupCount[strategyName] < 3)
                        continue;
                }

                if (!strategyStalled[strategyName] && elapsed >= heartbeatTimeoutSeconds)
                {
                    strategyStalled[strategyName] = true;
                    strategyStartupCount[strategyName] = 0;
                    string message = $"⚠️ Strategy '{strategyName}' has not updated for {elapsed:F0}s. It may have stopped or disabled itself.";
                    LogToOutput(message);
                    if (sendPushNotifications)
                        SendPushNotification(DateTime.Now, message);
                }
                else if (strategyStalled[strategyName] && elapsed < heartbeatTimeoutSeconds)
                {
                    if (strategyStartupCount[strategyName] < 3)
                    {
                        strategyStartupCount[strategyName]++;
                        continue;
                    }

                    strategyStalled[strategyName] = false;
                    string message = $"✅ Strategy '{strategyName}' resumed updates ({elapsed:F0}s since last heartbeat).";
                    LogToOutput(message);
                    if (sendPushNotifications)
                        SendPushNotification(DateTime.Now, message);
                }
            }
        }

        private void SendPushNotification(DateTime timestamp, string message)
        {
            if (sendToTelegram)
                SendTelegramNotification(timestamp, message);
            if (sendToDiscord)
                SendDiscordNotification(timestamp, message);
        }

        private void SendTelegramNotification(DateTime timestamp, string message)
        {
            if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId))
                return;

            string url = $"https://api.telegram.org/bot{botToken}/sendMessage";
            string postData = $"chat_id={chatId}&text={Uri.EscapeDataString(message)}";

            try
            {
                using (WebClient client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.ContentType] = "application/x-www-form-urlencoded";
                    client.UploadString(url, postData);
                }
            }
            catch (WebException ex)
            {
                LogToOutput($"{timestamp} - Failed to send Telegram notification: {ex.Message}");
            }
        }

        private void SendDiscordNotification(DateTime timestamp, string message)
        {
            if (string.IsNullOrWhiteSpace(discordWebhookUrl))
                return;

            try
            {
                Dictionary<string, string> payload = new Dictionary<string, string> { { "content", message } };
                string jsonPayload = new JavaScriptSerializer().Serialize(payload);

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(discordWebhookUrl);
                request.Method = "POST";
                request.ContentType = "application/json; charset=utf-8";

                byte[] body = Encoding.UTF8.GetBytes(jsonPayload);
                request.ContentLength = body.Length;

                using (Stream requestStream = request.GetRequestStream())
                    requestStream.Write(body, 0, body.Length);

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                }
            }
            catch (WebException ex)
            {
                LogToOutput($"{timestamp} - Discord send error: {ex.Message}");
            }
        }

        private void LogToOutput(string message)
        {
            if (debug)
                NinjaTrader.Code.Output.Process(message, PrintTo.OutputTab2);
        }

        private static bool ParseBool(string value, bool fallback)
        {
            return bool.TryParse(value, out bool parsed) ? parsed : fallback;
        }

        private static int ParseInt(string value, int fallback)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;
        }

        private static IEnumerable<string> ParseCsv(string csv)
        {
            return (csv ?? string.Empty)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => part.Length > 0);
        }

        private void EnsureMenuItem(ControlCenter controlCenter)
        {
            if (launcherMenuItem != null)
                return;

            if (newMenuItem == null)
                newMenuItem = FindTopLevelMenuItem(controlCenter, "newMenuItem", "New");
            if (newMenuItem == null)
                return;

            if (autoEdgeMenuItem == null)
            {
                autoEdgeMenuItem = FindChildMenuItem(newMenuItem, "AutoEdge");
                if (autoEdgeMenuItem == null)
                {
                    autoEdgeMenuItem = new NTMenuItem
                    {
                        Header = "AutoEdge",
                        Style = Application.Current.TryFindResource("MainMenuItem") as Style
                    };
                    newMenuItem.Items.Add(autoEdgeMenuItem);
                }
            }

            launcherMenuItem = new NTMenuItem
            {
                Header = "Trade Messenger",
                Style = Application.Current.TryFindResource("MainMenuItem") as Style
            };
            launcherMenuItem.Click += OnLauncherMenuItemClick;
            autoEdgeMenuItem.Items.Add(launcherMenuItem);
        }

        private NTMenuItem FindTopLevelMenuItem(DependencyObject parent, string expectedName, string expectedHeader)
        {
            if (parent == null)
                return null;

            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                NTMenuItem menuItem = child as NTMenuItem;
                if (menuItem != null)
                {
                    string headerText = GetHeaderText(menuItem.Header);
                    if (string.Equals(menuItem.Name, expectedName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(headerText, expectedHeader, StringComparison.OrdinalIgnoreCase))
                        return menuItem;
                }

                NTMenuItem nestedMatch = FindTopLevelMenuItem(child, expectedName, expectedHeader);
                if (nestedMatch != null)
                    return nestedMatch;
            }

            return null;
        }

        private NTMenuItem FindChildMenuItem(ItemsControl parent, string expectedHeader)
        {
            if (parent == null)
                return null;

            foreach (object item in parent.Items)
            {
                NTMenuItem menuItem = item as NTMenuItem;
                if (menuItem == null)
                    continue;

                if (string.Equals(GetHeaderText(menuItem.Header), expectedHeader, StringComparison.OrdinalIgnoreCase))
                    return menuItem;
            }

            return null;
        }

        private string GetHeaderText(object header)
        {
            if (header == null)
                return string.Empty;

            TextBlock textBlock = header as TextBlock;
            if (textBlock != null)
                return textBlock.Text ?? string.Empty;

            return header.ToString() ?? string.Empty;
        }

        private void OnLauncherMenuItemClick(object sender, RoutedEventArgs e)
        {
            if (settingsWindow == null)
            {
                settingsWindow = new TradeMessengerAddOnWindow();
                settingsWindow.Closed += OnSettingsWindowClosed;
            }

            RefreshMonitoringState();
            settingsWindow.LoadFromSettings(GetSettingsSnapshot(), GetStatusSnapshot());
            settingsWindow.Show();
            settingsWindow.Activate();
        }

        private void OnSettingsWindowClosed(object sender, EventArgs e)
        {
            if (settingsWindow != null)
                settingsWindow.Closed -= OnSettingsWindowClosed;
            settingsWindow = null;
            RefreshMonitoringState();
        }

        private void CloseSettingsWindow()
        {
            if (settingsWindow != null)
            {
                settingsWindow.Close();
                settingsWindow = null;
            }
        }

        private void RemoveMenuItem()
        {
            if (launcherMenuItem != null)
            {
                launcherMenuItem.Click -= OnLauncherMenuItemClick;
                if (autoEdgeMenuItem != null && autoEdgeMenuItem.Items.Contains(launcherMenuItem))
                    autoEdgeMenuItem.Items.Remove(launcherMenuItem);
                launcherMenuItem = null;
            }

            if (autoEdgeMenuItem != null && autoEdgeMenuItem.Items.Count == 0)
            {
                if (newMenuItem != null && newMenuItem.Items.Contains(autoEdgeMenuItem))
                    newMenuItem.Items.Remove(autoEdgeMenuItem);
                autoEdgeMenuItem = null;
            }
        }
    }

    public sealed class TradeMessengerAddOnSettings
    {
        public bool Debug { get; set; }
        public bool SendPushNotifications { get; set; }
        public bool ShowEntry { get; set; }
        public bool ShowExit { get; set; }
        public bool SendToDiscord { get; set; }
        public string DiscordWebhookUrl { get; set; }
        public bool SendToTelegram { get; set; }
        public string BotToken { get; set; }
        public string ChatId { get; set; }
        public bool DataFeedReporting { get; set; }
        public int WatchdogDataTimeoutSeconds { get; set; }
        public bool HeartbeatReporting { get; set; }
        public string HeartbeatFilePath { get; set; }
        public int HeartbeatTimeoutSeconds { get; set; }
        public bool RunHeadlessWhenWindowClosed { get; set; }
        public bool AutoReconnectEnabled { get; set; }
        public int ReconnectInitialDelaySeconds { get; set; }
        public int ReconnectMaxDelaySeconds { get; set; }
        public int ReconnectMaxAttempts { get; set; }
        public string MonitoredConnectionName { get; set; }
        public string MonitoredAccountName { get; set; }
        public string MonitoredInstrumentsCsv { get; set; }
    }

    public class TradeMessengerAddOnWindow : NTWindow
    {
        private static readonly Brush TitleTextBrush = new SolidColorBrush(Color.FromRgb(0xC4, 0xC4, 0xC4));
        private static readonly Brush DescriptionTextBrush = new SolidColorBrush(Color.FromRgb(0x98, 0x98, 0x98));

        private CheckBox debugCheckBox;
        private CheckBox pushNotificationsCheckBox;
        private CheckBox showEntryCheckBox;
        private CheckBox showExitCheckBox;
        private CheckBox discordCheckBox;
        private TextBox discordWebhookTextBox;
        private CheckBox telegramCheckBox;
        private TextBox botTokenTextBox;
        private TextBox chatIdTextBox;
        private CheckBox dataFeedReportingCheckBox;
        private TextBox watchdogTimeoutTextBox;
        private CheckBox heartbeatReportingCheckBox;
        private TextBox heartbeatFilePathTextBox;
        private TextBox heartbeatTimeoutTextBox;
        private CheckBox runHeadlessCheckBox;
        private CheckBox autoReconnectCheckBox;
        private TextBox reconnectInitialDelayTextBox;
        private TextBox reconnectMaxDelayTextBox;
        private TextBox reconnectMaxAttemptsTextBox;
        private TextBox monitoredConnectionTextBox;
        private TextBox monitoredAccountTextBox;
        private ComboBox accountPickerComboBox;
        private TextBox monitoredInstrumentsTextBox;
        private TextBlock statusTextBlock;

        public TradeMessengerAddOnWindow()
        {
            Caption = "Trade Messenger";
            Width = 760;
            Height = 820;
            Content = BuildContent();
        }

        public void LoadFromSettings(TradeMessengerAddOnSettings settings, string statusText)
        {
            if (settings == null)
                return;

            debugCheckBox.IsChecked = settings.Debug;
            pushNotificationsCheckBox.IsChecked = settings.SendPushNotifications;
            showEntryCheckBox.IsChecked = settings.ShowEntry;
            showExitCheckBox.IsChecked = settings.ShowExit;
            discordCheckBox.IsChecked = settings.SendToDiscord;
            discordWebhookTextBox.Text = settings.DiscordWebhookUrl ?? string.Empty;
            telegramCheckBox.IsChecked = settings.SendToTelegram;
            botTokenTextBox.Text = settings.BotToken ?? string.Empty;
            chatIdTextBox.Text = settings.ChatId ?? string.Empty;
            dataFeedReportingCheckBox.IsChecked = settings.DataFeedReporting;
            watchdogTimeoutTextBox.Text = settings.WatchdogDataTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
            heartbeatReportingCheckBox.IsChecked = settings.HeartbeatReporting;
            heartbeatFilePathTextBox.Text = settings.HeartbeatFilePath ?? string.Empty;
            heartbeatTimeoutTextBox.Text = settings.HeartbeatTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
            runHeadlessCheckBox.IsChecked = settings.RunHeadlessWhenWindowClosed;
            autoReconnectCheckBox.IsChecked = settings.AutoReconnectEnabled;
            reconnectInitialDelayTextBox.Text = settings.ReconnectInitialDelaySeconds.ToString(CultureInfo.InvariantCulture);
            reconnectMaxDelayTextBox.Text = settings.ReconnectMaxDelaySeconds.ToString(CultureInfo.InvariantCulture);
            reconnectMaxAttemptsTextBox.Text = settings.ReconnectMaxAttempts.ToString(CultureInfo.InvariantCulture);
            monitoredConnectionTextBox.Text = settings.MonitoredConnectionName ?? string.Empty;
            monitoredAccountTextBox.Text = settings.MonitoredAccountName ?? string.Empty;
            monitoredInstrumentsTextBox.Text = settings.MonitoredInstrumentsCsv ?? string.Empty;
            statusTextBlock.Text = statusText ?? string.Empty;
            RefreshAccountPickerOptions();
        }

        private UIElement BuildContent()
        {
            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Border header = new Border
            {
                Padding = new Thickness(16, 14, 16, 12),
                Child = new TextBlock
                {
                    Text = "Configure reconnect, reporting, account filtering, and monitored instruments.",
                    Foreground = TitleTextBrush,
                    TextWrapping = TextWrapping.Wrap
                }
            };
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            ScrollViewer scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(16, 0, 16, 0),
                Content = BuildSettingsPanel()
            };
            Grid.SetRow(scrollViewer, 1);
            root.Children.Add(scrollViewer);

            StackPanel footer = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(16, 12, 16, 16)
            };
            statusTextBlock = new TextBlock
            {
                Foreground = TitleTextBrush,
                Margin = new Thickness(0, 0, 0, 12),
                TextWrapping = TextWrapping.Wrap
            };
            footer.Children.Add(statusTextBlock);

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            Button refreshButton = new Button { Content = "Refresh", MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
            refreshButton.Click += OnRefreshClick;
            buttons.Children.Add(refreshButton);

            Button saveButton = new Button { Content = "Save", MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
            saveButton.Click += OnSaveClick;
            buttons.Children.Add(saveButton);

            Button closeButton = new Button { Content = "Close", MinWidth = 90 };
            closeButton.Click += (_, __) => Close();
            buttons.Children.Add(closeButton);

            footer.Children.Add(buttons);
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            return root;
        }

        private UIElement BuildSettingsPanel()
        {
            StackPanel stack = new StackPanel();
            stack.Children.Add(BuildSection(
                "Notifications",
                out pushNotificationsCheckBox, "Send Push Notifications", "Master switch for sending alerts to NinjaTrader, Telegram, and Discord."));
            stack.Children.Add(BuildHiddenElement(BuildCheckBoxWithDescription(
                out debugCheckBox,
                "Debug Output",
                "Writes extra diagnostic messages to the NinjaScript Output window.")));
            stack.Children.Add(BuildNotificationChannelSection(
                "Delivery Channels",
                out discordCheckBox, "Send To Discord", "Enable Discord delivery. Requires a valid webhook URL below.",
                out discordWebhookTextBox, "Discord Webhook URL", "Paste the full Discord incoming webhook URL. Leave blank if Discord is disabled.",
                out telegramCheckBox, "Send To Telegram", "Enable Telegram delivery. Requires both bot token and chat ID below.",
                out botTokenTextBox, "Telegram Bot Token", "BotFather token for your Telegram bot, for example `123456:ABC...`.",
                out chatIdTextBox, "Telegram Chat ID", "Numeric chat or channel ID that should receive messages."));

            stack.Children.Add(BuildSection(
                "Trade Notifications",
                out showEntryCheckBox, "Send Entry Messages", "Send alerts when a filled order opens a position.",
                out showExitCheckBox, "Send Exit Messages", "Send alerts when a filled order closes or reduces a position."));

            stack.Children.Add(BuildMonitoringSection());
            stack.Children.Add(BuildHiddenElement(BuildSection(
                "Recovery",
                out autoReconnectCheckBox, "Auto Reconnect", "Automatically retry the selected connection when a disconnect or feed stall is detected.")));
            stack.Children.Add(BuildHiddenElement(BuildLabeledTextBox(
                "Reconnect Initial Delay Seconds",
                "Delay before the first reconnect attempt after a problem is detected. Minimum 1.",
                out reconnectInitialDelayTextBox)));
            stack.Children.Add(BuildHiddenElement(BuildLabeledTextBox(
                "Reconnect Max Delay Seconds",
                "Maximum backoff delay between reconnect attempts. Must be at least the initial delay.",
                out reconnectMaxDelayTextBox)));
            stack.Children.Add(BuildHiddenElement(BuildLabeledTextBox(
                "Reconnect Max Attempts (0 = unlimited)",
                "Maximum reconnect tries before stopping. Use 0 to keep retrying indefinitely.",
                out reconnectMaxAttemptsTextBox)));

            stack.Children.Add(BuildSectionHeader("Filters"));
            stack.Children.Add(BuildLabeledTextBox("Monitored Connection Name", "Exact NinjaTrader connection name to monitor, for example `Rithmic`. Leave blank to include all connections. This is optional if account filtering alone is enough.", out monitoredConnectionTextBox));
            stack.Children.Add(BuildLabeledTextBox("Monitored Account Names", "Comma-separated account names or display names to monitor, for example `Playback101,Apex-01,Lucid-02`. Leave blank to include all accounts.", out monitoredAccountTextBox));
            stack.Children.Add(BuildAccountPickerRow());

            return stack;
        }

        private Border BuildMonitoringSection()
        {
            StackPanel panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(BuildSectionHeader("Monitoring"));

            panel.Children.Add(BuildCheckBoxWithDescription(
                out dataFeedReportingCheckBox,
                "Data Feed Reporting",
                "Watch the instruments below and report when market data stops updating."));
            panel.Children.Add(BuildHiddenElement(BuildIndentedContent(BuildLabeledTextBox(
                "Watchdog Timeout Seconds",
                "How long an instrument can go without a `Last` tick before it is treated as stalled. Minimum 1.",
                out watchdogTimeoutTextBox))));
            panel.Children.Add(BuildIndentedContent(BuildLabeledTextBox(
                "Monitored Instruments CSV",
                "Comma-separated instrument names to watch for feed stalls, for example `ES 06-26,NQ 06-26`.",
                out monitoredInstrumentsTextBox)));

            panel.Children.Add(BuildCheckBoxWithDescription(
                out heartbeatReportingCheckBox,
                "Heartbeat Reporting",
                "Monitor the heartbeat CSV written by your strategies to detect stalled scripts."));
            panel.Children.Add(BuildHiddenElement(BuildIndentedContent(BuildLabeledTextBox(
                "Heartbeat File Path",
                "Full path to the heartbeat CSV file produced by your strategies.",
                out heartbeatFilePathTextBox))));
            panel.Children.Add(BuildHiddenElement(BuildIndentedContent(BuildLabeledTextBox(
                "Heartbeat Timeout Seconds",
                "How old a strategy heartbeat can be before the strategy is marked stalled. Minimum 1.",
                out heartbeatTimeoutTextBox))));

            panel.Children.Add(BuildCheckBoxWithDescription(
                out runHeadlessCheckBox,
                "Run Headless When Window Closed",
                "Keep monitoring and reporting active after this window is closed."));

            return new Border { Child = panel };
        }

        private Border BuildNotificationChannelSection(string title, out CheckBox discordToggle, string discordLabel, string discordDescription, out TextBox discordTextBox, string discordTextLabel, string discordTextDescription, out CheckBox telegramToggle, string telegramLabel, string telegramDescription, out TextBox telegramTokenTextBox, string telegramTokenLabel, string telegramTokenDescription, out TextBox telegramChatTextBox, string telegramChatLabel, string telegramChatDescription)
        {
            StackPanel panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(BuildSectionHeader(title));

            panel.Children.Add(BuildCheckBoxWithDescription(out discordToggle, discordLabel, discordDescription));
            panel.Children.Add(BuildIndentedContent(BuildLabeledTextBox(discordTextLabel, discordTextDescription, out discordTextBox)));

            panel.Children.Add(BuildCheckBoxWithDescription(out telegramToggle, telegramLabel, telegramDescription));
            panel.Children.Add(BuildIndentedContent(BuildLabeledTextBox(telegramTokenLabel, telegramTokenDescription, out telegramTokenTextBox)));
            panel.Children.Add(BuildIndentedContent(BuildLabeledTextBox(telegramChatLabel, telegramChatDescription, out telegramChatTextBox)));

            return new Border { Child = panel };
        }

        private Border BuildAccountPickerRow()
        {
            StackPanel panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(new TextBlock
            {
                Text = "Available Accounts",
                Foreground = TitleTextBrush,
                Margin = new Thickness(0, 0, 0, 4)
            });

            DockPanel row = new DockPanel { LastChildFill = true };

            Button refreshAccountsButton = new Button
            {
                Content = "Refresh Accounts",
                MinWidth = 130,
                Margin = new Thickness(8, 0, 0, 0)
            };
            refreshAccountsButton.Click += (_, __) => RefreshAccountPickerOptions();
            DockPanel.SetDock(refreshAccountsButton, Dock.Right);
            row.Children.Add(refreshAccountsButton);

            Button addAccountButton = new Button
            {
                Content = "Add Account",
                MinWidth = 100,
                Margin = new Thickness(8, 0, 0, 0)
            };
            addAccountButton.Click += OnAddAccountClick;
            DockPanel.SetDock(addAccountButton, Dock.Right);
            row.Children.Add(addAccountButton);

            accountPickerComboBox = new ComboBox
            {
                MinWidth = 280
            };
            row.Children.Add(accountPickerComboBox);

            panel.Children.Add(row);
            panel.Children.Add(new TextBlock
            {
                Text = "Choose one of the currently available NinjaTrader accounts and append it to the monitored-account list.",
                Foreground = DescriptionTextBrush,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });

            return new Border { Child = panel };
        }

        private Border BuildSection(string title, out CheckBox first, string firstLabel, string firstDescription, out CheckBox second, string secondLabel, string secondDescription, out CheckBox third, string thirdLabel, string thirdDescription)
        {
            return BuildSection(title, out first, firstLabel, firstDescription, out second, secondLabel, secondDescription, out third, thirdLabel, thirdDescription, out _, string.Empty, string.Empty);
        }

        private Border BuildSection(string title, out CheckBox first, string firstLabel, string firstDescription)
        {
            return BuildSection(title, out first, firstLabel, firstDescription, out _, string.Empty, string.Empty, out _, string.Empty, string.Empty, out _, string.Empty, string.Empty);
        }

        private Border BuildSection(string title, out CheckBox first, string firstLabel, string firstDescription, out CheckBox second, string secondLabel, string secondDescription)
        {
            return BuildSection(title, out first, firstLabel, firstDescription, out second, secondLabel, secondDescription, out _, string.Empty, string.Empty, out _, string.Empty, string.Empty);
        }

        private Border BuildSection(string title, out CheckBox first, string firstLabel, string firstDescription, out CheckBox second, string secondLabel, string secondDescription, out CheckBox third, string thirdLabel, string thirdDescription, out CheckBox fourth, string fourthLabel, string fourthDescription)
        {
            StackPanel panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(BuildSectionHeader(title));

            if (!string.IsNullOrWhiteSpace(firstLabel))
            {
                panel.Children.Add(BuildCheckBoxWithDescription(out first, firstLabel, firstDescription));
            }
            else
            {
                first = null;
            }

            if (!string.IsNullOrWhiteSpace(secondLabel))
            {
                panel.Children.Add(BuildCheckBoxWithDescription(out second, secondLabel, secondDescription));
            }
            else
            {
                second = null;
            }

            if (!string.IsNullOrWhiteSpace(thirdLabel))
            {
                panel.Children.Add(BuildCheckBoxWithDescription(out third, thirdLabel, thirdDescription));
            }
            else
            {
                third = null;
            }

            if (!string.IsNullOrWhiteSpace(fourthLabel))
            {
                panel.Children.Add(BuildCheckBoxWithDescription(out fourth, fourthLabel, fourthDescription));
            }
            else
            {
                fourth = null;
            }

            return new Border { Child = panel };
        }

        private TextBlock BuildSectionHeader(string title)
        {
            return new TextBlock
            {
                Text = title,
                Foreground = TitleTextBrush,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 0, 8)
            };
        }

        private Border BuildCheckBoxWithDescription(out CheckBox checkBox, string label, string description)
        {
            StackPanel panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

            checkBox = new CheckBox
            {
                Content = label,
                Margin = new Thickness(0, 0, 0, 2),
                ToolTip = description
            };
            panel.Children.Add(checkBox);

            if (!string.IsNullOrWhiteSpace(description))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = description,
                    Foreground = DescriptionTextBrush,
                    Margin = new Thickness(18, 0, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
            }

            return new Border { Child = panel };
        }

        private Border BuildIndentedContent(UIElement child)
        {
            return new Border
            {
                Margin = new Thickness(18, 0, 0, 8),
                Child = child
            };
        }

        private Border BuildHiddenElement(UIElement child)
        {
            return new Border
            {
                Visibility = Visibility.Collapsed,
                Child = child
            };
        }

        private Border BuildLabeledTextBox(string label, string description, out TextBox textBox)
        {
            StackPanel panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = TitleTextBrush,
                Margin = new Thickness(0, 0, 0, 4)
            });

            textBox = new TextBox { MinWidth = 320, ToolTip = description };
            panel.Children.Add(textBox);

            if (!string.IsNullOrWhiteSpace(description))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = description,
                    Foreground = DescriptionTextBrush,
                    Margin = new Thickness(0, 4, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
            }

            return new Border { Child = panel };
        }

        private void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            if (TradeMessengerAddOn.Instance == null)
                return;

            LoadFromSettings(TradeMessengerAddOn.Instance.GetSettingsSnapshot(), TradeMessengerAddOn.Instance.GetStatusSnapshot());
        }

        private void OnAddAccountClick(object sender, RoutedEventArgs e)
        {
            string selectedAccount = accountPickerComboBox != null ? accountPickerComboBox.SelectedItem as string : null;
            if (string.IsNullOrWhiteSpace(selectedAccount) || monitoredAccountTextBox == null)
                return;

            List<string> existing = ParseCsvLocal(monitoredAccountTextBox.Text).ToList();
            if (!existing.Any(value => string.Equals(value, selectedAccount, StringComparison.OrdinalIgnoreCase)))
                existing.Add(selectedAccount);

            monitoredAccountTextBox.Text = string.Join(",", existing);
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (TradeMessengerAddOn.Instance == null)
                return;

            TradeMessengerAddOnSettings settings = new TradeMessengerAddOnSettings
            {
                Debug = debugCheckBox.IsChecked == true,
                SendPushNotifications = pushNotificationsCheckBox.IsChecked == true,
                ShowEntry = showEntryCheckBox.IsChecked == true,
                ShowExit = showExitCheckBox.IsChecked == true,
                SendToDiscord = discordCheckBox.IsChecked == true,
                DiscordWebhookUrl = discordWebhookTextBox.Text,
                SendToTelegram = telegramCheckBox.IsChecked == true,
                BotToken = botTokenTextBox.Text,
                ChatId = chatIdTextBox.Text,
                DataFeedReporting = dataFeedReportingCheckBox.IsChecked == true,
                WatchdogDataTimeoutSeconds = ParseIntOrDefault(watchdogTimeoutTextBox.Text, 60),
                HeartbeatReporting = heartbeatReportingCheckBox.IsChecked == true,
                HeartbeatFilePath = heartbeatFilePathTextBox.Text,
                HeartbeatTimeoutSeconds = ParseIntOrDefault(heartbeatTimeoutTextBox.Text, 60),
                RunHeadlessWhenWindowClosed = runHeadlessCheckBox.IsChecked == true,
                AutoReconnectEnabled = autoReconnectCheckBox.IsChecked == true,
                ReconnectInitialDelaySeconds = ParseIntOrDefault(reconnectInitialDelayTextBox.Text, 10),
                ReconnectMaxDelaySeconds = ParseIntOrDefault(reconnectMaxDelayTextBox.Text, 120),
                ReconnectMaxAttempts = ParseIntOrDefault(reconnectMaxAttemptsTextBox.Text, 0),
                MonitoredConnectionName = monitoredConnectionTextBox.Text,
                MonitoredAccountName = monitoredAccountTextBox.Text,
                MonitoredInstrumentsCsv = monitoredInstrumentsTextBox.Text
            };

            TradeMessengerAddOn.Instance.ApplySettings(settings);
            statusTextBlock.Text = "Settings saved. " + TradeMessengerAddOn.Instance.GetStatusSnapshot();
        }

        private static int ParseIntOrDefault(string value, int fallback)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;
        }

        private static IEnumerable<string> ParseCsvLocal(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv))
                yield break;

            foreach (string value in csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = value.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                    yield return trimmed;
            }
        }

        private void RefreshAccountPickerOptions()
        {
            if (accountPickerComboBox == null)
                return;

            string selected = accountPickerComboBox.SelectedItem as string;
            List<string> accountNames = new List<string>();

            lock (Connection.Connections)
            {
                foreach (Connection connection in Connection.Connections)
                {
                    if (connection == null || connection.Status != ConnectionStatus.Connected)
                        continue;

                    lock (connection.Accounts)
                    {
                        foreach (Account account in connection.Accounts)
                        {
                            if (account == null)
                                continue;

                            string name = !string.IsNullOrWhiteSpace(account.DisplayName)
                                ? account.DisplayName
                                : account.Name;
                            if (!string.IsNullOrWhiteSpace(name))
                                accountNames.Add(name);
                        }
                    }
                }
            }

            accountNames = accountNames
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            accountPickerComboBox.ItemsSource = accountNames;
            if (!string.IsNullOrWhiteSpace(selected))
                accountPickerComboBox.SelectedItem = accountNames.FirstOrDefault(name => string.Equals(name, selected, StringComparison.OrdinalIgnoreCase));

            if (accountPickerComboBox.SelectedItem == null && accountNames.Count > 0)
                accountPickerComboBox.SelectedIndex = 0;
        }
    }
}
