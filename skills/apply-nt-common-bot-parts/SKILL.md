---
name: apply-nt-common-bot-parts
description: Apply standardized non-strategy-specific modules to NinjaTrader strategies (session/skip/news windows, transition safety cancel+flatten logic, startup timeframe/instrument validation, canonical infobox order, optional webhooks, and branding) using docs/bot-common-parts-spec.md as source of truth.
---

# Apply NT Common Bot Parts

Use this skill when integrating a new strategy file from external developers and you need to add/normalize shared modules used across bots.

## Load Order
1. Read `docs/bot-common-parts-spec.md`.
2. Read `references/checklist.md`.
3. If naming/structure differs, read `references/mapping.md`.

## Inputs
- Target strategy path (required)
- Reference strategy path (optional, recommended: `Duo.cs` or `ORBO.cs`)
- Module set:
  - `core` (default)
  - `webhooks` (optional)
  - `branding` (optional)

## Procedure
1. Identify existing equivalents in target strategy:
- session window methods
- skip/news gating methods
- cancel/flatten helpers
- signal naming helpers/constants
- strategy lifecycle hooks for startup/shutdown
- heartbeat reporter wiring
- timeframe/instrument validation guards
- infobox builder and renderer
- webhook plumbing and properties

2. Apply missing core parts first:
- drawing windows and transition safety hooks
- entry gating checks
- session boundary guards
- strategy-name-prefixed signal constants/helpers for entries and exits
- heartbeat reporting using `StrategyHeartbeatReporter`
- primary timeframe validation (`DataLoaded` validation + `OnBarUpdate` early-return guard + invalid-configuration cancel/flatten)
- primary instrument validation (`DataLoaded` validation + `OnBarUpdate` early-return guard + `NQ`/`MNQ` allow-list + invalid-configuration cancel/flatten)
- news data helpers

3. For signal naming, follow the reference strategy pattern:
- Prefix entry and exit signal names with the strategy name.
- Prefer constants/helpers over inline string literals.
- Wire protective exits/stops/targets to the active prefixed entry signal.
- Use `Duo.cs` as the default reference for this convention unless the target already has an equivalent strategy-specific helper pattern.

4. For heartbeat reporting, follow the reference strategy pattern:
- Add a strategy-level `HeartbeatStrategyName` constant.
- Add a `StrategyHeartbeatReporter heartbeatReporter` field.
- Instantiate it during `State.DataLoaded` with `TradeMessengerHeartbeats.csv` under `NinjaTrader.Core.Globals.UserDataDir`.
- Start it in `State.Realtime`.
- Dispose and null it in `State.Terminated`.
- Use `Duo.cs` as the default reference for lifecycle placement unless the target already has an equivalent implementation.

5. Normalize infobox to canonical row order:
1. Header
2. Contracts
3. News
4. Session
5. Footer

6. Apply news rules exactly:
- `UseNewsSkip == false` => single `News: Disabled` row
- enabled + no events => blocked icon row (default `News: 🚫`; allow equivalent blocked icon if project standard differs)
- enabled + events => rows with fade for passed events

7. For infobox icon rendering:
- Route emoji values to an emoji-capable font (`Segoe UI Emoji`).
- Route symbol-only values to a symbol font (`Segoe UI Symbol`) when needed.
- In WPF/NinjaTrader, prefer applying value brush to emoji runs for deterministic non-white rendering when color emoji layers are not reliable.

8. If module includes `webhooks`, apply provider and event mapping.

9. If module includes `branding`, apply header/footer text color only.

10. Validate with checklist and provide compliance report.

## Output Format
Return a compliance report with four sections:
- Added
- Already Present
- Skipped/Conflict
- Follow-up Needed

## Constraints
- Preserve strategy-specific trade logic.
- Do not import strategy-specific infobox rows from the reference strategy.
- Keep infobox invariants strict.
