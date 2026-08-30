Better product search
======================
For nopCommerce 4.50.

Replaces nopCommerce's product search with a relevance-ranked index, built
with Lucene.NET, that matches SKUs by substring and tolerates typos in
product names and descriptions. It is a drop-in replacement: no template
changes, no new search page. The storefront search box, the autocomplete
dropdown and the admin product list all keep using the same input box and
the same result list - only what gets matched and how it is ranked
changes.

  System name: Misc.BetterSearch


Where results get better, and one place they get worse
-----------------------------------------------------------
Multi-word queries, typo tolerance and SKU substring matching are all much
better than stock. Stock nopCommerce matches a product name with a plain
substring test (Name.Contains(keywords)); this plugin matches whole
analysed tokens plus length-scaled fuzziness instead. That is a strict
upgrade for a whole word, misspelled or not - but it means a PARTIAL word
typed as a prefix of a product name can stop matching. "hydraul" no longer
finds "Hydraulic Flange Assembly": matching it against "hydraulic" needs
two edits, and at seven characters this plugin allows only one. Stock's
raw substring test had no such limit.

This is most noticeable in the autocomplete dropdown, which fires on every
keystroke and so spends most of its life showing partial words. It does
not affect identifiers: SKUs and manufacturer part numbers are indexed as
n-grams specifically so a partial fragment like "234" keeps matching
"fmsa-ab-1234" the same way it always did.


REQUIRED SETTING - read this before anything else
---------------------------------------------------
Go to Configuration > Settings > Catalog settings and set "Minimum search
term length" (ProductSearchTermMinimumLength) to 2.

nopCommerce rejects a search term shorter than this setting before this
plugin - or any plugin - is ever consulted. This store's part numbers carry
a two-character middle segment (see below), and the default minimum term
length is longer than that. Skip this step and searching that segment
returns nothing, no matter what the plugin does. This is not a plugin bug
and the plugin cannot work around it; the setting has to be changed.


SKU matching
-------------
Matching is by SUBSTRING, and it is CASE-INSENSITIVE throughout - "AB-1234"
and "ab-1234" return identical results.

Take this store's real SKU pattern, fmsa-ab-1234, apart:

  - "1234"     finds it (matches the number segment)
  - "ab-1234"  finds it (matches two segments together)
  - "ab1234"   finds it (the hyphen is not required)
  - "234"      finds it (matches inside the number segment)

The constant "fmsa" prefix needs no special handling. It's the same four
characters on every product, so it carries almost no weight in ranking -
searching "fmsa" alone matches everything and distinguishes nothing, while
the segments that vary from product to product are what rank a result to
the top.


The two-pass rule: exact first, fuzzy only as a fallback
-----------------------------------------------------------
Identifiers (SKU and manufacturer part number) are searched in two passes:

  1. First, a strict pass: exact match, segment match, and substring match,
     as described above. If this pass finds anything, those results are
     what gets returned.

  2. Only when the strict pass finds NOTHING does the plugin fall back to a
     fuzzy pass that tolerates a mistyped identifier.

The reason for the two passes, rather than blending them: two part numbers
one digit apart are different parts. If a customer searches an exact SKU
that exists in the catalogue, they should get that product and nothing
else - not that product plus a neighbor whose SKU happens to be similar. So
whenever a strict, exact-ish match exists, that is all that is shown, full
stop. Fuzzy identifier results only ever appear when the strict pass came
back empty, and those results are approximate - it is a best guess at what
was meant, not a confirmed match. Product names and descriptions are always
matched with typo tolerance, in both passes; only identifiers get the
two-pass treatment, because a wrong guess on a name is a minor annoyance
and a wrong guess on a part number is a wrong part.


Known limitation
------------------
If a product's NAME happens to contain its own SKU as text, a near-miss
identifier search can surface that product through the name field's own
typo tolerance, bypassing the strict-first rule described above - because
that match came from the name field, not the identifier fields. This does
not affect the current catalogue: none of its product names contain SKUs.
Worth knowing if that ever changes.


Settings
---------
Admin area > Configuration > Local plugins > "Better product search" >
Configure.

  Enabled
      The master switch. Off after installation, so a fresh install never
      touches search results until you turn it on. While off, every search
      goes through nopCommerce's stock behaviour, unchanged.

      This setting can be overridden per store, and doing so controls
      whether that store's searches are served from the index.

      What is NOT per store is the index itself. There is one index - a
      single set of files under App_Data - shared by every store this
      installation runs. Index maintenance follows the global setting
      rather than any per-store override, so the index stays complete and
      current no matter which stores are currently using it. Store
      filtering of results is done by nopCommerce's own query at search
      time, never by the index, so a store can never see another store's
      products through this plugin.

  Maximum index results
      An internal cap on how many candidate ids the index hands back before
      nopCommerce applies its own filtering (publish status, ACL, store,
      etc.). The default is generous; you are unlikely to need to change
      it.

Below the settings, the configuration page also shows the live document
count and whether the index is currently available, and has a "Rebuild
now" button.


Product images in the autocomplete dropdown
--------------------------------------------
nopCommerce can show a product thumbnail beside each autocomplete suggestion.
This is a stock feature, not part of this plugin, and it works normally with
the plugin installed - the plugin decides which products match, nopCommerce
still builds the display models and their picture URLs.

Two settings, in two different places:

  Configuration > Settings > Catalog settings
      "Show product images in search auto complete" - off by default.

  Configuration > Settings > Media settings
      "Auto complete search thumb picture size" - defaults to 20 pixels,
      which is too small to recognise a product. 40 is a more useful
      starting point; adjust it against your own photography.

Products without a picture fall back to the store's default image, so the
dropdown stays aligned rather than showing gaps.

This applies to the storefront autocomplete only. The admin product search
uses a different view and shows no thumbnails.


Installing
-----------
"Misc.BetterSearch" contains the compiled plugin. Drop it into the
\Plugins directory on your server, then in the admin area go to
Configuration > Local plugins, click "Reload list of plugins", find
"Better product search" and click Install.

The plugin ships DISABLED. After installing, set the minimum search term
length as described above, then go to the plugin's Configure page, tick
Enabled, save, and click "Rebuild now" to build the index for the first
time. Nothing the plugin does affects search results until both of those
steps are done.


Upgrading over an existing installation
-----------------------------------------
Replace the "Misc.BetterSearch" folder on the server and restart the site.
Do NOT uninstall the plugin first - uninstalling removes its settings and
its scheduled rebuild task, and you would have to reconfigure and rebuild
from scratch.


The index, and what happens when it's missing
-------------------------------------------------
The index lives under App_Data on the web server, as a set of files the
plugin manages itself - it is not stored in the nopCommerce database.

This design assumes a SINGLE web server. If this installation ever runs
behind a load balancer with more than one web server, each server would
build and hold its own copy of the index from its own local App_Data, and
the two copies would drift apart independently as products change - one
server's search results could disagree with another's. Do not add a second
web server to this installation without revisiting that.

If the index is missing (for example, deleted by hand) or fails to open
(for example, corrupted), the plugin detects that and falls back to
nopCommerce's stock search automatically. Search keeps working; it just
loses the ranking and substring matching described above until the index
is rebuilt. Search degrades, it does not break.


Keeping the index current
----------------------------
Two mechanisms keep the index matching the catalogue:

  1. Live updates. When a product is added, changed, deleted, published or
     unpublished, the plugin updates the index for that product
     immediately, so search reflects catalogue changes right away.

  2. A scheduled full rebuild, every 15 minutes. This is the safety net
     for anything the live-update path might have missed - a dropped
     event, a direct database change that bypassed nopCommerce's normal
     product-update path. Before replacing the live index, the rebuild
     compares the old document count against the newly built one; if they
     differ, it writes a warning to the nopCommerce log (Admin area >
     System > Log) naming both counts, so a drift between the two
     mechanisms is a visible, investigable event rather than something
     that silently corrects itself run after run.

You can also trigger a rebuild manually at any time from the Configure
page's "Rebuild now" button - useful right after installing, or any time
you want to confirm the index matches the catalogue without waiting.


Unpublished products
----------------------
The index holds every product, published or not, and admin search
(Catalog > Products) can find unpublished products through it exactly as
before. Ranking, not filtering, is where the index does its work - it is
nopCommerce's own visibility and store rules that decide, at query time,
which of the ranked results a given caller is allowed to see. Storefront
search only ever shows published products to it; the admin area sees
everything.


Disabling
----------
Untick Enabled and save. Search reverts to nopCommerce's stock behaviour
immediately - no restart required, and the index itself is left alone on
disk so re-enabling later does not require a rebuild (though the scheduled
task will have kept it current anyway).


Built against nopCommerce 4.50.2, targeting net6.0. The Nop assemblies
carry version 4.5.0 across the whole 4.50.x line and the interfaces this
plugin uses are unchanged between patches, so it should load on any
4.50.x. To be certain, rebuild against your exact version - see below.

"Nop.Plugin.Misc.BetterSearch" contains the source code.


Building from source
---------------------
The project references Nop.Web, so it has to be built from inside a
nopCommerce 4.50 source tree:

  1. Copy the "Nop.Plugin.Misc.BetterSearch" directory into "src/Plugins"
     in your nopCommerce source tree.
  2. Add it to the solution and build:

       cd <nopcommerce>/src
       dotnet sln NopCommerce.sln add \
         Plugins/Nop.Plugin.Misc.BetterSearch/Nop.Plugin.Misc.BetterSearch.csproj
       dotnet build \
         Plugins/Nop.Plugin.Misc.BetterSearch/Nop.Plugin.Misc.BetterSearch.csproj \
         -c Release

  3. The output appears in
     src/Presentation/Nop.Web/Plugins/Misc.BetterSearch.
  4. Deploy that folder as described under "Installing" above.

The build directory ends up holding far more than the plugin needs to
deploy. DO NOT ship everything the build produces - deploy exactly this
allow-list, ten files:

  - Nop.Plugin.Misc.BetterSearch.dll
  - Nop.Plugin.Misc.BetterSearch.deps.json
  - Nop.Plugin.Misc.BetterSearch.pdb
  - plugin.json
  - logo.png
  - Views\_ViewImports.cshtml
  - Views\Configure.cshtml
  - Lucene.Net.dll
  - Lucene.Net.Analysis.Common.dll
  - J2N.dll

Everything else the build emits belongs to nopCommerce itself, is already
present on the server, and must NOT be copied into the plugin folder.
That includes the entire "runtimes\" tree the build produces (SkiaSharp,
SqlClient's native SNI library, System.Drawing - roughly 65MB on its own):
none of it is a dependency of this plugin, all of it ships with
nopCommerce already, and copying it in triples the deploy size for
nothing. It also includes the Nop.Web.* host files a newer SDK tends to
copy into the plugin output - Nop.Web, Nop.Web.deps.json,
Nop.Web.runtimeconfig.json, Nop.Web.staticwebassets.* - and any other
Nop.*.dll or Microsoft.*.dll that isn't in the list above; those are
nopCommerce's own assemblies, loaded already, and the bare "Nop.Web" file
in particular is a compiled host executable, not a plugin file.

The shipped "Misc.BetterSearch" directory in this repository is exactly
this allow-list and nothing else - use it as the reference for what a
correct build output looks like.

Notes if you are not building with the .NET 6 SDK:

  - nopCommerce's global.json pins SDK 6.0.101. A newer SDK works if you
    change "rollForward" to "latestMajor".
  - The post-build helper (src/Build/ClearPluginAssemblies.dll) is a net6.0
    app and needs the .NET 6 runtime. Without it, set
    DOTNET_ROLL_FORWARD=Major before running dotnet build.
  - A newer SDK is also more likely to emit the "runtimes\" tree and the
    Nop.Web.* host files called out above. Neither is cleaned up
    automatically regardless of SDK version - always apply the allow-list
    by hand before deploying.


Running the tests
------------------
"Nop.Plugin.Misc.BetterSearch.Tests" contains the test suite, under the
"Tests" directory alongside "nopCommerce 4.50" in this repository. Like
the plugin itself, its .csproj carries a ProjectReference to the plugin
project by a relative path, so it only resolves once the test project sits
at src/Tests/ of a nopCommerce 4.50.2 source tree, alongside the plugin at
src/Plugins/. It cannot be built or run from this repository in place.

  1. Copy both projects into the nopCommerce tree:

       rm -rf <nopcommerce>/src/Plugins/Nop.Plugin.Misc.BetterSearch \
              <nopcommerce>/src/Tests/Nop.Plugin.Misc.BetterSearch.Tests
       cp -R Nop.Plugin.Misc.BetterSearch <nopcommerce>/src/Plugins/
       cp -R Tests/Nop.Plugin.Misc.BetterSearch.Tests <nopcommerce>/src/Tests/

  2. Run the tests from inside the copied test project:

       cd <nopcommerce>/src/Tests/Nop.Plugin.Misc.BetterSearch.Tests
       DOTNET_ROLL_FORWARD=Major dotnet test

The DOTNET_ROLL_FORWARD=Major prefix is required whenever the machine has
only a newer .NET SDK installed and not the net6.0 runtime that
global.json and the project files target: it tells the .NET host to roll
forward to the highest installed major version instead of failing to find
a matching net6.0 runtime. It is harmless, and unnecessary, on a machine
that does have the net6.0 runtime installed.
