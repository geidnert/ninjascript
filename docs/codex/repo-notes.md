# Codex Repo Notes

Last reviewed: 2026-08-02.

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
- `Strategies/DUO.cs` is the default reference for shared bot modules:
  session/skip/news windows, transition safety, heartbeat lifecycle,
  strategy-prefixed signals, entry confirmation, `MaxAccountBalance`,
  account-currency `MaxDailyProfit` with a per-calendar-date net-liquidation baseline,
  TradersPost ticker override, and DUO-style ProjectX transport/account/contract
  handling.
- 2026-07-21: `Strategies/DUOlo.cs` is the separate DUO limit-order variant. It
  is a full copy of the current DUO implementation with only the initial primary
  entry lifecycle changed: the real order rests exactly on the active entry EMA,
  follows that EMA at each completed five-minute bar, and is canceled whenever a
  shadow DUO trade reaches any terminal exit. `Entry Variance` keeps DUO's live
  random 1-10 second delay when enabled and submits immediately when disabled.
  A real fill discards all shadow state and starts normal DUO management fresh
  from the actual fill. DUOlo reuses DUO vendor license `337` and has its own
  prefixed signals, drawing tags, converter, and heartbeat identity. ProjectX
  entry limits are mirrored at submit time and amended through `/api/Order/modify`;
  TradersPost timing is unchanged. Compile it through the explicit
  `NinjaTrader.Custom.csproj` `Strategies\\DUOlo.cs` entry.
- 2026-07-24: `Strategies/PULL.cs` is a lightweight NT8-only prototype modeled
  on `EMAL.cs`; its four-letter name is intentionally temporary. It is a
  long/short NQ/MNQ minute-chart strategy that compares a completed directional
  candle's range with the average range of preceding candles. Long impulses must
  close above a configurable EMA and shorts below it. The strategy stages a
  limit at a configurable percentage of the impulse range, with protective
  stop/target prices beyond the directionally appropriate impulse extremes plus
  optional point padding. An unfilled entry is canceled if price reaches its
  directional impulse target first. There is no TradersPost or ProjectX routing
  in this initial version. Compile it through the explicit
  `NinjaTrader.Custom.csproj` `Strategies\\PULL.cs` entry.
- 2026-07-28: Steve's Version 11 `Strategies/EMAL.cs` removes the old Sunday
  window controls, starts the Asia gate at 18:30 ET, and retains the inclusive
  daily news blackout with optional flatten plus DST-aware Asia/Europe/US
  sessions. It includes selectable limit references and offsets, fill/reference
  bracket anchoring, Points/ATR-ratio slope thresholds, adaptive TP/SL modes,
  realized-points daily profit/loss entry gates resetting at 18:00 ET, a WPF
  chart panel, graceful invalid-chart refusal, and a 51-column feature CSV with
  entry mode/reference, offset, and MAE/MFE. The post-import audit also made the
  queued bracket reference clear with other queued state and made daily realized
  points use partial-entry and quantity-weighted multi-fill exit prices. Preserve
  those features together with EMAL's existing safety layer when importing later
  replacements: exact historical-to-realtime order translation, execution-driven
  wrong-side stop validation, stop-first protective staging to avoid rejected-OCO
  reuse, rejection-triggered emergency exits, and the `MaxAccountBalance` latch.
  NT8's `OnExecutionUpdate` override ends with `DateTime time`; a trailing Boolean
  is not a valid NT8 signature.
- 2026-08-01: Steve's second Version 18 `Strategies/EMAL.cs` is the baseline for
  EMAL ProjectX parity. EMAL mirrors market and staged limit entries to ProjectX
  at NT8 submit time, attaches brackets immediately, routes confirmed entry
  cancellations without cancelling protection after a partial fill, syncs live
  stop/target price amendments, and uses idempotent flatten recovery. Its NT8
  display name is assembly-versioned (`EMAL` + version digits) while order signal
  names remain stable and EMAL-prefixed. A static rolling order-action guard is
  shared by EMAL instances on the same `Account.Connection`; the default local
  ceiling is 1100 actions/hour with six actions reserved per new trade. The guard
  blocks new entries only. Provider rate-limit rejections add a one-hour shared
  entry cooldown, while stop/target/cancel/emergency actions remain enabled.
  This counter cannot see manual orders, other strategies, or the same provider
  user running in another NT8 process/VPS, so it is a conservative safety buffer,
  not an exact provider-request meter. It is active only for realtime,
  non-Playback execution; Strategy Analyzer/historical processing and Market
  Replay do not count actions or block tuning trades, while the infobox retains
  the row as `API: Off`.
- 2026-08-01: Steve's Version 19 EMAL delta gives M5 its own continuous
  09:35-10:30 ET schedule, keeps the 09:50 preset boundary without the M1/M15
  09:50-09:55 gap, and blocks the 10:05 M5 bar. M1 and M15 behavior remains
  unchanged. Preserve this schedule when importing later Steve replacements.
- 2026-08-02: Current `Strategies/EMAL.cs` adds a visible account-currency
  `MaxDailyProfit` guard alongside the hidden realized-points research controls.
  It captures a net-liquidation baseline for each primary-bar calendar date,
  includes unrealized P&L, cancels pending entries, flattens at the threshold,
  and latches for the rest of that date. Preserve this separately from the
  18:00 ET realized-points daily profit/loss controls in future imports.
- 2026-08-05: Current `Strategies/EMAL.cs` deliberately restores the exact
  EMAL-1019 / Git `610f48b` trade and managed-order baseline after the later
  multi-contract protection watchdog produced emergency-exit/normal-bracket
  races, opposite-side positions, cleanup orders, and strategy shutdowns in
  live trading. Do not restore the removed entry-acknowledgement, connection
  reconciliation, continuous protection audit/repair, or overfill-flatten
  package without a new design and isolated validation.
  Two non-order fixes from that work are intentionally retained. The EMA is
  explicitly bound to primary `Close` and receives `ema.Update()` before any
  primary-bar gate. Playback and broker-account instances use historical bars
  for warmup only, while the Strategy Analyzer `Backtest` account retains the
  complete historical order/fill path. These fixes prevent the re-enable EMA
  out-of-range failure and historical managed entries suppressing fresh 09:30
  or mid-session realtime entries.
  Steve's EMAL-1022 settings delta is forward-ported on top: the informational
  `EMALVersion` property defaults to `version_1022`, ProjectX is the default
  provider, the minute-filter master switch is removed (the five 1a-1e boxes
  directly define allowed minutes), and the property grid is reorganized under
  `A. Version` through `F. Logging` with advanced fields hidden as in Steve's
  cut. The NinjaTrader strategy/list label is deliberately the stable plain
  `EMAL` name (the version remains visible separately), while NinjaTrader
  `VendorLicense`, ProjectX routing, the 1100-action guard, and EMAL-1019
  stop-first/wrong-side/OCO protections remain intact.
  ProjectX remains Steve's default provider but is inactive until base URL,
  username, API key, and either account selectors or the internal all-accounts
  flag are present. An incomplete setup produces one startup status line and no
  authentication, discovery, entry, exit, cancel, or protective-sync requests;
  signal-time processing must not repeat missing-credential errors.
- 2026-08-07: Current EMAL is `version_1026`. The 1023-1026 settings cleanup
  leaves only the two NY morning windows, fixes entry to passive
  `Limit(BidAsk)` with fill-relative protection, adds `Disabled` to both window
  presets, defaults direct minutes to `true,false,false,true,true`, and removes
  the obsolete entry/offset, Asia/late-US, bucket/news/time-stop, separate
  enable, and daily-cap properties. Preserve that reduced surface in later
  cuts.
  The visible informational `Version` enum must remain `[XmlIgnore]`. Earlier
  cuts serialized values such as `version_1022`/`version_1023`, then removed
  those enum members; loading the next vendor assembly consequently produced
  `unable to deserialize user data: There is an error in XML document` for
  every saved instance. Excluding Version from workspace/template XML lets old
  elements be ignored while `State.SetDefaults` supplies the installed cut.
- 2026-08-02: `Strategies/EMAL5.cs` is Steve's separate, self-contained native
  five-minute EMA-direction strategy, initially imported from `EMAL5-1.cs`.
  It uses one configurable ET session (09:30-10:30 by default), a separately
  tunable EMA slope lookback, fixed TP/SL settings, NQ/MNQ and five-minute chart
  validation, passive entries, ProjectX/TradersPost routing, and EMAL5-prefixed
  signals and log files. It deliberately does not share its class, enums, or
  rate-guard state with EMAL; the EMAL and EMAL5 order-action budgets therefore
  do not coordinate when both run on the same connection. Its visible
  account-currency `MaxDailyProfit` guard uses a per-primary-bar-calendar-date
  net-liquidation baseline, includes unrealized P&L, cancels pending entries,
  flattens at the threshold, and latches for the rest of that date. ATR,
  London/Tokyo log columns, news blackout, time stop, and the M1-specific
  filters are absent.
- 2026-07-20: Current DUO `State.SetDefaults` session defaults in
  `Strategies/DUO.cs` are mirrored from Steve's
  `/Volumes/Documents/NinjaTrader 8/bin/Custom/Strategies/DUOTesting-Trader-202.xml`
  and correspond to released NT8 identity `2.1.1.0`. The sync intentionally
  preserves `BarsRequiredToTrade = 250` even though the exported optimizer XML
  contains the NT8 framework default `20`. The #202 XML predates the nine
  per-session `TakeProfitPostTriggerPriceTrail` properties. Steve supplied
  those defaults separately: `true` for Asia 1, Asia 2, and America 2, and
  `false` for the other sessions (including disabled America 3). When Steve
  sends new DUO defaults, sync NT8 DUO and TraderPro DUO in the same pass
  unless explicitly scoped to one side.
- DUO and DUOrc are maintained directly in `Strategies/DUO.cs` and
  `Strategies/DUOrc.cs`; do not recreate separate DUO/DUOrc testing variants
  unless the user explicitly asks.
- 2026-07-12: DUO and DUOrc use strategy type converters to hide an entire
  session group from the NinjaTrader settings UI whenever that session's
  `Contracts` value is `0`. Keep every configurable session slot represented in
  its strategy's converter when session slots are added or renamed.
- 2026-07-20: DUO and DUOrc trade-line overlays show the TP-percent trigger as
  a dotted green line and the stop-move destination as a dotted red line. The
  destination uses the same entry-to-target percentage calculation as live stop
  management. Keep active, historical, restored-position, custom-rendered, and
  fallback `Draw.Line` paths aligned when changing these indicators.
- 2026-07-24: Current DUOrc `State.SetDefaults` mapped defaults in
  `Strategies/DUOrc.cs` are mirrored from Steve's
  `/Volumes/Documents/NinjaTrader 8/bin/Custom/Strategies/DUOrcTesting-155.xml`
  and correspond to the NT8 `1.0.5.8` release.
  Preserve `BarsRequiredToTrade = 250` even when an exported optimizer XML omits
  it or reflects the NT8 framework default of `20`.
  When Steve sends new DUOrc defaults XML, sync both NT8 `Strategies/DUOrc.cs`
  and Trader `src/Trader.Strategies.Duorc.Core/DuorcStrategyCore.cs` in the same
  pass unless the user explicitly asks for only one side. Audit both global and
  per-session mappings. The 155 XML includes TP-percent trigger/move fields and
  no retired secondary-entry fields. DUOrc's additive EMA slope filter fields
  (`Asia2/Asia3/London/NewYork2/NewYork4/NewYork5 EnableEmaSlopeFilter` and
  `MinEmaSlopeNorm`) must stay hidden from the NT8 UI (`[Browsable(false)]`)
  while remaining `[NinjaScriptProperty]` inputs with internal defaults. Future
  Steve XML exports may omit those hidden settings; use Steve's explicit slope
  table/source defaults instead of assuming the latest XML contains them. Steve's
  v32/v148 DUOrc slope retune enables only Asia3 at `0.0096` and London at
  `0.0097`; Asia2, NewYork2, NewYork4, and NewYork5 stay disabled at `0.01`.
  The per-session `TakeProfitPostTriggerPriceTrail` Boolean is also hidden and
  may be absent from XML exports; preserve Steve-confirmed defaults as true for
  Asia2, NewYork2, and NewYork5, and false for the other DUOrc sessions unless
  he explicitly retunes that hidden field.
- 2026-07-11: DUOrc has fixed rollover blackout dates for the Mar/Jun 2026
  playback windows plus the next four quarterly contract rollover windows:
  Sep/Dec 2026 and Mar/Jun 2027, using the Steve-provided +/-4 calendar-day
  rule with Saturdays omitted. Mar 19, 2026 is also blocked because Steve later
  confirmed that filled data should be excluded. Keep this aligned in both NT8
  `Strategies/DUOrc.cs` and Trader DUOrc. The blackout blocks new entries,
  delayed entry variance, and stop-out flip entries only; it does not
  force-flatten an already-open position.
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
- `AddOns/NRDToCSV.cs` converts replay `.nrd` files and now includes an
  `Audit replay coverage` mode that writes timestamped `replay-audit-*.csv` and
  `.txt` reports for missing, partial, duplicate, and suspicious replay days.
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
- Do not recreate separate DUO/DUOrc testing variants for normal DUO/DUOrc
  work. Active DUO/DUOrc changes belong in `Strategies/DUO.cs` and
  `Strategies/DUOrc.cs`.
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
