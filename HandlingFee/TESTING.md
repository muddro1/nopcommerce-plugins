# Handling fee plugin — staging test script

**Plugin:** Misc.HandlingFee 1.00, built against nopCommerce 4.50.2
**Purpose:** confirm on a real store what the automated tests could only confirm against mocks.

Every one of the 21 automated tests feeds the calculation from mocked nopCommerce
services. Nothing has run against a real database, a real cart, or a real tax
provider. This script closes that gap. It changes the money on every qualifying
order, so run it before the plugin goes live.

**Run this on staging, not production.** Take a database backup first.

Budget about 30 minutes.

---

## The rule being tested

A handling fee is charged **only** when all four hold:

1. the plugin is enabled
2. the cart needs shipping — at least one item is ship-enabled
3. the goods subtotal after discounts is **at or below** the threshold
4. the shipping charge is zero

Anything else means no fee.

---

## Part 1 — Setup

### 1.1 Install

Drop the `Misc.HandlingFee` folder into `\Plugins` on the staging site, then
**Configuration → Local plugins → Reload list of plugins → Install**. The site
restarts itself.

### 1.2 Configure

**Configuration → Plugins → Handling fee for small orders → Configure**

| Setting | Value for this test |
| --- | --- |
| Enabled | ticked |
| Order threshold | 50 |
| Handling fee | 4.95 |
| No fee when shipping is charged | ticked |

Save.

> **Check the page itself while you are here.** No automated test renders this
> view — it is the least-exercised code in the plugin. Confirm every field shows
> a readable label rather than a raw string like
> `Plugins.Misc.HandlingFee.Fields.FeeAmount`. Save, reload, and confirm the four
> values persisted.

### 1.3 Rename the label (optional, but do it before testing)

The fee displays under nopCommerce's shared "Payment method additional fee"
label. To show it as **Small Order Handling Charge**, go to **Configuration →
Languages → (your language) → String resources**, search
`PaymentMethodAdditionalFee`, and rename these four customer-facing resources:

| Resource | Current value | Note |
| --- | --- | --- |
| `ShoppingCart.Totals.PaymentMethodAdditionalFee` | Payment method additional fee | cart and checkout |
| `Order.PaymentMethodAdditionalFee` | Payment method additional fee | order details |
| `PDFInvoice.PaymentMethodAdditionalFee` | Payment Method Additional Fee**:** | **keep the trailing colon** |
| `Messages.Order.PaymentMethodAdditionalFee` | Payment method additional fee**:** | **keep the trailing colon** |

Optionally also rename `Admin.Orders.Fields.PaymentMethodAdditionalFee` and
`Admin.Orders.Fields.Edit.PaymentMethodAdditionalFee` so staff see consistent
wording. Leave the `Admin.Configuration.Settings.Tax.*` ones alone — they
describe the tax setting itself.

Repeat per language if the store runs more than one.

Doing this before you test matters for two reasons: the rest of this script
refers to the line by its new name, and checking all four places is itself part
of the test — a missed resource shows up as an invoice or email still saying
"Payment method additional fee".

### 1.4 Turn fee tax off for Part 2

**Configuration → Settings → Tax settings** → untick **"Payment method additional
fee is taxable"**. Part 2's expected figures assume this is off. Part 4 turns it
back on.

### 1.5 Confirm the settings actually stored

Run against the staging database:

```sql
SELECT [Name], [Value], [StoreId]
FROM [Setting]
WHERE [Name] LIKE 'handlingfeesettings.%'
   OR [Name] LIKE 'taxsettings.paymentmethodadditionalfee%'
ORDER BY [Name];
```

Expected:

```
handlingfeesettings.enabled                        True
handlingfeesettings.feeamount                      4.95
handlingfeesettings.suppresswhenshippingcharged    True
handlingfeesettings.thresholdamount                50
taxsettings.paymentmethodadditionalfeeistaxable    False
```

If `handlingfeesettings.*` rows are missing entirely, the plugin was not
installed properly — stop and fix that first.

### 1.6 Products you will need

- **P-PHYS-30** — a shippable product priced 30.00
- **P-PHYS-100** — a shippable product priced 100.00
- **P-DL-30** — a downloadable product priced 30.00, *Shipping enabled* unticked
- **P-DL-10** — a downloadable product priced 10.00, *Shipping enabled* unticked
- a free shipping method, and a paid one at 8.00
- a gift card worth 80.00 for Part 3

Prices above are **excluding tax**, which is what the threshold is compared
against. See section 6.2 if your store is configured to display prices inclusive
of tax.

---

## Part 2 — The core cases

For each: build the cart, go through checkout, **place the order**, then record
what the order actually stored. Screen values are not enough — the stored columns
are what the accounts and the invoice use.

| # | Cart | Subtotal | Shipping | Fee expected | Order total expected |
| --- | --- | --- | --- | --- | --- |
| 1 | 1 × P-PHYS-30 | 30.00 | free | **4.95** | 34.95 |
| 2 | 1 × P-PHYS-30 | 30.00 | paid 8.00 | **0.00** | 38.00 |
| 3 | 1 × P-PHYS-100 | 100.00 | free | **0.00** | 100.00 |
| 4 | 1 × P-DL-30 | 30.00 | none required | **0.00** | 30.00 |
| 5 | 1 × P-PHYS-30 + 1 × P-DL-10 | 40.00 | free | **4.95** | 44.95 |
| 6 | 2 × P-PHYS-30 | 60.00 | free | **0.00** | 60.00 |

Each case isolates one clause of the rule:

- **1** — the base case, everything qualifies
- **2** — paid shipping suppresses the fee
- **3** — above the threshold
- **4** — nothing to post, so nothing to charge for
- **5** — the mixed basket. The fee applies because something physical needs
  posting, and the threshold is measured on the **whole** 40.00 subtotal, the
  10.00 download included. This is the rule most likely to surprise you later,
  so it is worth seeing once.
- **6** — the threshold boundary from above. 60.00 exceeds 50, so no fee. If you
  want the exact boundary too, a cart of precisely 50.00 **should** be charged —
  the comparison is *at or below*.

### 2.1 Before placing each order — check the cart page

Case 1 should show a **Small Order Handling Charge** line of 4.95 on the shopping cart
page, *before* any payment method has been selected. That line existing at that
point is the entire reason for one of the two service overrides — if it only
appears later in checkout, something is wrong.

Then pick the paid shipping method and confirm the line **disappears**.

### 2.2 On the payment method page — expected oddity

With several payment methods active you will see the same 4.95 shown next to
**every** payment method, as though each one charged it. This is expected and
documented: nopCommerce asks each method for its fee, and ours answers the same
number every time. **The fee is charged once regardless of which method is
chosen.** Confirm the placed order carries 4.95 once, not once per method.

### 2.3 After placing each order — verify the stored columns

```sql
SELECT TOP 10
    o.[Id],
    o.[CustomOrderNumber],
    o.[OrderSubtotalExclTax],
    o.[OrderSubTotalDiscountExclTax],
    o.[OrderShippingExclTax],
    o.[PaymentMethodAdditionalFeeExclTax],   -- the handling fee lives here
    o.[OrderTax],
    o.[OrderDiscount],
    o.[OrderTotal],
    ( o.[OrderSubtotalExclTax]
    - o.[OrderSubTotalDiscountExclTax]
    + o.[OrderShippingExclTax]
    + o.[PaymentMethodAdditionalFeeExclTax]
    + o.[OrderTax]
    - o.[OrderDiscount]
    - o.[OrderTotal] ) AS [ReconciliationDelta]
FROM [Order] o
ORDER BY o.[Id] DESC;
```

**`ReconciliationDelta` must be 0.00 on every row.**

That column is the single most important check in this script. A non-zero delta
means the fee is being counted a different number of times in the stored
components than in the total — the one failure mode that would silently
overcharge or undercharge customers. The automated suite cannot detect it
because it never places an order.

> The query assumes no gift card and no reward points on these orders. Keep
> cases 1-6 free of both; gift cards get their own test in Part 3.

### 2.4 Also confirm

- **Order details page** (admin and customer-facing) shows the fee line
- **Order confirmation email** shows it
- **PDF invoice** shows it

---

## Part 3 — Gift cards

The design says store credit is a way of paying, never a change to what the
order is worth. Two cases prove it:

| # | Cart | Gift card | Fee expected | Why |
| --- | --- | --- | --- | --- |
| 7 | 1 × P-PHYS-100, free shipping | 80.00 | **0.00** | threshold sees 100.00, above 50 — the card does not drag it under |
| 8 | 1 × P-PHYS-30, free shipping | 80.00 | **4.95** | threshold sees 30.00 — the card does not rescue it from the fee |

Case 8 also confirms the card **absorbs** the fee: the customer should owe 0.00,
and the gift card's remaining balance should drop by 34.95, not 30.00. Check the
balance under **Sales → Gift cards**.

Case 7 is the one you specifically asked about. If it charges a fee, the
threshold is being measured in the wrong place.

---

## Part 4 — Tax on the fee

Re-tick **Configuration → Settings → Tax settings → "Payment method additional
fee is taxable"** and set its tax category. Then repeat **case 1**.

Expected at a 20% rate:

```
OrderSubtotalExclTax                30.00
OrderShippingExclTax                 0.00
PaymentMethodAdditionalFeeExclTax    4.95
OrderTax                             6.99      (6.00 on goods + 0.99 on the fee)
OrderTotal                          41.94
ReconciliationDelta                  0.00
```

The figure that matters is **`OrderTax` including 0.99 exactly once**. Your tax
provider — not this plugin — decides the rate, so if you are on a different rate
adjust accordingly; what must hold is that the fee is taxed once, not twice and
not zero times.

---

## Part 5 — The off switch

Untick **Enabled**, save, and rebuild the case 1 cart.

- no fee line anywhere on the cart or checkout
- a placed order stores `PaymentMethodAdditionalFeeExclTax = 0.00`

A disabled plugin must be completely inert. Re-tick Enabled afterwards if you
intend to go live.

---

## Part 6 — Two open questions to decide

Neither is a bug. Both are behaviour nobody has chosen yet, and both are easier
to settle by looking at a real store than by reasoning about it.

### 6.1 Pickup in store

Build the case 1 cart and choose **pickup in store** rather than delivery.

The shipping charge is zero, so **the fee will be charged.** Someone still picks
and packs the order, so that may be exactly right — but the plugin's stated
purpose is covering the cost of *posting*, and nobody has ruled on pickup.

Record what happens, then decide: should a pickup order pay the handling fee?
If not, it is a small change to the calculator.

### 6.2 Threshold versus tax-inclusive pricing

The threshold is measured on the goods subtotal **excluding tax**.

If your store displays prices inclusive of VAT, a threshold of 50 catches
baskets up to 60.00 inclusive — not 50.00 as displayed. Put a product priced
just over 50 inclusive (about 41.67 + VAT) in a cart and see which side of the
line it falls.

If that surprises you, the threshold value needs adjusting, or the rule needs
changing to measure inclusive of tax.

---

## Sign-off

| Check | Pass |
| --- | --- |
| Admin config page renders readable labels, saves, persists | ☐ |
| Cases 1-6 produce the expected fee and total | ☐ |
| **`ReconciliationDelta` is 0.00 on every order** | ☐ |
| Fee line appears on the cart page before payment selection | ☐ |
| Fee line disappears when paid shipping is chosen | ☐ |
| Fee appears once on the order, not once per payment method | ☐ |
| Order details, confirmation email and PDF invoice all show the fee | ☐ |
| Case 7: 100.00 order with an 80.00 gift card pays no fee | ☐ |
| Case 8: 30.00 order with a gift card still pays the fee, card absorbs it | ☐ |
| Fee taxed exactly once when taxability is on | ☐ |
| Disabling the plugin makes it completely inert | ☐ |
| Pickup-in-store behaviour observed and decided | ☐ |
| Tax-inclusive threshold behaviour observed and decided | ☐ |

---

## If something fails

Take the plugin out of service first, then diagnose:

1. **Untick Enabled** in the plugin configuration. That is the fastest, safest
   stop — the calculator returns zero before doing any work.
2. If it needs to come out entirely, **delete the folder from `\Plugins` and
   restart**. Do **not** use Uninstall as a first move: uninstalling deletes the
   plugin's settings.
3. Note that the service overrides stay registered while the folder is present,
   installed or not. Only removing the folder and restarting fully detaches them.

When reporting a failure, include the failing order's Id and the full output of
the Part 2.3 query for that order — the stored columns say far more than a
screenshot does.
