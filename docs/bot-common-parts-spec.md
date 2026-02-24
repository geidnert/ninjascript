# Bot Common Parts Spec

## Purpose
This file defines reusable, non-strategy-specific parts that must be consistently applied to new NinjaTrader strategies.

## Scope
- Core module (required)
- Webhooks module (optional)
- Branding module (optional)

## Canonical Infobox Contract
Infobox row order is strict:
1. Header
2. Contracts
3. Strategy-specific rows (zero or more)
4. News
5. Session
6. Footer

Hard rules:
- `Contracts` is always the first row after header.
- `News` is always directly above `Session`.
- `Session` is always directly above footer.
- Only strategy-specific rows are allowed between `Contracts` and `News`.

### Infobox Colors
- Header/footer text: branding color (per strategy family).
- Contracts/News/Session value text: same light value color.
- News rows fade when passed (enabled mode).

### News Row Behavior
- If `UseNewsSkip == false`: show exactly one row: `News: Disabled`.
- If enabled and no events in week: show blocked icon row (`News: 🚫` default; strategy may use equivalent blocked icon).
  The blocked icon value brush should be red (`Brushes.IndianRed`) to indicate active blocking mode with no events.
- If enabled with events: show rows for each event.
- Past event rows must use faded brush (`PassedNewsRowBrush`).

### InfoBox Icon Rendering
- Emoji-style state icons (for example `✅`, `🚫`, `🕒`) should render via emoji-capable font (`Segoe UI Emoji`).
- For WPF/NinjaTrader environments where color emoji can flatten to white/gray, emoji value runs should explicitly use the row/value brush for deterministic coloring.
- Symbol-only glyphs can use `Segoe UI Symbol` (or strategy-chosen equivalent) when cleaner than default text rendering.

## Core Module Requirements

### Session/Window Drawing
- Session background drawing.
- No-trades-after window/line drawing.
- Skip window drawing.
- News window drawing (when enabled).

### Entry/Trade Gating
- Entry path checks for:
  - session availability
  - no-trades-after window
  - skip window
  - news skip window

### Transition Safety
On entering blocked windows (skip/news/no-trades-after):
- Cancel working entries/orders.
- Flatten open position with reason tag.

Reason tags used by convention:
- `NoTradesAfter`
- `SkipWindow`
- `NewsSkip`
- `SessionEnd`

### Session Boundary Safety
- `IsLastBarOfSession()` guard to prevent unsafe late entries.
- Session end flatten guard.
- Half-day/session-edge crash protection.

### Shared Helpers
- `CancelAllOrders()`
- `ExitAllPositions(string reason)`
- `GetCurrentWeekNews(DateTime time)`
- `EnsureNewsDatesInitialized()` / loader equivalent

### Primary Timeframe Safety
- Validate required primary chart timeframe during `State.DataLoaded`.
- Keep a strategy-level validity flag (for example `isConfiguredTimeframeValid`).
- If invalid timeframe is detected:
  - write a clear warning log including expected vs actual timeframe,
  - show a one-time warning popup,
  - disable trading path in `OnBarUpdate` by returning early,
  - cancel working orders and flatten any open position with reason tag `InvalidConfiguration` (or `InvalidTimeframe` if strategy has not yet adopted combined config reason tags).
- Timeframe check should verify both period type and minute value when strategy requires minute charts.

### Primary Instrument Safety
- Validate required primary instrument during `State.DataLoaded`.
- Keep a strategy-level validity flag (for example `isConfiguredInstrumentValid`).
- Allowed instruments for current bot family: `NQ` and `MNQ` only.
- If invalid instrument is detected:
  - write a clear warning log including actual instrument,
  - show a one-time warning popup,
  - disable trading path in `OnBarUpdate` by returning early,
  - cancel working orders and flatten any open position with reason tag `InvalidConfiguration`.
- Instrument check should use `Instrument.MasterInstrument.Name` (normalized, case-insensitive).

## ORBO-Specific Common UI Rules (still reusable pattern)
- Include `OR Size` row in infobox where strategy exposes OR range.
- OR size format: points (`{0:F2} pts`).

## Webhooks Module (Optional)

### Providers
- TradersPost
- ProjectX

### Requirements
- Provider selection enum/property.
- Entry/exit/cancel webhook events.
- Safe no-op when configuration missing.
- ProjectX auth token caching/reuse.
- Optional ProjectX order id/contract id tracking for cancels.

## Branding Module (Optional)
- Header/footer text color per strategy family.
- Keep body rows unchanged.

## Integration Workflow For New External Strategy
1. Import strategy file.
2. Apply core module.
3. Apply webhook module if required.
4. Apply branding module.
5. Insert strategy-specific rows into infobox middle zone only (between Contracts and News).
6. Run compliance checks.

## Compliance Checks (minimum)
- Infobox row order follows contract.
- `UseNewsSkip=false` renders `News: Disabled` single row.
- Passed news rows fade.
- Contracts/News/Session value colors are consistent.
- Window transition cancel/flatten hooks exist.
- Session-end and last-bar guards exist.
- Primary instrument validator exists and is called in `State.DataLoaded`.
- Invalid instrument blocks trading path and triggers cancel/flatten protection.

## Commit-Derived Baseline Notes
From ORBOTesting history after baseline commit `cabe7871c1bf1642f43be3bb7f388d9da6930c85`, common parts were added/finalized in phases:
- Session drawing + skip/no-trades transitions
- News skip framework + windows + transitions
- Infobox simplification + contracts/armed/news/session
- Session edge safety fixes (half-day/last-bar)
- Webhook provider framework
- Infobox polish (OR size points, news fade, disabled-news row)
- Primary timeframe safety guard (validate in `DataLoaded`, warn once, cancel/flatten and block trading when invalid) from commit `ddf7f1ede836712ffdf77d0b6c6b4c8b137b039f` (2026-02-24)
- Primary instrument safety guard (`NQ`/`MNQ` allow-list, validate in `DataLoaded`, warn once, cancel/flatten and block trading when invalid) from commit lineage on 2026-02-24
