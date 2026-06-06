#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using NinjaTrader.Cbi;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.AutoEdge
{
    internal sealed class ORBOibTestingProjectXOrderRouter
    {
        private sealed class ProjectXAccountInfo
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public bool CanTrade { get; set; }
            public bool IsVisible { get; set; }
        }

        private sealed class ProjectXContractInfo
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }
            public string SymbolId { get; set; }
            public bool ActiveContract { get; set; }
        }

        private readonly Func<string> getBaseUrl;
        private readonly Func<string> getUsername;
        private readonly Func<string> getApiKey;
        private readonly Func<string> getAccountSelectors;
        private readonly Func<string> getContractOverride;
        private readonly Func<bool> getTradeAllAccounts;
        private readonly Func<Instrument> getInstrument;
        private readonly Func<double> getTickSize;
        private readonly Func<MarketPosition> getMarketPosition;
        private readonly Func<int> getPositionQuantity;
        private readonly Action<string> logStatus;
        private readonly Action<string> logDebug;

        private readonly Dictionary<string, long> lastOrderIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private string sessionToken;
        private DateTime tokenAcquiredUtc = Core.Globals.MinDate;
        private List<ProjectXAccountInfo> accounts;
        private string resolvedContractId;
        private string resolvedInstrumentKey = string.Empty;
        private double lastSyncedStopPrice;
        private double lastSyncedTargetPrice;

        public ORBOibTestingProjectXOrderRouter(
            Func<string> getBaseUrl,
            Func<string> getUsername,
            Func<string> getApiKey,
            Func<string> getAccountSelectors,
            Func<string> getContractOverride,
            Func<bool> getTradeAllAccounts,
            Func<Instrument> getInstrument,
            Func<double> getTickSize,
            Func<MarketPosition> getMarketPosition,
            Func<int> getPositionQuantity,
            Action<string> logStatus,
            Action<string> logDebug)
        {
            this.getBaseUrl = getBaseUrl;
            this.getUsername = getUsername;
            this.getApiKey = getApiKey;
            this.getAccountSelectors = getAccountSelectors;
            this.getContractOverride = getContractOverride;
            this.getTradeAllAccounts = getTradeAllAccounts;
            this.getInstrument = getInstrument;
            this.getTickSize = getTickSize;
            this.getMarketPosition = getMarketPosition;
            this.getPositionQuantity = getPositionQuantity;
            this.logStatus = logStatus;
            this.logDebug = logDebug;
        }

        public void Reset()
        {
            sessionToken = null;
            tokenAcquiredUtc = Core.Globals.MinDate;
            accounts = null;
            resolvedContractId = null;
            resolvedInstrumentKey = string.Empty;
            lastOrderIds.Clear();
            ResetProtectiveSync();
        }

        public void ResetProtectiveSync()
        {
            lastSyncedStopPrice = 0.0;
            lastSyncedTargetPrice = 0.0;
        }

        public void RunStartupPreflight()
        {
            string instrumentKey = GetProjectXInstrumentKey();
            string selectors = (getTradeAllAccounts != null && getTradeAllAccounts())
                ? "<all>"
                : string.Join(", ", ParseAccountSelectors(GetString(getAccountSelectors)).ToArray());

            LogDebug(string.Format(
                CultureInfo.InvariantCulture,
                "startup preflight begin | instrument={0} selectors={1} baseUrl={2}",
                string.IsNullOrWhiteSpace(instrumentKey) ? "<unknown>" : instrumentKey,
                string.IsNullOrWhiteSpace(selectors) ? "<none>" : selectors,
                string.IsNullOrWhiteSpace(GetString(getBaseUrl)) ? "<empty>" : GetString(getBaseUrl)));

            if (!EnsureSession())
            {
                LogDebug("startup preflight failed | stage=auth");
                return;
            }

            List<ProjectXAccountInfo> loadedAccounts;
            if (!TryLoadAccounts(out loadedAccounts))
            {
                LogDebug("startup preflight failed | stage=accounts");
                return;
            }

            string contractId;
            if (!TryResolveContractId(out contractId))
            {
                LogDebug("startup preflight failed | stage=contract");
                return;
            }

            List<ProjectXAccountInfo> targetAccounts;
            string targetContractId;
            if (!TryGetTargets(out targetAccounts, out targetContractId))
            {
                LogDebug("startup preflight failed | stage=targets");
                return;
            }

            LogStatus(string.Format(
                CultureInfo.InvariantCulture,
                "webhook targets | count={0} contractId={1}",
                targetAccounts.Count,
                targetContractId ?? string.Empty));

            foreach (var account in targetAccounts)
            {
                LogStatus(string.Format(
                    CultureInfo.InvariantCulture,
                    "target account | id={0} name={1}",
                    account.Id,
                    account.Name ?? string.Empty));
            }

            LogDebug(string.Format(
                CultureInfo.InvariantCulture,
                "startup preflight ready | accounts={0} contractId={1}",
                FormatAccountsForLog(targetAccounts),
                targetContractId));
        }

        public bool Send(string eventType, double entryPrice, double takeProfit, double stopLoss, bool isMarketEntry, int quantity)
        {
            if (!EnsureSession())
            {
                LogDebug(string.Format(CultureInfo.InvariantCulture, "webhook skipped | event={0} reason=auth-unavailable", eventType));
                return false;
            }

            List<ProjectXAccountInfo> targetAccounts;
            string contractId;
            if (!TryGetTargets(out targetAccounts, out contractId))
            {
                LogDebug(string.Format(CultureInfo.InvariantCulture, "webhook skipped | event={0} reason=account-selection-or-contract-unavailable", eventType));
                return false;
            }

            LogDebug(string.Format(
                CultureInfo.InvariantCulture,
                "targets | event={0} accounts={1} contractId={2}",
                eventType,
                FormatAccountsForLog(targetAccounts),
                contractId));

            foreach (var account in targetAccounts)
            {
                try
                {
                    string action = (eventType ?? string.Empty).ToLowerInvariant();
                    if (action == "buy" || action == "sell")
                    {
                        if (PrepareForEntry(account.Id, contractId))
                            PlaceOrder(action, account.Id, contractId, entryPrice, takeProfit, stopLoss, isMarketEntry, quantity);
                    }
                    else if (action == "exit")
                    {
                        FlattenPosition(account.Id, contractId);
                    }
                    else if (action == "cancel")
                    {
                        CancelOrders(account.Id, contractId);
                        ResetProtectiveSync();
                    }
                }
                catch (Exception ex)
                {
                    LogDebug(string.Format(
                        CultureInfo.InvariantCulture,
                        "account error | event={0} accountId={1} accountName={2} error={3}",
                        eventType,
                        account.Id,
                        account.Name ?? string.Empty,
                        ex.Message));
                }
            }

            return targetAccounts.Count > 0;
        }

        public void SyncProtectiveOrder(Order order, double limitPrice, double stopPrice, OrderState orderState, Func<string, bool> isEntrySignalName)
        {
            if (order == null || GetCurrentMarketPosition() == MarketPosition.Flat)
                return;
            if (!IsProtectiveOrderActiveState(orderState))
                return;

            bool isStopOrder;
            if (!TryGetProtectiveOrderKind(order, isEntrySignalName, out isStopOrder))
                return;

            if (isStopOrder)
            {
                double actualStopPrice = stopPrice > 0.0 ? RoundToInstrumentTick(stopPrice) : RoundToInstrumentTick(order.StopPrice);
                if (actualStopPrice <= 0.0 || ArePricesEquivalent(lastSyncedStopPrice, actualStopPrice))
                    return;

                if (SyncProtectivePrice(actualStopPrice, true))
                    lastSyncedStopPrice = actualStopPrice;
            }
            else
            {
                double actualTargetPrice = limitPrice > 0.0 ? RoundToInstrumentTick(limitPrice) : RoundToInstrumentTick(order.LimitPrice);
                if (actualTargetPrice <= 0.0 || ArePricesEquivalent(lastSyncedTargetPrice, actualTargetPrice))
                    return;

                if (SyncProtectivePrice(actualTargetPrice, false))
                    lastSyncedTargetPrice = actualTargetPrice;
            }
        }

        public bool SyncProtectivePrice(double price, bool isStopOrder)
        {
            price = RoundToInstrumentTick(price);
            if (price <= 0.0)
                return false;
            if (!EnsureSession())
                return false;

            List<ProjectXAccountInfo> targetAccounts;
            string contractId;
            if (!TryGetTargets(out targetAccounts, out contractId))
                return false;

            bool modifiedAny = false;
            foreach (var account in targetAccounts)
            {
                try
                {
                    if (ModifyProtectiveOrders(account.Id, contractId, price, isStopOrder))
                        modifiedAny = true;
                }
                catch (Exception ex)
                {
                    LogDebug(string.Format(
                        CultureInfo.InvariantCulture,
                        "protective sync error | kind={0} accountId={1} contractId={2} price={3:0.########} error={4}",
                        isStopOrder ? "stop" : "target",
                        account.Id,
                        contractId,
                        price,
                        ex.Message));
                }
            }

            return modifiedAny;
        }

        private bool EnsureSession()
        {
            if (string.IsNullOrWhiteSpace(GetString(getBaseUrl)))
            {
                LogStatus("login failed | reason=empty-base-url");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(sessionToken) &&
                (DateTime.UtcNow - tokenAcquiredUtc).TotalHours < 23)
                return true;

            if (string.IsNullOrWhiteSpace(GetString(getUsername)) || string.IsNullOrWhiteSpace(GetString(getApiKey)))
            {
                LogStatus("login failed | reason=missing-credentials");
                return false;
            }

            string loginJson = string.Format(
                CultureInfo.InvariantCulture,
                "{{\"userName\":{0},\"apiKey\":{1}}}",
                ToJsonString(GetString(getUsername)),
                ToJsonString(GetString(getApiKey)));

            string response = Post("/api/Auth/loginKey", loginJson, false, true);
            if (string.IsNullOrWhiteSpace(response))
            {
                LogStatus("login failed | reason=empty-response");
                return false;
            }

            string token;
            if (!TryGetJsonString(response, "token", out token))
            {
                LogStatus("login failed | reason=missing-token");
                return false;
            }

            sessionToken = token;
            tokenAcquiredUtc = DateTime.UtcNow;
            accounts = null;
            resolvedContractId = null;
            resolvedInstrumentKey = string.Empty;
            lastOrderIds.Clear();
            ResetProtectiveSync();
            LogStatus("login succeeded");
            return true;
        }

        private bool TryGetTargets(out List<ProjectXAccountInfo> targetAccounts, out string contractId)
        {
            targetAccounts = null;
            contractId = null;

            if (!TryResolveContractId(out contractId))
                return false;

            List<ProjectXAccountInfo> loadedAccounts;
            if (!TryLoadAccounts(out loadedAccounts))
                return false;

            if (getTradeAllAccounts != null && getTradeAllAccounts())
            {
                targetAccounts = loadedAccounts.Where(a => a.CanTrade).ToList();
                if (targetAccounts.Count == 0)
                {
                    LogDebug("account selection failed | reason=no-tradable-accounts");
                    return false;
                }

                return true;
            }

            var selectors = ParseAccountSelectors(GetString(getAccountSelectors));
            if (selectors.Count == 0)
            {
                LogStatus("warning | no webhooks will be sent because ProjectX Accounts is empty.");
                LogDebug("account selection failed | reason=no-selection");
                return false;
            }

            var matchedAccounts = new List<ProjectXAccountInfo>();
            var matchedIds = new HashSet<int>();
            var unmatchedSelectors = new List<string>();

            foreach (string selector in selectors)
            {
                int accountId;
                List<ProjectXAccountInfo> matches = int.TryParse(selector, NumberStyles.Integer, CultureInfo.InvariantCulture, out accountId)
                    ? loadedAccounts.Where(a => a.CanTrade && a.Id == accountId).ToList()
                    : loadedAccounts.Where(a => a.CanTrade && string.Equals(a.Name ?? string.Empty, selector, StringComparison.OrdinalIgnoreCase)).ToList();

                if (matches.Count == 0)
                {
                    unmatchedSelectors.Add(selector);
                    continue;
                }

                foreach (var match in matches)
                {
                    if (matchedIds.Add(match.Id))
                        matchedAccounts.Add(match);
                }
            }

            if (unmatchedSelectors.Count > 0)
            {
                LogDebug(string.Format(
                    CultureInfo.InvariantCulture,
                    "account selection unmatched | selectors={0}",
                    string.Join(", ", unmatchedSelectors.ToArray())));
            }

            if (matchedAccounts.Count == 0)
            {
                LogDebug("account selection failed | reason=no-matching-tradable-accounts");
                return false;
            }

            targetAccounts = matchedAccounts;
            return true;
        }

        private bool TryLoadAccounts(out List<ProjectXAccountInfo> loadedAccounts)
        {
            if (accounts != null && accounts.Count > 0)
            {
                loadedAccounts = accounts;
                return true;
            }

            string response = Post("/api/Account/search", "{\"onlyActiveAccounts\":true}", true, true);
            if (string.IsNullOrWhiteSpace(response))
            {
                LogStatus("warning | no webhooks will be sent because no ProjectX accounts were found.");
                LogDebug("account load failed | reason=empty-response");
                loadedAccounts = null;
                return false;
            }

            loadedAccounts = ExtractAccounts(response).ToList();
            accounts = loadedAccounts.Count > 0 ? loadedAccounts : null;

            LogStatus(string.Format(CultureInfo.InvariantCulture, "accounts found | count={0}", loadedAccounts.Count));
            if (loadedAccounts.Count == 0)
            {
                LogStatus("warning | no webhooks will be sent because no ProjectX accounts were found.");
                return false;
            }

            foreach (var account in loadedAccounts)
            {
                LogStatus(string.Format(
                    CultureInfo.InvariantCulture,
                    "account | id={0} name={1} canTrade={2} isVisible={3}",
                    account.Id,
                    account.Name ?? string.Empty,
                    account.CanTrade,
                    account.IsVisible));
            }

            return true;
        }

        private bool TryResolveContractId(out string contractId)
        {
            contractId = null;

            string instrumentKey = GetProjectXInstrumentKey();
            string overrideContractId = GetString(getContractOverride).Trim();
            if (!string.IsNullOrWhiteSpace(overrideContractId))
            {
                contractId = overrideContractId;
                resolvedContractId = contractId;
                resolvedInstrumentKey = instrumentKey;
                LogDebug(string.Format(
                    CultureInfo.InvariantCulture,
                    "contract override | instrument={0} contractId={1}",
                    instrumentKey,
                    contractId));
                return true;
            }

            if (!string.IsNullOrWhiteSpace(resolvedContractId) &&
                string.Equals(resolvedInstrumentKey, instrumentKey, StringComparison.OrdinalIgnoreCase))
            {
                contractId = resolvedContractId;
                return true;
            }

            string root = GetProjectXInstrumentRoot();
            if (string.IsNullOrWhiteSpace(root))
            {
                LogDebug("contract resolve failed | reason=empty-instrument-root");
                return false;
            }

            DateTime expiry;
            string desiredSuffix = TryGetInstrumentExpiry(out expiry) || TryParseInstrumentExpiryFromFullName(out expiry)
                ? GetFuturesMonthCode(expiry.Month) + expiry.ToString("yy", CultureInfo.InvariantCulture)
                : string.Empty;

            List<ProjectXContractInfo> contracts;
            if (!TrySearchContracts(root, desiredSuffix, out contracts))
                return false;

            ProjectXContractInfo selected = SelectContract(root, desiredSuffix, contracts);
            if (selected == null || string.IsNullOrWhiteSpace(selected.Id))
            {
                LogDebug(string.Format(
                    CultureInfo.InvariantCulture,
                    "contract resolve failed | root={0} desiredSuffix={1} candidates={2}",
                    root,
                    string.IsNullOrWhiteSpace(desiredSuffix) ? "active" : desiredSuffix,
                    string.Join(", ", contracts.Select(c => c.Id ?? string.Empty).ToArray())));
                return false;
            }

            contractId = selected.Id;
            resolvedContractId = contractId;
            resolvedInstrumentKey = instrumentKey;
            LogDebug(string.Format(
                CultureInfo.InvariantCulture,
                "contract resolved | instrument={0} root={1} desiredSuffix={2} contractId={3} name={4} active={5}",
                instrumentKey,
                root,
                string.IsNullOrWhiteSpace(desiredSuffix) ? "active" : desiredSuffix,
                selected.Id,
                selected.Name ?? string.Empty,
                selected.ActiveContract));
            return true;
        }

        private bool TrySearchContracts(string root, string desiredSuffix, out List<ProjectXContractInfo> contracts)
        {
            contracts = null;

            string primarySearchText = !string.IsNullOrWhiteSpace(desiredSuffix) ? root + desiredSuffix : root;
            if (TrySearchContractsByText(primarySearchText, root, out contracts) && contracts.Count > 0)
                return true;

            if (!string.Equals(primarySearchText, root, StringComparison.OrdinalIgnoreCase) &&
                TrySearchContractsByText(root, root, out contracts) && contracts.Count > 0)
                return true;

            LogDebug(string.Format(
                CultureInfo.InvariantCulture,
                "contract search failed | root={0} desiredSuffix={1}",
                root,
                string.IsNullOrWhiteSpace(desiredSuffix) ? "active" : desiredSuffix));
            return false;
        }

        private bool TrySearchContractsByText(string searchText, string root, out List<ProjectXContractInfo> contracts)
        {
            if (TrySearchContractsByText(searchText, root, true, out contracts) && contracts.Count > 0)
                return true;

            if (TrySearchContractsByText(searchText, root, false, out contracts) && contracts.Count > 0)
                return true;

            contracts = new List<ProjectXContractInfo>();
            return false;
        }

        private bool TrySearchContractsByText(string searchText, string root, bool live, out List<ProjectXContractInfo> contracts)
        {
            string requestJson = string.Format(
                CultureInfo.InvariantCulture,
                "{{\"live\":{0},\"searchText\":{1}}}",
                live ? "true" : "false",
                ToJsonString(searchText));
            string response = Post("/api/Contract/search", requestJson, true, true);
            contracts = ExtractContracts(response)
                .Where(c => DoesContractMatchRoot(c, root))
                .ToList();

            LogDebug(string.Format(
                CultureInfo.InvariantCulture,
                "contract search | searchText={0} live={1} matches={2}",
                searchText,
                live,
                contracts.Count));
            return !string.IsNullOrWhiteSpace(response);
        }

        private ProjectXContractInfo SelectContract(string root, string desiredSuffix, List<ProjectXContractInfo> contracts)
        {
            if (contracts == null || contracts.Count == 0)
                return null;

            if (!string.IsNullOrWhiteSpace(desiredSuffix))
            {
                var exactMatches = contracts
                    .Where(c => !string.IsNullOrWhiteSpace(c.Id) &&
                        c.Id.EndsWith("." + desiredSuffix, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (exactMatches.Count > 0)
                    return exactMatches.FirstOrDefault(c => c.ActiveContract) ?? exactMatches[0];
            }

            var activeMatches = contracts.Where(c => c.ActiveContract).ToList();
            if (activeMatches.Count > 0)
                return activeMatches[0];

            return contracts[0];
        }

        private bool DoesContractMatchRoot(ProjectXContractInfo contract, string root)
        {
            if (contract == null || string.IsNullOrWhiteSpace(root))
                return false;

            string expectedSymbolId = "F.US." + root;
            if (!string.IsNullOrWhiteSpace(contract.SymbolId) &&
                string.Equals(contract.SymbolId, expectedSymbolId, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrWhiteSpace(contract.Id) &&
                contract.Id.IndexOf(".US." + root + ".", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (!string.IsNullOrWhiteSpace(contract.Name) &&
                contract.Name.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private string PlaceOrder(string side, int accountId, string contractId, double entryPrice, double takeProfit, double stopLoss, bool isMarketEntry, int quantity)
        {
            int orderSide = side.Equals("buy", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            int orderType = isMarketEntry ? 2 : 1;
            int normalizedQuantity = Math.Max(1, quantity);
            double entry = RoundToInstrumentTick(entryPrice);
            bool isLong = orderSide == 0;

            string limitPart = isMarketEntry || entry <= 0.0
                ? string.Empty
                : string.Format(CultureInfo.InvariantCulture, ",\"limitPrice\":{0}", FormatPrice(entry));

            string takeProfitPart = string.Empty;
            if (takeProfit > 0.0 && entry > 0.0)
            {
                int tpTicks = NormalizeBracketTicks(
                    PriceToTicks(takeProfit - entry),
                    4,
                    isLong ? 1 : -1);
                takeProfitPart = string.Format(
                    CultureInfo.InvariantCulture,
                    ",\"takeProfitBracket\":{{\"quantity\":{0},\"type\":1,\"ticks\":{1}}}",
                    normalizedQuantity,
                    tpTicks);
            }

            string stopLossPart = string.Empty;
            if (stopLoss > 0.0 && entry > 0.0)
            {
                int slTicks = NormalizeBracketTicks(
                    PriceToTicks(stopLoss - entry),
                    1,
                    isLong ? -1 : 1);
                stopLossPart = string.Format(
                    CultureInfo.InvariantCulture,
                    ",\"stopLossBracket\":{{\"quantity\":{0},\"type\":4,\"ticks\":{1}}}",
                    normalizedQuantity,
                    slTicks);
            }

            string json = string.Format(
                CultureInfo.InvariantCulture,
                "{{\"accountId\":{0},\"contractId\":{1},\"type\":{2},\"side\":{3},\"size\":{4}{5}{6}{7}}}",
                accountId,
                ToJsonString(contractId),
                orderType,
                orderSide,
                normalizedQuantity,
                limitPart,
                takeProfitPart,
                stopLossPart);

            string response = Post("/api/Order/place", json, true);
            long orderId;
            if (TryGetJsonLong(response, "orderId", out orderId))
                lastOrderIds[GetOrderKey(accountId, contractId)] = orderId;

            return response;
        }

        private int NormalizeBracketTicks(int rawTicks, int minAbsTicks, int zeroTickDirection)
        {
            int direction = rawTicks == 0 ? Math.Sign(zeroTickDirection) : Math.Sign(rawTicks);
            int absTicks = Math.Abs(rawTicks);
            if (absTicks < minAbsTicks)
                absTicks = minAbsTicks;
            return direction * absTicks;
        }

        private bool PrepareForEntry(int accountId, string contractId)
        {
            CancelOrders(accountId, contractId);
            if (!WaitForOrdersCleared(accountId, contractId, 4000))
            {
                LogDebug(string.Format(CultureInfo.InvariantCulture, "prepare failed | stage=cancel-clear accountId={0} contractId={1}", accountId, contractId));
                return false;
            }

            int positionSize;
            if (TryGetOpenPositionSize(accountId, contractId, out positionSize) && positionSize != 0)
            {
                ClosePosition(accountId, contractId);
                if (!WaitForFlat(accountId, contractId, 4000))
                {
                    LogDebug(string.Format(CultureInfo.InvariantCulture, "prepare failed | stage=flat accountId={0} contractId={1} positionSize={2}", accountId, contractId, positionSize));
                    return false;
                }

                CancelOrders(accountId, contractId);
                if (!WaitForOrdersCleared(accountId, contractId, 4000))
                {
                    LogDebug(string.Format(CultureInfo.InvariantCulture, "prepare failed | stage=post-close-cancel accountId={0} contractId={1}", accountId, contractId));
                    return false;
                }
            }

            ResetProtectiveSync();
            return true;
        }

        private void FlattenPosition(int accountId, string contractId)
        {
            CancelOrders(accountId, contractId);
            if (!WaitForOrdersCleared(accountId, contractId, 4000))
                LogDebug(string.Format(CultureInfo.InvariantCulture, "flatten warning | stage=cancel-clear accountId={0} contractId={1}", accountId, contractId));

            int positionSize;
            if (TryGetOpenPositionSize(accountId, contractId, out positionSize) && positionSize != 0)
            {
                ClosePosition(accountId, contractId);
                if (!WaitForFlat(accountId, contractId, 4000))
                    LogDebug(string.Format(CultureInfo.InvariantCulture, "flatten warning | stage=flat accountId={0} contractId={1} positionSize={2}", accountId, contractId, positionSize));
            }

            CancelOrders(accountId, contractId);
            if (!WaitForOrdersCleared(accountId, contractId, 4000))
                LogDebug(string.Format(CultureInfo.InvariantCulture, "flatten warning | stage=post-close-cancel accountId={0} contractId={1}", accountId, contractId));

            ResetProtectiveSync();
        }

        private string ClosePosition(int accountId, string contractId)
        {
            string json = string.Format(
                CultureInfo.InvariantCulture,
                "{{\"accountId\":{0},\"contractId\":{1}}}",
                accountId,
                ToJsonString(contractId));
            return Post("/api/Position/closeContract", json, true);
        }

        private void CancelOrders(int accountId, string contractId)
        {
            foreach (long orderId in GetOpenOrderIds(accountId, contractId))
            {
                string cancelJson = string.Format(
                    CultureInfo.InvariantCulture,
                    "{{\"accountId\":{0},\"orderId\":{1}}}",
                    accountId,
                    orderId);
                Post("/api/Order/cancel", cancelJson, true);
            }

            lastOrderIds.Remove(GetOrderKey(accountId, contractId));
        }

        private bool WaitForFlat(int accountId, string contractId, int timeoutMs)
        {
            DateTime deadlineUtc = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow <= deadlineUtc)
            {
                int positionSize;
                if (TryGetOpenPositionSize(accountId, contractId, out positionSize) && positionSize == 0)
                    return true;

                System.Threading.Thread.Sleep(150);
            }

            return false;
        }

        private bool WaitForOrdersCleared(int accountId, string contractId, int timeoutMs)
        {
            DateTime deadlineUtc = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow <= deadlineUtc)
            {
                if (GetOpenOrderIds(accountId, contractId).Count == 0)
                    return true;

                System.Threading.Thread.Sleep(150);
            }

            return false;
        }

        private bool ModifyProtectiveOrders(int accountId, string contractId, double price, bool isStopOrder)
        {
            int expectedSide = GetCurrentMarketPosition() == MarketPosition.Long ? 1 : 0;
            int expectedType = isStopOrder ? 4 : 1;
            int fallbackSize = Math.Max(1, Math.Abs(GetCurrentPositionQuantity()));
            bool modifiedAny = false;

            foreach (var order in GetOpenOrders(accountId, contractId))
            {
                int type;
                if (!TryGetOrderInt(order, "type", out type) || type != expectedType)
                    continue;

                int side;
                if (TryGetOrderInt(order, "side", out side) && side != expectedSide)
                    continue;

                long orderId;
                if (!TryGetOrderLong(order, "id", out orderId) || orderId <= 0)
                    continue;

                int size;
                if (!TryGetOrderInt(order, "size", out size) || size <= 0)
                    size = fallbackSize;

                double existingPrice;
                if (isStopOrder)
                {
                    if (TryGetOrderDouble(order, "stopPrice", out existingPrice) &&
                        ArePricesEquivalent(RoundToInstrumentTick(existingPrice), price))
                        continue;
                }
                else
                {
                    if (TryGetOrderDouble(order, "limitPrice", out existingPrice) &&
                        ArePricesEquivalent(RoundToInstrumentTick(existingPrice), price))
                        continue;
                }

                string response = ModifyOrder(accountId, orderId, size, isStopOrder ? (double?)null : price, isStopOrder ? (double?)price : null);
                bool success;
                if (TryGetJsonBool(response, "success", out success) && !success)
                {
                    LogDebug(string.Format(
                        CultureInfo.InvariantCulture,
                        "protective sync failed | kind={0} accountId={1} orderId={2} price={3:0.########}",
                        isStopOrder ? "stop" : "target",
                        accountId,
                        orderId,
                        price));
                    continue;
                }

                modifiedAny = true;
            }

            if (!modifiedAny)
            {
                LogDebug(string.Format(
                    CultureInfo.InvariantCulture,
                    "protective sync skipped | kind={0} accountId={1} contractId={2} price={3:0.########} reason=no-open-order",
                    isStopOrder ? "stop" : "target",
                    accountId,
                    contractId,
                    price));
            }

            return modifiedAny;
        }

        private string ModifyOrder(int accountId, long orderId, int size, double? limitPrice, double? stopPrice)
        {
            string json = string.Format(
                CultureInfo.InvariantCulture,
                "{{\"accountId\":{0},\"orderId\":{1},\"size\":{2},\"limitPrice\":{3},\"stopPrice\":{4},\"trailPrice\":null}}",
                accountId,
                orderId,
                Math.Max(1, size),
                limitPrice.HasValue ? FormatPrice(limitPrice.Value) : "null",
                stopPrice.HasValue ? FormatPrice(stopPrice.Value) : "null");
            return Post("/api/Order/modify", json, true);
        }

        private List<long> GetOpenOrderIds(int accountId, string contractId)
        {
            var orderIds = new List<long>();
            foreach (var order in GetOpenOrders(accountId, contractId))
            {
                long id;
                if (TryGetOrderLong(order, "id", out id) && id > 0)
                    orderIds.Add(id);
            }

            return orderIds;
        }

        private List<Dictionary<string, object>> GetOpenOrders(int accountId, string contractId)
        {
            var orders = new List<Dictionary<string, object>>();
            string searchJson = string.Format(CultureInfo.InvariantCulture, "{{\"accountId\":{0}}}", accountId);
            string searchResponse = Post("/api/Order/searchOpen", searchJson, true);
            bool success;
            if (TryGetJsonBool(searchResponse, "success", out success) && !success)
                return orders;

            foreach (var order in ExtractDictionaries(searchResponse, "orders"))
            {
                object contractObj;
                if (!order.TryGetValue("contractId", out contractObj))
                    continue;
                if (!string.Equals(contractObj != null ? contractObj.ToString() : string.Empty, contractId, StringComparison.OrdinalIgnoreCase))
                    continue;

                orders.Add(order);
            }

            return orders;
        }

        private bool TryGetOpenPositionSize(int accountId, string contractId, out int signedSize)
        {
            signedSize = 0;
            string searchJson = string.Format(CultureInfo.InvariantCulture, "{{\"accountId\":{0}}}", accountId);
            string searchResponse = Post("/api/Position/searchOpen", searchJson, true);
            bool success;
            if (TryGetJsonBool(searchResponse, "success", out success) && !success)
                return false;

            foreach (var position in ExtractDictionaries(searchResponse, "positions"))
            {
                object contractObj;
                if (!position.TryGetValue("contractId", out contractObj))
                    continue;
                if (!string.Equals(contractObj != null ? contractObj.ToString() : string.Empty, contractId, StringComparison.OrdinalIgnoreCase))
                    continue;

                int type;
                int size;
                if (!TryGetOrderInt(position, "type", out type))
                    continue;
                if (!TryGetOrderInt(position, "size", out size) || size <= 0)
                    continue;

                signedSize += type == 2 ? -size : size;
            }

            return true;
        }

        private string Post(string path, string json, bool requiresAuth)
        {
            return Post(path, json, requiresAuth, false);
        }

        private string Post(string path, string json, bool requiresAuth, bool alwaysLog)
        {
            string baseUrl = GetString(getBaseUrl).TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl))
                return null;

            if (alwaysLog)
                LogDebug(string.Format(CultureInfo.InvariantCulture, "request | url={0}{1} auth={2} payload={3}", baseUrl, path, requiresAuth, SanitizeJson(json)));
            else
                LogDebug(string.Format(CultureInfo.InvariantCulture, "request | url={0}{1} auth={2} payload={3}", baseUrl, path, requiresAuth, SanitizeJson(json)));

            try
            {
                using (var client = new System.Net.WebClient())
                {
                    client.Headers[System.Net.HttpRequestHeader.ContentType] = "application/json";
                    if (requiresAuth && !string.IsNullOrWhiteSpace(sessionToken))
                        client.Headers[System.Net.HttpRequestHeader.Authorization] = "Bearer " + sessionToken;

                    string response = client.UploadString(baseUrl + path, "POST", json);
                    LogDebug(string.Format(CultureInfo.InvariantCulture, "response | url={0}{1} body={2}", baseUrl, path, SanitizeJson(response)));
                    return response;
                }
            }
            catch (System.Net.WebException ex)
            {
                string errorBody = ReadWebExceptionResponse(ex);
                LogDebug(string.Format(CultureInfo.InvariantCulture, "request failed | url={0}{1} error={2} body={3}", baseUrl, path, ex.Message, SanitizeJson(errorBody)));
                return errorBody;
            }
            catch (Exception ex)
            {
                LogDebug(string.Format(CultureInfo.InvariantCulture, "request failed | url={0}{1} error={2}", baseUrl, path, ex.Message));
                return null;
            }
        }

        private string ReadWebExceptionResponse(System.Net.WebException ex)
        {
            if (ex == null || ex.Response == null)
                return null;

            try
            {
                using (var stream = ex.Response.GetResponseStream())
                {
                    if (stream == null)
                        return null;
                    using (var reader = new System.IO.StreamReader(stream))
                        return reader.ReadToEnd();
                }
            }
            catch
            {
                return null;
            }
        }

        private bool IsProtectiveOrderActiveState(OrderState orderState)
        {
            return orderState == OrderState.Submitted
                || orderState == OrderState.Accepted
                || orderState == OrderState.Working
                || orderState == OrderState.PartFilled;
        }

        private bool TryGetProtectiveOrderKind(Order order, Func<string, bool> isEntrySignalName, out bool isStopOrder)
        {
            isStopOrder = false;
            if (order == null)
                return false;

            string orderName = order.Name ?? string.Empty;
            string fromEntrySignal = order.FromEntrySignal ?? string.Empty;
            bool belongsToManagedEntry = isEntrySignalName != null && isEntrySignalName(fromEntrySignal);

            if (!belongsToManagedEntry
                && !orderName.Equals("Stop loss", StringComparison.OrdinalIgnoreCase)
                && !orderName.Equals("Profit target", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (order.OrderType == OrderType.StopMarket
                || order.OrderType == OrderType.StopLimit
                || orderName.Equals("Stop loss", StringComparison.OrdinalIgnoreCase))
            {
                isStopOrder = true;
                return true;
            }

            if (order.OrderType == OrderType.Limit
                || orderName.Equals("Profit target", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private double RoundToInstrumentTick(double price)
        {
            Instrument instrument = getInstrument != null ? getInstrument() : null;
            return instrument != null && instrument.MasterInstrument != null
                ? instrument.MasterInstrument.RoundToTickSize(price)
                : price;
        }

        private bool ArePricesEquivalent(double left, double right)
        {
            double tickSize = GetCurrentTickSize();
            if (left <= 0.0 || right <= 0.0 || tickSize <= 0.0)
                return false;

            return Math.Abs(left - right) <= tickSize * 0.5;
        }

        private int PriceToTicks(double priceDistance)
        {
            double tickSize = GetCurrentTickSize();
            if (tickSize <= 0.0)
                return 0;
            return (int)Math.Round(priceDistance / tickSize, MidpointRounding.AwayFromZero);
        }

        private string FormatPrice(double price)
        {
            return RoundToInstrumentTick(price).ToString("0.########", CultureInfo.InvariantCulture);
        }

        private string GetProjectXInstrumentKey()
        {
            Instrument instrument = getInstrument != null ? getInstrument() : null;
            if (instrument == null)
                return string.Empty;

            string fullName = instrument.FullName ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(fullName))
                return fullName.Trim().ToUpperInvariant();

            return GetProjectXInstrumentRoot();
        }

        private string GetProjectXInstrumentRoot()
        {
            Instrument instrument = getInstrument != null ? getInstrument() : null;
            return instrument != null && instrument.MasterInstrument != null
                ? (instrument.MasterInstrument.Name ?? string.Empty).Trim().ToUpperInvariant()
                : string.Empty;
        }

        private bool TryGetInstrumentExpiry(out DateTime expiry)
        {
            expiry = Core.Globals.MinDate;
            Instrument instrument = getInstrument != null ? getInstrument() : null;
            if (instrument == null)
                return false;

            try
            {
                PropertyInfo property = instrument.GetType().GetProperty("Expiry", BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (property == null)
                    return false;

                object raw = property.GetValue(instrument, null);
                if (!(raw is DateTime))
                    return false;

                DateTime dt = (DateTime)raw;
                if (dt.Year < 2000)
                    return false;

                expiry = dt;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryParseInstrumentExpiryFromFullName(out DateTime expiry)
        {
            expiry = Core.Globals.MinDate;
            Instrument instrument = getInstrument != null ? getInstrument() : null;
            string fullName = instrument != null ? (instrument.FullName ?? string.Empty).Trim().ToUpperInvariant() : string.Empty;
            if (string.IsNullOrWhiteSpace(fullName))
                return false;

            Match match = Regex.Match(fullName, @"\b(?<month>\d{1,2})[-/](?<year>\d{2,4})\b");
            if (!match.Success)
                return false;

            int month;
            int year;
            if (!int.TryParse(match.Groups["month"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out month) ||
                !int.TryParse(match.Groups["year"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out year))
                return false;

            if (year < 100)
                year += 2000;
            if (month < 1 || month > 12 || year < 2000)
                return false;

            expiry = new DateTime(year, month, 1);
            return true;
        }

        private string GetFuturesMonthCode(int month)
        {
            switch (month)
            {
                case 1: return "F";
                case 2: return "G";
                case 3: return "H";
                case 4: return "J";
                case 5: return "K";
                case 6: return "M";
                case 7: return "N";
                case 8: return "Q";
                case 9: return "U";
                case 10: return "V";
                case 11: return "X";
                case 12: return "Z";
                default: return string.Empty;
            }
        }

        private List<string> ParseAccountSelectors(string raw)
        {
            return (raw ?? string.Empty)
                .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private string FormatAccountsForLog(IEnumerable<ProjectXAccountInfo> targetAccounts)
        {
            if (targetAccounts == null)
                return "<none>";

            var items = targetAccounts
                .Select(a => string.Format(CultureInfo.InvariantCulture, "{0}:{1}", a.Id, a.Name ?? string.Empty))
                .ToArray();
            return items.Length > 0 ? string.Join(", ", items) : "<none>";
        }

        private string GetOrderKey(int accountId, string contractId)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}|{1}", accountId, contractId ?? string.Empty);
        }

        private string SanitizeJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return string.Empty;

            string sanitized = json;
            sanitized = RedactJsonValue(sanitized, "apiKey");
            sanitized = RedactJsonValue(sanitized, "loginKey");
            sanitized = RedactJsonValue(sanitized, "token");
            sanitized = RedactJsonValue(sanitized, "newToken");
            return sanitized;
        }

        private string RedactJsonValue(string json, string key)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
                return json ?? string.Empty;

            return Regex.Replace(
                json,
                "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"[^\"]*\"",
                "\"" + key + "\":\"***\"");
        }

        private string ToJsonString(string value)
        {
            return new JavaScriptSerializer().Serialize(value ?? string.Empty);
        }

        private bool TryGetJsonString(string json, string key, out string value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                var serializer = new JavaScriptSerializer();
                var data = serializer.Deserialize<Dictionary<string, object>>(json);
                object raw;
                if (data == null || !data.TryGetValue(key, out raw) || raw == null)
                    return false;
                value = raw.ToString();
                return !string.IsNullOrWhiteSpace(value);
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetJsonLong(string json, string key, out long value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                var serializer = new JavaScriptSerializer();
                var data = serializer.Deserialize<Dictionary<string, object>>(json);
                object raw;
                if (data == null || !data.TryGetValue(key, out raw) || raw == null)
                    return false;
                return TryConvertToLong(raw, out value);
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetJsonBool(string json, string key, out bool value)
        {
            value = false;
            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                var serializer = new JavaScriptSerializer();
                var data = serializer.Deserialize<Dictionary<string, object>>(json);
                object raw;
                if (data == null || !data.TryGetValue(key, out raw) || raw == null)
                    return false;
                return TryConvertToBool(raw, out value);
            }
            catch
            {
                return false;
            }
        }

        private IEnumerable<ProjectXAccountInfo> ExtractAccounts(string json)
        {
            foreach (var dict in ExtractDictionaries(json, "accounts"))
            {
                int id;
                object idObj;
                if (!dict.TryGetValue("id", out idObj) || !TryConvertToInt(idObj, out id) || id <= 0)
                    continue;

                object nameObj;
                object canTradeObj;
                object isVisibleObj;
                bool canTrade;
                bool isVisible;
                dict.TryGetValue("name", out nameObj);
                dict.TryGetValue("canTrade", out canTradeObj);
                dict.TryGetValue("isVisible", out isVisibleObj);
                TryConvertToBool(canTradeObj, out canTrade);
                TryConvertToBool(isVisibleObj, out isVisible);

                yield return new ProjectXAccountInfo
                {
                    Id = id,
                    Name = nameObj != null ? nameObj.ToString() : string.Empty,
                    CanTrade = canTrade,
                    IsVisible = isVisible
                };
            }
        }

        private IEnumerable<ProjectXContractInfo> ExtractContracts(string json)
        {
            foreach (var dict in ExtractDictionaries(json, "contracts"))
            {
                object idObj;
                if (!dict.TryGetValue("id", out idObj) || idObj == null)
                    continue;

                object nameObj;
                object descriptionObj;
                object symbolIdObj;
                object activeObj;
                bool activeContract;
                dict.TryGetValue("name", out nameObj);
                dict.TryGetValue("description", out descriptionObj);
                dict.TryGetValue("symbolId", out symbolIdObj);
                dict.TryGetValue("activeContract", out activeObj);
                TryConvertToBool(activeObj, out activeContract);

                yield return new ProjectXContractInfo
                {
                    Id = idObj.ToString(),
                    Name = nameObj != null ? nameObj.ToString() : string.Empty,
                    Description = descriptionObj != null ? descriptionObj.ToString() : string.Empty,
                    SymbolId = symbolIdObj != null ? symbolIdObj.ToString() : string.Empty,
                    ActiveContract = activeContract
                };
            }
        }

        private IEnumerable<Dictionary<string, object>> ExtractDictionaries(string json, string key)
        {
            if (string.IsNullOrWhiteSpace(json))
                yield break;

            var serializer = new JavaScriptSerializer();
            Dictionary<string, object> data;
            try
            {
                data = serializer.Deserialize<Dictionary<string, object>>(json);
            }
            catch
            {
                yield break;
            }

            object raw;
            if (data == null || !data.TryGetValue(key, out raw) || raw == null)
                yield break;

            var items = raw as System.Collections.IEnumerable;
            if (items == null)
                yield break;

            foreach (var item in items)
            {
                var dict = item as Dictionary<string, object>;
                if (dict != null)
                    yield return dict;
            }
        }

        private bool TryGetOrderInt(Dictionary<string, object> order, string key, out int value)
        {
            value = 0;
            object raw;
            return order != null && order.TryGetValue(key, out raw) && TryConvertToInt(raw, out value);
        }

        private bool TryGetOrderLong(Dictionary<string, object> order, string key, out long value)
        {
            value = 0;
            object raw;
            return order != null && order.TryGetValue(key, out raw) && TryConvertToLong(raw, out value);
        }

        private bool TryGetOrderDouble(Dictionary<string, object> order, string key, out double value)
        {
            value = 0.0;
            object raw;
            return order != null && order.TryGetValue(key, out raw) && TryConvertToDouble(raw, out value);
        }

        private bool TryConvertToInt(object raw, out int value)
        {
            value = 0;
            if (raw == null)
                return false;

            if (raw is int)
            {
                value = (int)raw;
                return true;
            }

            if (raw is long)
            {
                long longValue = (long)raw;
                if (longValue < int.MinValue || longValue > int.MaxValue)
                    return false;
                value = (int)longValue;
                return true;
            }

            if (raw is decimal)
            {
                value = (int)(decimal)raw;
                return true;
            }

            if (raw is double)
            {
                value = (int)(double)raw;
                return true;
            }

            return int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private bool TryConvertToLong(object raw, out long value)
        {
            value = 0;
            if (raw == null)
                return false;

            if (raw is int)
            {
                value = (int)raw;
                return true;
            }

            if (raw is long)
            {
                value = (long)raw;
                return true;
            }

            if (raw is decimal)
            {
                decimal decimalValue = (decimal)raw;
                if (decimalValue < long.MinValue || decimalValue > long.MaxValue)
                    return false;
                value = (long)decimalValue;
                return true;
            }

            if (raw is double)
            {
                double doubleValue = (double)raw;
                if (doubleValue < long.MinValue || doubleValue > long.MaxValue)
                    return false;
                value = (long)doubleValue;
                return true;
            }

            return long.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private bool TryConvertToDouble(object raw, out double value)
        {
            value = 0.0;
            if (raw == null)
                return false;

            if (raw is double)
            {
                value = (double)raw;
                return true;
            }

            if (raw is decimal)
            {
                value = (double)(decimal)raw;
                return true;
            }

            if (raw is int)
            {
                value = (int)raw;
                return true;
            }

            if (raw is long)
            {
                value = (long)raw;
                return true;
            }

            return double.TryParse(raw.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }

        private bool TryConvertToBool(object raw, out bool value)
        {
            value = false;
            if (raw == null)
                return false;

            if (raw is bool)
            {
                value = (bool)raw;
                return true;
            }

            return bool.TryParse(raw.ToString(), out value);
        }

        private string GetString(Func<string> getter)
        {
            return getter != null ? (getter() ?? string.Empty) : string.Empty;
        }

        private double GetCurrentTickSize()
        {
            return getTickSize != null ? getTickSize() : 0.0;
        }

        private MarketPosition GetCurrentMarketPosition()
        {
            return getMarketPosition != null ? getMarketPosition() : MarketPosition.Flat;
        }

        private int GetCurrentPositionQuantity()
        {
            return getPositionQuantity != null ? getPositionQuantity() : 0;
        }

        private void LogStatus(string message)
        {
            if (logStatus != null)
                logStatus(message ?? string.Empty);
        }

        private void LogDebug(string message)
        {
            if (logDebug != null)
                logDebug(message ?? string.Empty);
        }
    }
}
