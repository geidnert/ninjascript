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
- [ ] Invalid primary timeframe triggers early return in `OnBarUpdate`
- [ ] Invalid primary timeframe path cancels working orders and flattens open position (`InvalidTimeframe`)
- [ ] Invalid timeframe warning is user-visible once (popup and/or explicit log)

## Infobox
- [ ] Canonical order: Header > Contracts > Strategy-specific > News > Session > Footer
- [ ] Contracts first line after header
- [ ] News directly above Session
- [ ] Session directly above footer
- [ ] `UseNewsSkip=false` => one row `News: Disabled`
- [ ] Passed news rows faded
- [ ] Contracts/News/Session values use same light value color

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
rg -n "ValidateRequiredPrimaryTimeframe|isConfiguredTimeframeValid|InvalidTimeframe|timeframePopupShown" <target>
rg -n "Webhook|ProjectX|TradersPost|SendWebhook" <target>
```
