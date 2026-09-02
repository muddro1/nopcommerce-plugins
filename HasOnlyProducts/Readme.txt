"Customer has only these products in the cart" discount requirement rule
=======================================================================
For nopCommerce 4.50.

A fork of the nopCommerce team's "Customer has all of these products in the cart"
plugin (DiscountRequirement.HasAllProducts). It behaves the same way, plus two
optional conditions: the cart may be required to contain nothing but the listed
products, and any one of the listed products may be accepted instead of all of
them.

The original plugin is untouched. Both can be installed at the same time - they
use different system names, settings keys, controllers and locale resources.

  Original: DiscountRequirement.HasAllProducts
  This one: DiscountRequirement.HasOnlyProducts

Upstream it was forked from
---------------------------
  Plugin:   "Customer has all of these products in the cart" (DiscountRequirement.HasAllProducts)
  Author:   nopCommerce team, Nop Solutions, Ltd
  Version:  1.36, the nopCommerce 4.50 build from the marketplace package
  Source:   https://github.com/nopSolutions/HasAllProducts-discount-requiremement-plugin-for-nopcommerce
  Listing:  https://www.nopcommerce.com/has-all-products-discount-requirement-rule
  License:  GNU General Public License v3

Copyright (c) Nop Solutions, Ltd. Modified by muddro1 starting 2026-08-29:
added the "nothing else in the cart" and "any one of these is enough"
conditions, and renamed the system name, settings keys, controller and locale
resources so this plugin installs alongside the original. This modified version
is distributed under the GNU General Public License v3, the same terms as the
original; see LICENSE.md at the root of this repository.

The marketplace package used to be vendored in this repo under
18541_1030_HasAllProducts/. It was removed - it was unmodified third-party code,
publicly available at the URL above, and it carried ten nopCommerce versions
(3.90 to 4.90) that this fork never used. To read the exact files this fork
started from:

  git show 9ad8d70:"18541_1030_HasAllProducts/HasAllProducts/nopCommerce 4.50/Nop.Plugin.DiscountRules.HasAllProducts/HasAllProductsDiscountRequirementRule.cs"


Configuration
-------------
Admin area > Promotions > Discounts > (a discount) > Requirements tab
> "Customer has only these products in the cart".

  Restricted products [and quantity range]
      Same format as the original plugin:
        77, 123, 156          product identifiers
        77:1, 123:2           product identifier with an exact quantity
        77:1-3, 123:2-5       product identifier with a quantity range

  Any one of these products is enough
      Unchecked (the default): every product in the list must be in the cart,
      which is how the original plugin behaves.
      Checked: at least one of the listed products must be in the cart.

  These must be the only products in the cart
      Checked (the default): the cart must not contain any product outside the
      list above. Anything else in the cart removes the discount.
      Unchecked: other products are allowed in the cart.

The two checkboxes are independent, giving four behaviours. With "77, 123"
configured:

  any  only   cart                        discount
  ---  ----   ----                        --------
  off  off    77 and 123, plus anything   yes     <- the original plugin
  off  on     77 and 123, nothing else    yes
  on   off    77 or 123, plus anything    yes
  on   on     77 or 123, nothing outside  yes

Quantities are unaffected by the checkbox. With "77" configured, a cart holding
five of product 77 and nothing else still qualifies; to cap the quantity, use
the "77:1" or "77:1-3" forms as before.

Examples, with "77" configured and the checkbox ticked:

  1 x #77                 discount applies
  5 x #77                 discount applies
  1 x #77 + 1 x #99       no discount
  1 x #99                 no discount
  empty cart              no discount


Installing
----------
"DiscountRules.HasOnlyProducts" contains the compiled plugin. Just drop it into
the \Plugins directory on your server, then in the admin area go to
Configuration > Local plugins, click "Reload list of plugins", find "Customer
has only these products in the cart" and click Install. The site restarts
itself.

Then attach it to a discount: Promotions > Discounts > (a discount) >
Requirements tab > "Customer has only these products in the cart".

Upgrading over an existing installation
---------------------------------------
Replace the "DiscountRules.HasOnlyProducts" folder on the server and restart the
site. Do NOT uninstall the plugin first - uninstalling deletes every discount
requirement that uses it.

On startup nopCommerce compares the version in plugin.json with the installed
one and, when they differ, calls the plugin's update logic, which re-registers
all of the locale resources. That is how labels added in a newer version get
their text. If a field ever shows a raw resource key such as
"Plugins.DiscountRules.HasOnlyProducts.Fields.MatchAnyProduct" instead of a
readable label, it means that step has not run yet - restart the site.

Built against nopCommerce 4.50.2, targeting net6.0. The Nop assemblies carry
version 4.5.0 across the whole 4.50.x line and the interfaces this plugin uses
are unchanged between patches, so it should load on any 4.50.x. To be certain,
rebuild against your exact version - see below.

"Nop.Plugin.DiscountRules.HasOnlyProducts" contains the source code.


Building from source
--------------------
The project references Nop.Web, so it has to be built from inside a nopCommerce
4.50 source tree:

  1. Copy the "Nop.Plugin.DiscountRules.HasOnlyProducts" directory into
     "src/Plugins" in your nopCommerce source tree.
  2. Add it to the solution and build:

       cd <nopcommerce>/src
       dotnet sln NopCommerce.sln add \
         Plugins/Nop.Plugin.DiscountRules.HasOnlyProducts/Nop.Plugin.DiscountRules.HasOnlyProducts.csproj
       dotnet build \
         Plugins/Nop.Plugin.DiscountRules.HasOnlyProducts/Nop.Plugin.DiscountRules.HasOnlyProducts.csproj \
         -c Release

  3. The output appears in
     src/Presentation/Nop.Web/Plugins/DiscountRules.HasOnlyProducts.
  4. Deploy that folder as described under "Installing" above.

Notes if you are not building with the .NET 6 SDK:

  - nopCommerce's global.json pins SDK 6.0.101. A newer SDK works if you change
    "rollForward" to "latestMajor".
  - The post-build helper (src/Build/ClearPluginAssemblies.dll) is a net6.0 app
    and needs the .NET 6 runtime. Without it, set DOTNET_ROLL_FORWARD=Major
    before running dotnet build.
  - A newer SDK also copies some Nop.Web host files (Nop.Web, Nop.Web.deps.json,
    Nop.Web.runtimeconfig.json, Nop.Web.staticwebassets.*) into the plugin output
    that the post-build helper does not clean up. Delete them; the plugin folder
    should contain only the files listed in the shipped
    "DiscountRules.HasOnlyProducts" directory.
