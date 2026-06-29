# Codex Repo Notes

Last reviewed: 2026-06-29.

This note is durable handoff memory for future Codex threads. It complements
`AGENTS.md`; the behavior specs and skills remain the source of truth for their
domains.

## Repo Shape
- This workspace is a NinjaTrader 8 user-data `bin/Custom` tree, not a normal
  standalone .NET app. Product source is mostly under `Strategies/`, with stock
  or generated NinjaTrader folders such as `Indicators/`, `AddOns/`,
  `BarsTypes/`, `DrawingTools/`, and localization folders also present.
- `NinjaTrader.Custom.csproj` has `EnableDefaultCompileItems=false` and explicit
  `<Compile Include=...>` entries. Do not churn it for formatting or ordering.
  Add or remove compile entries only when a source file genuinely needs to be in
  the NT8 compile.
- There is no `scripts/` directory as of this review. The reusable repo
  procedures are the `skills/*/SKILL.md` workflows.
- `.bak-*` files and `.tmp/` are local working artifacts. Compiled `.dll`,
  `.pdb`, `.xml`, `bin/`, `obj/`, localization outputs, and stock NT8 source
  folders are not good evidence of intentional source changes by themselves.

## Docs And Skills
- `AGENTS.md` is the current Codex entry point.
- `Strategies/AGENTS.md` is a legacy reference. It contains stale absolute paths
  and is not authoritative.
- Read the relevant source-of-truth spec before touching matching behavior:
  `docs/bot-common-parts-spec.md`, `docs/projectx-parity-spec.md`, and
  `docs/testing-to-public-spec.md`.
- Follow the matching skill when the task fits:
  `skills/apply-nt-common-bot-parts/`,
  `skills/apply-nt-projectx-parity/`, or
  `skills/apply-nt-testing-to-public/`.
- Current `.gitignore` still ignores `/docs/`. `docs/bot-common-parts-spec.md`
  is tracked, but other docs under `docs/` can be ignored unless force-added.
  Check with `git status --ignored=matching docs` or `git check-ignore -v`.
  Only use `git add -f` for ignored docs after user approval.

## Key Source Files
- `Strategies/DUO.cs` and `Strategies/DUOTesting.cs` are the default reference
  for shared bot modules: session/skip/news windows, transition safety,
  heartbeat lifecycle, strategy-prefixed signals, entry confirmation,
  `MaxAccountBalance`, TradersPost ticker override, and DUO-style ProjectX
  transport/account/contract handling.
- `Strategies/MICH.cs` and `Strategies/MICHTesting.cs` are the reference for
  dynamic stop/target ProjectX protective-order sync and flip-exit suppression
  when a bot actually has that exit model.
- `Strategies/ORBO.cs` and `Strategies/ORBOTesting.cs` are the reference for
  staged limit entries: ProjectX entry mirroring belongs at NT8 submit time,
  not later execution time.
- `Strategies/HUGO*.cs`, `Strategies/ADAM*.cs`, and `Strategies/ORBOib*.cs`
  use paired `*ProjectXSupport.cs` router files. Keep public/testing routers
  aligned when changing shared ProjectX transport behavior.
- `Strategies/StrategyHeartbeatReporter.cs` writes
  `TradeMessengerHeartbeats.csv` under `NinjaTrader.Core.Globals.UserDataDir`.
  `Strategies/TradeMessenger.cs` consumes heartbeat/data-feed state for
  monitoring and notifications.
- Analyzer utilities include `Strategies/AnalyzerBarsExporter.cs` and
  `Strategies/AnalyzerDuoStateExporter.cs`; they export Strategy Analyzer data
  under the user-data `db/analyzer-bars` path by default.
- `Strategies/AutoEdgeLicensing.cs` contains the AutoEdge license gate. Treat
  server URL, status names, cache/grace behavior, and key storage as product
  contract unless explicitly asked to change licensing.

## Build And Verification
- Do not present local `dotnet build` as meaningful verification for these
  NinjaScript files. The authoritative compile happens inside NinjaTrader 8.
- When a change needs compile validation, say that NT8 compile was not run here
  and list what static checks or greps were performed.
- `NinjaTrader.Custom.csproj`, `.dll`, `.pdb`, and generated XML can reflect the
  last NT8 compile rather than the source edit currently under review.

## Working Rules
- Expect a dirty worktree. Inspect status before edits and preserve unrelated
  user/NT8 changes.
- Stage and commit only files belonging to the current request. If docs are
  ignored, force-add only the approved doc files, not broad directories.
- Prefer `rg`/`rg --files` for inspection. Use focused greps for the large
  strategy files because broad ProjectX searches can produce huge output.
- Do not change product code when the request is memory/docs cleanup unless a
  docs-only task reveals a necessary non-product metadata fix and the user agrees.

## Current Worktree Snapshot
This snapshot was observed on 2026-06-29 and must be rechecked before future
work:
- Branch: `main...origin/main`.
- Existing non-doc product state included a modified `NinjaTrader.Custom.csproj`
  and untracked `Strategies/AnalyzerBarsExporter.cs`,
  `Strategies/AnalyzerDuoStateExporter.cs`, and
  `Strategies/AutoEdgeLicensing.cs`.
- `AGENTS.md` and `CLAUDE.md` were untracked before this memory cleanup.
- `docs/projectx-parity-spec.md` and `docs/testing-to-public-spec.md` existed
  but were ignored by `.gitignore`.

## Things Not To Reintroduce
- Do not rebuild public strategies by selectively porting methods from testing
  files. Use the full-copy testing-to-public workflow and restore the public
  contract afterward.
- Do not move TradersPost webhook timing unless explicitly requested.
- Do not send ProjectX entries on execution for staged NT8 pending-order
  strategies; mirror at submit time.
- Do not double-send ProjectX exits during flip or pre-announced exit flows, but
  also do not add suppression latches where the strategy does not need one.
- Do not expose hidden/internal ProjectX settings in public bots unless the user
  asks or the requested public contract is DUO-style ProjectX visibility.
- Do not copy strategy-specific infobox rows from a reference strategy. Keep the
  canonical common order: Header, Contracts, News, Session, Footer.
- Do not reintroduce unprefixed entry/exit signal literals or protective orders
  that are not bound to the active prefixed entry signal.
- Do not narrow ProjectX account selection back to a single integer-only field;
  it must support comma-separated account ids or exact account names.
- Do not make manual ProjectX contract id mandatory when automatic contract
  resolution is available.
- Do not let webhook string properties regress to null values in the NinjaScript
  property grid.
- Do not remove chart drawing logic merely because public UI controls are hidden;
  preserve hidden-but-active visual defaults.

## Maintenance Rule
When future work changes durable project knowledge, update memory before
finishing:
- Update `AGENTS.md` for top-level agent rules and required reading.
- Update `docs/codex/*.md` for repo layout, gotchas, current handoff memory, and
  operational notes.
- Update the spec docs for behavioral contracts.
- Update `skills/*/SKILL.md` or skill references when the repeatable procedure
  changes.

If no durable knowledge changed, state that no memory/docs update was needed in
the handoff.
