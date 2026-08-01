# Integration Checklist

## Core
- [ ] Session background draw exists
- [ ] No-trades-after draw/check exists
- [ ] Skip draw/check exists
- [ ] News draw/check exists (conditional on `UseNewsSkip`)
- [ ] Entry path blocks in skip/news/no-trades-after windows
- [ ] Entry and exit signal names are prefixed with the strategy name
- [ ] Protective stops/targets use the active prefixed entry signal
- [ ] Every tracked working order reference is translated with `GetRealtimeOrder()` at the historical-to-realtime boundary
- [ ] A null `GetRealtimeOrder()` result clears the tracked reference; it never falls back to the stale backtest order
- [ ] Execution-driven protective stops are validated against the current bid/ask before submit/change
- [ ] A gap-through stop triggers one latched market exit instead of an invalid stop submission
- [ ] Strategies using `RealtimeErrorHandling.IgnoreAllErrors` handle protective rejections in `OnOrderUpdate()` and do not submit a rejected OCO sibling
- [ ] Optional `RequireEntryConfirmation` property exists
- [ ] Entry confirmation helper exists (for example `ShowEntryConfirmation`)
- [ ] Confirmation check runs before each new entry submission path
- [ ] Visible `MaxAccountBalance` property exists and defaults to `0.0`
- [ ] Account-balance guard uses `NetLiquidation` or explicit cash+unrealized fallback
- [ ] Account-balance guard blocks new entries after hit
- [ ] Account-balance guard flattens open position when threshold is reached intratrade
- [ ] Visible `MaxDailyProfit` property exists and defaults to `0.0`
- [ ] Daily-profit guard captures a net-liquidation baseline for each calendar date
- [ ] Daily-profit guard includes unrealized PnL, blocks entries, and flattens with reason `MaxDailyProfit`
- [ ] Daily-profit latch resets only on a new calendar date or when disabled
- [ ] Transition to blocked window cancels orders
- [ ] Transition to blocked window flattens open position
- [ ] `HeartbeatStrategyName` constant exists
- [ ] `StrategyHeartbeatReporter heartbeatReporter` field exists
- [ ] Heartbeat reporter is created during `State.DataLoaded`
- [ ] Heartbeat reporter is started during `State.Realtime`
- [ ] Heartbeat reporter is disposed during `State.Terminated`
- [ ] `IsLastBarOfSession()` guard used in entry readiness
- [ ] Primary timeframe validator method exists (for example `ValidateRequiredPrimaryTimeframe`)
- [ ] Primary timeframe validation is called during `State.DataLoaded`
- [ ] Primary instrument validator method exists (for example `ValidateRequiredPrimaryInstrument`)
- [ ] Primary instrument validation is called during `State.DataLoaded`
- [ ] Invalid timeframe or instrument triggers early return in `OnBarUpdate`
- [ ] Invalid configuration path cancels working orders and flattens open position (`InvalidConfiguration` preferred)
- [ ] Invalid timeframe warning is user-visible once (popup and/or explicit log)
- [ ] Invalid instrument warning is user-visible once (popup and/or explicit log)

## Infobox
- [ ] Canonical order: Header > Contracts > News > Session > Footer
- [ ] Contracts first line after header
- [ ] News directly above Session
- [ ] Session directly above footer
- [ ] No strategy-specific rows copied from reference strategy
- [ ] `UseNewsSkip=false` => one row `News: Disabled`
- [ ] `UseNewsSkip=true` and no events => blocked icon row (default `News: 🚫` or approved equivalent)
- [ ] Empty-week blocked icon value brush is red (`Brushes.IndianRed`)
- [ ] Passed news rows faded
- [ ] Contracts/News/Session values use same light value color
- [ ] Emoji icon values use emoji-capable rendering path and do not regress to white/gray fallback

## Optional Webhooks
- [ ] Provider selection exists
- [ ] Visible `WebhookUrl` input exists
- [ ] Visible optional `WebhookTickerOverride` input exists
- [ ] ProjectX settings remain hidden/internal unless explicitly requested
- [ ] Entry webhook events mapped
- [ ] Exit webhook events mapped
- [ ] Cancel webhook events mapped
- [ ] TradersPost ticker uses override when present, otherwise chart instrument name
- [ ] Webhook string inputs are initialized/null-safe (`string.Empty` or equivalent)
- [ ] Missing-config safe no-op behavior

## Suggested Verification Greps
```bash
rg -n "BuildInfoLines|RenderInfoBoxOverlay|EnsureInfoBoxOverlay" <target>
rg -n "UseNewsSkip|NewsBlockMinutes|GetCurrentWeekNews|PassedNewsRowBrush" <target>
rg -n "MaxAccountBalance|maxAccountLimitHit|accountBalanceLimitReached|NetLiquidation|GetUnrealizedProfitLoss" <target>
rg -n "MaxDailyProfit|dailyProfitStartBalance|dailyProfitLimitHit|dailyProfitLimitReached" <target>
rg -n "CancelAllOrders|ExitAllPositions|IsLastBarOfSession" <target>
rg -n "HeartbeatStrategyName|heartbeatReporter|State == State.Realtime|State == State.Terminated" <target>
rg -n "LongEntrySignal|ShortEntrySignal|BuildExitSignalName|GetOpenLongEntrySignal|GetOpenShortEntrySignal|SetStopLoss|SetProfitTarget" <target>
rg -n "GetRealtimeOrder|OnExecutionUpdate|RealtimeErrorHandling|OrderState.Rejected|ProtectiveReject|GapStop" <target>
rg -n "DrawSession|DrawSkip|DrawNews|NoTradesAfter" <target>
rg -n "ValidateRequiredPrimaryTimeframe|isConfiguredTimeframeValid|ValidateRequiredPrimaryInstrument|isConfiguredInstrumentValid|InvalidConfiguration|timeframePopupShown|instrumentPopupShown" <target>
rg -n "WebhookUrl|WebhookTickerOverride|Webhook|ProjectX|TradersPost|SendWebhook" <target>
```
