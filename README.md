# nopCommerce plugins

Seven plugins for nopCommerce. Five target both **nopCommerce 4.50** and **nopCommerce 4.90** and are kept here as source and as a compiled folder you can drop straight onto a server; the two builds of a plugin behave the same and differ only in the nopCommerce version they compile against. Customer profile targets 4.90 only and follows the same layout. Estimate Builder targets 4.90 only and lives in its own repository, included here as a git submodule (see [Repository layout](#repository-layout)).

| Plugin | System name | Version (4.50 / 4.90) | Does |
|---|---|---|---|
| [Better product search](BetterSearch/Readme.txt) | `Misc.BetterSearch` | 1.00 / 2.01 | Replaces product search with a Lucene.NET index that ranks by relevance, matches SKUs by substring and tolerates typos |
| [Handling fee for small orders](HandlingFee/Readme.txt) | `Misc.HandlingFee` | 1.03 / 2.11 | Charges a fee on small physical orders that qualify for free shipping |
| [Customer has only these products](HasOnlyProducts/Readme.txt) | `DiscountRequirement.HasOnlyProducts` | 1.02 / 2.00 | Discount requirement rule: the cart holds all of the listed products, or any one of them, and optionally nothing else |
| [Estimate builder](estimate-builder/README.md) | `Misc.EstimateBuilder` | — / 1.3.0 | Customers save products and their options into named estimates, export them to Excel or PDF, or move them to the cart; admins manage every estimate and build quotes for contacts without an account |
| [Accurate attribute combination pricing](AttributeCombinationPricing/Readme.txt) | `Misc.AttributeCombinationPricing` | 1.01 / 2.01 | Corrects the price label on an Associated-to-Product attribute value - on the product page, cart, checkout and newly placed orders - so it reflects the attribute combination's Overridden price instead of the associated product's own price |
| [Customer profile](CustomerProfile/Readme.txt) | `Misc.CustomerProfile` | — / 1.0.8 | B2B customer details: BMW dealership, tax exemption certificates with an exempt category and review, expiry reminders and automatic removal, an admin-only Net 30 payment method, and additional contacts who get copies of chosen order emails |
| [XPS Ship webhooks](XpsShip/Readme.txt) | `Misc.XpsShip` | 1.02 / 2.02 | XPS Ship pulls ready-to-ship orders and posts each label back; every label becomes a nopCommerce shipment with its tracking number, marked shipped. Holds, exchange release, backorder rules, voids and a carrier tracking-link email token |

Each plugin has its own `Readme.txt` (Estimate Builder: `README.md`) covering configuration, edge cases and upgrade steps. Read that before deploying. The summaries below only cover what you would want to know before opening one.

## Better product search

Stock nopCommerce matches a product name with a plain substring test. This plugin matches analysed tokens with length-scaled fuzziness, so multi-word queries, misspellings and SKU fragments all improve. The storefront search box, the autocomplete dropdown and the admin product list keep working through the same UI.

**Set "Minimum search term length" to 2** under Configuration → Settings → Catalog settings before you install. nopCommerce rejects shorter terms before the plugin ever sees them.

One regression is worth knowing about. A partial word typed as a prefix can stop matching where stock found it: `hydraul` no longer reaches "Hydraulic Flange Assembly", because two edits separate them and seven characters buy only one. Identifiers are exempt, since SKUs and part numbers get indexed as n-grams, so `234` still finds `fmsa-ab-1234`. The `Readme.txt` goes into when this bites.

On nopCommerce 4.90, which can hand keyword search to a search provider plugin such as nopCommerce's own Lucene search, this plugin takes precedence while it is enabled. Running both gains nothing.

## Handling fee for small orders

For stores offering free shipping above some order size that still want to recover picking and packing costs below it. The fee applies only to physical orders at or below the threshold that ship for free, measured on the goods subtotal excluding tax. On nopCommerce 4.90 (2.10 and later) the rule is configurable from the admin page: flat, percentage or tiered fees; shipping-method, country and customer-role conditions; product and category exclusions; and a live plain-English summary with a "try a cart" tester. Every new option defaults to the original behaviour.

The fee rides nopCommerce's payment-method-additional-fee channel, which is why it persists on the order, displays in the cart, admin, emails and PDF invoices, and picks up tax without extra code. Taxability comes from Configuration → Tax settings, not from the plugin.

If you charge sales tax on this fee, give it a dedicated **Handling** tax category rather than reusing Shipping. Several states tax handling while exempting separately stated shipping, so inheriting shipping's exemptions can under-collect.

## Customer has only these products in the cart

A fork of the nopCommerce team's `DiscountRequirement.HasAllProducts` (upstream version 1.36), adding two conditions: the cart may be required to contain nothing but the listed products, and any one of them may satisfy the rule instead of all of them. Both plugins install side by side, since they use separate system names, settings keys, controllers and locale resources.

## Accurate attribute combination pricing

Fixes a stock nopCommerce bug: when an "Associated to product" attribute value has a matching attribute combination with an Overridden price, the `[+$X]` label next to that value shows the associated product's own standalone price instead of the price the Overridden price actually implies. A $299 product with a Mount value associated to a $50 product, and an Overridden price of $320 for that combination, shows `[+$50.00]` in stock nopCommerce; this plugin corrects the label to `[+$21.00]`, the real difference between the combination's total and the base price. Selecting the option already charged the correct $320 before this plugin - only the label was wrong.

The fix has two parts: the product page's dropdown/radio labels are corrected directly, and the underlying price-adjustment calculation nopCommerce uses everywhere else - cart, checkout review, and the description text stored on an order when it's placed - is corrected at its source, so those pick up the same fix automatically. New orders placed after installing show the corrected label in their confirmation email and PDF invoice; orders placed before installing keep whatever was already stored, since this plugin doesn't rewrite historical order data.

The fix applies automatically, store-wide, with no configuration screen. Known limitations: the label is computed once at page load against the product's default attribute state and doesn't recompute if the customer changes a different, interacting attribute afterward (doesn't currently affect any product in use); and the corrected label can be wrong on a product with an active discount or tier price, since it doesn't apply that same discount to the combination's Overridden price - see `Readme.txt` for the details and a worked example.

## Estimate builder

Adds "Add To Estimate" to the product page and a My Estimates area to the customer account. An estimate keeps each product with its selected attributes; customers can change quantities, export to Excel or PDF, or move everything into the cart, which reports any item the cart refuses. Admins get an Estimate Builder menu to search, edit and export every estimate, create estimates for contacts with no account and later assign them to a customer, add notes that print under the export totals, and exclude categories from the builder.

Prices come from nopCommerce's own pricing - tier, role and special prices, discounts, attribute adjustments, tax display and currency - for the estimate's owner, or at guest prices for contact-only estimates. The admin pages need *Orders. View* to look and *Orders. Create, edit, delete* to change anything, and are closed to vendors.

## Customer profile

Adds a Business profile card to the admin customer page and Business details and Tax exemption pages to My Account. Customers upload tax exemption certificates; an admin approves them with an expiration date, which ticks nopCommerce's own *Is tax exempt* flag. A schedule task emails the customer, their contacts and the store before a certificate expires, and unticks the flag the day after it does. The plugin never adds a second tax-exempt flag, so tax calculation is nopCommerce's (or the tax plugin's) as before.

Net 30 is a payment method only customers you switch on can see; the order is placed unpaid with a due date and settled with *Mark as paid*. Additional contacts get their own copy of the order emails they subscribe to, which works by replacing nopCommerce's workflow message service with a subclass, so re-test contact emails after a nopCommerce upgrade. Check on staging that your tax plugin (TaxJar) honours *Is tax exempt*; `TESTING.md` part 3 covers it.

Uninstalling keeps its data, tax certificates included, so a reinstall picks it up; Settings has an Export (zip) of everything and a separate "Delete all plugin data".

## XPS Ship webhooks

XPS Ship (eCommerce Webship) polls three HTTPS URLs on the store, all requiring a secret key. List Orders hands it every ready-to-ship order: paid or a ship-before-payment method, not held, not an unreleased exchange order, and not fully backordered. Update Order turns each label into one nopCommerce shipment with the posted lines and tracking number. That shipment is marked shipped through `ShipAsync`, so status, notes and the shipped email work as when staff ship by hand. Get Order serves partial shipments. Orders and lines travel as their GUIDs, so the plugin keeps no numeric ids and survives a move to a new install.

The admin page (Sales → XPS Ship) has a Preview of exactly what XPS would receive and why other orders are left out. It also has tabs for hold and exchange release, a Labels tab with Void, the webhook log, and catalog warnings. XPS never reports voids, so staff click Void after voiding in XPS. That deletes the shipment and, unlike nopCommerce's own Delete shipment, puts the shipping status back. The token `%Shipment.XpsTrackingUrl%` gives shipped emails a working carrier link. The design spec is `docs/superpowers/specs/2026-09-24-xps-ship-design.md`.

## Repository layout

Every plugin follows the same two-folder convention inside each of its `nopCommerce 4.50/` and `nopCommerce 4.90/` directories:

```
BetterSearch/
├── nopCommerce 4.50/
│   ├── Nop.Plugin.Misc.BetterSearch/   ← source project
│   └── Misc.BetterSearch/              ← compiled, drop into /Plugins
├── nopCommerce 4.90/
│   ├── Nop.Plugin.Misc.BetterSearch/   ← source project
│   ├── Misc.BetterSearch/              ← compiled, drop into /Plugins
│   └── Tests/                          ← NUnit tests for the 4.90 build
├── Tests/                              ← NUnit tests for the 4.50 build
├── Readme.txt
└── TESTING.md                          ← manual test script
```

The `Nop.Plugin.` prefix marks source. The same name without it is the deployable folder. Compiled output is committed rather than built by CI, so a source edit needs a rebuild and a re-copy before the deployable folder is current.

Zips are not tracked. They duplicate the deployable folder byte for byte, and a compressed binary cannot be delta-compressed against its predecessor, so every repackage used to add a full copy to history. They are now built at release time and attached to a release instead.

Estimate Builder is the exception: `estimate-builder/` is a git submodule pointing at [muddro1/estimate-builder](https://github.com/muddro1/estimate-builder), a single project folder that builds against prebuilt nopCommerce assemblies instead of sitting in a nopCommerce source tree. Clone with `git clone --recurse-submodules`, or run `git submodule update --init` in an existing clone. Its `README.md` covers building; `dotnet build -c Release -p:NopRefs=<folder with the Nop*.dll files>` writes the deployable folder to `output/` beside it, which is ignored here.

Every `[NopMigration]` timestamp must be unique across **all** of these plugins. nopCommerce records applied migrations by timestamp alone, in one table shared by every plugin, so a reused timestamp makes nopCommerce skip the second migration as already applied. Customer profile 1.0.0 shipped with Estimate Builder's `2026/10/12 12:00:00` and installed without its tables. Check before adding one: `grep -rn 'NopMigration("' --include='*.cs' .`

`docs/superpowers/` holds the design specs and implementation plans for the handling fee, search, customer profile and XPS Ship work.

## Installing

1. Download the plugin's `.zip` from [Releases](https://github.com/muddro1/nopcommerce-plugins/releases) and upload it through the admin area, or copy the plugin folder (the one without the `Nop.Plugin.` prefix) straight into `\Plugins` on the server.
2. Go to Configuration → Local plugins and click **Reload list of plugins**.
3. Find the plugin and click **Install**. The site restarts itself.
4. Configure it. Discount rules attach under Promotions → Discounts → a discount → Requirements. Better product search, Handling fee for small orders and Estimate builder get a Configure button on the plugin list, and their 4.90 builds also list their settings pages under an **FMS Plugins** item in the admin menu (shown to admins with *Manage plugins*). Estimate builder's entry there is a group that also holds All Estimates and New Estimate; Accurate attribute combination pricing has no settings, so it has none.

When upgrading over an existing install, follow the upgrade section in that plugin's `Readme.txt`. Some of them touch locale resources that need restoring in order.

## Building

The projects expect to sit inside a nopCommerce source tree, referencing `..\..\Presentation\Nop.Web\Nop.Web.csproj` and writing output to `Presentation\Nop.Web\Plugins\<name>`. Copy the source folder into `src/Plugins/` in your nopCommerce checkout, then:

```bash
cd <nopcommerce>/src
dotnet sln NopCommerce.sln add Plugins/Nop.Plugin.Misc.BetterSearch/Nop.Plugin.Misc.BetterSearch.csproj
dotnet build Plugins/Nop.Plugin.Misc.BetterSearch/Nop.Plugin.Misc.BetterSearch.csproj -c Release
```

Build each version's source inside the matching nopCommerce tree: `nopCommerce 4.50/` against 4.50.2 (net6.0), `nopCommerce 4.90/` against 4.90.8 (net9.0).

nopCommerce 4.50 pins SDK 6.0.101 in `global.json`, and 4.90 pins 9.0.100. A newer SDK works if you set `rollForward` to `latestMajor`. Without the matching runtime installed, also set `DOTNET_ROLL_FORWARD=Major`, or the post-build cleaner fails to start (exit code 150). A newer SDK also copies Nop.Web host files into the plugin output that the cleaner leaves behind. Delete them. Each `Readme.txt` lists the files that belong in a clean plugin folder.

## Tests

BetterSearch, HandlingFee and AttributeCombinationPricing carry NUnit suites: under `Tests/` for the 4.50 build and `nopCommerce 4.90/Tests/` for the 4.90 build. BetterSearch and HandlingFee account for roughly 120 cases between them, covering the search query builder, SKU normalisation, index management, fee calculation and order totals; AttributeCombinationPricing's suite covers the corrected price-adjustment arithmetic and both overrides that apply it - the product page's display-layer factory and the shared price calculation service behind cart, checkout and order attribute descriptions. They run against fakes, so no database or nopCommerce host is needed.

BetterSearch and HandlingFee also ship a `TESTING.md` with a manual script for a real store: SQL to verify stored order columns, expected figures per scenario and the settings to toggle between parts.

## Releases

Each plugin versions independently, under a namespaced tag: `better-search/v1.00`, `handling-fee/v1.03`, `has-only-products/v1.02`, `attribute-combination-pricing/v1.00`, `estimate-builder/v1.3.0`. The 4.90 builds start at 2.00, so a tag's major version tells you which nopCommerce it targets: 1.x for 4.50, 2.x for 4.90. Download the packaged zips from [Releases](https://github.com/muddro1/nopcommerce-plugins/releases).

To cut one, bump `Version` in both copies of the plugin's `plugin.json` (source and deployable folder), rebuild, copy the output over the deployable folder, then package and publish:

```bash
cd "HandlingFee/nopCommerce 4.50"
cp ../../LICENSE.md Misc.HandlingFee/
zip -X -r -q ../Misc.HandlingFee.zip Misc.HandlingFee -x '*.DS_Store'
cd ../..
git tag -a handling-fee/v1.04 -m "Handling fee for small orders 1.04 (nopCommerce 4.50)"
git push origin handling-fee/v1.04
gh release create handling-fee/v1.04 HandlingFee/Misc.HandlingFee.zip --title "Handling fee for small orders 1.04"
```

A 4.90 release works the same way from `nopCommerce 4.90/`. Give the zip a distinct name so it can't overwrite the 4.50 one:

```bash
cd "HandlingFee/nopCommerce 4.90"
cp ../../LICENSE.md Misc.HandlingFee/
zip -X -r -q ../Misc.HandlingFee-4.90.zip Misc.HandlingFee -x '*.DS_Store'
cd ../..
git tag -a handling-fee/v2.00 -m "Handling fee for small orders 2.00 (nopCommerce 4.90)"
git push origin handling-fee/v2.00
gh release create handling-fee/v2.00 HandlingFee/Misc.HandlingFee-4.90.zip --title "Handling fee for small orders 2.00"
```

Estimate Builder is released the same way, from the deployable folder its own build produces (see its `README.md`), packaged as `Misc.EstimateBuilder-4.90.zip` under an `estimate-builder/v…` tag. It has no 4.50 build, so its versions don't follow the 1.x/2.x rule.

`LICENSE.md` goes inside the package because the zip distributes compiled GPL v3 code and the license has to travel with it. The zip itself stays untracked.

## Licensing

Everything here is licensed under the **GNU General Public License v3**. The full text is in [LICENSE.md](LICENSE.md).

That is inherited, not chosen. nopCommerce ships under the nopCommerce Public License v4.0, which is AGPL v3 plus a requirement that "powered by nopCommerce" appear on every user interface screen. These plugins compile against and subclass nopCommerce types, and `HandlingFeeOrderTotalCalculationService` reproduces a method body from `Nop.Services.Orders.OrderTotalCalculationService` outright, so they are derivative works rather than independent programs. Running them on a public store also brings AGPL §13 and the "powered by nopCommerce" attribution into play, which is a matter for the store rather than this repository.

**Customer has only these products in the cart**
Copyright © Nop Solutions, Ltd, as the original "Customer has all of these products in the cart" plugin ([source](https://github.com/nopSolutions/HasAllProducts-discount-requiremement-plugin-for-nopcommerce), GPL v3, upstream version 1.36).
Modified by muddro1 starting 2026-08-29: added the exclusive-cart and match-any conditions, renamed the system name, settings keys, controller and locale resources.

**Better product search**, **Handling fee for small orders** and **Accurate attribute combination pricing**
Copyright © 2026 muddro1. Original work, released under GPL v3 as derivative works of nopCommerce.

BetterSearch bundles Lucene.NET and J2N, both under the Apache License 2.0.
