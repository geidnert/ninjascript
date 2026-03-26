#region Using declarations
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
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
using System.Windows.Input;
#endregion

namespace NinjaTrader.NinjaScript.AddOns
{
    public class TradeMessengerAddOn : AddOnBase
    {
        internal static TradeMessengerAddOn Instance { get; private set; }
        private const int TradeMessengerVendorLicenseId = 338;

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
            public bool IsEntrySideFill { get; set; }
            public string BotName { get; set; }
            public string Message { get; set; }
        }

        private sealed class StrategyRecoveryEntry
        {
            public string Key { get; set; }
            public string DisplayName { get; set; }
            public string StrategyName { get; set; }
        }

        private sealed class StrategyUiEntry
        {
            public string Key { get; set; }
            public string DisplayName { get; set; }
            public string StrategyName { get; set; }
            public bool? IsEnabled { get; set; }
            public object EnableCommand { get; set; }
            public object EnableParameter { get; set; }
            public object PreferredCommandSource { get; set; }
            public object SourceObject { get; set; }
        }

        private readonly object reconnectLock = new object();
        private readonly Dictionary<string, DateTime> strategyHeartbeats = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> strategyStalled = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> strategyStartupCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> lastTickTimeByInstrument = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> feedStalledByInstrument = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ConnectionStatus> lastConnectionStatusByName = new Dictionary<string, ConnectionStatus>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TrackedPositionState> trackedPositionsByKey = new Dictionary<string, TrackedPositionState>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> recentExecutionKeys = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> recentMessages = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly List<MarketDataSubscription> marketDataSubscriptions = new List<MarketDataSubscription>();
        private readonly HashSet<string> subscribedAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<StrategyRecoveryEntry> strategiesPendingRecovery = new List<StrategyRecoveryEntry>();
        private static readonly TimeSpan ExecutionDeduplicationWindow = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan MessageDeduplicationWindow = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan StrategyRestoreStabilityDelay = TimeSpan.FromSeconds(15);
        private Timer watchdogTimer;
        private string configFilePath;
        private string heartbeatFilePath;
        private bool isEnabled;
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
        private DateTime nextStrategyRestoreAttemptUtc = DateTime.MinValue;
        private string lastReconnectFailure = string.Empty;
        private string lastReconnectTargetName = string.Empty;
        private bool strategyRestorePending;
        private bool connectionIssueActive;
        private bool monitoringActive;
        private DateTime lastExecutionEventTimeUtc = DateTime.MinValue;
        private bool manualFeedStallEnabled;
        private bool licenseValidated;
        private string licenseFailureMessage;
        private bool licenseFailureShown;
        private Window controlCenterWindow;
        private NTMenuItem connectionsMenuItem;
        private object strategiesGridRoot;
        private NTMenuItem newMenuItem;
        private NTMenuItem autoEdgeMenuItem;
        private NTMenuItem launcherMenuItem;
        private TradeMessengerAddOnWindow settingsWindow;

        public TradeMessengerAddOn()
        {
            try
            {
                VendorLicense(TradeMessengerVendorLicenseId);
                licenseValidated = true;
                licenseFailureMessage = string.Empty;
            }
            catch (Exception ex)
            {
                licenseValidated = false;
                licenseFailureMessage = string.IsNullOrWhiteSpace(ex.Message)
                    ? "Trade Messenger license validation failed."
                    : ex.Message;
            }
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "TradeMessenger";
            }
            else if (State == State.Active)
            {
                Instance = this;
                LogToOutput($"🚀 TradeMessenger OnStateChange Active. LicenseValidated={licenseValidated}");
                if (!licenseValidated)
                {
                    LogToOutput($"⚠️ TradeMessenger license invalid: {licenseFailureMessage}");
                    StopMonitoring();
                    RemoveMenuItem();
                    ShowLicenseFailureMessage();
                    return;
                }

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
            if (!licenseValidated)
                return;

            LogToOutput($"🪟 OnWindowCreated: type={window?.GetType().FullName}, title={window?.Title}");

            if (LooksLikeControlCenterWindow(window))
            {
                controlCenterWindow = window;
                LogToOutput($"🪟 Captured main window: type={window.GetType().FullName}, title={window.Title}");
            }

            ControlCenter controlCenter = window as ControlCenter;
            if (controlCenter != null)
                controlCenter.Dispatcher.BeginInvoke(new Action(() => EnsureMenuItem(controlCenter)));
        }

        protected override void OnWindowDestroyed(Window window)
        {
            if (ReferenceEquals(controlCenterWindow, window))
                controlCenterWindow = null;

            if (window is ControlCenter)
            {
                connectionsMenuItem = null;
                strategiesGridRoot = null;
                RemoveMenuItem();
            }
        }

        private void ApplyDefaultSettings()
        {
            isEnabled = true;
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
            autoReconnectEnabled = false;
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
                Enabled = isEnabled,
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
                !isEnabled ? "disabled" : monitoringActive ? (reconnectLoopActive ? "armed" : "idle") : "stopped",
                AnyFeedCurrentlyStalled() ? "yes" : "no",
                connectionIssueActive ? "yes" : "no",
                subscribedAccountCount,
                subscribedInstrumentCount)
                + string.Format(
                    CultureInfo.InvariantCulture,
                    " | Manual stall: {0}",
                    manualFeedStallEnabled ? "on" : "off");
        }

        internal bool ToggleFeedStallSimulation()
        {
            manualFeedStallEnabled = !manualFeedStallEnabled;
            DateTime nowUtc = DateTime.UtcNow;

            if (manualFeedStallEnabled)
            {
                LogToOutput("🧪 Manual feed stall enabled. Incoming Last ticks will be ignored until disabled.");
                foreach (string instrumentName in lastTickTimeByInstrument.Keys.ToList())
                {
                    feedStalledByInstrument[instrumentName] = true;
                    LogToOutput($"⚠️ Manual feed stall active on {instrumentName}.");
                }

                RememberReconnectTargetFromCurrentConnections();
                CaptureEnabledStrategiesForRecovery();
                StartReconnectLoop(nowUtc);
            }
            else
            {
                LogToOutput("🧪 Manual feed stall disabled. Incoming Last ticks will be processed again.");
                foreach (string instrumentName in feedStalledByInstrument.Keys.ToList())
                {
                    if (!feedStalledByInstrument[instrumentName])
                        continue;

                    feedStalledByInstrument[instrumentName] = false;
                    lastTickTimeByInstrument[instrumentName] = nowUtc;
                    LogToOutput($"✅ Data feed resumed on {instrumentName} (manual release).");
                    if (sendPushNotifications && dataFeedReporting)
                        SendPushNotification(nowUtc, $"✅ Data feed resumed on {instrumentName} (manual release).");
                }

                if (!connectionIssueActive && !AnyFeedCurrentlyStalled())
                {
                    ScheduleStrategyRestore(nowUtc);
                    StopReconnectLoop();
                }
            }

            return manualFeedStallEnabled;
        }

        internal void ApplySettings(TradeMessengerAddOnSettings settings)
        {
            if (settings == null)
                return;

            isEnabled = settings.Enabled;
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
                    case "Enabled":
                        isEnabled = ParseBool(value, isEnabled);
                        break;
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
                "Enabled=true",
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
                "AutoReconnectEnabled=false",
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
                "Enabled=" + settings.Enabled.ToString().ToLowerInvariant(),
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
            if (!licenseValidated || !isEnabled)
            {
                StopMonitoring();
                return;
            }

            if (runHeadlessWhenWindowClosed || settingsWindow != null)
                StartMonitoring();
            else
                StopMonitoring();
        }

        private void StartMonitoring()
        {
            if (!licenseValidated || monitoringActive)
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

            lock (recentExecutionKeys)
                recentExecutionKeys.Clear();

            lock (recentMessages)
                recentMessages.Clear();

            strategyHeartbeats.Clear();
            strategyStalled.Clear();
            strategyStartupCount.Clear();
            strategiesPendingRecovery.Clear();
            lastConnectionStatusByName.Clear();
            connectionIssueActive = false;
            reconnectInProgress = false;
            reconnectAttemptCount = 0;
            nextReconnectAttemptUtc = DateTime.MinValue;
            nextStrategyRestoreAttemptUtc = DateTime.MinValue;
            lastReconnectFailure = string.Empty;
            lastReconnectTargetName = string.Empty;
            strategyRestorePending = false;
            lastExecutionEventTimeUtc = DateTime.MinValue;
            manualFeedStallEnabled = false;
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
            ResetExecutionTrackingIfPlaybackRewound(e.Time);
            if (IsDuplicateExecutionEvent(accountName, instrumentName, e.Execution, e.Time))
                return;

            ExecutionMessageResult result = BuildExecutionMessage(account, e.Execution, accountName, instrumentName);
            if (result == null || string.IsNullOrEmpty(result.Message))
                return;

            if ((result.IsEntryNotification && !showEntry) || (result.IsExitNotification && !showExit))
                return;

            if (result.IsEntrySideFill && !showEntry)
                return;

            string message = result.Message;
            if (IsDuplicateMessage(message))
                return;

            LogToOutput(message);
            if (sendPushNotifications)
                SendPushNotification(e.Time, message);
        }

        private bool IsDuplicateExecutionEvent(string accountName, string instrumentName, Execution execution, DateTime eventTime)
        {
            string key = BuildExecutionDeduplicationKey(accountName, instrumentName, execution, eventTime);
            DateTime nowUtc = DateTime.UtcNow;

            lock (recentExecutionKeys)
            {
                List<string> expiredKeys = null;
                foreach (KeyValuePair<string, DateTime> item in recentExecutionKeys)
                {
                    if (nowUtc - item.Value <= ExecutionDeduplicationWindow)
                        continue;

                    if (expiredKeys == null)
                        expiredKeys = new List<string>();

                    expiredKeys.Add(item.Key);
                }

                if (expiredKeys != null)
                {
                    foreach (string expiredKey in expiredKeys)
                        recentExecutionKeys.Remove(expiredKey);
                }

                if (recentExecutionKeys.ContainsKey(key))
                    return true;

                recentExecutionKeys[key] = nowUtc;
                return false;
            }
        }

        private bool IsDuplicateMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return false;

            DateTime nowUtc = DateTime.UtcNow;

            lock (recentMessages)
            {
                List<string> expiredKeys = null;
                foreach (KeyValuePair<string, DateTime> item in recentMessages)
                {
                    if (nowUtc - item.Value <= MessageDeduplicationWindow)
                        continue;

                    if (expiredKeys == null)
                        expiredKeys = new List<string>();

                    expiredKeys.Add(item.Key);
                }

                if (expiredKeys != null)
                {
                    foreach (string expiredKey in expiredKeys)
                        recentMessages.Remove(expiredKey);
                }

                if (recentMessages.ContainsKey(message))
                    return true;

                recentMessages[message] = nowUtc;
                return false;
            }
        }

        private void ResetExecutionTrackingIfPlaybackRewound(DateTime eventTime)
        {
            DateTime eventTimeUtc = eventTime.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(eventTime, DateTimeKind.Utc)
                : eventTime.ToUniversalTime();

            if (lastExecutionEventTimeUtc != DateTime.MinValue && eventTimeUtc < lastExecutionEventTimeUtc.AddSeconds(-30))
            {
                lock (trackedPositionsByKey)
                    trackedPositionsByKey.Clear();

                lock (recentExecutionKeys)
                    recentExecutionKeys.Clear();

                lock (recentMessages)
                    recentMessages.Clear();
            }

            lastExecutionEventTimeUtc = eventTimeUtc;
        }

        private static string BuildExecutionDeduplicationKey(string accountName, string instrumentName, Execution execution, DateTime eventTime)
        {
            Order order = execution != null ? execution.Order : null;
            string orderId = order != null ? order.OrderId ?? string.Empty : string.Empty;
            string orderName = order != null ? order.Name ?? string.Empty : string.Empty;
            string fromEntrySignal = order != null ? order.FromEntrySignal ?? string.Empty : string.Empty;
            string action = order != null ? order.OrderAction.ToString() : string.Empty;
            string quantity = execution != null ? execution.Quantity.ToString(CultureInfo.InvariantCulture) : "0";
            string price = execution != null ? execution.Price.ToString("0.########", CultureInfo.InvariantCulture) : "0";
            string time = string.IsNullOrEmpty(orderId)
                ? eventTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture)
                : string.Empty;

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}|{8}",
                accountName ?? string.Empty,
                instrumentName ?? string.Empty,
                orderId,
                orderName,
                fromEntrySignal,
                action,
                quantity,
                price,
                time);
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
                    IsEntrySideFill = true,
                    BotName = botName,
                    Message = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}\n{1}",
                        FormatMessageTitle(botName, instrumentName),
                        signedFillQuantity > 0 ? "Long" : "Short")
                };

            if (execution.Order.OrderAction == OrderAction.Buy || execution.Order.OrderAction == OrderAction.SellShort)
                return new ExecutionMessageResult
                {
                    IsEntrySideFill = true,
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

            if (manualFeedStallEnabled)
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
                {
                    ScheduleStrategyRestore(DateTime.UtcNow);
                    StopReconnectLoop();
                }
            }
        }

        private void WatchdogTimerElapsed(object sender, ElapsedEventArgs e)
        {
            DateTime nowUtc = DateTime.UtcNow;
            CheckConnectionStatuses(nowUtc);

            if (dataFeedReporting)
                CheckFeedStalls(nowUtc);

            if (heartbeatReporting)
                CheckHeartbeatFileMulti();

            TryReconnectIfDue(nowUtc);
            TryRestoreStrategiesIfDue(nowUtc);
        }

        private void CheckConnectionStatuses(DateTime nowUtc)
        {
            bool anyConnectionIssue = false;

            foreach (Connection connection in GetAllKnownConnections())
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
                    if (!string.IsNullOrWhiteSpace(connectionName))
                        lastReconnectTargetName = connectionName;

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

                    CaptureEnabledStrategiesForRecovery();
                    StartReconnectLoop(nowUtc);
                }
                else if (currentStatus == ConnectionStatus.Connected && wasDisconnected)
                {
                    LogToOutput($"✅ Connection restored on {connectionName}.");
                    if (sendPushNotifications)
                        SendPushNotification(nowUtc, $"✅ Connection restored on {connectionName}.");

                    ScheduleStrategyRestore(nowUtc);
                }

                lastConnectionStatusByName[connectionName] = currentStatus;
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
                RememberReconnectTargetFromCurrentConnections();
                CaptureEnabledStrategiesForRecovery();
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

                CaptureEnabledStrategiesForRecovery();
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

        private void ScheduleStrategyRestore(DateTime nowUtc)
        {
            if (!autoReconnectEnabled || strategiesPendingRecovery.Count == 0)
            {
                LogToOutput($"⚠️ Strategy restore not scheduled. AutoReconnect={autoReconnectEnabled}, CapturedStrategies={strategiesPendingRecovery.Count}.");
                return;
            }

            strategyRestorePending = true;
            nextStrategyRestoreAttemptUtc = nowUtc.Add(StrategyRestoreStabilityDelay);
            LogToOutput($"🕓 Strategy restore scheduled for {nextStrategyRestoreAttemptUtc:HH:mm:ss} after reconnect stabilization.");
        }

        private void TryRestoreStrategiesIfDue(DateTime nowUtc)
        {
            if (!autoReconnectEnabled || !strategyRestorePending || nowUtc < nextStrategyRestoreAttemptUtc)
                return;

            if (connectionIssueActive || AnyFeedCurrentlyStalled() || reconnectLoopActive)
            {
                LogToOutput($"🕓 Strategy restore waiting. ConnectionIssue={connectionIssueActive}, FeedStalled={AnyFeedCurrentlyStalled()}, ReconnectLoop={reconnectLoopActive}.");
                return;
            }

            bool restoredAny = RestoreStrategiesAfterReconnect(out string details);
            strategyRestorePending = false;
            nextStrategyRestoreAttemptUtc = DateTime.MinValue;

            if (restoredAny)
                LogToOutput($"✅ Strategy re-enable completed: {details}");
            else if (!string.IsNullOrWhiteSpace(details))
                LogToOutput($"⚠️ Strategy re-enable skipped: {details}");

            strategiesPendingRecovery.Clear();
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
                    LogToOutput($"🔁 Reconnect attempt #{reconnectAttemptCount} issued: {details}. Next retry in {delaySeconds}s if needed.");
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

            if (TryResumePlaybackIfPaused(out details))
                return true;

            List<Connection> connections = GetTargetConnections();
            if (connections.Count == 0)
            {
                string fallbackConnectionName = ResolveReconnectTargetName();
                if (!string.IsNullOrWhiteSpace(fallbackConnectionName))
                {
                    bool menuIssued = TryReconnectViaControlCenterMenu(fallbackConnectionName, out string menuDetails);
                    details = string.Format(
                        CultureInfo.InvariantCulture,
                        "No matching connections found. Fallback target={0}. Menu={1}. {2}",
                        fallbackConnectionName,
                        string.IsNullOrWhiteSpace(menuDetails) ? "none" : menuDetails,
                        DescribeKnownConnections());
                    return menuIssued;
                }

                details = "No matching connections found. " + DescribeKnownConnections();
                return false;
            }

            bool anyIssued = false;
            List<string> connectionResults = new List<string>();

            foreach (Connection connection in connections)
            {
                try
                {
                    string connectionName = GetConnectionName(connection);
                    if (!string.IsNullOrWhiteSpace(connectionName))
                        lastReconnectTargetName = connectionName;
                    bool connectIssued;
                    bool disconnectIssued = false;
                    bool canConnect = HasParameterlessMethod(connection, "Connect");
                    bool isPlaybackConnection = connectionName.IndexOf("Playback", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (connection.Status == ConnectionStatus.Connected && AnyFeedCurrentlyStalled())
                    {
                        if (isPlaybackConnection && !canConnect)
                        {
                            connectionResults.Add(string.Format(
                                CultureInfo.InvariantCulture,
                                "{0}(skip disconnect: playback has no connect path)",
                                connectionName));
                            continue;
                        }

                        disconnectIssued = TryInvokeParameterless(connection, "Disconnect");
                        System.Threading.Thread.Sleep(300);
                    }

                    connectIssued = TryInvokeParameterless(connection, "Connect");
                    if (!connectIssued)
                    {
                        bool reflectedIssued = TryReconnectViaReflectedMethods(connection, out string reflectedDetails);
                        if (!string.IsNullOrWhiteSpace(reflectedDetails))
                            connectionResults.Add(string.Format(CultureInfo.InvariantCulture, "{0}(Reflected={1})", connectionName, reflectedDetails));
                        connectIssued = reflectedIssued;
                    }
                    if (!connectIssued)
                    {
                        connectIssued = TryReconnectViaControlCenterMenu(connectionName, out string menuDetails);
                        if (!string.IsNullOrWhiteSpace(menuDetails))
                            connectionResults.Add(string.Format(CultureInfo.InvariantCulture, "{0}(Menu={1})", connectionName, menuDetails));
                    }
                    anyIssued |= disconnectIssued || connectIssued;
                    connectionResults.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}(Disconnect={1}, Connect={2})",
                        connectionName,
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

            details = string.Join("; ", connectionResults) + " | " + DescribeKnownConnections();
            return anyIssued;
        }

        private string ResolveReconnectTargetName()
        {
            if (!string.IsNullOrWhiteSpace(monitoredConnectionName))
                return monitoredConnectionName;

            if (!string.IsNullOrWhiteSpace(lastReconnectTargetName) && IsKnownReconnectTarget(lastReconnectTargetName))
                return lastReconnectTargetName;

            if (lastConnectionStatusByName.Count == 1)
                return lastConnectionStatusByName.Keys.FirstOrDefault() ?? string.Empty;

            return string.Empty;
        }

        private void RememberReconnectTargetFromCurrentConnections()
        {
            if (!string.IsNullOrWhiteSpace(monitoredConnectionName))
            {
                lastReconnectTargetName = monitoredConnectionName;
                return;
            }

            List<string> connectedNames = GetAllKnownConnections()
                .Where(connection => connection != null && IsMatchingConnection(connection))
                .Select(GetConnectionName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (connectedNames.Count == 1)
                lastReconnectTargetName = connectedNames[0];
        }

        private bool IsKnownReconnectTarget(string connectionName)
        {
            if (string.IsNullOrWhiteSpace(connectionName))
                return false;

            if (lastConnectionStatusByName.Keys.Any(name => string.Equals(name, connectionName, StringComparison.OrdinalIgnoreCase)))
                return true;

            return GetAllKnownConnections().Any(connection =>
                string.Equals(GetConnectionName(connection), connectionName, StringComparison.OrdinalIgnoreCase));
        }

        private List<Connection> GetTargetConnections()
        {
            List<Connection> connections = new List<Connection>();
            foreach (Connection connection in GetAllKnownConnections())
            {
                if (connection != null && IsMatchingConnection(connection))
                    connections.Add(connection);
            }

            return connections;
        }

        private IEnumerable<Connection> GetAllKnownConnections()
        {
            Dictionary<string, Connection> connections = new Dictionary<string, Connection>(StringComparer.OrdinalIgnoreCase);

            lock (Connection.Connections)
            {
                foreach (Connection connection in Connection.Connections)
                {
                    if (connection == null)
                        continue;

                    string key = BuildConnectionIdentity(connection);
                    if (!connections.ContainsKey(key))
                        connections[key] = connection;
                }
            }

            Connection playbackConnection = GetSpecialConnection("PlaybackConnection");
            if (playbackConnection != null)
            {
                string key = BuildConnectionIdentity(playbackConnection);
                if (!connections.ContainsKey(key))
                    connections[key] = playbackConnection;
            }

            return connections.Values;
        }

        private string DescribeKnownConnections()
        {
            List<string> descriptions = new List<string>();

            try
            {
                int count = 0;
                lock (Connection.Connections)
                {
                    foreach (Connection connection in Connection.Connections)
                    {
                        if (connection == null)
                            continue;

                        descriptions.Add(string.Format(
                            CultureInfo.InvariantCulture,
                            "Connections[{0}]={1}/{2}",
                            count,
                            GetConnectionName(connection),
                            connection.Status));
                        count++;
                    }
                }

                if (count == 0)
                    descriptions.Add("Connections=empty");
            }
            catch (Exception ex)
            {
                descriptions.Add("Connections[error]=" + ex.Message);
            }

            try
            {
                Connection playbackConnection = GetSpecialConnection("PlaybackConnection");
                descriptions.Add(playbackConnection == null
                    ? "PlaybackConnection=null"
                    : string.Format(
                        CultureInfo.InvariantCulture,
                        "PlaybackConnection={0}/{1}",
                        GetConnectionName(playbackConnection),
                        playbackConnection.Status));
            }
            catch (Exception ex)
            {
                descriptions.Add("PlaybackConnection[error]=" + ex.Message);
            }

            try
            {
                descriptions.Add("PlaybackWindow=" + GetPlaybackWindowStateSafe());
            }
            catch (Exception ex)
            {
                descriptions.Add("PlaybackWindow[error]=" + ex.Message);
            }

            return descriptions.Count == 0 ? "No known connections." : string.Join(" | ", descriptions);
        }

        private Connection GetSpecialConnection(string propertyName)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
                return null;

            try
            {
                Connection directConnection = GetStaticConnectionProperty(typeof(Connection), propertyName);
                if (directConnection != null)
                    return directConnection;

                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try
                    {
                        types = assembly.GetTypes();
                    }
                    catch (ReflectionTypeLoadException ex)
                    {
                        types = ex.Types.Where(type => type != null).ToArray();
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (Type type in types)
                    {
                        Connection connection = GetStaticConnectionProperty(type, propertyName);
                        if (connection != null)
                            return connection;
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static Connection GetStaticConnectionProperty(Type type, string propertyName)
        {
            if (type == null)
                return null;

            PropertyInfo property = type.GetProperty(propertyName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null || !typeof(Connection).IsAssignableFrom(property.PropertyType))
                return null;

            try
            {
                return property.GetValue(null, null) as Connection;
            }
            catch
            {
                return null;
            }
        }

        private string BuildConnectionIdentity(Connection connection)
        {
            if (connection == null)
                return string.Empty;

            string name = GetConnectionName(connection);
            if (!string.IsNullOrWhiteSpace(name))
                return name;

            return connection.GetHashCode().ToString(CultureInfo.InvariantCulture);
        }

        private bool TryResumePlaybackIfPaused(out string details)
        {
            details = string.Empty;
            if (Application.Current == null || Application.Current.Dispatcher == null)
                return false;

            try
            {
                string localDetails = string.Empty;
                bool resumed = Application.Current.Dispatcher.Invoke(() =>
                {
                    foreach (Window window in Application.Current.Windows)
                    {
                        if (window == null || window.GetType().Name.IndexOf("PlaybackControlCenter", StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

                        bool? isPaused = GetNullableBoolean(window, "IsPaused")
                            ?? GetNullableBoolean(GetPropertyValue(window, "DataContext"), "IsPaused");
                        if (isPaused != true)
                            continue;

                        if (TryInvokeParameterless(window, "Play")
                            || TryInvokeParameterless(window, "Resume")
                            || TryInvokeParameterless(window, "OnPlay")
                            || TrySetBooleanProperty(window, "IsPaused", false)
                            || TryInvokePlaybackButton(window))
                        {
                            localDetails = "Playback resumed.";
                            return true;
                        }

                        localDetails = "Playback was paused, but resume command could not be invoked.";
                        return false;
                    }

                    localDetails = "Playback resume not used. " + GetPlaybackWindowStateSafe();
                    return false;
                });
                details = localDetails;
                return resumed;
            }
            catch (Exception ex)
            {
                details = "Playback resume error: " + ex.Message;
                return false;
            }
        }

        private bool TryInvokePlaybackButton(DependencyObject root)
        {
            if (root == null)
                return false;

            Button button = FindNamedDescendant<Button>(root, "btnPlay");
            if (button != null)
            {
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                return true;
            }

            return false;
        }

        private string DescribePlaybackWindowState()
        {
            if (Application.Current == null)
                return "Application.Current=null";

            List<string> states = new List<string>();
            foreach (Window window in Application.Current.Windows)
            {
                if (window == null || window.GetType().Name.IndexOf("PlaybackControlCenter", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                bool? isPaused = GetNullableBoolean(window, "IsPaused")
                    ?? GetNullableBoolean(GetPropertyValue(window, "DataContext"), "IsPaused");
                states.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}(IsPaused={1})",
                    window.GetType().Name,
                    isPaused.HasValue ? isPaused.Value.ToString() : "unknown"));
            }

            return states.Count == 0 ? "not found" : string.Join("; ", states);
        }

        private string GetPlaybackWindowStateSafe()
        {
            if (Application.Current == null || Application.Current.Dispatcher == null)
                return "Application.Current.Dispatcher=null";

            if (Application.Current.Dispatcher.CheckAccess())
                return DescribePlaybackWindowState();

            return Application.Current.Dispatcher.Invoke(() => DescribePlaybackWindowState());
        }

        private T FindNamedDescendant<T>(DependencyObject root, string expectedName) where T : FrameworkElement
        {
            if (root == null)
                return null;

            int childCount;
            try
            {
                childCount = VisualTreeHelper.GetChildrenCount(root);
            }
            catch
            {
                return null;
            }

            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                T typedChild = child as T;
                if (typedChild != null && string.Equals(typedChild.Name, expectedName, StringComparison.OrdinalIgnoreCase))
                    return typedChild;

                T nested = FindNamedDescendant<T>(child, expectedName);
                if (nested != null)
                    return nested;
            }

            return null;
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

        private bool IsMatchingInstrumentName(string instrumentName)
        {
            if (string.IsNullOrWhiteSpace(instrumentName))
                return false;

            if (string.IsNullOrWhiteSpace(monitoredInstrumentsCsv))
                return true;

            foreach (string instrumentFilter in ParseCsv(monitoredInstrumentsCsv))
            {
                if (string.Equals(instrumentName, instrumentFilter, StringComparison.OrdinalIgnoreCase))
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

        private bool HasParameterlessMethod(object target, string methodName)
        {
            if (target == null || string.IsNullOrEmpty(methodName))
                return false;

            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);

            return method != null;
        }

        private bool TryReconnectViaReflectedMethods(object target, out string details)
        {
            details = string.Empty;
            if (target == null)
                return false;

            string[] candidates = { "Connect", "Reconnect", "ConnectNow", "Start", "Open", "Resume", "Enable", "OnConnect" };
            List<string> available = GetParameterlessMethodNames(target, candidates).ToList();

            foreach (string candidate in candidates)
            {
                if (!HasParameterlessMethod(target, candidate))
                    continue;

                try
                {
                    if (TryInvokeParameterless(target, candidate))
                    {
                        details = string.Format(
                            CultureInfo.InvariantCulture,
                            "invoked {0}; available={1}",
                            candidate,
                            available.Count == 0 ? "none" : string.Join(",", available));
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    details = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} threw {1}; available={2}",
                        candidate,
                        ex.Message,
                        available.Count == 0 ? "none" : string.Join(",", available));
                    return false;
                }
            }

            details = "no reconnect method; available=" + (available.Count == 0 ? "none" : string.Join(",", available));
            return false;
        }

        private IEnumerable<string> GetParameterlessMethodNames(object target, IEnumerable<string> candidateNames)
        {
            if (target == null || candidateNames == null)
                yield break;

            foreach (string candidateName in candidateNames)
            {
                if (HasParameterlessMethod(target, candidateName))
                    yield return candidateName;
            }
        }

        private bool TryReconnectViaControlCenterMenu(string connectionName, out string details)
        {
            details = string.Empty;
            if (string.IsNullOrWhiteSpace(connectionName))
            {
                details = "dispatcher unavailable";
                return false;
            }

            try
            {
                if (TryReconnectViaCachedConnectionsMenu(connectionName, out string cachedMenuDetails))
                {
                    details = "CachedMenu=" + cachedMenuDetails;
                    return true;
                }

                if (TryReconnectViaKnownMenuTree(connectionName, out string knownMenuDetails))
                {
                    details = "KnownMenu=" + knownMenuDetails;
                    return true;
                }

                if (TryReconnectViaGuiReflection(connectionName, out string reflectionDetails))
                {
                    details = "GuiReflection=" + reflectionDetails;
                    return true;
                }

                System.Windows.Threading.Dispatcher dispatcher = controlCenterWindow != null && controlCenterWindow.Dispatcher != null
                    ? controlCenterWindow.Dispatcher
                    : Application.Current != null ? Application.Current.Dispatcher : null;
                if (dispatcher == null)
                {
                    details = "dispatcher unavailable";
                    return false;
                }

                string localDetails = string.Empty;
                bool reconnected = dispatcher.Invoke(() =>
                {
                    List<string> attempts = new List<string>();
                    attempts.Add("Windows=" + DescribeApplicationWindows());

                    foreach (Window window in GetCandidateControlCenterWindows())
                    {
                        if (!LooksLikeControlCenterWindow(window))
                            continue;

                        attempts.Add("ControlCenter=found");
                        NTMenuItem connectionsMenu = FindTopLevelMenuItem(window, "connectionsMenuItem", "Connections");
                        if (connectionsMenu == null)
                            connectionsMenu = FindTopLevelMenuItem(window, "mnuConnections", "Connections");
                        if (connectionsMenu == null)
                        {
                            attempts.Add("ConnectionsMenu=missing");
                            continue;
                        }

                        List<string> menuHeaders = new List<string>();
                        CollectMenuHeaders(connectionsMenu, menuHeaders, 0);
                        attempts.Add("ConnectionsMenuItems=" + string.Join(",", menuHeaders.Take(12).ToArray()));

                        NTMenuItem connectionMenuItem = FindMenuItemRecursive(connectionsMenu, connectionName);
                        if (connectionMenuItem == null)
                        {
                            attempts.Add("TargetMenuItem=missing");
                            continue;
                        }

                        if (connectionMenuItem.Dispatcher != null && !connectionMenuItem.Dispatcher.CheckAccess())
                        {
                            connectionMenuItem.Dispatcher.Invoke(() => connectionMenuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)));
                        }
                        else
                        {
                            connectionMenuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                        }

                        attempts.Add("TargetMenuItem=clicked:" + GetHeaderText(connectionMenuItem.Header));
                        localDetails = string.Join(" | ", attempts);
                        return true;
                    }

                    if (attempts.Count == 0)
                        attempts.Add("ControlCenter=missing");
                    localDetails = string.Join(" | ", attempts);
                    return false;
                });
                details = localDetails;
                return reconnected;
            }
            catch (Exception ex)
            {
                details = ex.Message;
                LogToOutput($"⚠️ Control Center menu reconnect failed for {connectionName}: {ex.Message}");
                return false;
            }
        }

        private bool TryReconnectViaCachedConnectionsMenu(string connectionName, out string details)
        {
            details = string.Empty;
            if (connectionsMenuItem == null || string.IsNullOrWhiteSpace(connectionName))
            {
                details = "connections menu unavailable";
                return false;
            }

            try
            {
                System.Windows.Threading.Dispatcher dispatcher = connectionsMenuItem.Dispatcher;
                if (dispatcher == null)
                {
                    details = "connections menu dispatcher unavailable";
                    return false;
                }

                string localDetails = string.Empty;
                bool clicked = dispatcher.Invoke(() =>
                {
                    EnsureWindowFocused(controlCenterWindow);
                    EnsureMenuExpanded(connectionsMenuItem);
                    NTMenuItem exactItem = FindExactMenuItemRecursive(connectionsMenuItem, connectionName);
                    if (exactItem == null)
                    {
                        List<string> headers = new List<string>();
                        CollectMenuHeaders(connectionsMenuItem, headers, 0);
                        localDetails = "target missing; headers=" + string.Join(",", headers.Take(20).ToArray());
                        return false;
                    }

                    exactItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    localDetails = "clicked:" + GetHeaderText(exactItem.Header);
                    return true;
                });
                details = localDetails;
                return clicked;
            }
            catch (Exception ex)
            {
                details = ex.Message;
                return false;
            }
        }

        private void EnsureMenuExpanded(NTMenuItem menuItem)
        {
            if (menuItem == null)
                return;

            try
            {
                if (!menuItem.IsSubmenuOpen)
                    menuItem.IsSubmenuOpen = true;
            }
            catch
            {
            }

            try
            {
                menuItem.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent));
            }
            catch
            {
            }
        }

        private void EnsureWindowFocused(Window window)
        {
            if (window == null)
                return;

            try
            {
                if (window.WindowState == WindowState.Minimized)
                    window.WindowState = WindowState.Normal;
            }
            catch
            {
            }

            try
            {
                window.Show();
            }
            catch
            {
            }

            try
            {
                window.Activate();
            }
            catch
            {
            }

            try
            {
                window.Focus();
            }
            catch
            {
            }
        }

        private bool TryReconnectViaKnownMenuTree(string connectionName, out string details)
        {
            details = string.Empty;
            if (string.IsNullOrWhiteSpace(connectionName))
                return false;

            try
            {
                ItemsControl rootMenu = GetRootMenuContainer();
                if (rootMenu == null)
                {
                    details = "root menu missing";
                    return false;
                }

                List<string> headers = new List<string>();
                CollectMenuHeaders(rootMenu, headers, 0);

                NTMenuItem connectionsMenu = FindMenuItemRecursive(rootMenu, "Connections");
                if (connectionsMenu == null)
                {
                    details = "connections menu missing; root headers=" + string.Join(",", headers.Take(12).ToArray());
                    return false;
                }

                NTMenuItem targetItem = FindMenuItemRecursive(connectionsMenu, connectionName);
                if (targetItem == null)
                {
                    List<string> connectionHeaders = new List<string>();
                    CollectMenuHeaders(connectionsMenu, connectionHeaders, 0);
                    details = "target missing; connections headers=" + string.Join(",", connectionHeaders.Take(20).ToArray());
                    return false;
                }

                targetItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                details = "clicked:" + GetHeaderText(targetItem.Header);
                return true;
            }
            catch (Exception ex)
            {
                details = ex.Message;
                return false;
            }
        }

        private ItemsControl GetRootMenuContainer()
        {
            if (newMenuItem == null)
                return null;

            DependencyObject current = newMenuItem;
            ItemsControl lastItemsControl = newMenuItem;

            while (current != null)
            {
                ItemsControl itemsControl = current as ItemsControl;
                if (itemsControl != null)
                    lastItemsControl = itemsControl;

                current = VisualTreeHelper.GetParent(current);
            }

            return lastItemsControl;
        }

        private bool LooksLikeControlCenterWindow(Window window)
        {
            if (window == null)
                return false;

            string typeName = window.GetType().Name ?? string.Empty;
            string fullTypeName = window.GetType().FullName ?? string.Empty;
            string title = window.Title ?? string.Empty;
            return typeName.IndexOf("ControlCenter", StringComparison.OrdinalIgnoreCase) >= 0
                || fullTypeName.IndexOf("ControlCenter", StringComparison.OrdinalIgnoreCase) >= 0
                || title.IndexOf("Control Center", StringComparison.OrdinalIgnoreCase) >= 0
                || title.IndexOf("NinjaTrader", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool TryReconnectViaGuiReflection(string connectionName, out string details)
        {
            details = string.Empty;
            if (string.IsNullOrWhiteSpace(connectionName))
                return false;

            try
            {
                System.Windows.Threading.Dispatcher dispatcher = controlCenterWindow != null && controlCenterWindow.Dispatcher != null
                    ? controlCenterWindow.Dispatcher
                    : Application.Current != null ? Application.Current.Dispatcher : null;
                if (dispatcher == null)
                {
                    details = "dispatcher unavailable";
                    return false;
                }

                string localDetails = string.Empty;
                bool result = dispatcher.Invoke(() =>
                {
                    HashSet<int> visited = new HashSet<int>();
                    List<string> traces = new List<string>();

                    foreach (object root in EnumerateGuiReflectionRoots())
                    {
                        NTMenuItem menuItem = FindConnectionMenuItemByReflection(root, connectionName, visited, 0);
                        if (menuItem == null)
                            continue;

                        menuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                        traces.Add("clicked:" + GetHeaderText(menuItem.Header));
                        localDetails = string.Join(" | ", traces);
                        return true;
                    }

                    List<string> rootNames = EnumerateGuiReflectionRoots()
                        .Take(10)
                        .Select(root => root == null ? "null" : root.GetType().FullName)
                        .ToList();
                    traces.Add("roots=" + (rootNames.Count == 0 ? "none" : string.Join(",", rootNames)));
                    localDetails = string.Join(" | ", traces);
                    return false;
                });
                details = localDetails;
                return result;
            }
            catch (Exception ex)
            {
                details = ex.Message;
                return false;
            }
        }

        private IEnumerable<object> EnumerateGuiReflectionRoots()
        {
            HashSet<string> yielded = new HashSet<string>(StringComparer.Ordinal);

            if (controlCenterWindow != null)
            {
                yielded.Add(controlCenterWindow.GetType().FullName + "@window");
                yield return controlCenterWindow;
            }

            if (newMenuItem != null && yielded.Add(newMenuItem.GetType().FullName + "@newMenu"))
                yield return newMenuItem;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string assemblyName = assembly.GetName().Name ?? string.Empty;
                if (assemblyName.IndexOf("NinjaTrader.Gui", StringComparison.OrdinalIgnoreCase) < 0
                    && assemblyName.IndexOf("NinjaTrader.Core", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(type => type != null).ToArray();
                }
                catch
                {
                    continue;
                }

                foreach (Type type in types)
                {
                    foreach (PropertyInfo property in type.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (!property.CanRead || property.GetIndexParameters().Length > 0)
                            continue;

                        if (!LooksLikeGuiRootProperty(type, property))
                            continue;

                        object value;
                        try
                        {
                            value = property.GetValue(null, null);
                        }
                        catch
                        {
                            continue;
                        }

                        if (value == null)
                            continue;

                        string key = type.FullName + "." + property.Name;
                        if (yielded.Add(key))
                            yield return value;
                    }

                    foreach (FieldInfo field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (!LooksLikeGuiRootField(type, field))
                            continue;

                        object value;
                        try
                        {
                            value = field.GetValue(null);
                        }
                        catch
                        {
                            continue;
                        }

                        if (value == null)
                            continue;

                        string key = type.FullName + "." + field.Name;
                        if (yielded.Add(key))
                            yield return value;
                    }
                }
            }
        }

        private bool LooksLikeGuiRootProperty(Type declaringType, PropertyInfo property)
        {
            string propertyName = property.Name ?? string.Empty;
            string typeName = declaringType != null ? declaringType.FullName ?? string.Empty : string.Empty;
            string propertyTypeName = property.PropertyType != null ? property.PropertyType.FullName ?? string.Empty : string.Empty;

            return propertyName.IndexOf("CcConnections", StringComparison.OrdinalIgnoreCase) >= 0
                || propertyName.IndexOf("ControlCenterCommands", StringComparison.OrdinalIgnoreCase) >= 0
                || propertyName.IndexOf("Connections", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("ControlCenterCommands", StringComparison.OrdinalIgnoreCase) >= 0
                || propertyTypeName.IndexOf("ControlCenterCommands", StringComparison.OrdinalIgnoreCase) >= 0
                || typeof(ItemsControl).IsAssignableFrom(property.PropertyType)
                || typeof(DependencyObject).IsAssignableFrom(property.PropertyType);
        }

        private bool LooksLikeGuiRootField(Type declaringType, FieldInfo field)
        {
            string fieldName = field.Name ?? string.Empty;
            string typeName = declaringType != null ? declaringType.FullName ?? string.Empty : string.Empty;
            string fieldTypeName = field.FieldType != null ? field.FieldType.FullName ?? string.Empty : string.Empty;

            return fieldName.IndexOf("CcConnections", StringComparison.OrdinalIgnoreCase) >= 0
                || fieldName.IndexOf("ControlCenterCommands", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("ControlCenterCommands", StringComparison.OrdinalIgnoreCase) >= 0
                || fieldTypeName.IndexOf("ControlCenterCommands", StringComparison.OrdinalIgnoreCase) >= 0
                || typeof(ItemsControl).IsAssignableFrom(field.FieldType)
                || typeof(DependencyObject).IsAssignableFrom(field.FieldType);
        }

        private NTMenuItem FindConnectionMenuItemByReflection(object root, string connectionName, HashSet<int> visited, int depth)
        {
            if (root == null || string.IsNullOrWhiteSpace(connectionName) || depth > 5)
                return null;

            Type type = root.GetType();
            if (IsSimpleType(type))
                return null;

            int objectId = RuntimeHelpers.GetHashCode(root);
            if (!visited.Add(objectId))
                return null;

            NTMenuItem menuItem = root as NTMenuItem;
            if (menuItem != null)
            {
                string header = GetHeaderText(menuItem.Header);
                if (string.Equals(header, connectionName, StringComparison.OrdinalIgnoreCase)
                    || NormalizeMenuHeader(header) == NormalizeMenuHeader(connectionName))
                    return menuItem;
            }

            ItemsControl itemsControl = root as ItemsControl;
            if (itemsControl != null)
            {
                foreach (object item in itemsControl.Items)
                {
                    NTMenuItem nested = FindConnectionMenuItemByReflection(item, connectionName, visited, depth + 1);
                    if (nested != null)
                        return nested;
                }
            }

            if (root is IEnumerable enumerable && !(root is string))
            {
                int count = 0;
                foreach (object item in enumerable)
                {
                    NTMenuItem nested = FindConnectionMenuItemByReflection(item, connectionName, visited, depth + 1);
                    if (nested != null)
                        return nested;

                    if (++count >= 100)
                        break;
                }
            }

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                    continue;

                if (!ShouldInspectGuiProperty(property))
                    continue;

                object value;
                try
                {
                    value = property.GetValue(root, null);
                }
                catch
                {
                    continue;
                }

                NTMenuItem nested = FindConnectionMenuItemByReflection(value, connectionName, visited, depth + 1);
                if (nested != null)
                    return nested;
            }

            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!ShouldInspectGuiField(field))
                    continue;

                object value;
                try
                {
                    value = field.GetValue(root);
                }
                catch
                {
                    continue;
                }

                NTMenuItem nested = FindConnectionMenuItemByReflection(value, connectionName, visited, depth + 1);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        private bool ShouldInspectGuiProperty(PropertyInfo property)
        {
            if (property == null)
                return false;

            string name = property.Name ?? string.Empty;
            string typeName = property.PropertyType != null ? property.PropertyType.FullName ?? string.Empty : string.Empty;
            return name.IndexOf("Connection", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Menu", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Command", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Item", StringComparison.OrdinalIgnoreCase) >= 0
                || typeof(ItemsControl).IsAssignableFrom(property.PropertyType)
                || typeof(DependencyObject).IsAssignableFrom(property.PropertyType)
                || typeName.IndexOf("Connection", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("Menu", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool ShouldInspectGuiField(FieldInfo field)
        {
            if (field == null)
                return false;

            string name = field.Name ?? string.Empty;
            string typeName = field.FieldType != null ? field.FieldType.FullName ?? string.Empty : string.Empty;
            return name.IndexOf("Connection", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Menu", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Command", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Item", StringComparison.OrdinalIgnoreCase) >= 0
                || typeof(ItemsControl).IsAssignableFrom(field.FieldType)
                || typeof(DependencyObject).IsAssignableFrom(field.FieldType)
                || typeName.IndexOf("Connection", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("Menu", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private IEnumerable<Window> GetCandidateControlCenterWindows()
        {
            HashSet<IntPtr> yieldedHandles = new HashSet<IntPtr>();

            if (controlCenterWindow != null)
            {
                IntPtr cachedHandle = new System.Windows.Interop.WindowInteropHelper(controlCenterWindow).Handle;
                yieldedHandles.Add(cachedHandle);
                yield return controlCenterWindow;
            }

            if (Application.Current == null)
                yield break;

            foreach (Window window in Application.Current.Windows)
            {
                if (window == null)
                    continue;

                IntPtr handle = IntPtr.Zero;
                try
                {
                    handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                }
                catch
                {
                }

                if (handle != IntPtr.Zero && yieldedHandles.Contains(handle))
                    continue;

                if (handle != IntPtr.Zero)
                    yieldedHandles.Add(handle);

                yield return window;
            }
        }

        private string DescribeApplicationWindows()
        {
            if (Application.Current == null)
                return "Application.Current=null";

            List<string> descriptions = new List<string>();
            foreach (Window window in Application.Current.Windows)
            {
                if (window == null)
                    continue;

                string typeName = window.GetType().Name ?? "UnknownType";
                string title = window.Title ?? string.Empty;
                descriptions.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}[{1}]",
                    typeName,
                    string.IsNullOrWhiteSpace(title) ? "no-title" : title));
            }

            return descriptions.Count == 0 ? "none" : string.Join(",", descriptions);
        }

        private void CaptureEnabledStrategiesForRecovery()
        {
            if (!autoReconnectEnabled || strategiesPendingRecovery.Count > 0 || Application.Current == null || Application.Current.Dispatcher == null)
                return;

            try
            {
                List<StrategyRecoveryEntry> heartbeatEntries = GetHeartbeatRecoveryEntries();
                if (heartbeatEntries.Count > 0)
                {
                    foreach (StrategyRecoveryEntry entry in heartbeatEntries)
                    {
                        if (entry == null || string.IsNullOrWhiteSpace(entry.Key))
                            continue;

                        if (strategiesPendingRecovery.Any(existing => string.Equals(existing.Key, entry.Key, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        strategiesPendingRecovery.Add(entry);
                    }

                    LogToOutput($"📝 Captured {strategiesPendingRecovery.Count} active strategies from heartbeat state for recovery after reconnect.");
                }
                else
                {
                    List<StrategyUiEntry> snapshot = GetStrategyEntriesSnapshot();
                    LogStrategyEntriesSnapshot("capture", snapshot);
                    foreach (StrategyUiEntry entry in snapshot)
                    {
                        if (entry == null || entry.IsEnabled != true || string.IsNullOrWhiteSpace(entry.Key))
                            continue;

                        if (strategiesPendingRecovery.Any(existing => string.Equals(existing.Key, entry.Key, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        strategiesPendingRecovery.Add(new StrategyRecoveryEntry
                        {
                            Key = entry.Key,
                            DisplayName = entry.DisplayName,
                            StrategyName = entry.StrategyName
                        });
                    }

                    if (strategiesPendingRecovery.Count > 0)
                        LogToOutput($"📝 Captured {strategiesPendingRecovery.Count} enabled strategies for recovery after reconnect.");
                    else
                        LogToOutput("⚠️ Strategy capture found no enabled strategies to recover.");
                }
            }
            catch (Exception ex)
            {
                LogToOutput($"⚠️ Unable to capture enabled strategies for recovery: {ex.Message}");
            }
        }

        private bool RestoreStrategiesAfterReconnect(out string details)
        {
            details = string.Empty;
            if (strategiesPendingRecovery.Count == 0)
            {
                details = "No saved enabled strategies.";
                return false;
            }

            string reconnectTarget = ResolveReconnectTargetName();
            if (!string.IsNullOrWhiteSpace(reconnectTarget)
                && reconnectTarget.IndexOf("Playback", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                details = "Playback strategy auto-restore is disabled.";
                return false;
            }

            System.Windows.Threading.Dispatcher dispatcher = Application.Current != null ? Application.Current.Dispatcher : null;
            if (dispatcher == null)
            {
                details = "Application dispatcher unavailable.";
                return false;
            }

            try
            {
                string localDetails = string.Empty;
                bool restored = dispatcher.Invoke(() =>
                {
                    List<StrategyUiEntry> uiEntries = EnumerateStrategyEntries().ToList();
                    LogStrategyEntriesSnapshot("restore", uiEntries);
                    if (uiEntries.Count == 0)
                    {
                        localDetails = "Strategy grid entries could not be located.";
                        return false;
                    }

                    int enabledCount = 0;
                    List<string> outcome = new List<string>();

                    foreach (StrategyRecoveryEntry pending in strategiesPendingRecovery)
                    {
                        try
                        {
                            StrategyUiEntry match = uiEntries.FirstOrDefault(entry => string.Equals(entry.Key, pending.Key, StringComparison.OrdinalIgnoreCase));
                            if (match == null && !string.IsNullOrWhiteSpace(pending.StrategyName))
                            {
                                match = uiEntries.FirstOrDefault(entry =>
                                    string.Equals(entry.StrategyName, pending.StrategyName, StringComparison.OrdinalIgnoreCase)
                                    || (!string.IsNullOrWhiteSpace(entry.DisplayName) && entry.DisplayName.IndexOf(pending.StrategyName, StringComparison.OrdinalIgnoreCase) >= 0)
                                    || (!string.IsNullOrWhiteSpace(entry.Key) && entry.Key.IndexOf(pending.StrategyName, StringComparison.OrdinalIgnoreCase) >= 0));
                            }
                            if (match == null)
                            {
                                outcome.Add($"{pending.DisplayName ?? pending.Key}(missing)");
                                continue;
                            }

                            if (match.IsEnabled == true)
                            {
                                outcome.Add($"{match.DisplayName ?? match.Key}(already enabled)");
                                continue;
                            }

                            if (EnableStrategyEntry(match, out string enableDetails))
                            {
                                enabledCount++;
                                outcome.Add($"{match.DisplayName ?? match.Key}(enabled:{enableDetails})");
                            }
                            else
                            {
                                outcome.Add($"{match.DisplayName ?? match.Key}(failed:{enableDetails})");
                            }
                        }
                        catch (Exception entryEx)
                        {
                            outcome.Add($"{pending.DisplayName ?? pending.Key}(error:{entryEx.Message})");
                        }
                    }

                    localDetails = string.Join("; ", outcome);
                    return enabledCount > 0;
                });
                details = localDetails;
                return restored;
            }
            catch (Exception ex)
            {
                details = ex.Message;
                return false;
            }
        }

        private List<StrategyUiEntry> GetStrategyEntriesSnapshot()
        {
            System.Windows.Threading.Dispatcher dispatcher = GetStrategyUiDispatcher();
            if (dispatcher == null)
                return new List<StrategyUiEntry>();

            return dispatcher.Invoke(() =>
            {
                return EnumerateStrategyEntries().ToList();
            });
        }

        private System.Windows.Threading.Dispatcher GetStrategyUiDispatcher()
        {
            if (strategiesGridRoot is System.Windows.Threading.DispatcherObject dispatcherObject
                && dispatcherObject.Dispatcher != null)
                return dispatcherObject.Dispatcher;

            if (connectionsMenuItem != null && connectionsMenuItem.Dispatcher != null)
                return connectionsMenuItem.Dispatcher;

            if (Application.Current != null)
                return Application.Current.Dispatcher;

            return null;
        }



        private void LogStrategyEntriesSnapshot(string stage, List<StrategyUiEntry> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                LogStrategiesGridDiagnostics(stage);
                LogToOutput($"⚠️ Strategy snapshot ({stage}) found no strategy entries.");
                return;
            }

            foreach (StrategyUiEntry entry in entries)
            {
                if (entry == null)
                    continue;

                LogToOutput(string.Format(
                    CultureInfo.InvariantCulture,
                    "🧭 Strategy snapshot ({0}): Key={1} | Name={2} | Enabled={3}",
                    stage,
                    entry.Key ?? string.Empty,
                    entry.DisplayName ?? entry.StrategyName ?? string.Empty,
                    entry.IsEnabled.HasValue ? entry.IsEnabled.Value.ToString() : "unknown"));
            }
        }

        private List<StrategyRecoveryEntry> GetHeartbeatRecoveryEntries()
        {
            List<StrategyRecoveryEntry> entries = new List<StrategyRecoveryEntry>();
            DateTime now = DateTime.Now;
            double maxAgeSeconds = Math.Max(heartbeatTimeoutSeconds * 3.0, 30.0);

            foreach (KeyValuePair<string, DateTime> pair in strategyHeartbeats)
            {
                string strategyName = pair.Key;
                if (string.IsNullOrWhiteSpace(strategyName))
                    continue;

                double ageSeconds = (now - pair.Value).TotalSeconds;
                if (ageSeconds < 0)
                    ageSeconds = 0;

                if (ageSeconds > maxAgeSeconds)
                    continue;

                entries.Add(new StrategyRecoveryEntry
                {
                    Key = strategyName,
                    DisplayName = strategyName,
                    StrategyName = strategyName
                });
            }

            return entries;
        }

        private void LogStrategiesGridDiagnostics(string stage)
        {
            if (strategiesGridRoot == null)
            {
                LogToOutput($"⚠️ Strategy grid diagnostics ({stage}): strategiesGridRoot=null");
                return;
            }

            try
            {
                string rootType = strategiesGridRoot.GetType().FullName ?? "unknown";
                object dataContext = GetPropertyValue(strategiesGridRoot, "DataContext");
                object itemsSource = GetPropertyValue(strategiesGridRoot, "ItemsSource");
                object items = GetPropertyValue(strategiesGridRoot, "Items");
                object gridProperties = GetFieldValue(strategiesGridRoot, "properties");

                LogToOutput($"🧪 Strategy grid diagnostics ({stage}): RootType={rootType}");
                LogToOutput($"🧪 Strategy grid diagnostics ({stage}): DataContextType={(dataContext == null ? "null" : dataContext.GetType().FullName)}");
                LogToOutput($"🧪 Strategy grid diagnostics ({stage}): ItemsSourceType={(itemsSource == null ? "null" : itemsSource.GetType().FullName)}");
                LogToOutput($"🧪 Strategy grid diagnostics ({stage}): ItemsType={(items == null ? "null" : items.GetType().FullName)}");
                LogToOutput($"🧪 Strategy grid diagnostics ({stage}): GridPropertiesType={(gridProperties == null ? "null" : gridProperties.GetType().FullName)}");

                LogEnumerableSample(stage, "ItemsSource", itemsSource);
                LogEnumerableSample(stage, "Items", items);
                LogPropertyCandidates(stage, "GridDataContext", dataContext);
                LogPropertyCandidates(stage, "GridProperties", gridProperties);
                LogEnumerableSample(stage, "GridProperties.Strategies", GetPropertyValue(gridProperties, "Strategies"));
                LogEnumerableSample(stage, "GridProperties.AvailableStrategies", GetPropertyValue(gridProperties, "AvailableStrategies"));
                LogEnumerableSample(stage, "GridProperties.CachedStrategies", GetPropertyValue(gridProperties, "CachedStrategies"));
                LogPrivateMemberCandidates(stage, gridProperties);
                LogPrivateMemberCandidates(stage, strategiesGridRoot);
            }
            catch (Exception ex)
            {
                LogToOutput($"⚠️ Strategy grid diagnostics ({stage}) failed: {ex.Message}");
            }
        }

        private void LogEnumerableSample(string stage, string label, object enumerableCandidate)
        {
            IEnumerable enumerable = enumerableCandidate as IEnumerable;
            if (enumerable == null || enumerableCandidate is string)
                return;

            int count = 0;
            foreach (object item in enumerable)
            {
                if (item == null)
                {
                    LogToOutput($"🧪 Strategy grid diagnostics ({stage}): {label}[{count}]=null");
                }
                else
                {
                    LogToOutput($"🧪 Strategy grid diagnostics ({stage}): {label}[{count}]Type={item.GetType().FullName}");
                    LogPropertyCandidates(stage, label + "[" + count.ToString(CultureInfo.InvariantCulture) + "]", item);
                }

                count++;
                if (count >= 3)
                    break;
            }

            if (count == 0)
                LogToOutput($"🧪 Strategy grid diagnostics ({stage}): {label}=empty");
        }

        private void LogPropertyCandidates(string stage, string label, object candidate)
        {
            if (candidate == null)
                return;

            string[] propertyNames =
            {
                "Strategy", "Strategies", "AvailableStrategies", "ItemsSource", "Name", "DisplayName",
                "Enabled", "IsEnabled", "EnableStrategyCommand", "EnableDisableSingleStrategyCommand",
                "CommandEnableStrategy", "CommandParameter", "Account", "Instrument"
            };

            foreach (string propertyName in propertyNames)
            {
                object value = GetPropertyValue(candidate, propertyName);
                if (value == null)
                    continue;

                string valueType = value.GetType().FullName ?? "unknown";
                string valueText = value is string || value.GetType().IsPrimitive || value is bool
                    ? value.ToString()
                    : valueType;
                LogToOutput($"🧪 Strategy grid diagnostics ({stage}): {label}.{propertyName}={valueText}");
            }
        }

        private void LogPrivateMemberCandidates(string stage, object candidate)
        {
            if (candidate == null)
                return;

            Type type = candidate.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            int logged = 0;

            foreach (FieldInfo field in type.GetFields(flags))
            {
                if (!LooksLikeStrategyDataMember(field.Name, field.FieldType))
                    continue;

                object value;
                try
                {
                    value = field.GetValue(candidate);
                }
                catch
                {
                    continue;
                }

                LogCandidateMember(stage, "Field", field.Name, value);
                if (++logged >= 12)
                    break;
            }

            if (logged < 12)
            {
                foreach (PropertyInfo property in type.GetProperties(flags))
                {
                    if (!property.CanRead || property.GetIndexParameters().Length > 0)
                        continue;

                    if (!LooksLikeStrategyDataMember(property.Name, property.PropertyType))
                        continue;

                    object value;
                    try
                    {
                        value = property.GetValue(candidate, null);
                    }
                    catch
                    {
                        continue;
                    }

                    LogCandidateMember(stage, "Property", property.Name, value);
                    if (++logged >= 12)
                        break;
                }
            }
        }

        private bool LooksLikeStrategyDataMember(string memberName, Type memberType)
        {
            string name = memberName ?? string.Empty;
            string typeName = memberType != null ? memberType.FullName ?? string.Empty : string.Empty;

            return name.IndexOf("data", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("item", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("source", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("view", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("record", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("row", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("strategy", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("collection", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("enumerable", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("view", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("record", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("grid", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void LogCandidateMember(string stage, string memberKind, string memberName, object value)
        {
            string valueType = value == null ? "null" : value.GetType().FullName ?? "unknown";
            LogToOutput($"🧪 Strategy grid diagnostics ({stage}): {memberKind}.{memberName}={valueType}");

            if (value == null || value is string)
                return;

            IEnumerable enumerable = value as IEnumerable;
            if (enumerable != null)
            {
                int count = 0;
                foreach (object item in enumerable)
                {
                    if (item == null)
                    {
                        LogToOutput($"🧪 Strategy grid diagnostics ({stage}): {memberKind}.{memberName}[{count}]=null");
                    }
                    else
                    {
                        LogToOutput($"🧪 Strategy grid diagnostics ({stage}): {memberKind}.{memberName}[{count}]Type={item.GetType().FullName}");
                        LogPropertyCandidates(stage, memberKind + "." + memberName + "[" + count.ToString(CultureInfo.InvariantCulture) + "]", item);
                    }

                    if (++count >= 3)
                        break;
                }

                if (count == 0)
                    LogToOutput($"🧪 Strategy grid diagnostics ({stage}): {memberKind}.{memberName}=empty");
            }
            else
            {
                LogPropertyCandidates(stage, memberKind + "." + memberName, value);
            }
        }

        private bool EnableStrategyEntry(StrategyUiEntry entry, out string details)
        {
            details = "none";
            if (entry == null)
                return false;

            if (TrySetBooleanProperty(entry.SourceObject, "Enabled", true)
                || TrySetBooleanProperty(entry.SourceObject, "IsEnabled", true))
            {
                details = "set property";
                return true;
            }

            object commandParameter = entry.EnableParameter ?? entry.SourceObject;
            if (!ReferenceEquals(commandParameter, entry.SourceObject)
                && (TrySetBooleanProperty(commandParameter, "Enabled", true)
                    || TrySetBooleanProperty(commandParameter, "IsEnabled", true)))
            {
                details = "set parameter property";
                return true;
            }

            details = "no enable path";
            return false;
        }

        private IEnumerable<object> EnumerateStrategyCommandSources(StrategyUiEntry entry)
        {
            if (entry == null)
                yield break;

            HashSet<int> yielded = new HashSet<int>();
            foreach (object candidate in new[]
            {
                entry.PreferredCommandSource,
                entry.SourceObject,
                GetFieldValue(strategiesGridRoot, "properties"),
                strategiesGridRoot,
                GetPropertyValue(controlCenterWindow, "DataContext"),
                controlCenterWindow
            })
            {
                if (candidate == null)
                    continue;

                int id = RuntimeHelpers.GetHashCode(candidate);
                if (!yielded.Add(id))
                    continue;

                yield return candidate;
            }
        }

        private IEnumerable<StrategyUiEntry> EnumerateStrategyEntries()
        {
            List<StrategyUiEntry> results = new List<StrategyUiEntry>();
            HashSet<int> visited = new HashSet<int>();

            if (strategiesGridRoot != null)
            {
                object gridProperties = GetFieldValue(strategiesGridRoot, "properties");
                if (strategiesGridRoot is DependencyObject strategyElement)
                    CollectStrategyEntriesFromElement(strategyElement, results, visited);
                CollectStrategyEntriesFromObject(strategiesGridRoot, results, visited, 0);
                CollectStrategyEntriesFromObject(GetPropertyValue(strategiesGridRoot, "DataContext"), results, visited, 0);
                CollectStrategyEntriesFromObject(GetPropertyValue(strategiesGridRoot, "Strategies"), results, visited, 0);
                CollectStrategyEntriesFromObject(GetPropertyValue(strategiesGridRoot, "AvailableStrategies"), results, visited, 0);
                CollectStrategyEntriesFromObject(gridProperties, results, visited, 0);
                CollectStrategyEntriesFromObject(GetPropertyValue(gridProperties, "Strategies"), results, visited, 0);
                CollectStrategyEntriesFromObject(GetPropertyValue(gridProperties, "AvailableStrategies"), results, visited, 0);
                CollectStrategyEntriesFromObject(GetPropertyValue(gridProperties, "CachedStrategies"), results, visited, 0);
            }

            if (controlCenterWindow != null)
            {
                CollectStrategyEntriesFromElement(controlCenterWindow, results, visited);
                CollectStrategyEntriesFromObject(GetPropertyValue(controlCenterWindow, "DataContext"), results, visited, 0);
            }

            foreach (Window window in Application.Current.Windows)
            {
                if (window == null)
                    continue;

                CollectStrategyEntriesFromElement(window, results, visited);
                CollectStrategyEntriesFromObject(GetPropertyValue(window, "DataContext"), results, visited, 0);
            }

            Dictionary<string, StrategyUiEntry> deduped = new Dictionary<string, StrategyUiEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (StrategyUiEntry entry in results)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Key))
                    continue;

                if (!deduped.ContainsKey(entry.Key))
                    deduped[entry.Key] = entry;
            }

            return deduped.Values;
        }

        private FrameworkElement FindDescendantByTypeName(DependencyObject root, string typeNameFragment)
        {
            if (root == null || string.IsNullOrWhiteSpace(typeNameFragment))
                return null;

            int childCount;
            try
            {
                childCount = VisualTreeHelper.GetChildrenCount(root);
            }
            catch
            {
                return null;
            }

            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                FrameworkElement element = child as FrameworkElement;
                if (element != null)
                {
                    string fullTypeName = element.GetType().FullName ?? string.Empty;
                    string shortTypeName = element.GetType().Name ?? string.Empty;
                    if (fullTypeName.IndexOf(typeNameFragment, StringComparison.OrdinalIgnoreCase) >= 0
                        || shortTypeName.IndexOf(typeNameFragment, StringComparison.OrdinalIgnoreCase) >= 0)
                        return element;
                }

                FrameworkElement nested = FindDescendantByTypeName(child, typeNameFragment);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        private void CollectStrategyEntriesFromElement(DependencyObject element, List<StrategyUiEntry> results, HashSet<int> visited)
        {
            if (element == null)
                return;

            CollectStrategyEntriesFromObject(element, results, visited, 0);

            int childCount;
            try
            {
                childCount = VisualTreeHelper.GetChildrenCount(element);
            }
            catch
            {
                return;
            }

            for (int i = 0; i < childCount; i++)
                CollectStrategyEntriesFromElement(VisualTreeHelper.GetChild(element, i), results, visited);
        }

        private void CollectStrategyEntriesFromObject(object candidate, List<StrategyUiEntry> results, HashSet<int> visited, int depth)
        {
            if (candidate == null || depth > 4 || IsSimpleType(candidate.GetType()))
                return;

            int objectId = RuntimeHelpers.GetHashCode(candidate);
            if (!visited.Add(objectId))
                return;

            StrategyUiEntry entry = TryBuildStrategyUiEntry(candidate);
            if (entry != null)
                results.Add(entry);

            if (candidate is IEnumerable enumerable && !(candidate is string))
            {
                int count = 0;
                foreach (object item in enumerable)
                {
                    CollectStrategyEntriesFromObject(item, results, visited, depth + 1);
                    if (++count >= 200)
                        break;
                }

                return;
            }

            foreach (PropertyInfo property in candidate.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                    continue;

                string propertyName = property.Name ?? string.Empty;
                if (!ShouldInspectProperty(propertyName))
                    continue;

                object value;
                try
                {
                    value = property.GetValue(candidate, null);
                }
                catch
                {
                    continue;
                }

                CollectStrategyEntriesFromObject(value, results, visited, depth + 1);
            }

            if (depth <= 2)
                CollectStrategyEntriesFromPrivateMembers(candidate, results, visited, depth);
        }

        private void CollectStrategyEntriesFromPrivateMembers(object candidate, List<StrategyUiEntry> results, HashSet<int> visited, int depth)
        {
            if (candidate == null)
                return;

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (FieldInfo field in candidate.GetType().GetFields(flags))
            {
                if (!ShouldInspectMember(field.Name, field.FieldType))
                    continue;

                object value;
                try
                {
                    value = field.GetValue(candidate);
                }
                catch
                {
                    continue;
                }

                CollectStrategyEntriesFromObject(value, results, visited, depth + 1);
            }

            foreach (PropertyInfo property in candidate.GetType().GetProperties(flags))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                    continue;

                if (!ShouldInspectMember(property.Name, property.PropertyType))
                    continue;

                object value;
                try
                {
                    value = property.GetValue(candidate, null);
                }
                catch
                {
                    continue;
                }

                CollectStrategyEntriesFromObject(value, results, visited, depth + 1);
            }
        }

        private StrategyUiEntry TryBuildStrategyUiEntry(object candidate)
        {
            if (candidate == null)
                return null;

            object strategyObject = GetPropertyValue(candidate, "Strategy") ?? candidate;
            bool? isEnabled = GetNullableBoolean(strategyObject, "Enabled")
                ?? GetNullableBoolean(strategyObject, "IsEnabled")
                ?? GetNullableBoolean(candidate, "Enabled")
                ?? GetNullableBoolean(candidate, "IsEnabled");

            string displayName = BuildStrategyDisplayName(candidate, strategyObject);
            string strategyName = FirstNonEmpty(
                GetStringValue(strategyObject, "DisplayName"),
                GetStringValue(strategyObject, "Name"),
                GetStringValue(candidate, "DisplayName"),
                GetStringValue(candidate, "Name"),
                GetStringValue(candidate, "StrategyName"));
            string key = BuildStrategyKey(candidate, strategyObject, displayName);

            object enableCommand = GetPropertyValue(candidate, "EnableStrategyCommand")
                ?? GetPropertyValue(candidate, "EnableDisableSingleStrategyCommand")
                ?? GetPropertyValue(candidate, "CommandEnableStrategy")
                ?? GetPropertyValue(strategyObject, "EnableStrategyCommand");

            object enableParameter = GetPropertyValue(candidate, "EnableParameter")
                ?? GetPropertyValue(candidate, "CommandParameter")
                ?? GetPropertyValue(candidate, "Strategy")
                ?? candidate;

            bool looksLikeStrategy = candidate.GetType().Name.IndexOf("Strategy", StringComparison.OrdinalIgnoreCase) >= 0
                || strategyObject.GetType().Name.IndexOf("Strategy", StringComparison.OrdinalIgnoreCase) >= 0
                || !string.IsNullOrWhiteSpace(displayName);

            if (!looksLikeStrategy || isEnabled == null || string.IsNullOrWhiteSpace(key))
                return null;

            return new StrategyUiEntry
            {
                Key = key,
                DisplayName = displayName,
                StrategyName = strategyName,
                IsEnabled = isEnabled,
                EnableCommand = enableCommand,
                EnableParameter = enableParameter,
                PreferredCommandSource = GetFieldValue(strategiesGridRoot, "properties") ?? strategiesGridRoot,
                SourceObject = candidate
            };
        }

        private string BuildStrategyDisplayName(object candidate, object strategyObject)
        {
            string name = FirstNonEmpty(
                GetStringValue(strategyObject, "DisplayName"),
                GetStringValue(strategyObject, "Name"),
                GetStringValue(candidate, "DisplayName"),
                GetStringValue(candidate, "Name"),
                GetStringValue(candidate, "StrategyName"));

            string account = GetNestedDisplayString(candidate, strategyObject, "Account", "AccountName", "DisplayAccountName");
            string instrument = GetNestedDisplayString(candidate, strategyObject, "Instrument", "InstrumentName", "DisplayInstrumentName");

            List<string> parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(name))
                parts.Add(name);
            if (!string.IsNullOrWhiteSpace(account))
                parts.Add(account);
            if (!string.IsNullOrWhiteSpace(instrument))
                parts.Add(instrument);

            return parts.Count == 0 ? string.Empty : string.Join(" | ", parts);
        }

        private string BuildStrategyKey(object candidate, object strategyObject, string displayName)
        {
            string strategyId = FirstNonEmpty(
                GetStringValue(strategyObject, "Id"),
                GetStringValue(strategyObject, "StrategyId"),
                GetStringValue(candidate, "Id"),
                GetStringValue(candidate, "StrategyId"));

            if (!string.IsNullOrWhiteSpace(strategyId))
                return strategyId;

            string name = FirstNonEmpty(
                GetStringValue(strategyObject, "DisplayName"),
                GetStringValue(strategyObject, "Name"),
                GetStringValue(candidate, "DisplayName"),
                GetStringValue(candidate, "Name"),
                GetStringValue(candidate, "StrategyName"));
            string account = GetNestedDisplayString(candidate, strategyObject, "Account", "AccountName", "DisplayAccountName");
            string instrument = GetNestedDisplayString(candidate, strategyObject, "Instrument", "InstrumentName", "DisplayInstrumentName");

            string composite = string.Join("|", new[] { name, account, instrument }.Where(part => !string.IsNullOrWhiteSpace(part)));
            return !string.IsNullOrWhiteSpace(composite) ? composite : displayName;
        }

        private string GetNestedDisplayString(object first, object second, params string[] names)
        {
            foreach (string name in names)
            {
                string direct = FirstNonEmpty(GetStringValue(first, name), GetStringValue(second, name));
                if (!string.IsNullOrWhiteSpace(direct))
                    return direct;
            }

            object nested = GetPropertyValue(first, "Account")
                ?? GetPropertyValue(second, "Account")
                ?? GetPropertyValue(first, "Instrument")
                ?? GetPropertyValue(second, "Instrument");

            if (nested == null)
                return string.Empty;

            return FirstNonEmpty(
                GetStringValue(nested, "DisplayName"),
                GetStringValue(nested, "Name"),
                nested.ToString());
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return string.Empty;
        }

        private static bool ShouldInspectProperty(string propertyName)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
                return false;

            string lower = propertyName.ToLowerInvariant();
            return lower.Contains("strategy")
                || lower.Contains("propert")
                || lower.Contains("data")
                || lower.Contains("context")
                || lower.Contains("item")
                || lower.Contains("entry")
                || lower.Contains("row")
                || lower.Contains("view")
                || lower.Contains("content")
                || lower.Contains("child")
                || lower.Contains("selected")
                || lower.Contains("tab");
        }

        private static bool ShouldInspectMember(string memberName, Type memberType)
        {
            if (string.IsNullOrWhiteSpace(memberName) && memberType == null)
                return false;

            string lower = (memberName ?? string.Empty).ToLowerInvariant();
            string typeName = memberType != null ? (memberType.FullName ?? string.Empty).ToLowerInvariant() : string.Empty;

            return lower.Contains("strategy")
                || lower.Contains("propert")
                || lower.Contains("data")
                || lower.Contains("context")
                || lower.Contains("item")
                || lower.Contains("entry")
                || lower.Contains("row")
                || lower.Contains("record")
                || lower.Contains("source")
                || lower.Contains("view")
                || lower.Contains("grid")
                || typeName.Contains("strategy")
                || typeName.Contains("collection")
                || typeName.Contains("enumerable")
                || typeName.Contains("record")
                || typeName.Contains("view")
                || typeName.Contains("grid");
        }

        private static bool IsSimpleType(Type type)
        {
            return type.IsPrimitive
                || type.IsEnum
                || type == typeof(string)
                || type == typeof(decimal)
                || type == typeof(DateTime)
                || type == typeof(TimeSpan);
        }

        private static object GetPropertyValue(object target, string propertyName)
        {
            if (target == null || string.IsNullOrWhiteSpace(propertyName))
                return null;

            PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null || !property.CanRead || property.GetIndexParameters().Length > 0)
                return null;

            try
            {
                return property.GetValue(target, null);
            }
            catch
            {
                return null;
            }
        }

        private static object GetFieldValue(object target, string fieldName)
        {
            if (target == null || string.IsNullOrWhiteSpace(fieldName))
                return null;

            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
                return null;

            try
            {
                return field.GetValue(target);
            }
            catch
            {
                return null;
            }
        }

        private static string GetStringValue(object target, string propertyName)
        {
            object value = GetPropertyValue(target, propertyName);
            return value == null ? string.Empty : value.ToString();
        }

        private static bool? GetNullableBoolean(object target, string propertyName)
        {
            object value = GetPropertyValue(target, propertyName);
            if (value is bool booleanValue)
                return booleanValue;

            if (value != null && bool.TryParse(value.ToString(), out bool parsed))
                return parsed;

            return null;
        }

        private static bool TrySetBooleanProperty(object target, string propertyName, bool value)
        {
            if (target == null || string.IsNullOrWhiteSpace(propertyName))
                return false;

            PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null || !property.CanWrite || property.PropertyType != typeof(bool))
                return false;

            try
            {
                property.SetValue(target, value, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TrySetReferenceProperty(object target, string propertyName, object value)
        {
            if (target == null || string.IsNullOrWhiteSpace(propertyName))
                return false;

            PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null || !property.CanWrite || property.GetIndexParameters().Length > 0)
                return false;

            try
            {
                if (value == null)
                {
                    if (property.PropertyType.IsValueType && Nullable.GetUnderlyingType(property.PropertyType) == null)
                        return false;

                    property.SetValue(target, null, null);
                    return true;
                }

                if (!property.PropertyType.IsInstanceOfType(value))
                    return false;

                property.SetValue(target, value, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryExecuteCommand(object commandObject, object parameter)
        {
            if (commandObject is ICommand command)
            {
                if (!command.CanExecute(parameter))
                    return false;

                command.Execute(parameter);
                return true;
            }

            return TryInvokeMethod(commandObject, "Execute", parameter) || TryInvokeMethod(commandObject, "Execute", null);
        }

        private static bool TryInvokeMethod(object target, string methodName, object argument)
        {
            if (target == null || string.IsNullOrWhiteSpace(methodName))
                return false;

            MethodInfo[] methods = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => string.Equals(method.Name, methodName, StringComparison.Ordinal))
                .ToArray();

            foreach (MethodInfo method in methods)
            {
                ParameterInfo[] parameters = method.GetParameters();
                try
                {
                    if (parameters.Length == 0 && argument == null)
                    {
                        method.Invoke(target, null);
                        return true;
                    }

                    if (parameters.Length == 1)
                    {
                        object value = argument;
                        if (value == null && parameters[0].ParameterType.IsValueType && Nullable.GetUnderlyingType(parameters[0].ParameterType) == null)
                            continue;

                        if (value != null && !parameters[0].ParameterType.IsInstanceOfType(value))
                        {
                            if (parameters[0].ParameterType == typeof(bool) && value is bool)
                            {
                            }
                            else
                            {
                                continue;
                            }
                        }

                        method.Invoke(target, new[] { value });
                        return true;
                    }
                }
                catch
                {
                }
            }

            return false;
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
            if (string.IsNullOrWhiteSpace(message))
                return;

            if (debug)
            {
                try
                {
                    NinjaTrader.Code.Output.Process(message, PrintTo.OutputTab2);
                }
                catch
                {
                }
            }

            if (message.StartsWith("🚀 TradeMessenger", StringComparison.Ordinal)
                || message.StartsWith("🪟 OnWindowCreated", StringComparison.Ordinal)
                || message.StartsWith("🧭 EnsureMenuItem", StringComparison.Ordinal)
                || message.StartsWith("⚠️ EnsureMenuItem", StringComparison.Ordinal)
                || message.StartsWith("✅ EnsureMenuItem", StringComparison.Ordinal)
                || message.StartsWith("ℹ️ EnsureMenuItem", StringComparison.Ordinal)
                || message.StartsWith("⚠️ TradeMessenger license invalid", StringComparison.Ordinal))
            {
                try
                {
                    Log(message, LogLevel.Information);
                }
                catch
                {
                }
            }
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
            LogToOutput("🧭 EnsureMenuItem called.");
            if (!licenseValidated)
            {
                LogToOutput("⚠️ EnsureMenuItem aborted: license invalid.");
                return;
            }

            if (launcherMenuItem != null)
            {
                LogToOutput("ℹ️ EnsureMenuItem skipped: launcherMenuItem already exists.");
                return;
            }

            if (newMenuItem == null)
                newMenuItem = FindTopLevelMenuItem(controlCenter, "newMenuItem", "New");
            LogToOutput(newMenuItem == null ? "⚠️ EnsureMenuItem: New menu not found." : "✅ EnsureMenuItem: New menu found.");
            if (newMenuItem == null)
                return;

            if (connectionsMenuItem == null)
                connectionsMenuItem = FindTopLevelMenuItem(controlCenter, "connectionsMenuItem", "Connections");
            if (connectionsMenuItem == null)
                connectionsMenuItem = FindTopLevelMenuItem(controlCenter, "mnuConnections", "Connections");

            if (strategiesGridRoot == null)
            {
                strategiesGridRoot = FindNamedDescendant<FrameworkElement>(controlCenter, "grdStrategies")
                    ?? (object)FindNamedDescendant<FrameworkElement>(controlCenter, "gridStrategies")
                    ?? FindDescendantByTypeName(controlCenter, "StrategiesGrid");
                if (strategiesGridRoot != null)
                    LogToOutput($"🧩 Cached strategies grid root: {strategiesGridRoot.GetType().FullName}");
            }

            if (autoEdgeMenuItem == null)
            {
                autoEdgeMenuItem = FindChildMenuItem(newMenuItem, "AutoEdge");
                if (autoEdgeMenuItem == null)
                {
                    LogToOutput("ℹ️ EnsureMenuItem: AutoEdge submenu not found, creating it.");
                    autoEdgeMenuItem = new NTMenuItem
                    {
                        Header = "AutoEdge",
                        Style = Application.Current.TryFindResource("MainMenuItem") as Style
                    };
                    newMenuItem.Items.Add(autoEdgeMenuItem);
                }
                else
                {
                    LogToOutput("✅ EnsureMenuItem: AutoEdge submenu found.");
                }
            }

            launcherMenuItem = new NTMenuItem
            {
                Header = "Trade Messenger",
                Style = Application.Current.TryFindResource("MainMenuItem") as Style
            };
            launcherMenuItem.Click += OnLauncherMenuItemClick;
            autoEdgeMenuItem.Items.Add(launcherMenuItem);
            LogToOutput("✅ EnsureMenuItem: Trade Messenger menu item added under New -> AutoEdge.");
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

        private NTMenuItem FindMenuItemRecursive(ItemsControl parent, string expectedHeader)
        {
            if (parent == null || string.IsNullOrWhiteSpace(expectedHeader))
                return null;

            NTMenuItem partialMatch = null;
            foreach (object item in parent.Items)
            {
                NTMenuItem menuItem = item as NTMenuItem;
                if (menuItem == null)
                    continue;

                string header = GetHeaderText(menuItem.Header);
                if (string.Equals(header, expectedHeader, StringComparison.OrdinalIgnoreCase)
                    || NormalizeMenuHeader(header) == NormalizeMenuHeader(expectedHeader))
                    return menuItem;

                NTMenuItem nested = FindMenuItemRecursive(menuItem, expectedHeader);
                if (nested != null)
                    return nested;

                if (partialMatch == null && IsSafePartialMenuMatch(header, expectedHeader))
                    partialMatch = menuItem;
            }

            return partialMatch;
        }

        private NTMenuItem FindExactMenuItemRecursive(ItemsControl parent, string expectedHeader)
        {
            if (parent == null || string.IsNullOrWhiteSpace(expectedHeader))
                return null;

            string normalizedExpected = NormalizeMenuHeader(expectedHeader);
            foreach (object item in parent.Items)
            {
                NTMenuItem menuItem = item as NTMenuItem;
                if (menuItem == null)
                    continue;

                string header = GetHeaderText(menuItem.Header);
                if (string.Equals(header, expectedHeader, StringComparison.OrdinalIgnoreCase)
                    || NormalizeMenuHeader(header) == normalizedExpected)
                    return menuItem;

                NTMenuItem nested = FindExactMenuItemRecursive(menuItem, expectedHeader);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        private void CollectMenuHeaders(ItemsControl parent, List<string> headers, int depth)
        {
            if (parent == null || headers == null || depth > 2)
                return;

            foreach (object item in parent.Items)
            {
                NTMenuItem menuItem = item as NTMenuItem;
                if (menuItem == null)
                    continue;

                string header = GetHeaderText(menuItem.Header);
                if (!string.IsNullOrWhiteSpace(header))
                    headers.Add(header);

                CollectMenuHeaders(menuItem, headers, depth + 1);
            }
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

        private string NormalizeMenuHeader(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            StringBuilder normalized = new StringBuilder(value.Length);
            foreach (char ch in value)
            {
                if (char.IsLetterOrDigit(ch))
                    normalized.Append(char.ToLowerInvariant(ch));
            }

            return normalized.ToString();
        }

        private bool IsSafePartialMenuMatch(string actualHeader, string expectedHeader)
        {
            if (string.IsNullOrWhiteSpace(actualHeader) || string.IsNullOrWhiteSpace(expectedHeader))
                return false;

            string normalizedActual = NormalizeMenuHeader(actualHeader);
            string normalizedExpected = NormalizeMenuHeader(expectedHeader);
            if (normalizedActual.Length == 0 || normalizedExpected.Length == 0)
                return false;

            if (!normalizedActual.StartsWith(normalizedExpected, StringComparison.OrdinalIgnoreCase))
                return false;

            return normalizedActual.Length <= normalizedExpected.Length + 2;
        }

        private void OnLauncherMenuItemClick(object sender, RoutedEventArgs e)
        {
            if (!licenseValidated)
            {
                ShowLicenseFailureMessage();
                return;
            }

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

        private void ShowLicenseFailureMessage()
        {
            if (licenseFailureShown)
                return;

            licenseFailureShown = true;
            string message = string.IsNullOrWhiteSpace(licenseFailureMessage)
                ? "Trade Messenger is not licensed on this machine. Monitoring, messaging, and the UI are disabled."
                : "Trade Messenger license not found. Monitoring, messaging, and the UI are disabled.\n\n" + licenseFailureMessage;

            if (Application.Current == null || Application.Current.Dispatcher == null)
                return;

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                System.Windows.MessageBox.Show(
                    message,
                    "Trade Messenger License",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }));
        }
    }

    public sealed class TradeMessengerAddOnSettings
    {
        public bool Enabled { get; set; }
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

        private CheckBox enabledCheckBox;
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
        private Button simulateFeedStallButton;

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

            enabledCheckBox.IsChecked = settings.Enabled;
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
            autoReconnectCheckBox.IsChecked = false;
            reconnectInitialDelayTextBox.Text = settings.ReconnectInitialDelaySeconds.ToString(CultureInfo.InvariantCulture);
            reconnectMaxDelayTextBox.Text = settings.ReconnectMaxDelaySeconds.ToString(CultureInfo.InvariantCulture);
            reconnectMaxAttemptsTextBox.Text = settings.ReconnectMaxAttempts.ToString(CultureInfo.InvariantCulture);
            monitoredConnectionTextBox.Text = settings.MonitoredConnectionName ?? string.Empty;
            monitoredAccountTextBox.Text = settings.MonitoredAccountName ?? string.Empty;
            monitoredInstrumentsTextBox.Text = settings.MonitoredInstrumentsCsv ?? string.Empty;
            statusTextBlock.Text = statusText ?? string.Empty;
            if (simulateFeedStallButton != null && TradeMessengerAddOn.Instance != null)
                simulateFeedStallButton.Content = TradeMessengerAddOn.Instance.GetStatusSnapshot().Contains("Manual stall: on")
                    ? "Manual Stall: On"
                    : "Manual Stall: Off";
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

            simulateFeedStallButton = new Button { Content = "Manual Stall: Off", MinWidth = 150, Margin = new Thickness(0, 0, 8, 0) };
            simulateFeedStallButton.Click += OnSimulateFeedStallClick;
            buttons.Children.Add(BuildHiddenElement(simulateFeedStallButton));

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
                "General",
                out enabledCheckBox, "Enable Trade Messenger", "Master kill switch. When disabled, monitoring, reconnect, alerts, and recovery are all stopped."));
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
                out autoReconnectCheckBox, "Auto Reconnect", "Automatically retry the selected connection when a disconnect or feed stall is detected. This also controls recovery after reconnect.")));
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
                Enabled = enabledCheckBox.IsChecked == true,
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
                AutoReconnectEnabled = false,
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

        private void OnSimulateFeedStallClick(object sender, RoutedEventArgs e)
        {
            if (TradeMessengerAddOn.Instance == null)
                return;

            bool enabled = TradeMessengerAddOn.Instance.ToggleFeedStallSimulation();
            if (simulateFeedStallButton != null)
                simulateFeedStallButton.Content = enabled ? "Manual Stall: On" : "Manual Stall: Off";
            statusTextBlock.Text = (enabled ? "Manual feed stall enabled. " : "Manual feed stall disabled. ")
                + TradeMessengerAddOn.Instance.GetStatusSnapshot();
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
