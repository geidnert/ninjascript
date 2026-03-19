# Naming/Structure Mapping Guide

Use this when external strategy files use different naming conventions.

## Common Equivalents
- `UseNewsSkip` may be named `EnableNewsFilter`, `UseNewsFilter`, `NewsEnabled`
- `NewsBlockMinutes` may be named `NewsBufferMinutes`, `NewsWindowMinutes`
- `CancelAllOrders` may be split into multiple cleanup methods
- `ExitAllPositions(reason)` may be split by side (`ExitLong`/`ExitShort`)
- `HeartbeatStrategyName` may be named `StrategyNameForHeartbeat` or similar, but should remain a single strategy-level constant
- `heartbeatReporter` may be named `strategyHeartbeatReporter` or similar
- `LongEntrySignal` / `ShortEntrySignal` may be represented by strategy-prefixed signal constants or builders
- `BuildExitSignalName(reason)` may be named `GetExitSignalName`, `CreateExitSignalName`, or equivalent
- `GetOpenLongEntrySignal` / `GetOpenShortEntrySignal` may be replaced by an equivalent active-entry resolver
- `ValidateRequiredPrimaryTimeframe(minutes)` may be named `ValidateTimeframe`, `ValidateChartTimeframe`, `EnsurePrimaryTimeframe`
- `ValidateRequiredPrimaryInstrument()` may be named `ValidateInstrument`, `ValidateChartInstrument`, `EnsurePrimaryInstrument`
- `isConfiguredTimeframeValid` may be named `isTimeframeValid`, `timeframeValid`, `allowTradingForTimeframe`
- `isConfiguredInstrumentValid` may be named `isInstrumentValid`, `instrumentValid`, `allowTradingForInstrument`
- `MaxAccountBalance` may be named `MaxBalance`, `MaxNetLiquidation`, `AccountBalanceTarget`
- `maxAccountLimitHit` may be named `accountBalanceLimitReached`, `maxBalanceHit`, `balanceTargetReached`
- `TryGetCurrentNetLiquidation` may be named `GetNetLiquidation`, `TryGetAccountBalance`, `GetCurrentAccountValue`
- `WebhookUrl` may be named `TradersPostWebhookUrl`, `OrderWebhookUrl`
- `WebhookTickerOverride` may be named `WebhookInstrumentOverride`, `TickerOverride`, `InstrumentOverride`, `TradersPostTicker`
- `timeframePopupShown` may be named `timeframeWarningShown`, `invalidTimeframeNotified`
- `instrumentPopupShown` may be named `instrumentWarningShown`, `invalidInstrumentNotified`

## Integration Rule
Prefer adapting to existing strategy naming if behavior is equivalent, but preserve canonical infobox contract and news behavior.

For timeframe safety equivalence, preserve this behavior even if names differ:
- validation runs in `State.DataLoaded`
- invalid timeframe blocks trading in `OnBarUpdate`
- invalid timeframe path cancels/flattens exposure
- user is warned once via popup/log

For instrument safety equivalence, preserve this behavior even if names differ:
- validation runs in `State.DataLoaded`
- allow-list check includes `NQ` and `MNQ` for this strategy family
- invalid instrument blocks trading in `OnBarUpdate`
- invalid instrument path cancels/flattens exposure
- user is warned once via popup/log

For account-balance-guard equivalence, preserve this behavior even if names differ:
- visible strategy input exists and defaults to disabled
- net liquidation includes unrealized PnL for threshold checks
- threshold hit blocks future entries
- threshold hit while in position flattens exposure immediately
- blocked state latches for the running strategy instance unless explicitly reset/disabled

For heartbeat equivalence, preserve this behavior even if names differ:
- reporter instance is created in `State.DataLoaded`
- reporter starts in `State.Realtime`
- reporter is disposed in `State.Terminated`
- heartbeat file path resolves under `NinjaTrader.Core.Globals.UserDataDir`

For signal naming equivalence, preserve this behavior even if names differ:
- entry signals are strategy-prefixed
- exit signal builders preserve the strategy prefix
- stops/targets/exits bind to the active prefixed entry signal

For webhook input equivalence, preserve this behavior even if names differ:
- visible TradersPost URL input exists
- visible optional ticker/instrument override input exists
- TradersPost uses override when present, otherwise chart instrument name
- ProjectX settings stay hidden/internal unless explicitly requested
- webhook string properties are initialized/null-safe so old workspaces do not surface null grid errors

## Infobox Rows
When applying this skill, keep only common infobox rows:
- Header
- Contracts
- News
- Session
- Footer
Do not copy strategy-specific rows from the reference strategy.
