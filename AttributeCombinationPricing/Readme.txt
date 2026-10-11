Accurate attribute combination pricing
=======================================
For nopCommerce 4.50 (version 1.01) and nopCommerce 4.90 (version 2.00).

Fixes a stock nopCommerce bug: an "Associated to product" attribute value
shows a "[+$X]" price label next to its name - on the product page, in the
shopping cart, at checkout review, and in the description nopCommerce
stores on a newly placed order. When that value's attribute combination
has an Overridden price set, nopCommerce ignores it and shows the
associated product's own standalone price instead. This plugin corrects
the label to reflect the combination's Overridden price everywhere it
appears.

  System name: Misc.AttributeCombinationPricing


The bug
-------
"Associated to product" is one of the ways to price an attribute value in
nopCommerce: instead of a plain price adjustment, the value is linked to
another product, and its own price is what usually shows as the "[+$X]"
label next to the option.

Separately, a product can have "attribute combinations" - specific
selections of attribute values, each with its own optional Overridden
price that replaces the normal price calculation whenever that exact
selection is chosen. Stock nopCommerce applies the Overridden price
correctly once you select the combination, but the label shown before you
select anything is still computed from the associated product's own price,
not from the combination's Overridden price. The two numbers can disagree,
and where they do, the label is misleading.

Example
-------
A holster is priced at $299. One of its attributes, "Mount", has a value
that is Associated to a product priced at $50. An attribute combination
exists for that Mount selection with an Overridden price of $320.

  Stock nopCommerce label:     [+$50.00]   (the associated product's own price)
  What the combination means:  $320 total, i.e. $21 more than the $299 base
  This plugin's label:         [+$21.00]

Selecting the option already charged the customer $320 before this plugin -
that part of nopCommerce was correct. Only the label was wrong.


What the plugin does
---------------------
It recalculates the label for every "Associated to product" attribute
value that has a matching attribute combination with an Overridden price,
using that combination's Overridden price minus the product's own base
price. Values with no matching combination, or a matching combination with
no Overridden price set, keep nopCommerce's stock label - the associated
product's own price - unchanged. Plain ("Simple") price-adjustment
attribute values are untouched; the bug and the fix only concern
"Associated to product" values.

Two corrections work together to cover every surface:

  - The product page's dropdown/radio labels are corrected directly.
  - The underlying price-adjustment calculation nopCommerce uses
    everywhere else - the shopping cart, checkout review, and the
    description text nopCommerce stores on an order at the moment it is
    placed - is corrected at its source, so all of those pick up the same
    fix automatically. This does not touch how real order totals are
    calculated; nopCommerce already charges the combination's Overridden
    price correctly once an option is selected; only the label text was
    ever wrong.

New orders placed after this version is installed show the corrected
label in their confirmation email and on their PDF invoice, since that
text is generated once, at the moment the order is placed, and stored on
the order from then on. Orders placed before this version was installed
keep whatever label was stored at the time - this plugin does not rewrite
historical order data.

The correction applies automatically, store-wide, to every product. There
is no configuration screen and nothing to set up beyond installing the
plugin.


Known limitation
-----------------
The label is computed once, when the product page loads, against the
product's default (pre-selected) attribute state. If a product has more
than one attribute that interacts with this one - for example, choosing a
different color changes which Mount combination and Overridden price
apply - the label does not recompute when the customer changes that other
attribute afterward; it still reflects the combination that matched the
page's initial state. None of the products this plugin was written for
have that shape (each has exactly one attribute driving the Overridden
price), so this does not currently bite, but it is worth knowing about
before using the plugin on a product with several interacting attributes.

Known limitation: discounted or tier-priced products
------------------------------------------------------
The corrected label can be wrong whenever an active discount or tier
price applies to the product - and this is not a rare race condition, it
happens every time such a discount or tier price is active. The label is
computed as the combination's Overridden price minus the product's own
*discounted* base price, but the Overridden price itself is never run
through that same discount. The real checkout total, by contrast,
applies the discount to both sides: it is
discount(Overridden price) minus discount(base price). Whenever a
discount or tier price changes the base price, those two calculations
diverge.

For example: a $299 product with a 10% active discount and a $320
attribute combination override. The real page shows a discounted price
of $269.10, and selecting the option charges $288.00 - a true delta of
$18.90. This plugin's label, however, shows +$50.90 for that option,
which is worse than stock nopCommerce's own +$50.00 bug in this
particular case.

Store owners using this plugin on a product with an active discount or
tier price should be aware the label may not match the real total in
that situation. This is a known limitation of v1, not a defect that will
be silently fixed later; correcting it would require the label
computation to apply the same discount/tier-price logic the checkout
total uses, which is a larger change reserved for a future version.


Installing
----------
"Misc.AttributeCombinationPricing" contains the compiled plugin. Drop it
into the \Plugins directory on your server, then in the admin area go to
Configuration > Local plugins, click "Reload list of plugins", find
"Accurate attribute combination pricing" and click Install. The site
restarts itself. There is nothing to configure afterward - the correction
is active immediately for every product with an affected combination.


Upgrading over an existing installation
-----------------------------------------
Replace the "Misc.AttributeCombinationPricing" folder on the server and
restart the site. The plugin keeps no settings or locale resources of its
own, so there is nothing to preserve by upgrading in place rather than
reinstalling.

Built against nopCommerce 4.50.2, targeting net6.0. The Nop assemblies
carry version 4.5.0 across the whole 4.50.x line and the interfaces this
plugin uses are unchanged between patches, so it should load on any
4.50.x. To be certain, rebuild against your exact version - see below.

"Nop.Plugin.Misc.AttributeCombinationPricing" contains the source code.


Building from source
---------------------
The project references Nop.Web, so it has to be built from inside a
nopCommerce 4.50 source tree:

  1. Copy the "Nop.Plugin.Misc.AttributeCombinationPricing" directory into
     "src/Plugins" in your nopCommerce source tree.
  2. Add it to the solution and build:

       cd <nopcommerce>/src
       dotnet sln NopCommerce.sln add \
         Plugins/Nop.Plugin.Misc.AttributeCombinationPricing/Nop.Plugin.Misc.AttributeCombinationPricing.csproj
       dotnet build \
         Plugins/Nop.Plugin.Misc.AttributeCombinationPricing/Nop.Plugin.Misc.AttributeCombinationPricing.csproj \
         -c Release

  3. The output appears in
     src/Presentation/Nop.Web/Plugins/Misc.AttributeCombinationPricing.
  4. Deploy that folder as described under "Installing" above.

Notes if you are not building with the .NET 6 SDK:

  - nopCommerce's global.json pins SDK 6.0.101. A newer SDK works if you
    change "rollForward" to "latestMajor".
  - The post-build helper (src/Build/ClearPluginAssemblies.dll) is a net6.0
    app and needs the .NET 6 runtime. Without it, set
    DOTNET_ROLL_FORWARD=Major before running dotnet build.
  - A newer SDK also copies some Nop.Web host files (Nop.Web,
    Nop.Web.deps.json, Nop.Web.runtimeconfig.json,
    Nop.Web.staticwebassets.*) into the plugin output that the post-build
    helper does not clean up. Delete them; the plugin folder should contain
    only the files listed in the shipped
    "Misc.AttributeCombinationPricing" directory.


Running the tests
------------------
"Nop.Plugin.Misc.AttributeCombinationPricing.Tests" contains the test
suite. Like the plugin itself, its .csproj carries a ProjectReference to
the plugin project by a relative path, so it only resolves once the test
project sits at src/Tests/ of a nopCommerce 4.50.2 source tree, alongside
the plugin at src/Plugins/. It cannot be built or run from this repository
in place.

  1. Copy both projects into the nopCommerce tree:

       rm -rf <nopcommerce>/src/Plugins/Nop.Plugin.Misc.AttributeCombinationPricing \
              <nopcommerce>/src/Tests/Nop.Plugin.Misc.AttributeCombinationPricing.Tests
       cp -R Nop.Plugin.Misc.AttributeCombinationPricing <nopcommerce>/src/Plugins/
       cp -R Nop.Plugin.Misc.AttributeCombinationPricing.Tests <nopcommerce>/src/Tests/

  2. Run the tests from inside the copied test project:

       cd <nopcommerce>/src/Tests/Nop.Plugin.Misc.AttributeCombinationPricing.Tests
       DOTNET_ROLL_FORWARD=Major dotnet test

The DOTNET_ROLL_FORWARD=Major prefix is required whenever the machine has
only a newer .NET SDK installed and not the net6.0 runtime that
global.json and the project files target: it tells the .NET host to roll
forward to the highest installed major version instead of failing to find
a matching net6.0 runtime. It is harmless, and unnecessary, on a machine
that does have the net6.0 runtime installed.


nopCommerce 4.90 build (version 2.00)
--------------------------------------
"nopCommerce 4.90" holds a separate build for nopCommerce 4.90, next to the
"nopCommerce 4.50" one: "Misc.AttributeCombinationPricing" is the compiled plugin and
"Nop.Plugin.Misc.AttributeCombinationPricing" the source. It behaves exactly like 1.01; only
what nopCommerce 4.90 changed underneath was ported. It is built against
nopCommerce 4.90.8 and targets net9.0. Install it, and upgrade from an
earlier 2.x, exactly as described above. The 4.50 build does not load on
4.90, and the 4.90 build does not load on 4.50.

nopCommerce 4.90 passes the store and the cart quantity into the price
adjustment calculation. The 4.90 build passes both through as well, so
the base price the corrected label is measured against is the same
store- and quantity-specific price nopCommerce itself uses (tier prices
at the item's quantity, rather than always at quantity 1). The
correction, and the reason it is safe for real order totals, are
unchanged: nopCommerce 4.90 still only asks for an attribute value's
price adjustment when no attribute combination's Overridden price
applies.

Moving a store from nopCommerce 4.50 to 4.90 with this plugin installed:
The plugin keeps no settings of its own, but there is still no need to
uninstall it. Instead:

  1. Upgrade nopCommerce itself, following nopCommerce's own upgrade
     instructions.
  2. Before starting the upgraded site, replace the "Misc.AttributeCombinationPricing"
     folder in \Plugins with the one from "nopCommerce 4.90".
  3. Start the site. The plugin stays installed and the correction is
     active again straight away; there is nothing to migrate.

Building the 4.90 source: same steps as above, but inside a nopCommerce
4.90 source tree. Its global.json pins SDK 9.0.100. With only a newer SDK
installed, change "rollForward" to "latestMajor" there, and set
DOTNET_ROLL_FORWARD=Major so the post-build helper (now a net9.0 app) can
run. The same Nop.Web.* host files still need deleting from the output.

Running the 4.90 tests: the suite is under "nopCommerce 4.90/Tests". Copy
"Nop.Plugin.Misc.AttributeCombinationPricing.Tests" into src/Tests/ of the 4.90 source tree,
next to the plugin in src/Plugins/, and run it from there with
DOTNET_ROLL_FORWARD=Major dotnet test, exactly as described for 4.50
above. The tree's Directory.Build.props turns on implicit usings, which the
4.90 test sources already account for.
