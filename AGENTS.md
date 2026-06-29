# AGENTS.md

Project guidance for Codex. This repo is a NinjaTrader 8 `bin/Custom` tree
containing custom NinjaScript Strategies, AddOns, Indicators, etc.

> History: this project was previously developed with Codex. Its conventions live
> in `docs/` and `skills/` and remain the source of truth. `Strategies/AGENTS.md`
> is the legacy Codex instruction file — kept for reference, not authoritative here.

## Source-of-truth docs (read the relevant one before related work)
- `docs/codex/repo-notes.md` - Codex handoff memory for repo layout, key
  files, current gotchas, and documentation-maintenance expectations. Read this
  before non-trivial repo work.
- `docs/bot-common-parts-spec.md` — shared, non-strategy-specific modules: session/
  skip/news windows, transition cancel+flatten safety, timeframe/instrument
  validation, `MaxAccountBalance` guard, canonical infobox order, heartbeat
  reporting, webhooks (TradersPost/ProjectX), branding.
- `docs/projectx-parity-spec.md` — keeping NT8-managed behavior aligned with
  mirrored ProjectX execution: auth/account/contract resolution, submit-time entry
  mirroring, cancel/exit routing, dynamic stop/target sync, flip-exit suppression.
- `docs/testing-to-public-spec.md` — converting a `*Testing` strategy into the
  closed/public version by full-file copy + restoring the public contract (never
  selective porting).

## Project workflows (skills)
These are real, reusable procedures. When a task matches one, follow its `SKILL.md`:
- `skills/apply-nt-common-bot-parts/` — apply/normalize the common modules above.
- `skills/apply-nt-projectx-parity/` — apply or review ProjectX parity.
- `skills/apply-nt-testing-to-public/` — rebuild a public strategy from its testing file.
Each skill's `SKILL.md` lists its own load order and references (`references/*.md`).
There is no `scripts/` directory in the current tree; reusable workflows live in
`skills/`.

## Strategy conventions
- Strategies come in pairs: `XXX.cs` (public/closed) and `XXXTesting.cs` (full logic).
  Families: DUO, DUOrc, HUGO, MICH, ORBO. `DUO.cs` / `DUOTesting.cs` are the canonical
  reference implementations for most shared patterns.
- Entry/exit signal names must be prefixed with the strategy name; prefer
  constants/helpers over inline string literals. Stops/targets/exits reference the
  active prefixed entry signal.
- Allowed instruments for the current bot family: `NQ` and `MNQ` only.
- Infobox row order is strict: Header, Contracts, News, Session, Footer.
- Preserve strategy-specific trade logic when applying shared modules — never import
  strategy-specific infobox rows from a reference strategy.

## Building / verifying
- These are NinjaScript files compiled **inside NinjaTrader 8**, not via local
  `dotnet build`. `NinjaTrader.Custom.csproj` and the committed `.dll`/`.pdb` reflect
  the last NT8 compile. I cannot compile here — when a change needs verification,
  state that explicitly and rely on a NT8 compile (per the specs' compliance checks).
- Always report compile/test status explicitly rather than implying success.

## Conventions for working here
- When applying a skill, produce the compliance/parity/handoff report its `SKILL.md`
  specifies (e.g. Added / Already Present / Skipped / Follow-up Needed).
- Don't change TradersPost webhook timing unless explicitly asked.
- Keep ProjectX settings hidden/internal in public bots unless asked to expose them.
- `.bak-*` files and `.tmp/` are working artifacts, not part of the build.
- If future work changes durable project knowledge (rules, workflows, file
  locations, current gotchas, or invariants), update the relevant `docs/codex`
  note or source-of-truth spec before finishing and mention that in the handoff.
