# Better Search — staging test script

**Plugin:** Misc.BetterSearch 1.00, built against nopCommerce 4.50.2
**Purpose:** confirm on a real catalogue what 85 automated tests could only confirm against fabricated data.

The test suite builds real Lucene indexes and asserts real matches, so the
matching rules are well covered. What it has never seen is **your catalogue**:
your SKU formats, your product names, your permissions, your data volume.
Everything below needs a running store.

**Run this on staging, not production.** Take a database backup first.

This plugin decides what customers can find. A wrong result here is not a
cosmetic bug — it is a customer ordering the wrong part.

Budget about 45 minutes.

---

## The rule being tested

Identifiers — SKU, manufacturer part number, GTIN — match **exactly, by segment,
and by substring**, and are **never fuzzy-matched** while the plugin is in its
default configuration. Product names and descriptions get typo tolerance.

The index only **ranks**. nopCommerce still decides what each customer is
allowed to see.

---

## Part 1 — Setup

### 1.1 The prerequisite that will otherwise waste your afternoon

**Configuration → Settings → Catalog settings → "Minimum search term length" → set it to 2.**

nopCommerce rejects shorter searches in its own code, *before* this plugin is
ever consulted. Your SKU middle segment (`fmsa-**xx**-xxxx`) is two characters.
With the default of 3, searching that segment returns "search term must be at
least 3 characters" no matter how well the plugin works.

The plugin's configuration page shows a warning if this is still above 2.

### 1.2 Install

Drop the `Misc.BetterSearch` folder into `\Plugins`, then **Configuration →
Local plugins → Reload list of plugins → Install**. The site restarts.

Confirm the plugin list shows a **magnifying glass icon**, not the generic
nopCommerce logo. If it shows the generic one, `logo.png` did not deploy.

### 1.3 Configure and build the index

**Configuration → Local plugins → Better product search → Configure.**

| Setting | Value |
| --- | --- |
| Enabled | ticked |
| Maximum index results | 2000 (leave it) |
| Allow approximate fallback | **leave OFF** — see Part 6 |

Save, then click **Rebuild now**.

The success message reports how many products were indexed. **Check that number
against your actual product count** (Catalog → Products shows the total). They
should match. If the page reports an error or a count mismatch instead, stop and
read the system log — that is the plugin telling you the rebuild did not do what
it claimed.

While you are here, confirm the page renders readable labels rather than raw
strings like `Plugins.Misc.BetterSearch.Fields.Enabled`.

### 1.4 Turn on autocomplete images

Two settings, in two different places.

**Configuration → Settings → Catalog settings:**
- tick **"Show product images in search auto complete"** (off by default)

**Configuration → Settings → Media settings:**
- **"Auto complete search thumb picture size"** — the default is **20 pixels**,
  which is too small to recognise anything. Try **40**, look at it, adjust.

These are nopCommerce's own settings, not the plugin's. The plugin returns real
product records, so the image pipeline works exactly as it does without it.

### 1.5 Note some real SKUs

Pick three or four products and write down their SKUs, including:

- one plain product SKU
- if you have them, one product carrying **variant SKUs** on its attribute
  combinations (Catalog → Products → edit → Product attributes → the
  combinations grid shows their SKUs)
- two products whose SKUs differ by a single character, if any exist

---

## Part 2 — SKU matching, the core requirement

Using a real SKU of the form `fmsa-ab-1234`:

| # | Search for | Expected |
| --- | --- | --- |
| 1 | the whole SKU, `fmsa-ab-1234` | that product, first |
| 2 | the whole SKU in capitals | identical results to lowercase |
| 3 | the last segment, `1234` | that product, plus any other SKU containing 1234 |
| 4 | two segments, `ab-1234` | that product, ranked above a bare `1234` search |
| 5 | no separators, `ab1234` | that product |
| 6 | a partial segment, `234` | products whose SKU contains 234 |
| 7 | just `fmsa` | effectively everything — the shared prefix carries almost no weight |

Case 6 is the one that stock nopCommerce could not do at all: its SKU matching
was exact-only, so a partial SKU returned nothing.

### 2.1 The near-miss test — the most important check here

Find two products whose SKUs differ by one character (or temporarily set a
second product's SKU to differ by one digit from a first).

Search the **first** SKU. The result must be **that product only**. The
one-character-different product must **not** appear.

Two part numbers one digit apart are different parts. If both come back, stop
and tell me — that is the failure this plugin's design exists to prevent.

### 2.2 Variant SKUs

If any product carries per-combination SKUs, search one of them. The **parent
product** should come back.

This matches what stock nopCommerce did. If it returns nothing, variant SKUs are
not reaching the index — worth telling me.

---

## Part 3 — Text search

| Search for | Expected |
| --- | --- |
| a full product name | that product, first |
| two words from a name, in the **wrong order** | still found |
| a product name with a **typo** (one wrong letter) | still found |
| a word that appears only in a description | that product, ranked below name matches |

### 3.1 A place it is worse than stock — check whether it matters to you

Stock nopCommerce matched product names by raw substring, so typing `hydraul`
found "Hydraulic Flange Assembly". This plugin matches whole words plus typo
tolerance, so a **partial word prefix** may no longer match.

Try a few partial words your customers actually type. If this turns out to hurt,
tell me — it is fixable by adding prefix matching to the name field, but it is a
deliberate trade today and I would rather change it on evidence than guess.

---

## Part 4 — Autocomplete

Type into the storefront search box, slowly, and watch the dropdown.

| Check | Expected |
| --- | --- |
| Suggestions appear as you type | yes |
| **Product images appear beside each suggestion** | yes, once 1.4 is set |
| Images are large enough to recognise | adjust the thumb size if not |
| Products with no photo | show the default placeholder, not a gap |
| Typing a partial SKU, e.g. `1234` | suggests the matching products |
| Response feels immediate | no visible lag per keystroke |

Autocomplete fires on **every keystroke** and is the hottest path in the plugin.
If it feels sluggish on your catalogue, say so — it is the one place where the
"fetch all matches, page in memory" design would show first.

---

## Part 5 — Permissions and visibility, which the index must never override

This is the plugin's central safety claim: the index says which products
*match*, nopCommerce still says who may *see* them.

| Setup | Search | Expected |
| --- | --- | --- |
| Unpublish a product | its SKU | **not** in storefront results |
| The same product | its SKU, in **admin** product search | **found** — admin sees unpublished |
| A product limited to a customer role you are not in | its SKU | **not** found on the storefront |
| Multi-store only: a product mapped to another store | its SKU | **not** found on this store |

Then **republish** and search again — it should reappear within 15 minutes, or
immediately after a manual Rebuild now.

If any product shows up where it should not, stop and tell me at once. Nothing
else on this list matters as much.

---

## Part 6 — The approximate fallback, deliberately left off

The plugin can fall back to fuzzy identifier matching when a strict search finds
nothing. **It ships off**, because nothing in this version labels those results
as approximate — and an unlabelled near-miss part number is worse than "no
results".

To see what it does, tick **Allow approximate fallback**, save, and search a
deliberately mistyped SKU. You should get the closest product back, with nothing
saying it is a guess. That is exactly why it is off.

**Turn it back off before going live.** It becomes safe once the "showing
closest matches" notice ships.

---

## Part 7 — Failure behaviour

| Test | Expected |
| --- | --- |
| Untick Enabled, save, search | stock nopCommerce behaviour returns immediately |
| Re-tick, save, search | plugin behaviour returns; no rebuild needed |
| Stop the site, delete `App_Data/BetterSearch/index`, start, search | results still come back, via stock search; a warning is logged |
| Click Rebuild now after that | index restored, count matches your product total |

The third row is the important one: a missing or corrupt index must **degrade**
to stock search, never break the page. The plugin logs a warning so you can tell
it happened — check **System → Log** and confirm the warning is there.

---

## Part 8 — Keeping current

| Test | Expected |
| --- | --- |
| Edit a product's name, search the new name | found within a few seconds |
| Add a new product, search its SKU | found within a few seconds |
| Delete a product, search its old SKU | gone |
| Import products in bulk, then search several | all found; check the log for warnings |

A scheduled task rebuilds the whole index every 15 minutes as a safety net, and
logs a warning if it finds the live index had drifted from what a fresh rebuild
produces. **After the bulk import, check System → Log for drift warnings** —
that is the plugin reporting that live updates missed something, which is
exactly what it is there for.

---

## Sign-off

| Check | Pass |
| --- | --- |
| Minimum search term length set to 2 | ☐ |
| Plugin shows its own magnifying-glass icon | ☐ |
| Rebuild reports a count matching the catalogue | ☐ |
| Whole SKU, segment, partial segment and no-separator searches all work | ☐ |
| Case makes no difference | ☐ |
| **A one-character-different SKU does NOT come back** | ☐ |
| Variant SKUs find the parent product | ☐ |
| Multi-word and typo'd name searches work | ☐ |
| Autocomplete shows images at a readable size | ☐ |
| **Unpublished / restricted / other-store products never appear** | ☐ |
| Approximate fallback confirmed OFF for go-live | ☐ |
| Disabling the plugin restores stock search | ☐ |
| Deleting the index degrades to stock search and logs a warning | ☐ |
| Edits and new products appear within seconds | ☐ |
| Bulk import leaves no drift warnings | ☐ |

---

## If something is wrong

1. **Untick Enabled** and save. Search reverts to stock immediately — no
   restart, no uninstall. That is the fastest safe stop.
2. If it must come out entirely, delete the folder from `\Plugins` and restart.
   Do **not** use Uninstall as a first move: it deletes the plugin's settings,
   the scheduled task and the index.
3. Note that the service overrides remain registered while the folder is
   present, installed or not. Only removing the folder and restarting fully
   detaches them.

When reporting a problem, include: what you searched for, what came back, what
you expected, and anything in **System → Log** from around that time. For a
wrong-results problem the search term matters more than a screenshot.
