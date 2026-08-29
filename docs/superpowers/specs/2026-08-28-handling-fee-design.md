# Handling fee plugin — design

**Date:** 2026-08-28
**Target:** nopCommerce 4.50.2, net6.0
**Plugin:** `Nop.Plugin.Misc.HandlingFee`, system name `Misc.HandlingFee`
**Status:** approved, ready for implementation planning

## Purpose

Charge a configurable handling fee on small physical orders that ship for free.

The fee covers the cost of picking, packing and posting an order too small to
carry that cost itself. It therefore applies only where physical handling
actually happens and where the customer is not already paying a shipping
charge that covers it.

## The rule

The fee applies when **all** of the following hold:

1. The plugin is enabled.
2. The cart requires shipping — at least one item is ship-enabled.
3. The goods subtotal, after item-level and subtotal-level discounts, is at or
   below the configured threshold.
4. The shipping charge is zero — provided the `SuppressWhenShippingCharged`
   setting is on, which is the default. With that setting off, condition 4 is
   skipped and the fee applies regardless of the shipping charge.

Otherwise the fee is zero.

A null shipping total — which is what nopCommerce reports before a shipping
method has been selected — counts as zero for condition 4, not as unknown. This
is what makes the fee visible on the cart page.

Expressed as the pure function at the centre of the design:

```csharp
decimal Calculate(decimal goodsSubtotalAfterDiscounts, decimal? shippingTotal, bool cartRequiresShipping)
```

| Condition | Result |
| --- | --- |
| not enabled | `0` |
| `!cartRequiresShipping` | `0` |
| `goodsSubtotalAfterDiscounts > Threshold` | `0` |
| `SuppressWhenShippingCharged && shippingTotal > 0` | `0` |
| otherwise | `FeeAmount` |

"At or below" means `<=`: a subtotal exactly equal to the threshold attracts
the fee.

## Decisions and rationale

### Threshold is measured on the goods subtotal after discounts

Shipping, tax, the fee itself, gift cards and reward points are all excluded
from the figure the threshold compares against.

Two of those exclusions are forced rather than chosen:

- **The fee itself must be excluded**, or the calculation oscillates. With a
  £50 threshold and a £4.95 fee, a £49 cart becomes £53.95, which is above the
  threshold, so the fee is removed, which returns it to £49, and so on.
- **Shipping is excluded** because a separate rule already suppresses the fee
  whenever shipping is charged. Including shipping in the basis as well would
  express the same idea twice.

Discounts *are* applied before the comparison, so a £100 basket discounted to
£40 counts as £40 and attracts the fee. The threshold tracks what the customer
is actually spending on goods.

### Gift cards and reward points never move the threshold

In nopCommerce's total calculation, gift cards and reward points are deducted
at the very bottom of the pipeline, after the order total is otherwise final.
They are ways of paying, not reductions in what the order is worth. The
threshold is measured well above them and cannot see them.

| Goods subtotal | Gift card | Threshold sees | Fee |
| --- | --- | --- | --- |
| £100 | £80 | £100 — above | none |
| £100 | £100 | £100 — above | none |
| £30 | £80 | £30 — at or below | charged |
| £30 | none | £30 | charged |

Both directions were confirmed explicitly: a large order does not become
fee-eligible because store credit covers most of it, and a small order does not
escape the fee by being paid with a gift card.

### Store credit can pay the fee

The fee joins the order total before gift cards and reward points are
deducted, exactly as nopCommerce's own payment method fee does. A customer with
sufficient credit pays nothing out of pocket; the fee is still recorded against
the order.

### Any paid shipping suppresses the fee entirely

Not pro-rated and not conditional on the shipping charge covering the fee. A
£1.50 shipping charge suppresses a £4.95 fee in full. This is the simplest rule
to explain to a customer and to support staff, and avoids residual amounts like
£3.45 appearing on orders.

### Downloadable and virtual orders are exempt

A cart with no ship-enabled item incurs no fee regardless of value, because no
physical handling takes place.

Detection uses `IShoppingCartService.ShoppingCartRequiresShippingAsync`, which
is an `Any()` over the cart. A mixed basket therefore counts as requiring
shipping, and the threshold is measured on the **whole** subtotal including any
downloadable items. A £10 download plus a £20 physical item is a £30 order for
threshold purposes.

### The fee is visible from the cart page

Shipping is not known until partway through checkout, so the fee appears on the
cart page and is removed once the customer selects a paid shipping method. The
total therefore only ever moves in the customer's favour.

The alternative — hiding the fee until shipping is known — was rejected because
it introduces a surprise charge late in checkout, which harms conversion and
sits badly with price-display expectations.

This decision is the sole reason for the second service override in the
architecture below. Dropping the requirement would remove that override and its
maintenance cost.

### Tax is delegated to the store's tax provider

The fee carries no tax settings of its own. It rides nopCommerce's existing
payment-fee tax path, which reads `TaxSettings.PaymentMethodAdditionalFeeIsTaxable`
and `TaxSettings.PaymentMethodAdditionalFeeTaxClassId`, then calls
`GetProductPriceAsync` — which routes to whichever `ITaxProvider` is installed.

The store's tax plugin decides the rate, including returning zero where the fee
is out of scope. Taxability can be switched off from
**Configuration → Tax settings** without touching the plugin.

## Architecture

### Why the payment-fee rail

nopCommerce 4.50 has no generic "extra fee" concept:

- The `Order` entity has exactly one pair of columns for a non-shipping
  surcharge, `PaymentMethodAdditionalFeeInclTax` / `PaymentMethodAdditionalFeeExclTax`.
- `Views/Shared/Components/OrderTotals/Default.cshtml` contains no widget zone,
  so a display line cannot be injected by a widget.
- View overriding in 4.50 is theme-based (`ThemeableViewLocationExpander`), not
  plugin-based, so a plugin cannot cleanly replace that view.

Reusing the payment-fee channel therefore gets the following with no additional
code: correct tax treatment, persistence to the order, reconciliation of the
order's stored components against `OrderTotal`, and display on the cart,
checkout, order confirmation, order details page, admin order page, email
templates and PDF invoice.

Alternatives considered and rejected:

- **A first-class handling fee** stored as a generic attribute. Clean semantics
  and its own label, but the order's stored components stop summing to
  `OrderTotal`, leaving an unexplained gap in the admin order page, PDF invoice
  and accounting exports. Cart display would still need a theme-level view
  override.
- **An auto-managed priced checkout attribute.** Avoids overriding core
  services, but checkout attribute prices land inside the subtotal, which is
  the threshold basis, reintroducing the circularity designed out above. Also
  needs synchronising on every cart mutation.

### Feasibility: this is a plugin, not a core change

No modification to nopCommerce source is required. Every extension point the
design depends on was verified against the 4.50.2 source.

**Precedent — cited as evidence only, not a dependency.** The plugin below is
referenced solely to show that replacing a core service from a plugin is a
supported technique. It is unrelated to handling fees, is not required, and
need not be installed.

`Nop.Plugin.Misc.Sendinblue` ships with nopCommerce and integrates an
email-marketing service. To route transactional email through that service it
subclasses the core `WorkflowMessageService` and re-registers it from the
plugin's own `NopStartup`:

```csharp
services.AddScoped<IWorkflowMessageService, SendinblueMessageService>();
services.AddScoped<IEmailSender, SendinblueEmailSender>();
public int Order => 3000;
```

Ten of the shipped plugins implement `INopStartup`. Subclassing a core service
and replacing its registration from a plugin is how the nopCommerce team does
it themselves, so `Order => 3000` is adopted here for consistency.

**Verified prerequisites.**

| Requirement | Finding |
| --- | --- |
| `OrderTotalCalculationService` inheritable | `public partial class`, not sealed |
| `PaymentService` inheritable | `public partial class`, not sealed |
| `GetAdditionalHandlingFeeAsync` overridable | `public virtual`, line 153 |
| `GetShoppingCartTotalAsync` overridable | `public virtual`, line 1190 |
| Base constructors callable from a subclass | both `public` |
| Plugin assemblies scanned for `INopStartup` | yes, via `typeFinder.FindClassesOfType<INopStartup>()` |

**Load ordering**, the one genuine risk, checks out.
`ServiceCollectionExtensions.ConfigureApplicationServices` calls
`InitializePlugins` at line 68, before the type finder is registered at line 70
and before startup classes are discovered. Plugin assemblies are therefore
loaded by the time nopCommerce looks for `INopStartup` implementations, and a
later `AddScoped` registration wins the resolve.

**Uninstall caveat.** `INopStartup` runs for any plugin present in the
`\Plugins` folder, whether or not it is installed. Uninstalling the plugin does
not remove the service overrides; only deleting the folder and restarting does.
The `Enabled` check must therefore be the first thing `HandlingFeeCalculator`
evaluates, so that a disabled or uninstalled plugin is a true no-op that
returns the base fee unchanged.

### Components

**`HandlingFeeSettings : ISettings`** — `Enabled`, `ThresholdAmount`,
`FeeAmount`, `SuppressWhenShippingCharged` (default `true`). Store-scoped, as
nopCommerce settings are by default.

**`HandlingFeeCalculator`** — the pure function above. No nopCommerce
dependencies, so it is directly unit-testable and cannot participate in a DI
cycle.

**`HandlingFeePaymentService : PaymentService`** — overrides
`GetAdditionalHandlingFeeAsync` to return `base + Calculate(...)`.

**`HandlingFeeOrderTotalCalculationService : OrderTotalCalculationService`** —
overrides `GetShoppingCartTotalAsync` for one reason only: to drop the
`!string.IsNullOrEmpty(paymentMethodSystemName)` guard, which otherwise hides
the fee whenever no payment method has been selected, including on the cart
page.

**`HandlingFeeStartup : INopStartup`** — `Order` above 2000 so both
registrations win over core's, which registers `IOrderTotalCalculationService`
in `NopStartup.ConfigureServices`.

**Admin configuration** — a standard plugin config page and controller for the
four settings, plus locale resources.

### Avoiding the DI cycle

`HandlingFeeOrderTotalCalculationService` depends on `IPaymentService`, and the
fee decision needs subtotal and shipping figures that come from
`IOrderTotalCalculationService`. Constructor-injecting both directions would
make the container throw a circular-dependency error at startup.

`HandlingFeePaymentService` therefore resolves `IOrderTotalCalculationService`
**lazily via `IServiceProvider`** at call time rather than in its constructor.

There is no *runtime* recursion to worry about: `GetShoppingCartSubTotalAsync`
and `GetShoppingCartShippingTotalAsync` contain no reference to
`_paymentService`, so the fee calculation cannot re-enter itself.

`IShoppingCartService`, needed for `ShoppingCartRequiresShippingAsync`, has no
payment or order-total dependency and can be constructor-injected safely.

## Edge cases

| Case | Behaviour |
| --- | --- |
| Subtotal exactly at threshold | Fee applies — the comparison is `<=` |
| Shipping not yet known (cart page, no address) | Treated as no shipping charge, so the fee shows |
| Downloadable or virtual only | No fee, at any value |
| Mixed physical and downloadable | Counts as physical; threshold measured on the whole subtotal |
| Free shipping via discount or free-shipping-over-X | Fee applies — the shipping charge is zero |
| Paid shipping of any amount | No fee |
| Gift card or reward points cover the total | Fee sits inside the total, so credit absorbs it |
| Empty cart | No fee — no order exists |
| Goods discounted to zero | Fee applies; zero is at or below the threshold |
| Recurring orders | `OrderProcessingService` copies the fee from the initial order for renewals rather than re-evaluating it, so renewals inherit the original amount |
| A payment method that charges its own fee | The two sum into one line and one column and cannot be told apart afterwards |
| Multi-store | Settings are per store |

## Testing

Two layers, mirroring what worked on the `HasOnlyProducts` plugin:

**Unit tests on `HandlingFeeCalculator`** — no mocking required. Cover the
threshold boundary (below, exactly at, above), shipping suppression at various
charges, the non-shippable exemption, and the disabled switch.

**Integration harness inside the nopCommerce source tree** — Moq fakes for
`ISettingService`, `IShoppingCartService` and the tax service, driving the real
overridden `GetShoppingCartTotalAsync` and asserting the final order total
across the matrix of: below / at / above threshold × free / paid shipping ×
physical / downloadable / mixed × gift card present / absent.

The harness must assert the **final total**, not just the fee, so that the
interaction with gift cards and tax is covered rather than assumed.

## Risks and maintenance

**The copied method body.** Overriding `GetShoppingCartTotalAsync` means
duplicating roughly eighty lines of core logic, because the guard being removed
sits in the middle of the method and C# offers no way to patch a single branch.
That copy is pinned to 4.50.2 and must be re-diffed against core on any
nopCommerce upgrade. It exists solely to satisfy cart-page visibility.

**The shared label.** The fee displays under the existing "Payment method fee"
locale resource, which can be renamed store-wide to "Handling fee". If a
payment method also charges a fee, both amounts merge into that single line and
into the single pair of order columns.

**Core service overrides generally.** Any future plugin that also overrides
`IPaymentService` or `IOrderTotalCalculationService` will conflict, with the
later `INopStartup.Order` winning.

## Out of scope

Deliberately excluded to keep the first version small:

- Percentage-based fees — the fee is a fixed amount.
- Per-customer-role or per-category exemptions.
- Per-currency fee amounts; the fee is configured in the primary store currency
  and converted for display by nopCommerce.
- A dedicated report of fees collected.
