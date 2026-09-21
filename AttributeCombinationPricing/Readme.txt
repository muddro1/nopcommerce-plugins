Accurate attribute combination pricing
=======================================
For nopCommerce 4.50.

Fixes a stock nopCommerce bug: on the storefront product page, an "Associated
to product" attribute value shows a "[+$X]" price label next to its name.
When that value's attribute combination has an Overridden price set,
nopCommerce ignores it and shows the associated product's own standalone
price instead. This plugin corrects the label to reflect the combination's
Overridden price.

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
