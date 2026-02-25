# Integration Checklist

## Core
- [ ] Session background draw exists
- [ ] No-trades-after draw/check exists
- [ ] Skip draw/check exists
- [ ] News draw/check exists (conditional on `UseNewsSkip`)
- [ ] Entry path blocks in skip/news/no-trades-after windows
- [ ] Transition to blocked window cancels orders
- [ ] Transition to blocked window flattens open position
- [ ] `IsLastBarOfSession()` guard used in entry readiness
- [ ] Primary timeframe validator method exists (for example `ValidateRequiredPrimaryTimeframe`)
- [ ] Primary timeframe validation is called during `State.DataLoaded`
- [ ] Primary instrument validator method exists (for example `ValidateRequiredPrimaryInstrument`)
- [ ] Primary instrument validation is called during `State.DataLoaded`
- [ ] Invalid timeframe or instrument triggers early return in `OnBarUpdate`
- [ ] Invalid configuration path cancels working orders and flattens open position (`InvalidConfiguration` preferred)
- [ ] Invalid timeframe warning is user-visible once (popup and/or explicit log)
- [ ] Invalid instrument warning is user-visible once (popup and/or explicit log)

## Infobox
- [ ] Canonical order: Header > Contracts > News > Session > Footer
- [ ] Contracts first line after header
- [ ] News directly above Session
- [ ] Session directly above footer
- [ ] No strategy-specific rows copied from reference strategy
- [ ] `UseNewsSkip=false` => one row `News: Disabled`
- [ ] `UseNewsSkip=true` and no events => blocked icon row (default `News: 🚫` or approved equivalent)
- [ ] Empty-week blocked icon value brush is red (`Brushes.IndianRed`)
- [ ] Passed news rows faded
- [ ] Contracts/News/Session values use same light value color
- [ ] Emoji icon values use emoji-capable rendering path and do not regress to white/gray fallback

## Optional Webhooks
- [ ] Provider selection exists
- [ ] Entry webhook events mapped
- [ ] Exit webhook events mapped
- [ ] Cancel webhook events mapped
- [ ] Missing-config safe no-op behavior

## Suggested Verification Greps
```bash
rg -n "BuildInfoLines|RenderInfoBoxOverlay|EnsureInfoBoxOverlay" <target>
rg -n "UseNewsSkip|NewsBlockMinutes|GetCurrentWeekNews|PassedNewsRowBrush" <target>
rg -n "CancelAllOrders|ExitAllPositions|IsLastBarOfSession" <target>
rg -n "DrawSession|DrawSkip|DrawNews|NoTradesAfter" <target>
rg -n "ValidateRequiredPrimaryTimeframe|isConfiguredTimeframeValid|ValidateRequiredPrimaryInstrument|isConfiguredInstrumentValid|InvalidConfiguration|timeframePopupShown|instrumentPopupShown" <target>
rg -n "Webhook|ProjectX|TradersPost|SendWebhook" <target>
```
