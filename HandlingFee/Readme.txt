Handling fee for small orders
==============================
For nopCommerce 4.50.

Adds a configurable handling fee to physical orders that are at or below a
threshold value and ship for free. It is meant for stores that offer free
shipping above a certain order size but still want to recover the cost of
picking, packing and shipping the small orders that fall under it.

  System name: Misc.HandlingFee


The rule
--------
The fee is charged only when every one of these is true:

  1. The order is physical - at least one item in the cart needs shipping.
     Purely downloadable or virtual orders never attract the fee.
  2. The goods subtotal, after discounts, is AT OR BELOW the configured
     threshold. Shipping and tax are not part of this figure, and "at or
     below" includes an order exactly equal to the threshold.
  3. The shipping charge is zero. Any shipping charge above zero, however
     small, removes the fee entirely.

All three conditions must hold. Fail any one of them and the order pays no
handling fee.


Settings
--------
Admin area > Configuration > Local plugins > "Handling fee for small orders"
> Configure.

  Enabled
      Turns the fee on or off. Off by default after installation, so a fresh
      install never charges anything until you configure it.

  Order threshold
      The fee applies when the goods subtotal, after discounts, is at or
      below this amount. Shipping, tax, gift cards and reward points are not
      counted toward it.

  Handling fee
      The amount charged, in the primary store currency, when the rule above
      is met.

  No fee when shipping is charged
      Ticked (the default): any shipping charge above zero removes the fee.
      Unticked: the fee is charged regardless of shipping cost, as long as
      the order is physical and at or below the threshold.


Worked examples
----------------
With the threshold set to 50.00 and the fee set to 4.95:

  Order                                        Fee
  -------------------------------------------  ----
  Physical, subtotal 30.00, free shipping       4.95 charged
  Physical, subtotal 30.00, paid shipping       none
  Physical, subtotal 100.00, free shipping      none (above threshold)
  Downloadable items only, subtotal 30.00       none (nothing to ship)
  Mixed cart: downloadable + physical,          4.95 charged on the whole
    subtotal 30.00, free shipping                 subtotal - one physical
                                                    item is enough to trigger
                                                    the rule


Gift cards and reward points
-----------------------------
Gift cards and reward points are a payment against the total, not a discount
on the goods subtotal, so they never move the threshold check:

  - A 100.00 order paid partly with an 80.00 gift card is still evaluated as
    a 100.00 order. Above a 50.00 threshold, it pays no fee.
  - A 30.00 order applied against a gift card is still evaluated as a 30.00
    order. Below the threshold with free shipping, it pays the fee, and the
    gift card simply absorbs that fee along with the rest of the total.


Where the fee appears
----------------------
The fee is charged through nopCommerce's own payment method additional fee
channel, so it shows on the cart page, checkout, order confirmation, order
details and PDF invoice under the existing "Payment method fee" label - the
same label a payment method's own surcharge would use. That label is driven
by two separate locale resources, and both would need renaming to relabel it
store-wide (Admin area > Configuration > Languages):

  - "ShoppingCart.Totals.PaymentMethodAdditionalFee" - used on the cart page
    and throughout checkout (Nop.Web's OrderTotals view component).
  - "Order.PaymentMethodAdditionalFee" - used on the order details page.

If either resource is renamed, the handling fee appears under the renamed
label wherever that resource is used. If the chosen payment method also
charges its own fee, the two amounts are combined into a single total; the
line does not distinguish which part came from which source.

Because the fee rides this channel, its taxability is controlled from
Configuration > Tax settings, not from this plugin: the "Payment method
additional fee is taxable" checkbox and its associated tax category decide
whether and how the fee is taxed. Whichever tax provider is installed on the
store applies its normal rate to the fee exactly as it would to a payment
surcharge. This plugin adds no tax settings of its own.

A consequence of riding this channel: on the payment method selection page
(/checkout/paymentmethod), nopCommerce calls the fee calculation once per
active payment method so that it can list each method's own surcharge next
to it. This plugin's fee is not a per-method surcharge - it is a single,
once-per-order charge - but nopCommerce has no way to know that, so it
prints the same handling fee amount next to every payment method shown on
that page. With three active payment methods and a 4.95 handling fee, the
customer sees "4.95" next to all three, as though choosing any one of them
adds a 4.95 surcharge. In fact the fee is charged exactly once regardless of
which method is chosen; the repeated display is a side effect of the
attribution nopCommerce assumes for that channel, not a bug in the amount
charged. There is no supported way to suppress the repeated display without
abandoning the payment-fee channel entirely, which would also lose the free
tax, persistence and reporting integration it provides.


Installing
----------
"Misc.HandlingFee" contains the compiled plugin. Drop it into the \Plugins
directory on your server, then in the admin area go to Configuration > Local
plugins, click "Reload list of plugins", find "Handling fee for small
orders" and click Install. The site restarts itself.

Then configure it: Configuration > Local plugins > "Handling fee for small
orders" > Configure. It ships disabled, so nothing is charged until you tick
Enabled and set a threshold and fee amount.


Upgrading over an existing installation
-----------------------------------------
Replace the "Misc.HandlingFee" folder on the server and restart the site.
Do NOT uninstall the plugin first - uninstalling deletes its settings and
its locale resources.

On startup nopCommerce compares the version in plugin.json with the
installed one and, when they differ, calls the plugin's update logic, which
re-registers all of the locale resources. That is how labels added in a
newer version get their text. If a field ever shows a raw resource key such
as "Plugins.Misc.HandlingFee.Fields.ThresholdAmount" instead of a readable
label, it means that step has not run yet - restart the site.

Built against nopCommerce 4.50.2, targeting net6.0. The Nop assemblies carry
version 4.5.0 across the whole 4.50.x line and the interfaces this plugin
uses are unchanged between patches, so it should load on any 4.50.x. To be
certain, rebuild against your exact version - see below.

"Nop.Plugin.Misc.HandlingFee" contains the source code.


Building from source
---------------------
The project references Nop.Web, so it has to be built from inside a
nopCommerce 4.50 source tree:

  1. Copy the "Nop.Plugin.Misc.HandlingFee" directory into "src/Plugins" in
     your nopCommerce source tree.
  2. Add it to the solution and build:

       cd <nopcommerce>/src
       dotnet sln NopCommerce.sln add \
         Plugins/Nop.Plugin.Misc.HandlingFee/Nop.Plugin.Misc.HandlingFee.csproj
       dotnet build \
         Plugins/Nop.Plugin.Misc.HandlingFee/Nop.Plugin.Misc.HandlingFee.csproj \
         -c Release

  3. The output appears in src/Presentation/Nop.Web/Plugins/Misc.HandlingFee.
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
    only the files listed in the shipped "Misc.HandlingFee" directory.


Running the tests
------------------
"Nop.Plugin.Misc.HandlingFee.Tests" contains the test suite. Like the plugin
itself, its .csproj carries a ProjectReference to the plugin project by a
relative path ("..\..\Plugins\Nop.Plugin.Misc.HandlingFee\..."), so it only
resolves once the test project sits at src/Tests/ of a nopCommerce 4.50.2
source tree, alongside the plugin at src/Plugins/. It cannot be built or run
from this repository in place.

  1. Copy both projects into the nopCommerce tree:

       rm -rf <nopcommerce>/src/Plugins/Nop.Plugin.Misc.HandlingFee \
              <nopcommerce>/src/Tests/Nop.Plugin.Misc.HandlingFee.Tests
       cp -R Nop.Plugin.Misc.HandlingFee <nopcommerce>/src/Plugins/
       cp -R Nop.Plugin.Misc.HandlingFee.Tests <nopcommerce>/src/Tests/

  2. Run the tests from inside the copied test project:

       cd <nopcommerce>/src/Tests/Nop.Plugin.Misc.HandlingFee.Tests
       DOTNET_ROLL_FORWARD=Major dotnet test

The DOTNET_ROLL_FORWARD=Major prefix is required whenever the machine has
only a newer .NET SDK installed and not the net6.0 runtime that
global.json and the project files target: it tells the .NET host to roll
forward to the highest installed major version instead of failing to find
a matching net6.0 runtime. It is harmless, and unnecessary, on a machine
that does have the net6.0 runtime installed.
