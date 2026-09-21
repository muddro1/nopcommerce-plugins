# Attribute combination pricing plugin — design

**Date:** 2026-09-20
**Target:** nopCommerce 4.50 (verified against `release-4.50.4` source), net6.0
**Plugin:** `Nop.Plugin.Misc.AttributeCombinationPricing`, system name `Misc.AttributeCombinationPricing`
**Status:** approved, ready for implementation planning

## Purpose

Correct the storefront price label shown next to a product attribute value
(the `[+$50]` in a dropdown or radio list) when that value is of type
"Associated to product" and the store has set an **Overridden price** on the
matching product attribute combination.

## The bug

A product can offer an attribute (e.g. "Mount") whose values are each linked
to a separate, real product (e.g. a specific mount SKU) via "Associated to
product". Stock nopCommerce shows the associated product's own standalone
price as the adjustment — `[+$50]` — regardless of what the store has set as
the **Overridden price** on that specific attribute combination.

A holster priced at $299 with a Mount attribute whose "Right-handed mount"
value is associated to a $50 product, but whose combination override sets the
bundle price to $320, still shows `[+$50]` next to that option. The true
adjustment is $21. The customer sees a number that doesn't match what they're
about to pay.

**This is a display-only bug.** The total price shown on the page, and the
price actually charged, are already correct — see Architecture below for how
that was confirmed. Only the small per-option label is wrong.

## Root cause, confirmed against `nopSolutions/nopCommerce` at tag `release-4.50.4`

- `PriceCalculationService.GetProductAttributeValuePriceAdjustmentAsync`
  (`Nop.Services/Catalog/PriceCalculationService.cs:455`) returns, for an
  `AssociatedToProduct` value, the associated product's own final price
  (line 482: `adjustment = (await GetFinalPriceAsync(associatedProduct,
  customer)).finalPrice * value.Quantity`). It has no knowledge of attribute
  combinations.
- `ProductModelFactory.PrepareProductAttributeModelsAsync`
  (`Nop.Web/Factories/ProductModelFactory.cs:836`, `protected virtual`, class
  is `public partial class ProductModelFactory : IProductModelFactory`, not
  sealed) is the only caller of that method for storefront display (line 890).
  It builds `ProductAttributeValueModel.PriceAdjustment` — the label text —
  and `PriceAdjustmentValue` — the raw decimal, used by no other code in this
  method.
- The **total price is unaffected**. When the customer changes a selection,
  `_ProductAttributes.cshtml` posts to
  `ShoppingCartController.ProductDetails_AttributeChange`, which calls
  `ShoppingCartService.GetUnitPriceAsync`
  (`Nop.Services/Orders/ShoppingCartService.cs:1397`):
  ```csharp
  var combination = await _productAttributeParser.FindProductAttributeCombinationAsync(product, attributesXml);
  if (combination?.OverriddenPrice.HasValue ?? false)
      // ... uses combination.OverriddenPrice.Value as the base price
  ```
  This is the correct, combination-aware calculation. It already produces the
  right total. The plugin's job is to make the per-value label agree with it,
  not to change how the total is computed.

## The rule

For each product attribute value of type `AssociatedToProduct`, on the one
attribute mapping this v1 targets (see Out of scope):

1. Build the attributes-XML for "only this value selected"
   (`IProductAttributeParser.AddProductAttribute("", mapping, value.Id.ToString())`).
2. Look up `IProductAttributeParser.FindProductAttributeCombinationAsync(product, thatXml)`
   — the same lookup `GetUnitPriceAsync` uses for the real total.
3. If a combination is found and `OverriddenPrice.HasValue`:
   corrected adjustment = `combination.OverriddenPrice.Value − basePrice`,
   where `basePrice` is the product's own final price with no attributes
   selected (what the page shows before the customer touches any control).
   Replace the label with this value, formatted through the same
   `_priceFormatter` / `_taxService` / `_currencyService` pipeline the
   original code already uses, so currency, tax-inclusive/exclusive display,
   and sign formatting stay identical to core's conventions.
4. Otherwise: leave the label exactly as core computed it. A value with no
   matching override combination keeps showing the associated product's own
   price — today's behavior, which is correct for a plain bundle that has no
   combination-level override.

Expressed as the pure function at the centre of the design:

```csharp
decimal? TryGetCorrectedAdjustment(decimal basePrice, decimal? combinationOverriddenPrice)
    => combinationOverriddenPrice.HasValue
        ? combinationOverriddenPrice.Value - basePrice
        : null;
```

A `null` result means "no correction — use core's original label."

## Decisions and rationale

### Override the display factory, not `IPriceCalculationService`

`GetProductAttributeValuePriceAdjustmentAsync` is also called from the real
pricing path (`GetUnitPriceAsync`'s additional-charge branch, used when *no*
combination override applies). Changing what it returns risks changing real
order totals for products that rely on the plain associated-product-price
behavior. Overriding `ProductModelFactory.PrepareProductAttributeModelsAsync`
instead confines every change to storefront display: nothing it touches feeds
into cart, checkout, order, tax, or payment calculations. This keeps the
plugin's entire risk surface at "a label might be wrong," never "an order
might be mispriced."

### Baseline is the product's own price with no attributes applied

This matches what `GetUnitPriceAsync` computes when `attributesXml` is empty,
which is what the page shows before the customer has touched any attribute
control. Using any other baseline (e.g. "currently displayed price," which
can already reflect a different in-progress selection) would make the label
drift from the number the customer saw a moment before clicking.

### Show the corrected delta, not a suppressed label

Confirmed with the store owner: show the real number (e.g. `+$21.00`) rather
than hiding the `[+$X]` tag entirely for overridden values. The tradeoff is
that the label and the live AJAX-updated total are computed by two separate
code paths (this plugin's label computation vs. `GetUnitPriceAsync`), so if a
customer-group price, tier price, or discount shifts the *base* price between
page load and the moment the customer clicks, the two numbers could
theoretically disagree for that one request. In the common case both read the
same base price at the same moment, so they agree. This is an accepted,
documented risk, not a defect — see Risks below.

### Scoped to a single relevant attribute per product (v1)

The store's actual affected products (e.g. the holster) have exactly one
attribute whose values need correcting. The label is computed once, against
the product's default state, at page load. It is **not** recomputed if the
customer changes a *different* attribute afterward — v1 does not hook the
`ProductDetails_AttributeChange` AJAX handler at all. For a product where a
second attribute's selection changes which combination (and therefore which
override) applies to the first attribute's values, the label can go stale
after the customer changes that second attribute. This only matters for
products with multiple interacting attributes, which the target products
don't have. See Out of scope.

### Automatic, store-wide, no configuration screen

The fix applies to any product where this pattern occurs — an
`AssociatedToProduct` value whose single-value combination has an
`OverriddenPrice` set. There is no incorrect case this could introduce: a
product with no such combination is untouched. A settings/config screen would
add surface area for a plugin whose entire job is "compute the number
correctly."

## Architecture

### Verified prerequisites

| Requirement | Finding |
| --- | --- |
| `ProductModelFactory` inheritable | `public partial class ProductModelFactory : IProductModelFactory`, not sealed |
| `PrepareProductAttributeModelsAsync` overridable | `protected virtual`, `ProductModelFactory.cs:836` |
| `ProductAttributeCombination.OverriddenPrice` field | `decimal?`, `Nop.Core/Domain/Catalog/ProductAttributeCombination.cs:46` |
| Combination lookup available to a plugin | `IProductAttributeParser.FindProductAttributeCombinationAsync(Product, string, bool)`, `ProductAttributeParser.cs:679` — public interface method |
| Attributes-XML builder available | `IProductAttributeParser.AddProductAttribute(string, ProductAttributeMapping, string, int?)`, `ProductAttributeParser.cs:462` — public interface method |
| Plugin can win the DI registration | Core registers `IProductModelFactory` in `Nop.Web.Infrastructure.NopStartup` at `Order => 2002` (`NopStartup.cs:67,127`). `HandlingFee`'s `NopStartup` already establishes `Order => 3000` beats it, for `ICheckoutModelFactory` at the same core `Order`. Same mechanism applies here. |
| Real total already combination-aware | `ShoppingCartService.GetUnitPriceAsync`, `ShoppingCartService.cs:1397`, checks `combination.OverriddenPrice.HasValue` before falling back to per-value adjustments |

### Components

**`AttributeCombinationPriceAdjustmentCalculator`** — the pure function
above (`TryGetCorrectedAdjustment`). No nopCommerce dependencies; takes the
two decimals it needs as parameters. Directly unit-testable.

**`AttributeCombinationProductModelFactory : ProductModelFactory`** —
overrides `PrepareProductAttributeModelsAsync`. Calls
`base.PrepareProductAttributeModelsAsync(product, updatecartitem)` to get
core's model (including today's label for every value), then for each
`ProductAttributeValueModel` whose underlying `ProductAttributeValue` is
`AttributeValueTypeId == AttributeValueType.AssociatedToProduct`:
re-fetches the raw `ProductAttributeValue` (needed because the returned model
carries no `AttributeValueTypeId`/`AssociatedProductId`) via
`IProductAttributeService.GetProductAttributeValuesAsync(attributeModel.Id)`,
builds the single-value attributes-XML, looks up the combination, and — when
`AttributeCombinationPriceAdjustmentCalculator.TryGetCorrectedAdjustment`
returns non-null — overwrites `PriceAdjustment` (formatted string) and
`PriceAdjustmentValue` (raw decimal) on that value model in place. Values
with no matching override, and `Simple`-type values, are left as the base
class produced them.

**`AttributeCombinationPricingStartup : INopStartup`** — registers
`services.AddScoped<IProductModelFactory, AttributeCombinationProductModelFactory>()`.
`Order => 3000`, matching the `HandlingFee` precedent for beating core's 2002.

No settings class, no controller, no config view — nothing to configure.

## Edge cases

| Case | Behaviour |
| --- | --- |
| Value is `AssociatedToProduct`, matching combination has `OverriddenPrice` set | Label shows `OverriddenPrice − basePrice`, signed |
| Value is `AssociatedToProduct`, no matching combination exists | Label unchanged — associated product's own price, as today |
| Value is `AssociatedToProduct`, matching combination exists but `OverriddenPrice` is null | Label unchanged — same as "no matching combination" |
| Value is `Simple` (plain price adjustment or percentage) | Untouched — this plugin only ever looks at `AssociatedToProduct` values |
| Corrected delta is exactly zero | Falls into core's existing "no `+`/`-` prefix" branch for a zero adjustment, same as any other value that costs nothing extra |
| Corrected delta is negative | Shows with a `-` prefix, same formatting core already applies for a negative `Simple` adjustment |
| `DisplayPrices` permission denied for the customer | No label is built at all — core's own permission check (`ProductModelFactory.cs:886`) already skips the whole block; the override never runs |
| Product has a second, unrelated attribute | That attribute's values are untouched; only `AssociatedToProduct` values anywhere on the product are candidates, each evaluated independently |
| Customer changes a different attribute after page load | Label for the corrected value does not recompute — known v1 limitation, not a regression from core's existing (also static) label behavior |

## Testing

Same style as `BetterSearch`/`HandlingFee` — NUnit against faked
`IProductAttributeService`, `IProductAttributeParser`, `IPriceCalculationService`
and the formatting services (all interfaces), no database or nopCommerce host
needed.

- **`AttributeCombinationPriceAdjustmentCalculatorTests`** — pure function:
  combination found with a price above/below/equal to base → correct signed
  delta; no combination found → null; combination found with null
  `OverriddenPrice` → null.
- **`AttributeCombinationProductModelFactoryTests`** — drives the overridden
  `PrepareProductAttributeModelsAsync` against fakes: a product with one
  `AssociatedToProduct` value backed by an override combination gets a
  corrected label; a second value with no override combination keeps its
  original label; a `Simple`-type value on the same product is never touched;
  a product with no combinations at all falls through unchanged for every
  value (regression guard against breaking products that don't use this
  pattern).

## Risks and maintenance

**Label/total divergence under a moving base price.** If a tier price,
customer-group price, or an active discount changes the product's base price
between page load and the moment the customer changes the selection, the
statically-computed label and the AJAX-recomputed total are produced by
separate calls and could disagree for that one page view. A full-page reload
recomputes both from the same current state. Documented in the plugin's
`Readme.txt`; not fixed in v1 because the target products don't have prices
that move within a single page view.

**Static per page load, not per attribute change.** As decided above, the
label does not re-run if the customer changes a different attribute
afterward. If a future product needs that, it requires hooking
`ShoppingCartController.ProductDetails_AttributeChange` as well, which is a
separate, larger change (see Out of scope).

**Core service override generally.** Any other installed plugin that also
replaces `IProductModelFactory` will conflict with this one; whichever has
the higher `INopStartup.Order` wins outright, silently.

## Out of scope

Deliberately excluded to keep the first version small:

- Live recomputation of the label via the `ProductDetails_AttributeChange`
  AJAX handler when a *different* attribute changes. Needed only for products
  with multiple interacting attributes; none of the currently affected
  products have that shape.
- Any configuration screen or per-product opt-out.
- Percentage-type combination overrides — `OverriddenPrice` is always an
  absolute amount in nopCommerce, so this doesn't arise.
- Correcting the equivalent label anywhere outside the storefront product
  details page (e.g. admin "add product to order" screens), which use a
  different, unrelated model-building path.
