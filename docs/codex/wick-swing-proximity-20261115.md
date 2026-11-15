# Wick swing proximity, 2026-11-15

`WickRejectionBreakout` now has default-OFF `UseSwingProximityFilter`,
strength2, lookback100 and distance2 points. Shorts require the upper-wick tip
within the inclusive absolute distance of the most recent confirmed swing
high; longs use the lower-wick tip/swing low. Crossing/reclaim is not required.
No older-but-closer fallback; no prior swing means no setup. Both rejection
and live confirmation bars are excluded from pivot confirmation.

`Strategies/NinjaSwingPattern.cs` is standalone, Framework-compatible source
with the same older-equal/newer-strict tie policy as Trader's NinjaSwing.
No external Trader DLL is required. Native's published project needed the
already tracked Wick compile item plus the new helper compile item. Local
EMA/other research changes and active defaults remain untouched and separate.

TP/SL, RSI, negative-time exits and standard next-bar entry are unchanged.
Logs expose pivot level/age, distance, allowed flag and unavailable/far vetoes.
Trader's new research contract is
`docs/codex/wick-swing-proximity-20261115/contract.md`: original published
NQ5m TP11/SL70/RSI23/negative2 baseline, bounded entry-only historical grid.

Focused Trader regression and source syntax checks are not native compilation.
**F5 inside NT8 and matched Playback remain required.** No live/default
promotion follows from these exploratory historical comparisons.

Completed bounded research:104 cases on the user-approved52 clean-source
sessions, same source mask on original/new controls. NQ5m OFF earns+$14,540
stressed (629 trades/$4,990 marked DD); default ON strength2/lookback100/
distance2 loses$925 (33 trades/$2,630 DD). The2m strength1/lookback100/
distance1 development nominee earns+$3,900 overall but fails its250ms
middle-period check. Keep the filter OFF; no profile/default promotion.
Full evidence in Trader `docs/codex/wick-swing-proximity-20261115/results.md`.
