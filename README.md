# nopCommerce plugins

Three plugins for **nopCommerce 4.50**, each kept here as source and as a compiled folder you can drop straight onto a server.

| Plugin | System name | Version | Does |
|---|---|---|---|
| [Better product search](BetterSearch/Readme.txt) | `Misc.BetterSearch` | 1.00 | Replaces product search with a Lucene.NET index that ranks by relevance, matches SKUs by substring and tolerates typos |
| [Handling fee for small orders](HandlingFee/Readme.txt) | `Misc.HandlingFee` | 1.03 | Charges a fee on small physical orders that qualify for free shipping |
| [Customer has only these products](HasOnlyProducts/Readme.txt) | `DiscountRequirement.HasOnlyProducts` | 1.02 | Discount requirement rule: the cart holds all of the listed products, or any one of them, and optionally nothing else |

Each plugin has its own `Readme.txt` covering configuration, edge cases and upgrade steps. Read that before deploying. The summaries below only cover what you would want to know before opening one.

## Better product search

Stock nopCommerce matches a product name with a plain substring test. This plugin matches analysed tokens with length-scaled fuzziness, so multi-word queries, misspellings and SKU fragments all improve. The storefront search box, the autocomplete dropdown and the admin product list keep working through the same UI.

**Set "Minimum search term length" to 2** under Configuration → Settings → Catalog settings before you install. nopCommerce rejects shorter terms before the plugin ever sees them.

One regression is worth knowing about. A partial word typed as a prefix can stop matching where stock found it: `hydraul` no longer reaches "Hydraulic Flange Assembly", because two edits separate them and seven characters buy only one. Identifiers are exempt, since SKUs and part numbers get indexed as n-grams, so `234` still finds `fmsa-ab-1234`. The `Readme.txt` goes into when this bites.

## Handling fee for small orders

For stores offering free shipping above some order size that still want to recover picking and packing costs below it. The fee applies only to physical orders at or below the threshold that ship for free, measured on the goods subtotal excluding tax.

The fee rides nopCommerce's payment-method-additional-fee channel, which is why it persists on the order, displays in the cart, admin, emails and PDF invoices, and picks up tax without extra code. Taxability comes from Configuration → Tax settings, not from the plugin.

If you charge sales tax on this fee, give it a dedicated **Handling** tax category rather than reusing Shipping. Several states tax handling while exempting separately stated shipping, so inheriting shipping's exemptions can under-collect.

## Customer has only these products in the cart

A fork of the nopCommerce team's `DiscountRequirement.HasAllProducts` (upstream version 1.36), adding two conditions: the cart may be required to contain nothing but the listed products, and any one of them may satisfy the rule instead of all of them. Both plugins install side by side, since they use separate system names, settings keys, controllers and locale resources.

## Repository layout

Every plugin follows the same two-folder convention inside its `nopCommerce 4.50/` directory:

```
BetterSearch/
├── nopCommerce 4.50/
│   ├── Nop.Plugin.Misc.BetterSearch/   ← source project
│   └── Misc.BetterSearch/              ← compiled, drop into /Plugins
├── Tests/                              ← NUnit tests
├── Readme.txt
└── TESTING.md                          ← manual test script
```

The `Nop.Plugin.` prefix marks source. The same name without it is the deployable folder. Compiled output is committed rather than built by CI, so a source edit needs a rebuild and a re-copy before the deployable folder is current.

Zips are not tracked. They duplicate the deployable folder byte for byte, and a compressed binary cannot be delta-compressed against its predecessor, so every repackage used to add a full copy to history. They are now built at release time and attached to a release instead.

`docs/superpowers/` holds the design specs and implementation plans for the handling fee and search work.

## Installing

1. Download the plugin's `.zip` from [Releases](https://github.com/muddro1/nopcommerce-plugins/releases) and upload it through the admin area, or copy the plugin folder (the one without the `Nop.Plugin.` prefix) straight into `\Plugins` on the server.
2. Go to Configuration → Local plugins and click **Reload list of plugins**.
3. Find the plugin and click **Install**. The site restarts itself.
4. Configure it. Discount rules attach under Promotions → Discounts → a discount → Requirements. The two `Misc.` plugins get a Configure button on the plugin list.

When upgrading over an existing install, follow the upgrade section in that plugin's `Readme.txt`. Some of them touch locale resources that need restoring in order.

## Building

The projects expect to sit inside a nopCommerce source tree, referencing `..\..\Presentation\Nop.Web\Nop.Web.csproj` and writing output to `Presentation\Nop.Web\Plugins\<name>`. Copy the source folder into `src/Plugins/` in your nopCommerce checkout, then:

```bash
cd <nopcommerce>/src
dotnet sln NopCommerce.sln add Plugins/Nop.Plugin.Misc.BetterSearch/Nop.Plugin.Misc.BetterSearch.csproj
dotnet build Plugins/Nop.Plugin.Misc.BetterSearch/Nop.Plugin.Misc.BetterSearch.csproj -c Release
```

nopCommerce 4.50 pins SDK 6.0.101 in `global.json`. A newer SDK works if you set `rollForward` to `latestMajor`, though it also copies Nop.Web host files into the plugin output that the post-build cleaner leaves behind. Delete them. Each `Readme.txt` lists the files that belong in a clean plugin folder.

## Tests

BetterSearch and HandlingFee carry NUnit suites under `Tests/`, roughly 120 cases between them, covering the search query builder, SKU normalisation, index management, fee calculation and order totals. They run against fakes, so no database or nopCommerce host is needed.

Both plugins also ship a `TESTING.md` with a manual script for a real store: SQL to verify stored order columns, expected figures per scenario and the settings to toggle between parts.

## Releases

Each plugin versions independently, under a namespaced tag: `better-search/v1.00`, `handling-fee/v1.03`, `has-only-products/v1.02`. Download the packaged zips from [Releases](https://github.com/muddro1/nopcommerce-plugins/releases).

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

`LICENSE.md` goes inside the package because the zip distributes compiled GPL v3 code and the license has to travel with it. The zip itself stays untracked.

## Licensing

Everything here is licensed under the **GNU General Public License v3**. The full text is in [LICENSE.md](LICENSE.md).

That is inherited, not chosen. nopCommerce ships under the nopCommerce Public License v4.0, which is AGPL v3 plus a requirement that "powered by nopCommerce" appear on every user interface screen. These plugins compile against and subclass nopCommerce types, and `HandlingFeeOrderTotalCalculationService` reproduces a method body from `Nop.Services.Orders.OrderTotalCalculationService` outright, so they are derivative works rather than independent programs. Running them on a public store also brings AGPL §13 and the "powered by nopCommerce" attribution into play, which is a matter for the store rather than this repository.

**Customer has only these products in the cart**
Copyright © Nop Solutions, Ltd, as the original "Customer has all of these products in the cart" plugin ([source](https://github.com/nopSolutions/HasAllProducts-discount-requiremement-plugin-for-nopcommerce), GPL v3, upstream version 1.36).
Modified by muddro1 starting 2026-08-29: added the exclusive-cart and match-any conditions, renamed the system name, settings keys, controller and locale resources.

**Better product search** and **Handling fee for small orders**
Copyright © 2026 muddro1. Original work, released under GPL v3 as derivative works of nopCommerce.

BetterSearch bundles Lucene.NET and J2N, both under the Apache License 2.0.
