# Better product search — design

**Date:** 2026-08-29
**Target:** nopCommerce 4.50.2, net6.0
**Plugin:** `Nop.Plugin.Misc.BetterSearch`, system name `Misc.BetterSearch`
**Status:** approved, ready for implementation planning

## Purpose

Replace nopCommerce's product search with one that tolerates typos, matches
partial SKUs, handles multi-word queries, and ranks results by how well they
match.

## What is wrong with stock search

This is the entire matching clause of `ProductService.SearchProductsAsync`
(4.50.2, around line 866):

```csharp
where p.Name.Contains(keywords) ||
    (searchDescriptions && (p.ShortDescription.Contains(keywords) || p.FullDescription.Contains(keywords))) ||
    (searchManufacturerPartNumber && p.ManufacturerPartNumber == keywords) ||
    (searchSku && p.Sku == keywords)
```

Five concrete failures follow from it:

1. **Multi-word queries barely work.** `Name.Contains(keywords)` matches the
   whole phrase as one substring. Searching "red running shoes" does not match
   a product called "Running Shoes - Red". This is the biggest real-world
   failure, ahead of typos.
2. **SKU matching is exact.** `searchSku` defaults to true, so SKU search
   already exists — but `p.Sku == keywords` means a partial SKU, or one typed
   without its punctuation, returns nothing.
3. **Manufacturer part number is exact** for the same reason.
4. **No relevance ranking.** Results are ordered by `ProductSortingEnum`
   — Position, Name, Price — never by match quality. An exact SKU hit and a
   vague description match sort identically.
5. **No typo tolerance and no stemming.** "shoe" does not match "shoes";
   "runnning" matches nothing.

`LIKE '%term%'` is also non-sargable, so every search scans. That is tolerable
at this catalogue size and is not a motivating problem here.

## Scope

**In:** the search results page, the autocomplete dropdown, and admin product
search. All three call `IProductService.SearchProductsAsync`, so one override
covers them.

**Out:** category and manufacturer listing pages. They share the same method
but usually carry no keyword, and the plugin delegates entirely to the base
implementation whenever `keywords` is empty.

## Decisions and rationale

### Lucene.NET, in process

The index is built and queried inside the nopCommerce process and stored on
disk under `App_Data`. No new service to run, monitor or pay for; it installs
like any other plugin.

Alternatives rejected:

- **SQL Server full-text search.** Gives word-level matching, stemming and
  ranking with no sync work, because the database maintains the index. But its
  fuzzy matching is `SOUNDEX`/`DIFFERENCE` — crude phonetic matching, not edit
  distance. It would catch "smith/smyth" and miss "recieve/receive". Typo
  tolerance is the headline requirement, so this fails on the main point.
- **A dedicated engine** (Meilisearch, Typesense, Elasticsearch). Better than
  Lucene on every axis, at the cost of a service to run, secure, back up and
  keep in sync, plus a new failure mode when it is down. Disproportionate for a
  catalogue of this size.

### The index ranks; nopCommerce filters

This is the most important decision in the design.

```
query "runnin shoe"
   ↓
Lucene index  →  product IDs, best match first
   ↓
nopCommerce's existing query, restricted to those IDs
   ↓ applies: published, ACL, store mapping, availability dates,
   ↓          category, manufacturer, vendor, price range, spec filters
   ↓
survivors re-sorted into the index's order
```

A Lucene index is a snapshot. It does not know that a product was unpublished,
moved out of a store, or had its customer-role ACL changed since the last
write. **A plugin that serves results straight from its index will eventually
show customers products they must not see.** Handing IDs back to nopCommerce's
own query makes that entire class of bug impossible and means this plugin never
reimplements permission logic.

It also degrades gracefully: a stale index can omit a newly published product
for a minute, but it cannot reveal a withdrawn one.

The cost is two round trips, and paging that cannot be pushed into SQL because
the ordering lives in Lucene. At under 5,000 products the plugin fetches all
matching IDs and pages in memory.

### Fuzziness is length-scaled, and exact always wins

| Term length | Edits allowed |
| --- | --- |
| 1-3 characters | 0 — exact only |
| 4-7 characters | 1 |
| 8 or more | 2 |

Short terms get no fuzziness because at three characters almost everything is
within one edit of everything else, which is how fuzzy search starts returning
noise. Exact matches are boosted so they always outrank fuzzy ones.

### Identifiers are not fuzzy-matched until everything else has failed

Typo tolerance is dangerous on part numbers: `1234` and `1284` are one edit
apart, so a fuzzy identifier match can confidently return a different real
product. Someone who sees no results searches again; someone who sees the wrong
part may order it.

The search therefore runs in two passes:

1. **Strict pass.** Fuzzy matching on name, descriptions and tags as above, but
   SKU, part number and GTIN are matched exactly, by segment and by substring
   only — never fuzzily. Almost every search ends here.
2. **Approximate pass**, run only when the strict pass returns nothing. The same
   query is retried with fuzziness allowed on identifiers too, and the results
   are marked as approximate so the page can say so.

The customer is never shown a near-miss part number while an exact answer
exists, and never shown one silently.

### SKU matching: substring, not prefix

The store's SKUs follow a fixed pattern — `fmsa-xx-xxxx` — where the leading
segment is constant across the whole catalogue and staff search by the varying
parts. Prefix matching is therefore useless here: every SKU shares the prefix,
and nobody searches by it.

Each SKU is indexed three ways:

| Indexed as | `fmsa-ab-1234` becomes | Boost |
| --- | --- | --- |
| Raw, lowercased | `fmsa-ab-1234` | highest |
| Segments, split on non-alphanumerics, plus the normalised whole | `fmsa`, `ab`, `1234`, `fmsaab1234` | high |
| N-grams over the normalised form (2 to 10 characters) | `123`, `234`, `ab12`, `fmsaab`, … | moderate |

This gives substring matching anywhere in the SKU:

- `fmsa-ab-1234` — exact hit, top of the results
- `1234` — segment hit
- `ab-1234` — two segment hits, so it outranks either alone
- `ab1234` — matches the normalised whole
- `234` — n-gram hit, ranked below a whole-segment match

**The constant prefix needs no special handling.** Because `fmsa` appears in
every SKU, Lucene's inverse-document-frequency weighting reduces its
contribution to near zero on its own. Searching it returns everything, ordered
by whatever else was typed.

The same treatment is applied to manufacturer part number. GTIN stays exact
only, since it is an external identifier that is either right or wrong.

### Indexed fields and weights

| Field | Weight | Matching |
| --- | --- | --- |
| SKU | highest | exact, segment and substring, as above |
| Manufacturer part number | high | exact, segment and substring |
| GTIN | high | exact only |
| Name | high | per-term, fuzzy, stemmed |
| Short description | medium | per-term, fuzzy, stemmed |
| Full description | low | per-term, fuzzy, stemmed |
| Product tags | medium | per-term |
| Category and manufacturer names | low | per-term |

A SKU or part-number hit always outranks a name or description hit for the same
term, so someone typing a part number gets the part, not a product that happens
to mention the number in its copy.

### Unpublished products are indexed

Admin product search passes `showHidden: true` and must find unpublished
products. Since the index only ranks and never authorises, indexing everything
is both safe and necessary — nopCommerce's own query applies `published` for
storefront callers and skips it for admin ones.

### Relevance ordering, unless the customer chose otherwise

When a keyword is present and the caller asks for the default ordering
(`ProductSortingEnum.Position`), results come back in relevance order. If the
customer explicitly sorts by price or name, that choice is honoured and
relevance is used only to decide *which* products match.

### Sync: live updates with a rebuild safety net, plus a drift check

Product changes update the index immediately via `EntityInsertedEvent<Product>`,
`EntityUpdatedEvent<Product>` and `EntityDeletedEvent<Product>` consumers. A
scheduled task rebuilds the whole index periodically regardless.

The periodic rebuild exists because event coverage is never provably complete —
imports, direct SQL, and plugin code paths can all change a product without
raising the event you hooked.

**That safety net has a failure mode of its own: it silently repairs drift, so
a missed event never presents as a symptom.** To stop it concealing bugs, each
scheduled rebuild compares the document count and a content checksum of the
freshly built index against the live one, and logs a warning when they differ.
The net stays, but it reports rather than hides.

At this catalogue size a full rebuild is a second or two, so the schedule can
be frequent and the rebuild can also run at startup.

### Falls back to stock search

If the index directory is missing, locked, corrupt, or a query throws, the
plugin logs a warning and delegates to `base.SearchProductsAsync`. Search
quality drops to stock; search never breaks. This also covers the window
between installing the plugin and the first index build completing.

## Architecture

### Components

| Component | Responsibility |
| --- | --- |
| `BetterSearchSettings` | enabled, fuzziness on/off, field weights, minimum term length, rebuild schedule |
| `SearchIndexManager` | owns the Lucene directory, writer and reader lifecycle; build, update one, delete one, rebuild all |
| `ProductDocumentBuilder` | maps a `Product` to a Lucene document — pure, testable |
| `SearchQueryBuilder` | maps a user query string to a Lucene query with weights and per-term fuzziness — pure, testable |
| `BetterSearchProductService : ProductService` | overrides `SearchProductsAsync`; delegates when there is no keyword or the index is unavailable |
| `ProductIndexEventConsumer` | keeps the index current on product insert/update/delete |
| `RebuildSearchIndexTask : IScheduleTask` | periodic full rebuild plus the drift check |
| `MiscBetterSearchController` + views | admin configuration, "Rebuild now", index status |
| `DidYouMeanViewComponent` | renders the suggestion into the search page's widget zone |
| `BetterSearchPlugin` | `BasePlugin, IMiscPlugin, IWidgetPlugin` — lifecycle, locales, and the widget zone declaration |

`ProductDocumentBuilder` and `SearchQueryBuilder` are deliberately pure: the
matching and ranking rules — the part most likely to be wrong — become
unit-testable without a store, a database or an index.

### Registration

An `INopStartup` with `Order` above 2002 registers
`IProductService → BetterSearchProductService`, following the pattern already
proven in this repository by the handling fee plugin, which nopCommerce's own
Sendinblue plugin uses to replace `IWorkflowMessageService`.

### Where the index lives

`App_Data/BetterSearch/index`, created on demand.

**This assumes a single instance.** Two web servers would each hold their own
index on their own disk and drift apart, giving different results depending on
which server answered. The store is single-instance, so this is fine — but it
is the assumption that would need revisiting first if the site is ever scaled
out, and the plugin should log its index path at startup so the situation is
visible.

## Extras

### "Did you mean" on zero results

When a search returns nothing, the plugin suggests the closest indexed term
using Lucene's spell-check support.

**Delivered as a widget, not another service override.** The search page already
exposes `PublicWidgetZones.ProductSearchPageBeforeResults`
(`Views/Catalog/Search.cshtml:90`), so the plugin additionally implements
`IWidgetPlugin` and renders a view component into that zone. This avoids
overriding `ICatalogModelFactory`, which would otherwise be needed because
`SearchProductsAsync` returns `IPagedList<Product>` and has nowhere to carry a
suggestion.

The same widget carries the **approximate-results notice** required by the
two-pass identifier rule. When the strict pass finds nothing and the
approximate pass supplies the results, the widget says so — for example
"No exact match for fmsa-ab-1284. Showing closest matches." Without that notice
the two-pass design would quietly present a different part number as though it
were the one asked for, which is the outcome the rule exists to prevent.

The suggestion is computed during the search itself, not by re-querying. When
the override runs it records the query and the result count in
`HttpContext.Items`; the widget reads that request-scoped value and renders only
when the count is zero. No extra database or index round trip.

With fuzzy matching already enabled, true zero-result searches become uncommon,
so this is a small safety net rather than a headline feature — and being a
widget, it is also the easiest part to drop if it proves unnecessary.

### Search term logging with result counts

nopCommerce already records search keywords: the `SearchTerm` entity holds
`Keyword`, `StoreId` and `Count`, and `CatalogModelFactory` writes to it
through `ISearchTermService`. That gives *what* was searched but not whether it
worked.

The plugin adds a `BetterSearchTermLog` table recording the keyword, the number
of results returned, and whether the fallback was used. That is the data needed
to tune weights and fuzziness later — specifically, which searches return
nothing and which return so much that ranking is doing the real work.

### Synonyms — not in version one

Deliberately excluded. Synonyms need an admin CRUD screen and catalogue-specific
curation, and their value is unknown until the core is running against real
traffic. The search-term log is what tells us whether they are needed.

## Store prerequisite: minimum search term length

`CatalogSettings.ProductSearchTermMinimumLength` defaults to **3** and is
enforced in `CatalogModelFactory` (around line 1689) and `CatalogController`
(line 333) — both **before** this plugin's override is reached. A search shorter
than the minimum never gets as far as the index.

The store's SKU pattern `fmsa-xx-xxxx` has a two-character middle segment, and
staff search by it. With the default setting, searching `ab` is rejected with a
"minimum length" message no matter what this plugin does.

**Set it to 2** (Configuration → Settings → Catalog settings). This is a store
setting, not something the plugin can override.

To stop this being rediscovered painfully, the plugin's configuration page
reads the current value and displays a warning when it is greater than 2,
explaining that short SKU segment searches will be blocked before the plugin
sees them.

## Edge cases

| Case | Behaviour |
| --- | --- |
| Empty or whitespace keyword | delegate entirely to base; the index is not consulted |
| Keyword shorter than `CatalogSettings.ProductSearchTermMinimumLength` | delegate to base, matching stock behaviour |
| Index missing, locked or corrupt | log a warning, delegate to base |
| Index empty because the first build has not run | delegate to base |
| Product unpublished since the last index write | indexed, but filtered out by nopCommerce's query |
| Product published since the last index write | missing until the next update or rebuild |
| Admin search with `showHidden: true` | unpublished products found, because the index holds them |
| Explicit sort by price or name | honoured; relevance decides membership only |
| Query matches more products than the page size | all IDs fetched, paged in memory |
| Strict pass finds nothing, approximate pass finds something | results shown, clearly labelled approximate |
| Query matches nothing in either pass | "did you mean" suggestion, and the miss is logged |
| Search term shorter than the store minimum | rejected by nopCommerce before the plugin runs — see the prerequisite above |
| Search for the constant SKU prefix `fmsa` | matches everything; IDF weighting makes it contribute almost nothing to ranking |
| Identifier search where an exact match exists | never fuzzy-matched; the exact product wins |
| Deleted product | removed from the index by the delete event; also gone after any rebuild |
| Multi-store | store filtering is applied by nopCommerce's query, not the index |

## Testing

**Unit tests, no nopCommerce required** — the bulk of the value:

- `SearchQueryBuilder`: fuzziness by term length, exact-match boosting,
  multi-term queries, punctuation, empty and whitespace input, and that
  identifier fields carry no fuzziness on the strict pass but do on the
  approximate pass.
- SKU handling specifically, using the store's real pattern: `fmsa-ab-1234`
  found by `fmsa-ab-1234`, `1234`, `ab-1234`, `ab1234` and `234`; `ab-1234`
  ranking above `1234` alone; `1284` never returning the `1234` product on the
  strict pass; and `fmsa` matching everything without dominating the ranking.
- `ProductDocumentBuilder`: every indexed field present, weights applied,
  null-safe on missing descriptions or SKUs.
- Drift check: identical indexes compare equal, a missing document is detected.

**Integration tests against a real temporary index** — build an index from a
handful of fabricated products in a temp directory, then assert end to end that
"runnin shoe" finds "Running Shoes", `ABC-12` finds `ABC-12345`, exact SKU
outranks a name match, and multi-word queries match across word order.

**Mocked-service tests** for the override itself: that an empty keyword
delegates to base, that a thrown index error delegates to base, and that
returned IDs are re-sorted into index order.

**Manual, on a staging store**, since no test can prove it: that unpublished and
out-of-store products never appear in storefront results.

## Risks and maintenance

**Lucene.NET 4.8 is perpetually in beta.** It is widely used in production and
stable in practice, but the dependency will read `4.8.0-beta...`. This is the
single largest external risk in the design and it is worth stating plainly
rather than discovering at review.

**A plugin with a NuGet dependency must set
`<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>`**, unlike both
existing plugins in this repository, which set it false. Without it the Lucene
assemblies are not copied to the plugin output and the plugin fails to load at
runtime with no build-time warning. nopCommerce's own Avalara plugin sets it
true for exactly this reason. The packaged file set will therefore be larger
than the seven files the other two plugins ship, and the packaging step must be
written against what the build actually produces rather than a fixed list.

**Index sync is where these plugins usually fail**, which is why the drift check
is part of version one rather than a later addition.

**Single-instance assumption**, as described above.

**Overriding `IProductService`** touches a much-used service. Any future plugin
overriding the same interface will conflict, later `INopStartup.Order` winning.

## Out of scope

- Synonyms and stop-word customisation
- Faceted search and search-driven filtering beyond what nopCommerce already does
- Indexing anything other than products — no categories, manufacturers, topics or blog posts
- Multi-instance or shared-index deployment
- Replacing category or manufacturer listing queries

## Spec self-review notes

- **Gap found and closed:** the first draft specified "did you mean" without
  saying how a suggestion reaches the page. `SearchProductsAsync` returns
  `IPagedList<Product>`, which cannot carry one, so the implied answer was a
  third core override of `ICatalogModelFactory`. The search page turns out to
  expose `ProductSearchPageBeforeResults`, so a widget does the job with no
  override at all. Checked before writing, not assumed.
- **Verified against the 4.50.2 source rather than recalled:** the stock
  matching clause; that all three target surfaces call `SearchProductsAsync`;
  `IScheduleTask` living in `Nop.Services.ScheduleTasks` with a single
  `ExecuteAsync`; the entity event types; the existing `SearchTerm` entity and
  `ISearchTermService`; the search page's widget zones; and that a plugin with
  a NuGet dependency must set `CopyLocalLockFileAssemblies` to true, as
  nopCommerce's own Avalara plugin does.
- **Known unverified:** nothing in this design has been compiled or run. The
  Lucene.NET API surface in particular is described from knowledge, not from a
  build, and the implementation plan should expect to correct details against
  the real package.
