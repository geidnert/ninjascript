# Naming/Structure Mapping Guide

Use this when external strategy files use different naming conventions.

## Common Equivalents
- `UseNewsSkip` may be named `EnableNewsFilter`, `UseNewsFilter`, `NewsEnabled`
- `NewsBlockMinutes` may be named `NewsBufferMinutes`, `NewsWindowMinutes`
- `CancelAllOrders` may be split into multiple cleanup methods
- `ExitAllPositions(reason)` may be split by side (`ExitLong`/`ExitShort`)
- `ValidateRequiredPrimaryTimeframe(minutes)` may be named `ValidateTimeframe`, `ValidateChartTimeframe`, `EnsurePrimaryTimeframe`
- `isConfiguredTimeframeValid` may be named `isTimeframeValid`, `timeframeValid`, `allowTradingForTimeframe`
- `timeframePopupShown` may be named `timeframeWarningShown`, `invalidTimeframeNotified`

## Integration Rule
Prefer adapting to existing strategy naming if behavior is equivalent, but preserve canonical infobox contract and news behavior.

For timeframe safety equivalence, preserve this behavior even if names differ:
- validation runs in `State.DataLoaded`
- invalid timeframe blocks trading in `OnBarUpdate`
- invalid timeframe path cancels/flattens exposure
- user is warned once via popup/log

## Infobox Middle Zone
Only strategy-specific rows belong between Contracts and News.
Do not place Session/News-specific rows in this middle zone.
