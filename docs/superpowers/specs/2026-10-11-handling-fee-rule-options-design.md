# Handling fee rule options — design

**Date:** 2026-10-11
**Target:** nopCommerce 4.90.8, net9.0 (the 4.90 build only; 4.50 stays at 1.03)
**Plugin:** `Nop.Plugin.Misc.HandlingFee`, system name `Misc.HandlingFee`, version 2.00 → 2.10
**Status:** implemented 2026-10-11 (2.10); awaiting live smoke test

## Purpose

Today the rule that decides when the handling fee applies is mostly fixed in
code. The admin page shows five flat fields and nothing about how they combine.
This design makes the rule configurable from the admin page in four ways:

1. **More rule conditions**: which orders, customers, destinations and
   shipping methods the fee applies to.
2. **Product and category exclusions**: items that waive the fee, or that
   don't count toward it.
3. **Fee amount options**: a flat fee, a percentage of the subtotal, or
   tiered bands.
4. **A live explanation**: a plain-English sentence describing the active
   rule, plus a "try a cart" tester, both updating as the form is edited.

## Non-negotiable: upgrading changes nothing

A store upgrading from 2.00 must charge exactly what it charged before until
someone changes a setting. Every new setting defaults to the value that
reproduces the 2.00 rule:

| New setting | Default | 2.00 equivalent |
|---|---|---|
| Fee type | Flat | Only type there was |
| Require a shipped item | On | Always required |
| Subtotal basis | After discounts | Fixed |
| Shipping methods | All | No filter |
| Countries | All | No filter |
| Exempt customer roles | None | No filter |
| Excluded products / categories | None | No filter |

A test asserts that a default-constructed settings object, given the 2.00
inputs, returns the 2.00 fee for every worked example in `Readme.txt`.

## The rule, extended

The fee applies when **all** of these hold. New conditions are marked ★.

1. The plugin is enabled.
2. ★ The customer has none of the exempt customer roles.
3. ★ (Exclusion mode "waive") The cart contains no excluded product, and no
   product in an excluded category.
   ★ (Exclusion mode "leave out") The cart is not made up entirely of
   excluded items. Otherwise there is nothing left to measure, and a zero
   subtotal must not pass the threshold.
4. The cart contains at least one shipped item. ★ This is now a setting
   (default on). In exclusion mode "leave out", excluded items don't count.
5. ★ The shipping address country is one of the selected countries, or no
   countries are selected.
6. ★ The selected shipping method is one of the selected methods, or no
   methods are selected.
7. The shipping charge is zero, when "No fee when shipping is charged" is on
   (unchanged).
8. The measured subtotal is at or below the threshold. In tiered mode, the
   threshold is the top band's upper limit.

**Measured subtotal:** the goods subtotal excluding tax, either ★ after
discounts (default, as today) or before discounts. In exclusion mode "leave
out" it is the subtotal of the cart *without* the excluded items, computed by
nopCommerce's own subtotal method on the reduced cart, so subtotal-level
discounts are re-evaluated properly rather than approximated.

**Unknown at cart time.** On the cart page the customer may not have chosen a
shipping method or entered an address yet. As with the shipping charge today,
an unknown value counts as passing (conditions 5 and 6). The fee is therefore
shown early and may drop off at checkout once the method or address rules it
out. It never appears late, so the customer is never surprised by a new
charge at the last step.

**Fee amount**, once the rule passes:

- **Flat:** the fee amount (as today).
- **Percentage:** percent × measured subtotal, rounded to 2 places, clamped
  to an optional minimum and maximum, then - when
  `ShoppingCartSettings.RoundPricesDuringCalculation` is on, as core requires
  for its own rounding - passed through
  `IPriceCalculationService.RoundPriceAsync` with no currency argument, which
  applies the primary store currency's rounding rule. The rounding happens in the payment service and the admin preview, so
  the rule itself stays free of nopCommerce services. Because it runs after
  the clamp, a currency that rounds to 0.05 can land a fee up to one rounding
  step above the maximum. Flat and tiered fees are charged exactly as entered,
  so a store upgraded from 2.00 is unaffected.
- **Tiered:** the bands are rows of "subtotal up to X → fee Y", sorted by X.
  The fee is the first band whose X is at or above the measured subtotal. Above
  the top band, there is no fee.

A computed fee of zero or less is still never charged (the current
defence-in-depth rule).

## Architecture

The design keeps the existing split, which is why the fee is cheap to test:

- **`HandlingFeeCalculator` becomes `HandlingFeeRule`.** It is still a pure
  static function with no nopCommerce services, but it now takes a
  `HandlingFeeContext` record (every fact about the cart) and returns a
  `HandlingFeeDecision` record: the fee, plus *which condition decided it*
  (`Applied`, `Disabled`, `ExemptRole`, `ExcludedItem`, `NoShippedItem`,
  `CountryNotIncluded`, `ShippingMethodNotIncluded`, `ShippingCharged`,
  `AboveThreshold`, `ZeroFee`).
- **`HandlingFeeContextFactory`** (new, scoped) gathers the facts from a cart:
  - customer role ids
  - product ids and their category ids (one batched lookup)
  - shipped/not-shipped per item
  - shipping address country
  - selected shipping option
  - subtotals, before and after discounts, and for the reduced cart
  - shipping total

  It does the expensive work only when the earlier cheap conditions haven't
  already ruled the fee out.
- **`HandlingFeePaymentService`** builds the context and calls the rule. That
  is its only change. The order-total and checkout-factory overrides are
  untouched, so the copied core method needs no re-diff.
- **The live explanation and the tester use the same rule.** The admin page
  posts its *unsaved* form values, plus a hypothetical cart, to a new
  `MiscHandlingFee/PreviewRule` action. That action runs `HandlingFeeRule` and
  returns the summary sentence and the decision. No rule logic is duplicated
  in JavaScript.

### Storage

Everything stays in `HandlingFeeSettings` (an `ISettings`). Per-store
overrides keep working the way they do today, and there is no new database
table or migration.

- **Simple values** stay simple properties: fee type (enum), percent, minimum
  and maximum, subtotal basis (enum), require-shipped-item (bool), exclusion
  mode (enum).
- **Id lists** are comma-separated strings: exempt role ids, country ids,
  shipping method ids, excluded product ids, excluded category ids.
- **Tier bands** are a JSON string. The same pattern as `LabelBackupJson`.

## Admin page

Four cards, plus a summary panel that stays visible:

```
┌ Rule summary (live) ───────────────────────────────────────────────────────┐
│ Charge $4.95 when an order with at least one shipped item has a subtotal   │
│ (after discounts) of $50.00 or less and ships for free. Not charged to     │
│ Wholesale customers, or when the cart contains anything from Gift Cards.   │
│                                                                            │
│ Try a cart: subtotal [ 30.00 ]  shipping [ 0.00 ]  [x] has a shipped item  │
│             role [Registered ▾]  country [Any ▾]  method [Any ▾]           │
│             [x] contains an excluded item                                  │
│   → No fee: the cart contains an excluded item (Gift Cards).               │
└────────────────────────────────────────────────────────────────────────────┘
┌ General ───────────────────────────────────────────────────────────────────┐
│ Enabled [x]        Label [Small Order Handling Charge        ]             │
└────────────────────────────────────────────────────────────────────────────┘
┌ Fee amount ────────────────────────────────────────────────────────────────┐
│ Type  (•) Flat   ( ) Percentage of subtotal   ( ) Tiered                   │
│  Flat:        threshold [ 50.00 ]   fee [ 4.95 ]                           │
│  Percentage:  threshold [ 50.00 ]   percent [ 10 ]%  min [ 2.00 ] max [ 6 ]│
│  Tiered:      up to [ 25.00 ] → [ 5.95 ]  [x]                              │
│               up to [ 50.00 ] → [ 3.95 ]  [x]     [+ Add band]             │
└────────────────────────────────────────────────────────────────────────────┘
┌ When the fee applies ──────────────────────────────────────────────────────┐
│ Measure subtotal       (•) after discounts  ( ) before discounts           │
│ Require a shipped item [x]                                                 │
│ No fee when shipping is charged [x]                                        │
│ Only these shipping methods   [ Free shipping, UPS (any service), … ]      │
│ Only these countries          [ multi-select, empty = all ]                │
│ Exempt customer roles         [ multi-select ]                             │
└────────────────────────────────────────────────────────────────────────────┘
┌ Excluded products & categories ────────────────────────────────────────────┐
│ When the cart has excluded items  (•) waive the fee                        │
│                                   ( ) leave them out of the subtotal       │
│ Categories  [ multi-select ]                                               │
│ Products    Gift Card $25, Gift Card $50   [Add products…] (popup picker)  │
└────────────────────────────────────────────────────────────────────────────┘
```

- Only the fields for the chosen fee type are shown. The others are hidden,
  not cleared, so switching type and back loses nothing.
- Multi-selects use the admin's standard `nop-select` with `asp-multiple`. The
  product picker reuses the popup pattern from the discount plugin.
- Every rule field keeps its per-store override checkbox.
- The summary and the tester refresh on change, debounced at 300 ms.

### Validation

- Percent: between 0 and 100. Minimum: at most the maximum.
- Tier bands: positive upper limits, no duplicates, and fees above zero.
- At least one band in tiered mode.
- Flat fee: above zero, as today.

## Resolved questions

1. **Shipping methods.** Stores normally ship for free, with optional UPS or
   FedEx upgrades. Carrier options such as "UPS Ground" are named on the fly,
   so the filter offers two kinds of entry:
   - **a whole provider**, e.g. "UPS (any service)", matched against the
     selected option's `ShippingRateComputationMethodSystemName`
   - **a named shipping method** from Configuration → Shipping → Shipping
     methods (the free method, for instance), matched by option name

   They are stored as tokens (`provider:Shipping.UPS`, `method:5`) in one
   comma-separated setting. A typical setup is "only the free method", which
   says outright that a paid UPS or FedEx upgrade never carries the fee, on
   top of what "No fee when shipping is charged" already does.
2. **Unknown values on the cart page** count as passing (confirmed).

## Testing

- Unit tests for `HandlingFeeRule`, covering:
  - every decision reason
  - each fee type, with tier edges (exactly on a band, above the top band,
    min/max clamps)
  - both exclusion modes
  - the "upgrading changes nothing" equivalence test
- Unit tests for `HandlingFeeContextFactory`, covering the reduced-cart
  subtotal, category expansion and unknown address/method, using the existing
  totals harness.
- The existing 29 tests are kept and adapted to the new signature.
- `TESTING.md` gains manual scenarios for each new setting.
- A live smoke test on a 4.90 store before release, which also covers the
  2.00 port, still untested on a live store.
