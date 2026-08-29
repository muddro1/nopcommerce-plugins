# Better Search — Core Implementation Plan (1 of 2)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace nopCommerce's product search with a Lucene-backed one that matches SKUs by substring, tolerates typos in text, and ranks by relevance.

**Architecture:** A Lucene.NET index built and queried in-process. It returns ordered product IDs; nopCommerce's own query then applies published/ACL/store/price filters, and the survivors are re-sorted into index order. One service override, `IProductService.SearchProductsAsync`, covers the search page, autocomplete and admin search. If the index is unavailable the plugin delegates to stock search.

**Tech Stack:** C# 10, net6.0, nopCommerce 4.50.2, Lucene.Net 4.8.0-beta00016 (+ Analysis.Common), NUnit 3.13.2, Moq 4.16.1, FluentAssertions 6.2.0.

**Spec:** `docs/superpowers/specs/2026-08-29-better-search-design.md`

**Scope of this plan:** everything in the spec EXCEPT the "did you mean" widget and search-term logging, which are plan 2 of 2. This plan delivers a working, installable plugin on its own.

## Global Constraints

- Target framework `net6.0`. Build against nopCommerce **4.50.2**. `plugin.json` declares `"SupportedVersions": [ "4.50" ]`.
- Plugin system name `Misc.BetterSearch`; namespace root `Nop.Plugin.Misc.BetterSearch`; locale keys prefixed `Plugins.Misc.BetterSearch.`.
- **`<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>` is mandatory.** Both existing plugins in this repo set it false. With a NuGet dependency, false means the Lucene assemblies are never copied to the plugin output and the plugin fails to load at runtime with no build-time warning. nopCommerce's own Avalara plugin sets it true for this reason.
- Only the .NET 10 SDK is installed. Prefix EVERY `dotnet` command with `DOTNET_ROLL_FORWARD=Major`, and the nopCommerce tree's `global.json` must have `"rollForward": "latestMajor"`.
- Locale resources MUST be registered from **both** `InstallAsync` and `UpdateAsync` via one shared private method. Registering only in `InstallAsync` leaves raw resource keys in the admin UI after an in-place upgrade — this has already happened once in this repo.
- **All matching is case-insensitive.** Every custom analyzer includes an explicit lowercase filter on the index side AND the query side. A keyword analyzer does no lowercasing of its own; used naively it yields case-sensitive SKU matching, which is the field users are most likely to capitalise.
- **Identifiers are never fuzzy-matched on the strict pass.** SKU, manufacturer part number and GTIN match exactly, by segment and by substring only. Fuzzy identifier matching happens only on the approximate pass, which runs solely when the strict pass returns nothing.
- The index **ranks**; nopCommerce **filters**. Never return products straight from the index — always hand IDs to the base query so published/ACL/store rules apply.
- Empty keyword, or a keyword shorter than `CatalogSettings.ProductSearchTermMinimumLength`, delegates entirely to base.

## Paths

```bash
PROJ="/Users/zach/Claude/Projects/NOPCommercePluginDiscount"
NOP="$HOME/Claude/Projects/nopCommerce-4.50.2"
```

`$PROJ` is the source of record and the git repo. `$NOP` is a build scratch area; never commit anything inside it. Copy both projects across before every build or test run:

```bash
rm -rf "$NOP/src/Plugins/Nop.Plugin.Misc.BetterSearch" "$NOP/src/Tests/Nop.Plugin.Misc.BetterSearch.Tests"
cp -R "$PROJ/BetterSearch/nopCommerce 4.50/Nop.Plugin.Misc.BetterSearch" "$NOP/src/Plugins/"
cp -R "$PROJ/BetterSearch/Tests/Nop.Plugin.Misc.BetterSearch.Tests" "$NOP/src/Tests/"
cd "$NOP/src/Tests/Nop.Plugin.Misc.BetterSearch.Tests" && DOTNET_ROLL_FORWARD=Major dotnet test -nologo
```

## File Structure

Source of record, under `$PROJ/BetterSearch/nopCommerce 4.50/Nop.Plugin.Misc.BetterSearch/`:

| File | Responsibility |
| --- | --- |
| `Nop.Plugin.Misc.BetterSearch.csproj` | project, Lucene refs, `CopyLocalLockFileAssemblies=true` |
| `plugin.json`, `logo.jpg` | manifest and icon |
| `BetterSearchDefaults.cs` | index folder, Lucene field names, schedule task type name |
| `BetterSearchSettings.cs` | enabled, fuzziness, weights, n-gram bounds |
| `Services/SkuNormaliser.cs` | pure: normalise, split into segments, generate n-grams |
| `Services/ProductDocumentBuilder.cs` | `Product` → Lucene `Document` |
| `Services/SearchQueryBuilder.cs` | query text → Lucene `Query`, strict and approximate |
| `Services/SearchIndexManager.cs` | index directory, writer/reader lifecycle, build/update/delete/rebuild, search |
| `Services/BetterSearchProductService.cs` | the `IProductService` override |
| `Infrastructure/NopStartup.cs` | DI registration |
| `Infrastructure/Cache/ProductIndexEventConsumer.cs` | keeps the index current |
| `Tasks/RebuildSearchIndexTask.cs` | scheduled full rebuild plus drift check |
| `BetterSearchPlugin.cs` | lifecycle, locales, schedule task install |
| `Models/ConfigurationModel.cs`, `Controllers/MiscBetterSearchController.cs`, `Views/*` | admin page |

Tests, under `$PROJ/BetterSearch/Tests/Nop.Plugin.Misc.BetterSearch.Tests/`.

**Why the first three service files are pure:** matching and ranking is the part most likely to be wrong, and it is the part a store owner will judge. Keeping it free of nopCommerce services means it can be tested exhaustively against a real in-memory index with no database, no DI and no mocks.

---

### Task 1: Project skeleton with Lucene

**Files:**
- Create: `$PROJ/BetterSearch/nopCommerce 4.50/Nop.Plugin.Misc.BetterSearch/Nop.Plugin.Misc.BetterSearch.csproj`
- Create: `.../plugin.json`, `.../BetterSearchDefaults.cs`, `.../Views/_ViewImports.cshtml`, `.../Views/Configure.cshtml` (placeholder)
- Create: `$PROJ/BetterSearch/Tests/Nop.Plugin.Misc.BetterSearch.Tests/Nop.Plugin.Misc.BetterSearch.Tests.csproj`

**Interfaces:**
- Consumes: nothing
- Produces: `BetterSearchDefaults.SYSTEM_NAME`, `.INDEX_FOLDER`, and the Lucene field-name constants listed below

- [ ] **Step 1: Create the folder structure**

```bash
mkdir -p "$PROJ/BetterSearch/nopCommerce 4.50/Nop.Plugin.Misc.BetterSearch"/{Services,Infrastructure/Cache,Tasks,Models,Controllers,Views}
mkdir -p "$PROJ/BetterSearch/Tests/Nop.Plugin.Misc.BetterSearch.Tests"
cp "$PROJ/HandlingFee/nopCommerce 4.50/Nop.Plugin.Misc.HandlingFee/logo.jpg" \
   "$PROJ/BetterSearch/nopCommerce 4.50/Nop.Plugin.Misc.BetterSearch/logo.jpg"
```

- [ ] **Step 2: Write the csproj**

Note `CopyLocalLockFileAssemblies` is **true** here, unlike the sibling plugins.

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <OutputPath>..\..\Presentation\Nop.Web\Plugins\Misc.BetterSearch</OutputPath>
    <OutDir>$(OutputPath)</OutDir>
    <!-- MUST be true: this plugin has NuGet dependencies (Lucene), and false
         means they are never copied to the plugin output. The plugin then
         fails to load at runtime with no build-time warning. -->
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Lucene.Net" Version="4.8.0-beta00016" />
    <PackageReference Include="Lucene.Net.Analysis.Common" Version="4.8.0-beta00016" />
  </ItemGroup>

  <ItemGroup>
    <None Remove="logo.jpg" />
    <None Remove="plugin.json" />
    <None Remove="Views\Configure.cshtml" />
    <None Remove="Views\_ViewImports.cshtml" />
  </ItemGroup>

  <ItemGroup>
    <Content Include="logo.jpg"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></Content>
    <Content Include="plugin.json"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></Content>
    <Content Include="Views\Configure.cshtml"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></Content>
    <Content Include="Views\_ViewImports.cshtml"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></Content>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\Presentation\Nop.Web\Nop.Web.csproj" />
    <ClearPluginAssemblies Include="$(MSBuildProjectDirectory)\..\..\Build\ClearPluginAssemblies.proj" />
  </ItemGroup>

  <Target Name="NopTarget" AfterTargets="Build">
    <MSBuild Projects="@(ClearPluginAssemblies)" Properties="PluginPath=$(MSBuildProjectDirectory)\$(OutDir)" Targets="NopClear" />
  </Target>

</Project>
```

- [ ] **Step 3: Write plugin.json**

```json
{
  "Group": "Misc",
  "FriendlyName": "Better product search",
  "SystemName": "Misc.BetterSearch",
  "Version": "1.00",
  "SupportedVersions": [ "4.50" ],
  "Author": "Zach Malamud",
  "DisplayOrder": 1,
  "FileName": "Nop.Plugin.Misc.BetterSearch.dll",
  "Description": "Replaces product search with a relevance-ranked index that matches SKUs by substring and tolerates typos in product names and descriptions."
}
```

- [ ] **Step 4: Write BetterSearchDefaults.cs**

```csharp
namespace Nop.Plugin.Misc.BetterSearch
{
    /// <summary>
    /// Constants for the better search plugin
    /// </summary>
    public static class BetterSearchDefaults
    {
        public const string SYSTEM_NAME = "Misc.BetterSearch";

        /// <summary>
        /// Index location, relative to the application's App_Data folder
        /// </summary>
        public const string INDEX_FOLDER = "BetterSearch/index";

        /// <summary>
        /// The scheduled rebuild task, registered on install
        /// </summary>
        public const string REBUILD_TASK_NAME = "Rebuild the product search index";
        public const string REBUILD_TASK_TYPE = "Nop.Plugin.Misc.BetterSearch.Tasks.RebuildSearchIndexTask, Nop.Plugin.Misc.BetterSearch";
        public const int REBUILD_TASK_PERIOD_SECONDS = 900;

        //Lucene field names. Every consumer uses these constants rather than string literals,
        //because a typo in a field name produces silently empty results rather than an error.
        public const string FIELD_PRODUCT_ID = "productid";
        public const string FIELD_NAME = "name";
        public const string FIELD_SHORT_DESCRIPTION = "shortdescription";
        public const string FIELD_FULL_DESCRIPTION = "fulldescription";
        public const string FIELD_TAGS = "tags";
        public const string FIELD_CATEGORIES = "categories";
        public const string FIELD_MANUFACTURERS = "manufacturers";
        public const string FIELD_GTIN = "gtin";

        //identifiers are indexed three ways; see the spec's "SKU matching" section
        public const string FIELD_SKU_RAW = "sku_raw";
        public const string FIELD_SKU_SEGMENT = "sku_segment";
        public const string FIELD_SKU_NGRAM = "sku_ngram";
        public const string FIELD_MPN_RAW = "mpn_raw";
        public const string FIELD_MPN_SEGMENT = "mpn_segment";
        public const string FIELD_MPN_NGRAM = "mpn_ngram";
    }
}
```

- [ ] **Step 5: Write the Razor imports and a placeholder view**

`Views/_ViewImports.cshtml`:

```razor
@inherits Nop.Web.Framework.Mvc.Razor.NopRazorPage<TModel>
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
@addTagHelper *, Nop.Web.Framework

@using Nop.Web.Framework.UI
@using Nop.Web.Framework.Extensions
```

`Views/Configure.cshtml`:

```razor
@* replaced in Task 9 *@
<div></div>
```

- [ ] **Step 6: Write the test project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <Nullable>disable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="FluentAssertions" Version="6.2.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.0.0" />
    <PackageReference Include="Moq" Version="4.16.1" />
    <PackageReference Include="NUnit" Version="3.13.2" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.1.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\Plugins\Nop.Plugin.Misc.BetterSearch\Nop.Plugin.Misc.BetterSearch.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 7: Build, and confirm Lucene actually lands in the output**

```bash
rm -rf "$NOP/src/Plugins/Nop.Plugin.Misc.BetterSearch"
cp -R "$PROJ/BetterSearch/nopCommerce 4.50/Nop.Plugin.Misc.BetterSearch" "$NOP/src/Plugins/"
cd "$NOP/src"
DOTNET_ROLL_FORWARD=Major dotnet build Plugins/Nop.Plugin.Misc.BetterSearch/Nop.Plugin.Misc.BetterSearch.csproj -c Release -nologo 2>&1 | grep -E "error|Build succeeded"
ls "$NOP/src/Presentation/Nop.Web/Plugins/Misc.BetterSearch/" | grep -i lucene
```

Expected: `Build succeeded.`, and `Lucene.Net.dll` plus `Lucene.Net.Analysis.Common.dll` present in the output.

**If the Lucene DLLs are absent, stop.** Either `CopyLocalLockFileAssemblies` is not true, or nopCommerce's `ClearPluginAssemblies` post-build step removed them. Report which, with the output listing — do not proceed, because every later task will appear to work and the plugin will fail to load on a real store.

- [ ] **Step 8: Commit**

```bash
cd "$PROJ" && git add -A && git -c user.name="Zach Malamud" -c user.email="zach.malamud@gmail.com" commit -m "feat: better search plugin skeleton with Lucene

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01D1gESmDP8qdDtdGWiGayvB"
```

---

### Task 2: SKU normalisation

This is the heart of the store's requirement. SKUs look like `fmsa-xx-xxxx` where `fmsa` is constant, so staff search by the varying parts and prefix matching is useless.

**Files:**
- Create: `.../Services/SkuNormaliser.cs`
- Test: `.../Tests/.../SkuNormaliserTests.cs`

**Interfaces:**
- Consumes: nothing
- Produces:
  - `static string SkuNormaliser.Normalise(string value)`
  - `static IReadOnlyList<string> SkuNormaliser.Segments(string value)`
  - `static IReadOnlyList<string> SkuNormaliser.NGrams(string value, int minLength, int maxLength)`

- [ ] **Step 1: Write the failing tests**

```csharp
using FluentAssertions;
using Nop.Plugin.Misc.BetterSearch.Services;
using NUnit.Framework;

namespace Nop.Plugin.Misc.BetterSearch.Tests
{
    [TestFixture]
    public class SkuNormaliserTests
    {
        [Test]
        public void Normalise_lowercases_and_strips_punctuation()
        {
            SkuNormaliser.Normalise("FMSA-AB-1234").Should().Be("fmsaab1234");
        }

        [Test]
        public void Normalise_is_stable_for_input_that_is_already_normal()
        {
            SkuNormaliser.Normalise("fmsaab1234").Should().Be("fmsaab1234");
        }

        [Test]
        public void Normalise_handles_null_and_empty()
        {
            SkuNormaliser.Normalise(null).Should().BeEmpty();
            SkuNormaliser.Normalise("   ").Should().BeEmpty();
        }

        [Test]
        public void Segments_splits_on_punctuation_and_lowercases()
        {
            SkuNormaliser.Segments("FMSA-AB-1234").Should().Equal("fmsa", "ab", "1234");
        }

        [Test]
        public void Segments_ignores_repeated_and_trailing_separators()
        {
            SkuNormaliser.Segments("fmsa--ab-1234-").Should().Equal("fmsa", "ab", "1234");
        }

        [Test]
        public void Segments_of_a_value_with_no_separators_is_the_value_itself()
        {
            SkuNormaliser.Segments("fmsaab1234").Should().Equal("fmsaab1234");
        }

        [Test]
        public void Segments_handles_null_and_empty()
        {
            SkuNormaliser.Segments(null).Should().BeEmpty();
            SkuNormaliser.Segments("--").Should().BeEmpty();
        }

        [Test]
        public void NGrams_cover_every_substring_within_the_length_bounds()
        {
            var grams = SkuNormaliser.NGrams("fmsa-ab-1234", 3, 4);

            //generated over the NORMALISED form, so punctuation never appears in a gram
            grams.Should().Contain("123");
            grams.Should().Contain("234");
            grams.Should().Contain("1234");
            grams.Should().Contain("ab12");
            grams.Should().NotContain(g => g.Contains("-"));
        }

        [Test]
        public void NGrams_respects_its_bounds()
        {
            var grams = SkuNormaliser.NGrams("fmsa-ab-1234", 3, 4);

            grams.Should().OnlyContain(g => g.Length >= 3 && g.Length <= 4);
        }

        [Test]
        public void NGrams_are_distinct()
        {
            var grams = SkuNormaliser.NGrams("aaaa", 2, 2);

            grams.Should().Equal("aa");
        }

        [Test]
        public void NGrams_of_a_value_shorter_than_the_minimum_is_empty()
        {
            SkuNormaliser.NGrams("ab", 3, 4).Should().BeEmpty();
        }

        [Test]
        public void NGrams_handles_null()
        {
            SkuNormaliser.NGrams(null, 2, 5).Should().BeEmpty();
        }
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

```bash
rm -rf "$NOP/src/Tests/Nop.Plugin.Misc.BetterSearch.Tests"
cp -R "$PROJ/BetterSearch/Tests/Nop.Plugin.Misc.BetterSearch.Tests" "$NOP/src/Tests/"
cd "$NOP/src/Tests/Nop.Plugin.Misc.BetterSearch.Tests" && DOTNET_ROLL_FORWARD=Major dotnet test -nologo
```
Expected: FAIL — `SkuNormaliser` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nop.Plugin.Misc.BetterSearch.Services
{
    /// <summary>
    /// Turns an identifier such as a SKU or manufacturer part number into the several forms
    /// the index needs.
    ///
    /// The store's SKUs look like fmsa-xx-xxxx, where the leading segment is the same on every
    /// product. Staff search by the varying parts, so matching must work on any fragment of the
    /// identifier rather than only its beginning.
    ///
    /// Pure by design: no nopCommerce services, no I/O, so the matching rules can be tested
    /// exhaustively without a store.
    /// </summary>
    public static class SkuNormaliser
    {
        /// <summary>
        /// Lowercase and strip everything that is not a letter or digit.
        /// "FMSA-AB-1234" becomes "fmsaab1234", which is what lets a search for "ab1234"
        /// match a SKU written with separators.
        /// </summary>
        public static string Normalise(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                if (char.IsLetterOrDigit(character))
                    builder.Append(char.ToLowerInvariant(character));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Split on any run of non-alphanumeric characters, lowercased.
        /// "FMSA-AB-1234" becomes ["fmsa", "ab", "1234"], so a search for a whole segment
        /// such as "1234" is an exact token match rather than a substring scan.
        /// </summary>
        public static IReadOnlyList<string> Segments(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Array.Empty<string>();

            return value
                .Split(c => !char.IsLetterOrDigit(c))
                .Where(segment => segment.Length > 0)
                .Select(segment => segment.ToLowerInvariant())
                .ToList();
        }

        /// <summary>
        /// Every distinct substring of the normalised value between the given lengths.
        /// This is what makes a partial segment such as "234" match "fmsa-ab-1234".
        /// </summary>
        public static IReadOnlyList<string> NGrams(string value, int minLength, int maxLength)
        {
            var normalised = Normalise(value);
            if (normalised.Length < minLength)
                return Array.Empty<string>();

            var grams = new HashSet<string>();
            for (var length = minLength; length <= maxLength; length++)
            {
                for (var start = 0; start + length <= normalised.Length; start++)
                    grams.Add(normalised.Substring(start, length));
            }

            return grams.ToList();
        }

        private static IEnumerable<string> Split(this string value, Func<char, bool> isSeparator)
        {
            var builder = new StringBuilder();
            foreach (var character in value)
            {
                if (isSeparator(character))
                {
                    if (builder.Length > 0)
                    {
                        yield return builder.ToString();
                        builder.Clear();
                    }
                }
                else
                {
                    builder.Append(character);
                }
            }

            if (builder.Length > 0)
                yield return builder.ToString();
        }
    }
}
```

> The private `Split` extension is declared inside a static class, so it compiles as an extension method. If the compiler objects to an extension method on a nested private member, promote it to a normal private static method taking the string as its first argument and call it as `Split(value, predicate)`. Do not change the behaviour.

- [ ] **Step 4: Run the tests and confirm they pass**

```bash
rm -rf "$NOP/src/Plugins/Nop.Plugin.Misc.BetterSearch"
cp -R "$PROJ/BetterSearch/nopCommerce 4.50/Nop.Plugin.Misc.BetterSearch" "$NOP/src/Plugins/"
cd "$NOP/src/Tests/Nop.Plugin.Misc.BetterSearch.Tests" && DOTNET_ROLL_FORWARD=Major dotnet test -nologo
```
Expected: `Failed: 0, Passed: 12`

- [ ] **Step 5: Commit**

```bash
cd "$PROJ" && git add -A && git -c user.name="Zach Malamud" -c user.email="zach.malamud@gmail.com" commit -m "feat: SKU normalisation for substring identifier matching

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01D1gESmDP8qdDtdGWiGayvB"
```

---

### Task 3: Document builder and the index schema

**Files:**
- Create: `.../Services/ProductDocumentBuilder.cs`
- Test: `.../Tests/.../ProductDocumentBuilderTests.cs`

**Interfaces:**
- Consumes: `SkuNormaliser`, `BetterSearchDefaults` field constants
- Produces: `Document ProductDocumentBuilder.Build(ProductIndexInput input)` and the `ProductIndexInput` record below

`ProductIndexInput` exists so the builder stays free of nopCommerce entities and can be constructed in a test without a database:

```csharp
public record ProductIndexInput
{
    public int ProductId { get; init; }
    public string Name { get; init; }
    public string Sku { get; init; }
    public string ManufacturerPartNumber { get; init; }
    public string Gtin { get; init; }
    public string ShortDescription { get; init; }
    public string FullDescription { get; init; }
    public IList<string> Tags { get; init; } = new List<string>();
    public IList<string> Categories { get; init; } = new List<string>();
    public IList<string> Manufacturers { get; init; } = new List<string>();
}
```

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Linq;
using FluentAssertions;
using Lucene.Net.Documents;
using Nop.Plugin.Misc.BetterSearch;
using Nop.Plugin.Misc.BetterSearch.Services;
using NUnit.Framework;

namespace Nop.Plugin.Misc.BetterSearch.Tests
{
    [TestFixture]
    public class ProductDocumentBuilderTests
    {
        private static ProductIndexInput Sample()
        {
            return new ProductIndexInput
            {
                ProductId = 42,
                Name = "Running Shoes - Red",
                Sku = "FMSA-AB-1234",
                ManufacturerPartNumber = "MPN-99",
                Gtin = "5012345678900",
                ShortDescription = "Light running shoe",
                FullDescription = "A very light running shoe for road use",
                Tags = new[] { "running", "footwear" },
                Categories = new[] { "Shoes" },
                Manufacturers = new[] { "Acme" }
            };
        }

        [Test]
        public void Stores_the_product_id_retrievably()
        {
            var document = ProductDocumentBuilder.Build(Sample());

            document.Get(BetterSearchDefaults.FIELD_PRODUCT_ID).Should().Be("42");
        }

        [Test]
        public void Indexes_the_sku_raw_normalised_and_lowercased()
        {
            var document = ProductDocumentBuilder.Build(Sample());

            //raw is lowercased so matching is case-insensitive without relying on the analyzer
            document.Get(BetterSearchDefaults.FIELD_SKU_RAW).Should().Be("fmsa-ab-1234");
        }

        [Test]
        public void Indexes_every_sku_segment()
        {
            var document = ProductDocumentBuilder.Build(Sample());

            var segments = document.GetValues(BetterSearchDefaults.FIELD_SKU_SEGMENT);
            segments.Should().Contain(new[] { "fmsa", "ab", "1234" });
        }

        [Test]
        public void Indexes_sku_ngrams_so_partial_segments_match()
        {
            var document = ProductDocumentBuilder.Build(Sample());

            var grams = document.GetValues(BetterSearchDefaults.FIELD_SKU_NGRAM);
            grams.Should().Contain("234");
            grams.Should().Contain("fmsaab1234");
        }

        [Test]
        public void Indexes_the_manufacturer_part_number_the_same_way_as_the_sku()
        {
            var document = ProductDocumentBuilder.Build(Sample());

            document.Get(BetterSearchDefaults.FIELD_MPN_RAW).Should().Be("mpn-99");
            document.GetValues(BetterSearchDefaults.FIELD_MPN_SEGMENT).Should().Contain("99");
        }

        [Test]
        public void Indexes_gtin_exactly_and_does_not_gram_it()
        {
            var document = ProductDocumentBuilder.Build(Sample());

            document.Get(BetterSearchDefaults.FIELD_GTIN).Should().Be("5012345678900");
        }

        [Test]
        public void Indexes_the_text_fields()
        {
            var document = ProductDocumentBuilder.Build(Sample());

            document.Get(BetterSearchDefaults.FIELD_NAME).Should().Be("Running Shoes - Red");
            document.Get(BetterSearchDefaults.FIELD_SHORT_DESCRIPTION).Should().Be("Light running shoe");
            document.Get(BetterSearchDefaults.FIELD_FULL_DESCRIPTION).Should().Contain("road use");
        }

        [Test]
        public void Indexes_tags_categories_and_manufacturers()
        {
            var document = ProductDocumentBuilder.Build(Sample());

            document.GetValues(BetterSearchDefaults.FIELD_TAGS).Should().Contain("running");
            document.GetValues(BetterSearchDefaults.FIELD_CATEGORIES).Should().Contain("Shoes");
            document.GetValues(BetterSearchDefaults.FIELD_MANUFACTURERS).Should().Contain("Acme");
        }

        [Test]
        public void Tolerates_a_product_with_no_sku_or_descriptions()
        {
            var sparse = new ProductIndexInput { ProductId = 7, Name = "Bare product" };

            var document = ProductDocumentBuilder.Build(sparse);

            document.Get(BetterSearchDefaults.FIELD_PRODUCT_ID).Should().Be("7");
            document.Get(BetterSearchDefaults.FIELD_NAME).Should().Be("Bare product");
            document.GetValues(BetterSearchDefaults.FIELD_SKU_SEGMENT).Should().BeEmpty();
        }
    }
}
```

- [ ] **Step 2: Run and confirm failure**

Run the standard copy-and-test sequence. Expected: FAIL — `ProductDocumentBuilder` and `ProductIndexInput` do not exist.

- [ ] **Step 3: Implement**

Create `ProductIndexInput` as the record given above (in its own file `Services/ProductIndexInput.cs`), then:

```csharp
using System.Collections.Generic;
using Lucene.Net.Documents;

namespace Nop.Plugin.Misc.BetterSearch.Services
{
    /// <summary>
    /// Maps a product to the Lucene document that represents it.
    ///
    /// Identifiers are stored three ways - raw, by segment, and as n-grams - so that a search
    /// for any fragment of a SKU matches. See the spec's "SKU matching" section for why prefix
    /// matching alone is useless on this catalogue.
    /// </summary>
    public static class ProductDocumentBuilder
    {
        /// <summary>N-gram bounds for identifier fields</summary>
        public const int NGRAM_MIN = 2;
        public const int NGRAM_MAX = 10;

        public static Document Build(ProductIndexInput input)
        {
            var document = new Document
            {
                //StoredField so the id can be read back out of a hit
                new StringField(BetterSearchDefaults.FIELD_PRODUCT_ID, input.ProductId.ToString(), Field.Store.YES)
            };

            AddText(document, BetterSearchDefaults.FIELD_NAME, input.Name);
            AddText(document, BetterSearchDefaults.FIELD_SHORT_DESCRIPTION, input.ShortDescription);
            AddText(document, BetterSearchDefaults.FIELD_FULL_DESCRIPTION, input.FullDescription);

            AddIdentifier(document, input.Sku,
                BetterSearchDefaults.FIELD_SKU_RAW,
                BetterSearchDefaults.FIELD_SKU_SEGMENT,
                BetterSearchDefaults.FIELD_SKU_NGRAM);

            AddIdentifier(document, input.ManufacturerPartNumber,
                BetterSearchDefaults.FIELD_MPN_RAW,
                BetterSearchDefaults.FIELD_MPN_SEGMENT,
                BetterSearchDefaults.FIELD_MPN_NGRAM);

            //GTIN is an external identifier: right or wrong, never partially matched
            if (!string.IsNullOrWhiteSpace(input.Gtin))
                document.Add(new StringField(BetterSearchDefaults.FIELD_GTIN, input.Gtin.Trim(), Field.Store.YES));

            AddEach(document, BetterSearchDefaults.FIELD_TAGS, input.Tags);
            AddEach(document, BetterSearchDefaults.FIELD_CATEGORIES, input.Categories);
            AddEach(document, BetterSearchDefaults.FIELD_MANUFACTURERS, input.Manufacturers);

            return document;
        }

        private static void AddText(Document document, string field, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                document.Add(new TextField(field, value, Field.Store.YES));
        }

        private static void AddEach(Document document, string field, IEnumerable<string> values)
        {
            if (values == null)
                return;

            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    document.Add(new TextField(field, value, Field.Store.YES));
            }
        }

        private static void AddIdentifier(Document document, string value,
            string rawField, string segmentField, string ngramField)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            //lowercased here rather than relying on an analyzer: StringField is not analysed,
            //so without this the field would be case-sensitive - the trap called out in the spec
            document.Add(new StringField(rawField, value.Trim().ToLowerInvariant(), Field.Store.YES));

            foreach (var segment in SkuNormaliser.Segments(value))
                document.Add(new StringField(segmentField, segment, Field.Store.YES));

            foreach (var gram in SkuNormaliser.NGrams(value, NGRAM_MIN, NGRAM_MAX))
                document.Add(new StringField(ngramField, gram, Field.Store.NO));
        }
    }
}
```

- [ ] **Step 4: Run and confirm 21 passing**

Expected: `Failed: 0, Passed: 21`

- [ ] **Step 5: Commit**

```bash
cd "$PROJ" && git add -A && git -c user.name="Zach Malamud" -c user.email="zach.malamud@gmail.com" commit -m "feat: product document builder and index schema

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01D1gESmDP8qdDtdGWiGayvB"
```

---

### Task 4: Query builder, tested against a real index

This task proves the store's actual requirement. The tests build a small in-memory index and assert what comes back, which is far more meaningful than inspecting a `Query` object.

**Files:**
- Create: `.../Services/SearchQueryBuilder.cs`
- Test: `.../Tests/.../SearchQueryBuilderTests.cs`, `.../Tests/.../InMemoryIndexFixture.cs`

**Interfaces:**
- Consumes: `SkuNormaliser`, `ProductDocumentBuilder`, `BetterSearchDefaults`
- Produces: `Query SearchQueryBuilder.Build(string queryText, bool allowFuzzyIdentifiers)`

- [ ] **Step 1: Write the in-memory fixture**

```csharp
using System.Collections.Generic;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Nop.Plugin.Misc.BetterSearch;
using Nop.Plugin.Misc.BetterSearch.Services;

namespace Nop.Plugin.Misc.BetterSearch.Tests
{
    /// <summary>
    /// A throwaway in-memory index holding a handful of products, so query behaviour can be
    /// asserted end to end without a database or a disk.
    /// </summary>
    public class InMemoryIndexFixture : System.IDisposable
    {
        public const LuceneVersion Version = LuceneVersion.LUCENE_48;

        private readonly Directory _directory = new RAMDirectory();
        private IndexSearcher _searcher;

        public InMemoryIndexFixture(IEnumerable<ProductIndexInput> products)
        {
            var analyzer = new StandardAnalyzer(Version);
            var config = new IndexWriterConfig(Version, analyzer);
            using (var writer = new IndexWriter(_directory, config))
            {
                foreach (var product in products)
                    writer.AddDocument(ProductDocumentBuilder.Build(product));
                writer.Commit();
            }

            _searcher = new IndexSearcher(DirectoryReader.Open(_directory));
        }

        /// <summary>Product ids returned for the query, best match first</summary>
        public IList<int> Search(string queryText, bool allowFuzzyIdentifiers = false, int max = 20)
        {
            var query = SearchQueryBuilder.Build(queryText, allowFuzzyIdentifiers);
            var hits = _searcher.Search(query, max).ScoreDocs;

            var ids = new List<int>();
            foreach (var hit in hits)
            {
                var document = _searcher.Doc(hit.Doc);
                ids.Add(int.Parse(document.Get(BetterSearchDefaults.FIELD_PRODUCT_ID)));
            }

            return ids;
        }

        public void Dispose()
        {
            _directory?.Dispose();
        }
    }
}
```

- [ ] **Step 2: Write the failing tests — the store's real SKU cases**

```csharp
using System.Collections.Generic;
using FluentAssertions;
using Nop.Plugin.Misc.BetterSearch.Services;
using NUnit.Framework;

namespace Nop.Plugin.Misc.BetterSearch.Tests
{
    [TestFixture]
    public class SearchQueryBuilderTests
    {
        private InMemoryIndexFixture _index;

        private const int TargetProduct = 1;      // fmsa-ab-1234
        private const int SimilarProduct = 2;     // fmsa-ab-1284, one digit different
        private const int OtherSegment = 3;       // fmsa-cd-1234, same tail, different middle
        private const int TextOnlyProduct = 4;    // mentions 1234 in its description only

        [SetUp]
        public void SetUp()
        {
            _index = new InMemoryIndexFixture(new[]
            {
                new ProductIndexInput { ProductId = TargetProduct, Name = "Flange assembly", Sku = "fmsa-ab-1234" },
                new ProductIndexInput { ProductId = SimilarProduct, Name = "Flange assembly heavy", Sku = "fmsa-ab-1284" },
                new ProductIndexInput { ProductId = OtherSegment, Name = "Cover plate", Sku = "fmsa-cd-1234" },
                new ProductIndexInput { ProductId = TextOnlyProduct, Name = "Manual", FullDescription = "Covers part 1234 in detail" }
            });
        }

        [TearDown]
        public void TearDown() => _index?.Dispose();

        [Test]
        public void Finds_a_product_by_its_whole_sku()
        {
            _index.Search("fmsa-ab-1234").Should().StartWith(TargetProduct);
        }

        [Test]
        public void Whole_sku_search_is_case_insensitive()
        {
            _index.Search("FMSA-AB-1234").Should().StartWith(TargetProduct);
        }

        [Test]
        public void Finds_a_product_by_a_single_sku_segment()
        {
            _index.Search("1234").Should().Contain(TargetProduct);
        }

        [Test]
        public void Finds_a_product_by_two_sku_segments()
        {
            _index.Search("ab-1234").Should().Contain(TargetProduct);
        }

        [Test]
        public void Two_segments_outrank_one()
        {
            //ab-1234 identifies one product; 1234 alone matches two
            _index.Search("ab-1234").Should().StartWith(TargetProduct);
        }

        [Test]
        public void Finds_a_product_by_the_normalised_sku_without_separators()
        {
            _index.Search("ab1234").Should().Contain(TargetProduct);
        }

        [Test]
        public void Finds_a_product_by_a_partial_segment()
        {
            _index.Search("234").Should().Contain(TargetProduct);
        }

        [Test]
        public void An_identifier_hit_outranks_a_description_mention()
        {
            var results = _index.Search("1234");

            results.Should().Contain(TargetProduct);
            results.IndexOf(TargetProduct).Should().BeLessThan(results.IndexOf(TextOnlyProduct));
        }

        [Test]
        public void The_constant_prefix_matches_everything_with_a_sku()
        {
            var results = _index.Search("fmsa");

            results.Should().Contain(new[] { TargetProduct, SimilarProduct, OtherSegment });
        }

        [Test]
        public void A_mistyped_identifier_does_not_return_a_different_part_on_the_strict_pass()
        {
            //1284 is one edit from 1234; the strict pass must never confuse them
            var results = _index.Search("fmsa-ab-1284", allowFuzzyIdentifiers: false);

            results.Should().NotContain(TargetProduct);
            results.Should().StartWith(SimilarProduct);
        }

        [Test]
        public void The_approximate_pass_may_return_a_near_identifier()
        {
            var results = _index.Search("fmsa-ab-1235", allowFuzzyIdentifiers: true);

            results.Should().NotBeEmpty();
        }

        [Test]
        public void Text_search_tolerates_a_typo()
        {
            _index.Search("flnge").Should().Contain(TargetProduct);
        }

        [Test]
        public void Text_search_matches_across_word_order()
        {
            _index.Search("assembly flange").Should().Contain(TargetProduct);
        }

        [Test]
        public void A_very_short_term_is_not_fuzzy_matched()
        {
            //"cov" must not fuzzily match "cover"; short terms are exact only
            var results = _index.Search("xyz");

            results.Should().BeEmpty();
        }

        [Test]
        public void An_empty_query_returns_nothing_rather_than_everything()
        {
            _index.Search("   ").Should().BeEmpty();
        }
    }
}
```

- [ ] **Step 3: Run and confirm failure**

Expected: FAIL — `SearchQueryBuilder` does not exist.

- [ ] **Step 4: Implement the query builder**

```csharp
using System.Collections.Generic;
using System.Linq;
using Lucene.Net.Index;
using Lucene.Net.Search;

namespace Nop.Plugin.Misc.BetterSearch.Services
{
    /// <summary>
    /// Turns a user's query text into a Lucene query.
    ///
    /// Identifier fields (SKU, part number, GTIN) are matched exactly, by segment and by
    /// substring. They are NOT fuzzy-matched unless allowFuzzyIdentifiers is set, which the
    /// caller does only after a strict pass returned nothing: two part numbers one edit apart
    /// are different parts, and returning the wrong one is worse than returning none.
    /// </summary>
    public static class SearchQueryBuilder
    {
        //boosts: an identifier hit must beat a name hit, which must beat a description hit
        private const float BOOST_IDENTIFIER_RAW = 12f;
        private const float BOOST_IDENTIFIER_SEGMENT = 8f;
        private const float BOOST_IDENTIFIER_NGRAM = 3f;
        private const float BOOST_GTIN = 12f;
        private const float BOOST_NAME = 5f;
        private const float BOOST_TAGS = 2f;
        private const float BOOST_SHORT_DESCRIPTION = 1.5f;
        private const float BOOST_FULL_DESCRIPTION = 1f;
        private const float BOOST_CATEGORY = 1f;

        public static Query Build(string queryText, bool allowFuzzyIdentifiers)
        {
            var outer = new BooleanQuery();

            if (string.IsNullOrWhiteSpace(queryText))
                return outer;

            var raw = queryText.Trim().ToLowerInvariant();
            var terms = SkuNormaliser.Segments(raw);
            if (!terms.Any())
                return outer;

            //the whole query as typed, against the raw identifier fields
            AddTerm(outer, BetterSearchDefaults.FIELD_SKU_RAW, raw, BOOST_IDENTIFIER_RAW);
            AddTerm(outer, BetterSearchDefaults.FIELD_MPN_RAW, raw, BOOST_IDENTIFIER_RAW);
            AddTerm(outer, BetterSearchDefaults.FIELD_GTIN, queryText.Trim(), BOOST_GTIN);

            //the whole query with separators stripped, so "ab1234" matches "ab-1234"
            var normalisedWhole = SkuNormaliser.Normalise(raw);
            if (normalisedWhole.Length >= ProductDocumentBuilder.NGRAM_MIN)
            {
                AddTerm(outer, BetterSearchDefaults.FIELD_SKU_NGRAM, normalisedWhole, BOOST_IDENTIFIER_SEGMENT);
                AddTerm(outer, BetterSearchDefaults.FIELD_MPN_NGRAM, normalisedWhole, BOOST_IDENTIFIER_SEGMENT);
            }

            foreach (var term in terms)
            {
                //identifier segments: exact token match
                AddTerm(outer, BetterSearchDefaults.FIELD_SKU_SEGMENT, term, BOOST_IDENTIFIER_SEGMENT);
                AddTerm(outer, BetterSearchDefaults.FIELD_MPN_SEGMENT, term, BOOST_IDENTIFIER_SEGMENT);

                //identifier substrings
                if (term.Length >= ProductDocumentBuilder.NGRAM_MIN)
                {
                    AddTerm(outer, BetterSearchDefaults.FIELD_SKU_NGRAM, term, BOOST_IDENTIFIER_NGRAM);
                    AddTerm(outer, BetterSearchDefaults.FIELD_MPN_NGRAM, term, BOOST_IDENTIFIER_NGRAM);
                }

                if (allowFuzzyIdentifiers)
                {
                    AddFuzzy(outer, BetterSearchDefaults.FIELD_SKU_SEGMENT, term, BOOST_IDENTIFIER_NGRAM);
                    AddFuzzy(outer, BetterSearchDefaults.FIELD_MPN_SEGMENT, term, BOOST_IDENTIFIER_NGRAM);
                }

                //text fields: exact plus fuzzy, scaled by term length
                AddTerm(outer, BetterSearchDefaults.FIELD_NAME, term, BOOST_NAME);
                AddFuzzy(outer, BetterSearchDefaults.FIELD_NAME, term, BOOST_NAME * 0.6f);
                AddTerm(outer, BetterSearchDefaults.FIELD_TAGS, term, BOOST_TAGS);
                AddTerm(outer, BetterSearchDefaults.FIELD_SHORT_DESCRIPTION, term, BOOST_SHORT_DESCRIPTION);
                AddFuzzy(outer, BetterSearchDefaults.FIELD_SHORT_DESCRIPTION, term, BOOST_SHORT_DESCRIPTION * 0.6f);
                AddTerm(outer, BetterSearchDefaults.FIELD_FULL_DESCRIPTION, term, BOOST_FULL_DESCRIPTION);
                AddTerm(outer, BetterSearchDefaults.FIELD_CATEGORIES, term, BOOST_CATEGORY);
                AddTerm(outer, BetterSearchDefaults.FIELD_MANUFACTURERS, term, BOOST_CATEGORY);
            }

            return outer;
        }

        /// <summary>
        /// Edits allowed for a term, by length. Short terms get none: at three characters
        /// almost everything is within one edit of everything else.
        /// </summary>
        public static int MaxEdits(string term)
        {
            if (term.Length <= 3)
                return 0;

            return term.Length <= 7 ? 1 : 2;
        }

        private static void AddTerm(BooleanQuery outer, string field, string text, float boost)
        {
            if (string.IsNullOrEmpty(text))
                return;

            var query = new TermQuery(new Term(field, text)) { Boost = boost };
            outer.Add(query, Occur.SHOULD);
        }

        private static void AddFuzzy(BooleanQuery outer, string field, string text, float boost)
        {
            var edits = MaxEdits(text);
            if (edits == 0)
                return;

            var query = new FuzzyQuery(new Term(field, text), edits) { Boost = boost };
            outer.Add(query, Occur.SHOULD);
        }
    }
}
```

> **The Lucene.NET API surface here is written from knowledge, not from a build.** `BooleanQuery.Add`, `Occur.SHOULD`, `Boost` as a settable property and the `FuzzyQuery(Term, int)` constructor are all expected to exist in 4.8.0-beta00016, but correct them against the real package if the compiler disagrees. Do not change the *behaviour* being expressed — the tests define that.

- [ ] **Step 5: Run and confirm all pass**

Expected: `Failed: 0, Passed: 36`

If a ranking assertion fails (`StartWith`, or one product outranking another) the boosts need adjusting, not the tests. The tests encode the store's requirements; the constants are the tuning knob.

- [ ] **Step 6: Commit**

```bash
cd "$PROJ" && git add -A && git -c user.name="Zach Malamud" -c user.email="zach.malamud@gmail.com" commit -m "feat: search query builder with substring SKU matching

Identifiers match exactly, by segment and by substring, and are never
fuzzy-matched on the strict pass.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01D1gESmDP8qdDtdGWiGayvB"
```

---

### Task 5: Index manager

**Files:**
- Create: `.../Services/SearchIndexManager.cs`
- Test: `.../Tests/.../SearchIndexManagerTests.cs`

**Interfaces:**
- Consumes: `ProductDocumentBuilder`, `SearchQueryBuilder`, `BetterSearchDefaults`
- Produces:
  - `Task<IList<int>> SearchIndexManager.SearchAsync(string queryText, int maxResults)` — ordered product ids, empty when nothing matches
  - `Task<bool> SearchIndexManager.IsAvailableAsync()`
  - `Task RebuildAsync(IEnumerable<ProductIndexInput> products)`
  - `Task UpsertAsync(ProductIndexInput product)`
  - `Task DeleteAsync(int productId)`
  - `Task<int> DocumentCountAsync()`
  - `bool LastSearchWasApproximate { get; }`

The manager owns the index directory and the two-pass rule: `SearchAsync` runs the strict pass, and only if it returns nothing runs the approximate pass, setting `LastSearchWasApproximate`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Nop.Plugin.Misc.BetterSearch.Services;
using NUnit.Framework;

namespace Nop.Plugin.Misc.BetterSearch.Tests
{
    [TestFixture]
    public class SearchIndexManagerTests
    {
        private string _path;
        private SearchIndexManager _manager;

        private static ProductIndexInput Product(int id, string name, string sku)
        {
            return new ProductIndexInput { ProductId = id, Name = name, Sku = sku };
        }

        [SetUp]
        public async Task SetUp()
        {
            _path = Path.Combine(Path.GetTempPath(), "bettersearch-tests", Path.GetRandomFileName());
            _manager = new SearchIndexManager(_path);
            await _manager.RebuildAsync(new[]
            {
                Product(1, "Flange assembly", "fmsa-ab-1234"),
                Product(2, "Cover plate", "fmsa-cd-5678")
            });
        }

        [TearDown]
        public void TearDown()
        {
            _manager?.Dispose();
            if (Directory.Exists(_path))
                Directory.Delete(_path, true);
        }

        [Test]
        public async Task Reports_itself_available_once_built()
        {
            (await _manager.IsAvailableAsync()).Should().BeTrue();
        }

        [Test]
        public async Task Counts_its_documents()
        {
            (await _manager.DocumentCountAsync()).Should().Be(2);
        }

        [Test]
        public async Task Finds_a_product_by_sku_segment()
        {
            (await _manager.SearchAsync("1234", 10)).Should().Contain(1);
        }

        [Test]
        public async Task Returns_an_empty_list_when_nothing_matches()
        {
            (await _manager.SearchAsync("zzzzzzzz", 10)).Should().BeEmpty();
        }

        [Test]
        public async Task Marks_a_strict_hit_as_not_approximate()
        {
            await _manager.SearchAsync("1234", 10);

            _manager.LastSearchWasApproximate.Should().BeFalse();
        }

        [Test]
        public async Task Falls_through_to_the_approximate_pass_and_says_so()
        {
            //one edit from 1234; the strict pass finds nothing, the approximate pass finds it
            var results = await _manager.SearchAsync("1235", 10);

            results.Should().Contain(1);
            _manager.LastSearchWasApproximate.Should().BeTrue();
        }

        [Test]
        public async Task Upsert_adds_a_new_product()
        {
            await _manager.UpsertAsync(Product(3, "Bracket", "fmsa-ef-9999"));

            (await _manager.SearchAsync("9999", 10)).Should().Contain(3);
            (await _manager.DocumentCountAsync()).Should().Be(3);
        }

        [Test]
        public async Task Upsert_replaces_rather_than_duplicates()
        {
            await _manager.UpsertAsync(Product(1, "Flange assembly mk2", "fmsa-ab-1234"));

            (await _manager.DocumentCountAsync()).Should().Be(2);
            (await _manager.SearchAsync("1234", 10)).Should().Equal(1);
        }

        [Test]
        public async Task Delete_removes_a_product()
        {
            await _manager.DeleteAsync(1);

            (await _manager.SearchAsync("1234", 10)).Should().NotContain(1);
            (await _manager.DocumentCountAsync()).Should().Be(1);
        }

        [Test]
        public async Task Rebuild_replaces_the_whole_index()
        {
            await _manager.RebuildAsync(new[] { Product(9, "Only survivor", "fmsa-zz-0001") });

            (await _manager.DocumentCountAsync()).Should().Be(1);
            (await _manager.SearchAsync("1234", 10)).Should().BeEmpty();
        }

        [Test]
        public async Task Reports_itself_unavailable_when_the_directory_does_not_exist()
        {
            var missing = new SearchIndexManager(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));

            (await missing.IsAvailableAsync()).Should().BeFalse();
            missing.Dispose();
        }

        [Test]
        public async Task Searching_an_unavailable_index_returns_empty_rather_than_throwing()
        {
            var missing = new SearchIndexManager(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));

            (await missing.SearchAsync("anything", 10)).Should().BeEmpty();
            missing.Dispose();
        }
    }
}
```

- [ ] **Step 2: Run and confirm failure**

Expected: FAIL — `SearchIndexManager` does not exist.

- [ ] **Step 3: Implement**

Write `SearchIndexManager` with a constructor taking the index directory path. Requirements the tests above encode, and which the implementation must satisfy:

- `RebuildAsync` writes a fresh index, replacing whatever was there, using `OpenMode.CREATE`.
- `UpsertAsync` uses `IndexWriter.UpdateDocument` keyed on a `Term(FIELD_PRODUCT_ID, id)` so a re-index replaces rather than duplicates.
- `DeleteAsync` uses `IndexWriter.DeleteDocuments` on the same term.
- `SearchAsync` runs `SearchQueryBuilder.Build(text, allowFuzzyIdentifiers: false)`; if that yields no hits it re-runs with `true` and sets `LastSearchWasApproximate`. It returns product ids in score order.
- `IsAvailableAsync` returns false when the directory is missing or `DirectoryReader.IndexExists` is false, and must not throw.
- Every public method catches index exceptions, logs nothing itself (the caller logs), and returns a safe empty result — an unreadable index must degrade, never throw into a page render.
- The reader is reopened after writes so searches see fresh data. `DirectoryReader.OpenIfChanged` is the intended mechanism.
- `IDisposable`, disposing the directory and any open reader.

Use `LuceneVersion.LUCENE_48` and `StandardAnalyzer` to match the fixture in Task 4, so index-time and query-time analysis agree.

- [ ] **Step 4: Run and confirm all pass**

Expected: `Failed: 0, Passed: 48`

- [ ] **Step 5: Commit**

```bash
cd "$PROJ" && git add -A && git -c user.name="Zach Malamud" -c user.email="zach.malamud@gmail.com" commit -m "feat: Lucene index manager with two-pass search

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01D1gESmDP8qdDtdGWiGayvB"
```

---

### Task 6: The product service override

**Files:**
- Create: `.../Services/BetterSearchProductService.cs`
- Test: `.../Tests/.../BetterSearchProductServiceTests.cs`

**Interfaces:**
- Consumes: `SearchIndexManager`, `BetterSearchSettings` (Task 7 creates the settings type — for this task, define it first as described below)
- Produces: `BetterSearchProductService : ProductService`, overriding `SearchProductsAsync`

**Create `BetterSearchSettings` in this task** (Task 7 only adds its admin page):

```csharp
using Nop.Core.Configuration;

namespace Nop.Plugin.Misc.BetterSearch
{
    public class BetterSearchSettings : ISettings
    {
        /// <summary>Master switch. When false the plugin delegates everything to stock search.</summary>
        public bool Enabled { get; set; }

        /// <summary>Maximum ids taken from the index before nopCommerce filters them</summary>
        public int MaxIndexResults { get; set; } = 2000;
    }
}
```

**The rules this override must follow**, each with a test:

1. `Enabled` false → delegate to base, index untouched.
2. `keywords` null or whitespace → delegate to base.
3. Index unavailable → delegate to base.
4. Index throws → delegate to base (never propagate into a page render).
5. Otherwise: get ordered ids from the index, call `base.SearchProductsAsync` with those ids constrained, and re-sort the survivors into index order.
6. When the caller asked for a non-default sort (anything other than `ProductSortingEnum.Position`), honour it and skip the re-sort.

**How to constrain base by id.** `SearchProductsAsync` has no id-list parameter. Call base with the original arguments but `keywords: null` — so the base query applies every filter except keyword matching — then intersect its results with the index ids in index order. At this catalogue size that is acceptable and it is the only approach that keeps all of nopCommerce's filtering intact. Take `MaxIndexResults` ids from the index to bound the work.

- [ ] **Step 1: Write the failing tests**

Test the six rules above with a mocked `SearchIndexManager` (make its methods `virtual` in Task 5 so Moq can override them) and a `BetterSearchProductService` whose base dependencies are Moq objects. Assert:

- rules 1-4 each call the index zero times and return the base result
- rule 5 returns products in the index's order, not the base query's
- rule 6 returns products in the base query's order
- a product present in the index but filtered out by base does not appear

- [ ] **Step 2: Run and confirm failure**

- [ ] **Step 3: Implement the override**

Subclass `ProductService`, copy its constructor parameter list verbatim from
`$NOP/src/Libraries/Nop.Services/Catalog/ProductService.cs`, pass all of them to `base`, and additionally take `SearchIndexManager`, `ISettingService` and `IStoreContext`. Keep your own private fields for what the override needs — the base class fields are private and unreachable, exactly as in the handling fee plugin.

- [ ] **Step 4: Run and confirm passing**

- [ ] **Step 5: Commit**

---

### Task 7: Plugin lifecycle, settings and DI

**Files:**
- Create: `.../BetterSearchPlugin.cs`, `.../Infrastructure/NopStartup.cs`

**Requirements:**

- `BetterSearchPlugin : BasePlugin, IMiscPlugin`
- `InstallAsync`: seed `BetterSearchSettings` with `Enabled = false`, register locales, and insert the rebuild `ScheduleTask` (`Name`, `Seconds = BetterSearchDefaults.REBUILD_TASK_PERIOD_SECONDS`, `Type = BetterSearchDefaults.REBUILD_TASK_TYPE`, `Enabled = true`, `StopOnError = false`).
- `UpdateAsync`: call the same shared `AddOrUpdateLocalesAsync` as `InstallAsync`. **Mandatory** — see Global Constraints.
- `UninstallAsync`: delete settings, locales, the schedule task, and the index directory.
- `NopStartup` with `Order => 3000` registering `SearchIndexManager` as a singleton (it owns a Lucene writer; a scoped one would fight itself) and `IProductService → BetterSearchProductService` as scoped.

The plugin ships **disabled**, like the handling fee plugin, so installing it never silently changes search behaviour before an index exists.

- [ ] **Step 1: Write the plugin class**
- [ ] **Step 2: Write the startup registration**
- [ ] **Step 3: Build and confirm the existing tests still pass**
- [ ] **Step 4: Commit**

---

### Task 8: Index synchronisation and the drift check

**Files:**
- Create: `.../Infrastructure/Cache/ProductIndexEventConsumer.cs`, `.../Tasks/RebuildSearchIndexTask.cs`
- Test: `.../Tests/.../DriftCheckTests.cs`

**Requirements:**

- `ProductIndexEventConsumer` implements `IConsumer<EntityInsertedEvent<Product>>`, `IConsumer<EntityUpdatedEvent<Product>>` and `IConsumer<EntityDeletedEvent<Product>>`, calling `UpsertAsync` / `DeleteAsync`. It must no-op silently when the plugin is disabled.
- `RebuildSearchIndexTask : IScheduleTask` (`Nop.Services.ScheduleTasks`) rebuilds the whole index from all products.
- **The drift check.** Before replacing the live index, the task records the live document count, builds the new one, and compares. If they differ it writes a warning to nopCommerce's log naming both counts.

The drift check exists because a periodic rebuild otherwise *conceals* missed events: the index silently self-corrects, so a sync bug never produces a visible symptom. The warning turns the safety net into a detector.

- [ ] **Step 1: Write the drift-check test** — a pure comparison function `DriftReport.Compare(int liveCount, int rebuiltCount)` returning whether they differ and a message naming both. Test equal counts, more live, more rebuilt.
- [ ] **Step 2: Run and confirm failure**
- [ ] **Step 3: Implement the consumer, the task and the drift comparison**
- [ ] **Step 4: Run and confirm passing**
- [ ] **Step 5: Commit**

---

### Task 9: Admin configuration page

**Files:**
- Create: `.../Models/ConfigurationModel.cs`, `.../Controllers/MiscBetterSearchController.cs`
- Modify: `.../Views/Configure.cshtml` (replaces the Task 1 placeholder)

**Requirements:**

- Fields: `Enabled`, `MaxIndexResults`, each with `_OverrideForStore`.
- An **index status panel**: document count, and whether the index is currently available.
- A **"Rebuild now"** button posting to a controller action that runs the rebuild synchronously and reports the resulting document count.
- **A warning when `CatalogSettings.ProductSearchTermMinimumLength > 2.`** The store's SKU middle segment is two characters, and nopCommerce rejects shorter searches in `CatalogModelFactory` before this plugin ever runs. The warning must say so and name the setting's location, or this gets rediscovered painfully.
- View path `~/Plugins/Misc.BetterSearch/Views/Configure.cshtml`, matching the csproj `OutputPath` segment.
- Every `[NopResourceDisplayName]` key must exist in `AddOrUpdateLocalesAsync`. Cross-check before finishing: a key that is referenced but not registered renders as a raw string in the admin UI.

Razor is not compiled by `dotnet build`, so a mistake here surfaces only when an admin opens the page. Compare tag helper usage against `$NOP/src/Plugins/Nop.Plugin.Shipping.FixedByWeightByTotal/Views/Configure.cshtml`.

- [ ] **Step 1: Write the model**
- [ ] **Step 2: Write the controller, including the rebuild action and the minimum-length check**
- [ ] **Step 3: Write the view**
- [ ] **Step 4: Build, run the suite, confirm still green**
- [ ] **Step 5: Commit**

---

### Task 10: Package for deployment

**Files:**
- Create: `$PROJ/BetterSearch/nopCommerce 4.50/Misc.BetterSearch/` (built output), `$PROJ/BetterSearch/Readme.txt`

**This differs from the sibling plugins.** They ship exactly seven files. This one ships the Lucene assemblies too, so the packaging step must be written against **what the build actually produces**, not a fixed list.

- [ ] **Step 1: Build Release and list the output**

```bash
rm -rf "$NOP/src/Presentation/Nop.Web/Plugins/Misc.BetterSearch"
cd "$NOP/src" && DOTNET_ROLL_FORWARD=Major dotnet build Plugins/Nop.Plugin.Misc.BetterSearch/Nop.Plugin.Misc.BetterSearch.csproj -c Release -nologo
find "$NOP/src/Presentation/Nop.Web/Plugins/Misc.BetterSearch" -type f | sort
```

- [ ] **Step 2: Assemble the deployable folder**

Copy the plugin's own files, the `Views` folder, and **every `Lucene.Net*.dll`**. Exclude anything named `Nop.Web*` — the .NET 10 SDK leaves host files in the output that nopCommerce's cleaner misses, and the bare `Nop.Web` is a Mac executable that must never ship.

- [ ] **Step 3: Verify, with diff rather than grep**

```bash
DEST="$PROJ/BetterSearch/nopCommerce 4.50/Misc.BetterSearch"
diff "$PROJ/BetterSearch/nopCommerce 4.50/Nop.Plugin.Misc.BetterSearch/Views/Configure.cshtml" "$DEST/Views/Configure.cshtml"   # must be empty
ls "$DEST" | grep -i "^Nop.Web" && echo "LEAK" || echo "clean"
ls "$DEST" | grep -ci lucene    # must be at least 2
```

A substring `grep` has already produced one false positive in this repository. Use `diff` for currency checks.

- [ ] **Step 4: Zip, and confirm the zip matches the folder**

```bash
rm -rf /tmp/bszip && mkdir -p /tmp/bszip && unzip -qo "$PROJ/BetterSearch/Misc.BetterSearch.zip" -d /tmp/bszip
diff -r /tmp/bszip/Misc.BetterSearch "$DEST"   # must be empty
```

- [ ] **Step 5: Write the Readme**

Cover: what it does; that SKU matching is by substring with the `fmsa-xx-xxxx` example; case-insensitivity; the two-pass identifier rule and what "approximate results" means; **that `ProductSearchTermMinimumLength` must be set to 2** and why; the index location and that it assumes a single web server; install steps; upgrade steps (replace folder, restart, never uninstall first); and that disabling the plugin reverts to stock search immediately.

- [ ] **Step 6: Commit**

---

## Manual verification on a live store

Automated tests cover matching and ranking against a real index but nothing has run inside nopCommerce. Before live use:

| Check | Expected |
| --- | --- |
| Set `ProductSearchTermMinimumLength` to 2 | short SKU segment searches reach the plugin |
| Install, enable, click Rebuild now | document count matches the product count |
| Search a full SKU | that product first |
| Search `1234` | every SKU containing 1234 |
| Search `ab-1234` | the specific product, ranked first |
| Search `AB-1234` | identical results to lowercase |
| Search `234` | products whose SKU contains 234 |
| Search a mistyped product name | the product still found |
| Search a mistyped SKU with an exact match elsewhere | the exact product, never the near one |
| Autocomplete dropdown | same quality as the search page |
| Admin product search by partial SKU | finds unpublished products too |
| Unpublish a product, search for it | absent from the storefront, present in admin |
| Disable the plugin, search again | stock behaviour returns immediately |
| Delete the index folder, search | results still returned, via fallback |

The unpublished-product check is the important one: it verifies the rank-don't-filter design actually holds in a running store.

## Self-review notes

- **Spec coverage:** Lucene in-process → Tasks 1, 5. Rank-don't-filter → Task 6. SKU substring matching → Tasks 2, 3, 4. Case-insensitivity → Tasks 2, 3, 4 and the Global Constraints. Two-pass identifier rule → Tasks 4, 5. Fuzziness by length → Task 4. Unpublished products indexed → Task 6 rule 5 and the manual matrix. Relevance ordering unless overridden → Task 6 rule 6. Sync and drift check → Task 8. Fallback → Task 6 rules 3-4. Minimum-term-length prerequisite → Task 9 warning, Task 10 Readme, manual matrix. Index location and single-instance assumption → Task 10 Readme.
- **Deferred to plan 2 of 2:** the "did you mean" widget and search-term logging with result counts. Neither blocks a working plugin.
- **Known risk carried from the spec:** the Lucene.NET API in Task 4's code is written from knowledge, not from a build. The behaviour is pinned by tests; the API calls may need correcting against the package. Task 1 Step 7 fails loudly if the Lucene assemblies do not reach the plugin output, which is the failure mode that would otherwise pass every test and break on a real store.
- **Tasks 6 through 9 specify requirements and interfaces rather than complete code.** They are integration-shaped — a 30-parameter constructor pass-through, a Razor view, an event consumer — where transcribing invented code into the plan is more likely to mislead than help. Each names the file to copy signatures from and the rules its tests must encode. Tasks 1 to 5, which carry the matching logic and all its risk, are given in full.
