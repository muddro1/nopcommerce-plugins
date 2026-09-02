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
├── Misc.BetterSearch.zip               ← the compiled folder, zipped
├── Readme.txt
└── TESTING.md                          ← manual test script
```

The `Nop.Plugin.` prefix marks source. The same name without it is the deployable folder. Compiled output is committed rather than built by CI, so a source edit needs a rebuild and a re-copy before the deployable folder is current.

`docs/superpowers/` holds the design specs and implementation plans for the handling fee and search work.

## Installing

1. Copy the plugin folder (the one without the `Nop.Plugin.` prefix) into `\Plugins` on the server, or upload the `.zip` through the admin area.
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

## Credits and licensing

BetterSearch and HandlingFee are original work. HasOnlyProducts derives from the nopCommerce team's [HasAllProducts discount requirement plugin](https://github.com/nopSolutions/HasAllProducts-discount-requiremement-plugin-for-nopcommerce) and carries that project's licensing. The repository has no top-level `LICENSE` file yet.
