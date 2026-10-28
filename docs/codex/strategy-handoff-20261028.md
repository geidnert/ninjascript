# Strategy Handoff — 2026-10-28

This is the short starting document for a new Codex account or a future agent.
It is a map, not a replacement for source code or the detailed research notes.

## Start here

1. Open the same local folders:
   - NT8 source: `/Volumes/Users/Andreas/Documents/NinjaTrader 8/bin/Custom`
   - TraderPro research: `/Users/andreas.geidnert/Dev/Trader`
2. In the NT8 folder, read `AGENTS.md` and `docs/codex/repo-notes.md` before
   changing code.
3. In the TraderPro folder, read `AGENTS.md`, `docs/codex/repo-memory.md`, and
   `docs/strategy-optimization-playbook.md` before running or changing research.
4. For licensing-server work, read
   `/Users/andreas.geidnert/Dev/autoedge-licensing-server/AGENTS.md`,
   `docs/codex/project-memory.md`, and `README.md` before changing anything.
5. Treat NinjaTrader Playback and compilation as the authority for NT8 behavior.
   A local `dotnet build` does **not** compile NinjaScript.
6. Inspect `git status --short` first. This working tree routinely contains
   unrelated user changes; preserve them.

## Current priority: WickRejectionBreakout

Source: `Strategies/WickRejectionBreakout.cs`
Research detail: `/Users/andreas.geidnert/Dev/Trader/docs/codex/wick-rejection-breakout-research.md`

This is a standalone five-minute NQ/MNQ rejection-wick prototype. It identifies
a rejection candle, then permits an entry only on the immediately next candle
when price breaks in the direction away from the rejection wick.

Current NT8 defaults / expected Playback test profile:

| Setting | Value |
| --- | --- |
| Chart | NQ, 5 minute |
| Minimum wick | 2 points |
| Minimum body | 3 points |
| Maximum opposite wick | 0.25 points |
| Breakout level | Body |
| Entry timing | Intrabar/on each tick |
| Target / fixed stop | 40 / 70 points |
| Session-close exit | On |
| New York skip slots | 20:00:00–00:59:59 and 07:00:00–09:59:59 |
| Third skip slot | Off by default; 12:00:00–15:59:59 is research-only |

Important current behavior:

- Skip windows are converted to New York time using Windows `Eastern Standard
  Time`, so they follow US DST even during Sweden/US mismatch weeks.
- Historical chart bars are warm-up/display only for Playback/broker/Sim
  instances; they must not create managed historical fills when a strategy is
  re-enabled. Strategy Analyzer `Backtest` retains its historical-fill path.
- Historical signal drawings are display-only. Gold `◆ TP` and red `■ SL`
  markers identify actual live managed fills after the marker change; do not
  claim historical OHLC visual markers are real fills.
- Green/red chart background is schedule-only: green means entry time is open;
  red is an enabled skip-time break. It does not promise a wick setup.
- Daily account-currency profit/loss caps use account net liquidation, include
  unrealized P&L, and reset at the next 18:00 ET CME trading-day boundary.

Research conclusions to preserve:

- The five-minute NQ candidate is an observed research result, not a live
  approval. The same profile failed to transfer to the available MNQ data.
- Current time filters and the 3-point minimum-body profile need independent
  NT8 Playback/new-data verification before any promotion.
- EMA/VWAP early exits were rejected. RSI(23) 58.5/41.5 is only a small,
  post-selected Playback A/B hypothesis, not an NT8 setting.
- A 0.25–10-point extra breakout distance and close-beyond confirmation were
  tested in TraderPro and rejected. Preserve the unbuffered intrabar Body
  trigger unless future untouched data changes that conclusion.
- Do not add unvalidated loss filters merely because one chart trade looks
  poor; test one causal family at a time against a frozen baseline.

## Production/reference strategies

| Strategy family | Role and caution |
| --- | --- |
| `EMAL.cs` | Mature multi-session EMA strategy with ProjectX/TradersPost behavior, safety layers, an action-rate guard, daily limits, and historical-to-realtime protections. Preserve the established managed-order and ProjectX sequence. Read the EMAL section of `repo-notes.md` before touching it. |
| `DUO.cs`, `DUOrc.cs` | Canonical references for shared bot patterns, sessions, skip/news windows, heartbeat, and some ProjectX behavior. They are exceptions to the normal public/testing pair convention: do **not** create separate testing variants unless explicitly requested. These source files were not present in the current mounted tree; locate the actual source before copying a DUO feature. |
| `DUOlo.cs` | Separate DUO limit-entry variant. It is not interchangeable with DUO; preserve its shadow-order lifecycle. |
| Paired HUGO/MICH/ORBO families | Public and `*Testing` variants. Use the repository testing-to-public workflow for any public release; never selectively copy methods. |

## Research / prototype strategies

| Strategy | Status |
| --- | --- |
| `SessionBrakeout.cs` | Five-minute NQ/MNQ session-range breakout prototype. Uses New York sessions and completed-bar decisions. Needs NT8 compile and Playback validation. |
| `ConsolidationBrakeout.cs` | Visual/research consolidation breakout. Defaults are chart-validation settings, not performance-selected. |
| `RsiThresholdDivergence.cs` | Research-only RSI/pivot divergence strategy with an auto-hosted visual indicator. |
| `OrderFlow.cs` | Research-only session-profile/order-flow prototype. Its first MNQ candidate failed holdouts; do not promote or retune the same observed data. |
| `RECOIL.cs` / `RECOILNQEDGE.cs` | Parameter-locked Market Replay ports. Preserve their separate documented parity protocol. |
| `E9.cs`, `PULL.cs`, `Brakeout.cs`, `EMAS.cs`, `FIFT.cs`, `VWAPS.cs`, and RSI/EMA experiments | Treat as prototypes unless their own note says otherwise. Do not describe them as live-approved without independent evidence. |

## TraderPro application and research repository

Repository: `/Users/andreas.geidnert/Dev/Trader`
Primary memory: `docs/codex/repo-memory.md`

TraderPro is the .NET 10 product and research workspace. Its main applications
are:

| Area | Location / role |
| --- | --- |
| Customer desktop app | `src/Trader.App.Desktop` — live/replay sessions, connections, profiles, packages, licensing, and updates. |
| Research desktop app | `src/Trader.Lab.Desktop` — imports, experiment inspection, sweeps, and comparisons. |
| CLI | `src/Trader.App.Cli` — headless import/analyze/run/sweep/profile/compare commands. |
| Research runners | `tools/Trader.*Research` — isolated discovery experiments, including `Trader.WickRejectionBreakoutResearch`. |
| Strategy/runtime libraries | `src/Trader.*` — indicators, recordings, execution, hosting, analyzers, strategy SDK, and individual strategy projects. |

TraderPro handoff rules:

- Its GitHub branch is the code recovery source. For completed work: test,
  update durable notes, commit only intended files, push, and run
  `bash scripts/recovery-doctor.sh`.
- Never use the current observed NQ Wick research as a live approval. The
  runner uses completed trade-print OHLC bars, not raw-tick NT8 parity.
- Preserve the staged optimization discipline: freeze a baseline; test one
  causal group at a time; retain chronological development/validation metrics;
  do not select a result from the same already-viewed data as if it were new.
- Local recording/replay stores can be large and are not Git artifacts. Read
  the data-root and coverage notes in `repo-memory.md` before deleting, moving,
  or combining them.
- Do not add GitHub Actions or cloud CI without explicit authorization. The
  repository's zero-cost policy is intentional.

## AutoEdge licensing and entitlement server

Repository: `/Users/andreas.geidnert/Dev/autoedge-licensing-server`
Primary memory: `docs/codex/project-memory.md`
Operational guide: `README.md`
Recovery contract: `docs/disaster-recovery.md`

This is the production-facing Python WSGI + SQLite entitlement control plane.
It receives Whop updates, records customer/product/subscription/device state,
serves licensing and release decisions to TraderPro/NT8, and provides the admin
UI. It is security-sensitive infrastructure, not a strategy-research project.

Non-negotiable server rules:

- Never commit, paste into chat, or copy private `.env` values, databases,
  artifact bundles, SSH keys, HMAC secrets, Whop secrets, OAuth secrets, or
  online/offline signing private keys.
- Preserve raw-body Whop webhook verification, hashed license keys and machine
  fingerprints, signed admin sessions, explicit client blocking statuses, and
  server-side lease signing. These are client contracts.
- Production timestamps/API responses are UTC; admin time inputs and displays
  use US Eastern time.
- The online license key and offline release-signing key are separate. Public
  keys can be versioned; private keys must remain in encrypted recovery storage
  outside Git and outside release archives.
- Do not touch production customers, entitlements, licenses, release rows,
  signing keys, backups, deploy configuration, or Whop/Tradovate state unless
  the user explicitly authorizes that exact external action.

Before completing server changes, run the relevant tests (normally
`python3 -m unittest discover -s tests`), then run
`./scripts/check_recovery_readiness.sh`. Commit and push source/documentation
changes, while reporting any intentionally local or secret state. The disaster
recovery guide requires encrypted off-host database/environment/artifact
backups plus separately protected release-key recovery; code alone is not a
complete server recovery.

## Non-negotiable safety and workflow rules

- Allowed current instruments are NQ and MNQ unless a strategy explicitly
  documents another contract.
- Entry/exit signals should remain strategy-prefixed internally and protective
  orders must reference the active entry signal. Wick currently uses readable
  external labels `Long`, `Short`, and `Exit` by deliberate user request.
- Do not change webhook timing, ProjectX mirroring timing, or hidden ProjectX
  settings without an explicit request and the applicable source-of-truth spec.
- Do not force a broad cleanup or discard dirty files. Use focused diffs.
- For TraderPro research, freeze a baseline, change one group at a time, retain
  development and chronological-validation metrics, and do not call
  post-selected observed results a validated live profile.

## Recommended first prompt on the new account

> Open the NT8, TraderPro, and AutoEdge licensing-server folders. Read every
> repository's `AGENTS.md`, the NT8 `docs/codex/repo-notes.md`, this handoff,
> TraderPro `docs/codex/repo-memory.md`, the detailed Wick research note, and
> the licensing server's project memory and README. Do not change code yet.
> Report each worktree status, current WickRejectionBreakout defaults, the next
> research/Playback validation step, and any production-sensitive server risks.

## Current uncommitted-work caution

At the time this handoff was written, the NT8 working tree contained multiple
user changes and research files. Recheck status rather than assuming this list
is current. In particular, do not overwrite `NinjaTrader.Custom.csproj`,
`Strategies/EMAL.cs`, or untracked strategy files while resolving another task.
