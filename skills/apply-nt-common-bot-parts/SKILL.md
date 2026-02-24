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
- timeframe/instrument validation guards
- infobox builder and renderer
- webhook plumbing and properties

2. Apply missing core parts first:
- drawing windows and transition safety hooks
- entry gating checks
- session boundary guards
- primary timeframe validation (`DataLoaded` validation + `OnBarUpdate` early-return guard + invalid-configuration cancel/flatten)
- primary instrument validation (`DataLoaded` validation + `OnBarUpdate` early-return guard + `NQ`/`MNQ` allow-list + invalid-configuration cancel/flatten)
- news data helpers

3. Normalize infobox to canonical row order:
1. Header
2. Contracts
3. Strategy-specific rows
4. News
5. Session
6. Footer

4. Apply news rules exactly:
- `UseNewsSkip == false` => single `News: Disabled` row
- enabled + no events => blocked icon row (default `News: 🚫`; allow equivalent blocked icon if project standard differs)
- enabled + events => rows with fade for passed events

5. For infobox icon rendering:
- Route emoji values to an emoji-capable font (`Segoe UI Emoji`).
- Route symbol-only values to a symbol font (`Segoe UI Symbol`) when needed.
- In WPF/NinjaTrader, prefer applying value brush to emoji runs for deterministic non-white rendering when color emoji layers are not reliable.

6. If module includes `webhooks`, apply provider and event mapping.

7. If module includes `branding`, apply header/footer text color only.

8. Validate with checklist and provide compliance report.

## Output Format
Return a compliance report with four sections:
- Added
- Already Present
- Skipped/Conflict
- Follow-up Needed

## Constraints
- Preserve strategy-specific trade logic.
- Do not reorder strategy-specific rows outside the infobox middle zone.
- Keep infobox invariants strict.
