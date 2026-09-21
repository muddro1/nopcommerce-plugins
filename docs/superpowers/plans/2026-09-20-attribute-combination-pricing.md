# Attribute Combination Pricing Plugin Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a nopCommerce 4.50.2 plugin that corrects the storefront `[+$X]` price label shown next to an "Associated to product" attribute value, so it reflects the matching attribute combination's Overridden price instead of the associated product's own standalone price.

**Architecture:** A pure calculator decides the corrected delta. `Nop.Web.Factories.ProductModelFactory` is subclassed and re-registered from the plugin's `INopStartup`, overriding only `PrepareProductAttributeModelsAsync` to post-process the labels core already built — no other service is touched, so the real order/cart pricing path is untouched.

**Tech Stack:** C# 10, net6.0, nopCommerce 4.50.2, NUnit 3.13.2, Moq 4.16.1, FluentAssertions 6.2.0.

**Spec:** `docs/superpowers/specs/2026-09-20-attribute-combination-pricing-design.md`

## Global Constraints

- Target framework is `net6.0`. Do not upgrade it.
- Build against **nopCommerce 4.50.2** specifically, at `$NOP` (defined below). `plugin.json` declares `"SupportedVersions": [ "4.50" ]`.
- The machine has only the .NET 10 SDK. `$NOP/global.json` already has `"rollForward": "latestMajor"` set from prior plugin work — do not touch it. Prefix every `dotnet build` / `dotnet test` with `DOTNET_ROLL_FORWARD=Major`.
- Plugin system name: `Misc.AttributeCombinationPricing`. Assembly/namespace root: `Nop.Plugin.Misc.AttributeCombinationPricing`.
- No settings class, no controller, no config view. The fix applies automatically, store-wide, to every product where the pattern occurs — this was decided in the spec and must not grow a configuration screen.
- Only `AttributeValueType.AssociatedToProduct` values are ever touched. `Simple`-type values must be left exactly as core produced them.
- Every source line number cited in this plan was verified against `$NOP` directly (nopCommerce 4.50.2, matches `release-4.50.4` upstream structure exactly at these locations) — re-open the cited file if anything doesn't match rather than guessing.

## Paths

```bash
PROJ="/Users/zach/Claude/Projects/NOPCommercePluginDiscount"
NOP="/Users/zach/Claude/Projects/nopCommerce-4.50.2"
```

`$PROJ` is the deliverable repository. `$NOP` is an existing nopCommerce 4.50.2 source tree (already used to build `BetterSearch` and `HandlingFee`) used only to compile and test against; it is not a deliverable.

## File Structure

Source of record, under `$PROJ/AttributeCombinationPricing/nopCommerce 4.50/Nop.Plugin.Misc.AttributeCombinationPricing/`:

| File | Responsibility |
| --- | --- |
| `Nop.Plugin.Misc.AttributeCombinationPricing.csproj` | Project definition, output path |
| `plugin.json` | Plugin manifest |
| `logo.png` | Plugin logo (reused from an existing plugin) |
| `AttributeCombinationPricingPlugin.cs` | `BasePlugin`/`IMiscPlugin` marker — no settings, no overrides needed |
| `Services/AttributeCombinationPriceAdjustmentCalculator.cs` | The corrected-delta decision — pure, no dependencies |
| `Factories/AttributeCombinationProductModelFactory.cs` | Subclasses `ProductModelFactory`; overrides the one method that builds attribute value labels |
| `Infrastructure/NopStartup.cs` | DI registration of the factory override |

Tests, under `$PROJ/AttributeCombinationPricing/Tests/Nop.Plugin.Misc.AttributeCombinationPricing.Tests/`:

| File | Responsibility |
| --- | --- |
| `Nop.Plugin.Misc.AttributeCombinationPricing.Tests.csproj` | Test project |
| `AttributeCombinationPriceAdjustmentCalculatorTests.cs` | Unit tests for the pure calculator |
| `AttributeCombinationProductModelFactoryTests.cs` | Tests the label-correction logic against faked nopCommerce services |

No `Models/`, `Controllers/`, `Views/` — there is nothing to configure.

---

### Task 0: Workspace and plugin folder skeleton

**Files:**
- Create: `$PROJ/AttributeCombinationPricing/` directory tree

**Interfaces:**
- Consumes: nothing
- Produces: folder skeleton for the plugin source and test projects; confirms `$NOP` still builds

- [ ] **Step 1: Confirm the existing nopCommerce tree still builds**

```bash
cd "$NOP/src"
DOTNET_ROLL_FORWARD=Major dotnet build Presentation/Nop.Web/Nop.Web.csproj -c Release -nologo 2>&1 | tail -5
```
Expected: `Build succeeded.` (this is an incremental rebuild of an already-built tree, so it should be fast).

- [ ] **Step 2: Create the plugin folder structure**

```bash
mkdir -p "$PROJ/AttributeCombinationPricing/nopCommerce 4.50/Nop.Plugin.Misc.AttributeCombinationPricing"/{Services,Factories,Infrastructure}
mkdir -p "$PROJ/AttributeCombinationPricing/Tests/Nop.Plugin.Misc.AttributeCombinationPricing.Tests"
cp "$PROJ/HandlingFee/nopCommerce 4.50/Nop.Plugin.Misc.HandlingFee/logo.png" \
   "$PROJ/AttributeCombinationPricing/nopCommerce 4.50/Nop.Plugin.Misc.AttributeCombinationPricing/logo.png"
```

- [ ] **Step 3: Commit**

```bash
cd "$PROJ" && git add -A && git commit -m "chore: scaffold attribute combination pricing plugin folders"
```

---

### Task 1: Project skeleton that compiles

**Files:**
- Create: `$PROJ/AttributeCombinationPricing/nopCommerce 4.50/Nop.Plugin.Misc.AttributeCombinationPricing/Nop.Plugin.Misc.AttributeCombinationPricing.csproj`
- Create: `.../plugin.json`
- Create: `.../AttributeCombinationPricingPlugin.cs`

**Interfaces:**
- Consumes: Task 0's folder structure
- Produces: `AttributeCombinationPricingPlugin : BasePlugin, IMiscPlugin` — a buildable, installable plugin with no behavior yet

- [ ] **Step 1: Write the csproj**

Create `Nop.Plugin.Misc.AttributeCombinationPricing.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <OutputPath>..\..\Presentation\Nop.Web\Plugins\Misc.AttributeCombinationPricing</OutputPath>
    <OutDir>$(OutputPath)</OutDir>
    <CopyLocalLockFileAssemblies>false</CopyLocalLockFileAssemblies>
  </PropertyGroup>

  <ItemGroup>
    <None Remove="logo.png" />
    <None Remove="plugin.json" />
  </ItemGroup>

  <ItemGroup>
    <Content Include="logo.png">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Include="plugin.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
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

- [ ] **Step 2: Write plugin.json**

```json
{
  "Group": "Misc",
  "FriendlyName": "Accurate attribute combination pricing",
  "SystemName": "Misc.AttributeCombinationPricing",
  "Version": "1.00",
  "SupportedVersions": [ "4.50" ],
  "Author": "muddro1",
  "DisplayOrder": 1,
  "FileName": "Nop.Plugin.Misc.AttributeCombinationPricing.dll",
  "Description": "Corrects the storefront price label on \"Associated to product\" attribute values so it reflects the matching attribute combination's Overridden price, instead of the associated product's own price."
}
```

- [ ] **Step 3: Write AttributeCombinationPricingPlugin.cs**

```csharp
using Nop.Services.Plugins;

namespace Nop.Plugin.Misc.AttributeCombinationPricing
{
    /// <summary>
    /// Corrects the storefront price-adjustment label for "Associated to product" attribute values
    /// that have a combination-level price override. No settings and no configuration page: the fix
    /// applies automatically wherever the pattern occurs. See
    /// docs/superpowers/specs/2026-09-20-attribute-combination-pricing-design.md.
    /// </summary>
    public class AttributeCombinationPricingPlugin : BasePlugin, IMiscPlugin
    {
    }
}
```

- [ ] **Step 4: Copy into the nopCommerce tree and build**

```bash
rm -rf "$NOP/src/Plugins/Nop.Plugin.Misc.AttributeCombinationPricing"
cp -R "$PROJ/AttributeCombinationPricing/nopCommerce 4.50/Nop.Plugin.Misc.AttributeCombinationPricing" "$NOP/src/Plugins/"
cd "$NOP/src"
DOTNET_ROLL_FORWARD=Major dotnet build Plugins/Nop.Plugin.Misc.AttributeCombinationPricing/Nop.Plugin.Misc.AttributeCombinationPricing.csproj -c Release -nologo 2>&1 | grep -E "error|Build succeeded|Build FAILED"
```
Expected: `Build succeeded.`

> Repeat this copy-then-build sequence at the end of every subsequent task. `$PROJ` is the source of record; `$NOP` is a build scratch area.

- [ ] **Step 5: Commit**

```bash
cd "$PROJ" && git add -A && git commit -m "feat: attribute combination pricing plugin skeleton"
```

---

### Task 2: The pure calculator

**Files:**
- Create: `.../Services/AttributeCombinationPriceAdjustmentCalculator.cs`
- Test: `$PROJ/AttributeCombinationPricing/Tests/Nop.Plugin.Misc.AttributeCombinationPricing.Tests/AttributeCombinationPriceAdjustmentCalculatorTests.cs`
- Create: `$PROJ/AttributeCombinationPricing/Tests/Nop.Plugin.Misc.AttributeCombinationPricing.Tests/Nop.Plugin.Misc.AttributeCombinationPricing.Tests.csproj`

**Interfaces:**
- Consumes: nothing
- Produces: `AttributeCombinationPriceAdjustmentCalculator.TryGetCorrectedAdjustment(decimal basePrice, decimal? combinationOverriddenPrice) : decimal?` — `null` means "no correction, use core's original label"

- [ ] **Step 1: Write the test project**

Create `Nop.Plugin.Misc.AttributeCombinationPricing.Tests.csproj`:

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
    <ProjectReference Include="..\..\Plugins\Nop.Plugin.Misc.AttributeCombinationPricing\Nop.Plugin.Misc.AttributeCombinationPricing.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Write the failing test**

Create `AttributeCombinationPriceAdjustmentCalculatorTests.cs`:

```csharp
using Nop.Plugin.Misc.AttributeCombinationPricing.Services;
using NUnit.Framework;

namespace Nop.Plugin.Misc.AttributeCombinationPricing.Tests
{
    [TestFixture]
    public class AttributeCombinationPriceAdjustmentCalculatorTests
    {
        [Test]
        public void Returns_null_when_no_combination_matches()
        {
            var result = AttributeCombinationPriceAdjustmentCalculator.TryGetCorrectedAdjustment(
                basePrice: 299m, combinationOverriddenPrice: null);

            Assert.That(result, Is.Null);
        }

        [Test]
        public void Returns_the_positive_delta_when_the_override_costs_more_than_base()
        {
            var result = AttributeCombinationPriceAdjustmentCalculator.TryGetCorrectedAdjustment(
                basePrice: 299m, combinationOverriddenPrice: 320m);

            Assert.That(result, Is.EqualTo(21m));
        }

        [Test]
        public void Returns_a_negative_delta_when_the_override_costs_less_than_base()
        {
            var result = AttributeCombinationPriceAdjustmentCalculator.TryGetCorrectedAdjustment(
                basePrice: 299m, combinationOverriddenPrice: 285m);

            Assert.That(result, Is.EqualTo(-14m));
        }

        [Test]
        public void Returns_zero_when_the_override_equals_base()
        {
            var result = AttributeCombinationPriceAdjustmentCalculator.TryGetCorrectedAdjustment(
                basePrice: 299m, combinationOverriddenPrice: 299m);

            Assert.That(result, Is.EqualTo(0m));
        }
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

```bash
rm -rf "$NOP/src/Tests/Nop.Plugin.Misc.AttributeCombinationPricing.Tests"
cp -R "$PROJ/AttributeCombinationPricing/Tests/Nop.Plugin.Misc.AttributeCombinationPricing.Tests" "$NOP/src/Tests/"
cd "$NOP/src/Tests/Nop.Plugin.Misc.AttributeCombinationPricing.Tests"
DOTNET_ROLL_FORWARD=Major dotnet test -nologo 2>&1 | tail -20
```
Expected: FAIL to build — `AttributeCombinationPriceAdjustmentCalculator` does not exist.

- [ ] **Step 4: Write the minimal implementation**

Create `Services/AttributeCombinationPriceAdjustmentCalculator.cs`:

```csharp
namespace Nop.Plugin.Misc.AttributeCombinationPricing.Services
{
    /// <summary>
    /// Decides the corrected price-adjustment for an "Associated to product" attribute value, when its
    /// attribute combination has an Overridden price. Deliberately pure: it takes every figure it needs
    /// as a parameter, so it has no dependency on nopCommerce services.
    /// </summary>
    public static class AttributeCombinationPriceAdjustmentCalculator
    {
        /// <summary>
        /// Calculate the corrected price adjustment
        /// </summary>
        /// <param name="basePrice">The product's own final price with no attributes selected</param>
        /// <param name="combinationOverriddenPrice">
        /// The Overridden price of the attribute combination matching this value plus the product's
        /// other attributes at their default state, or null if no such combination exists or it has no
        /// Overridden price set
        /// </param>
        /// <returns>
        /// The corrected adjustment (<paramref name="combinationOverriddenPrice"/> minus
        /// <paramref name="basePrice"/>), or null when there is nothing to correct and the caller should
        /// keep core's original label
        /// </returns>
        public static decimal? TryGetCorrectedAdjustment(decimal basePrice, decimal? combinationOverriddenPrice)
        {
            if (!combinationOverriddenPrice.HasValue)
                return null;

            return combinationOverriddenPrice.Value - basePrice;
        }
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

```bash
rm -rf "$NOP/src/Plugins/Nop.Plugin.Misc.AttributeCombinationPricing"
cp -R "$PROJ/AttributeCombinationPricing/nopCommerce 4.50/Nop.Plugin.Misc.AttributeCombinationPricing" "$NOP/src/Plugins/"
rm -rf "$NOP/src/Tests/Nop.Plugin.Misc.AttributeCombinationPricing.Tests"
cp -R "$PROJ/AttributeCombinationPricing/Tests/Nop.Plugin.Misc.AttributeCombinationPricing.Tests" "$NOP/src/Tests/"
cd "$NOP/src/Tests/Nop.Plugin.Misc.AttributeCombinationPricing.Tests"
DOTNET_ROLL_FORWARD=Major dotnet test -nologo 2>&1 | tail -10
```
Expected: `Passed!` — 4 tests passed.

- [ ] **Step 6: Commit**

```bash
cd "$PROJ" && git add -A && git commit -m "feat: add the corrected price-adjustment calculator"
```

---

### Task 3: The product model factory override

This is the core of the plugin. `ProductModelFactory.PrepareProductAttributeModelsAsync` is `protected virtual` at `Nop.Web/Factories/ProductModelFactory.cs:836`, and the class is `public partial class ProductModelFactory : IProductModelFactory` (not sealed) — verified directly in `$NOP`.

**Files:**
- Create: `.../Factories/AttributeCombinationProductModelFactory.cs`
- Test: `$PROJ/AttributeCombinationPricing/Tests/Nop.Plugin.Misc.AttributeCombinationPricing.Tests/AttributeCombinationProductModelFactoryTests.cs`

**Interfaces:**
- Consumes: `AttributeCombinationPriceAdjustmentCalculator.TryGetCorrectedAdjustment` from Task 2
- Produces: `AttributeCombinationProductModelFactory : ProductModelFactory`, with a `public virtual Task ApplyCorrectedAdjustmentsAsync(Product product, IList<ProductDetailsModel.ProductAttributeModel> model, Customer customer)` method and a `protected override` of `PrepareProductAttributeModelsAsync` that calls it

**Why a separate `ApplyCorrectedAdjustmentsAsync` method, and why `public`:** `PrepareProductAttributeModelsAsync` first calls `base.PrepareProductAttributeModelsAsync`, which alone touches roughly fifteen nopCommerce services (localization, images, captcha, specification attributes...) that this plugin does not change and is not responsible for re-verifying — that's already-shipped, already-tested core behavior. Pulling the new logic into its own method means the tests below only need to fake the handful of services the *new* logic actually touches. Making it `public` (rather than `protected`, which is what it would naturally be) means tests can call it directly on a real instance, with no need for a second "testable subclass" file.

**Why post-process rather than reproduce the whole method:** `PrepareProductAttributeModelsAsync` is roughly 150 lines covering images, quantities, conditional attributes and more. Reproducing all of that (the way `HandlingFeeOrderTotalCalculationService` reproduces a ~80-line method body elsewhere in this repo) would pin a much larger surface to this exact nopCommerce version for no benefit. Calling `base.PrepareProductAttributeModelsAsync` first and only touching the two fields the spec cares about (`PriceAdjustment`, `PriceAdjustmentValue`) on values of type `AssociatedToProduct` keeps the diff — and the risk — small.

- [ ] **Step 1: Write the failing test**

Create `AttributeCombinationProductModelFactoryTests.cs`:

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Discounts;
using Nop.Plugin.Misc.AttributeCombinationPricing.Factories;
using Nop.Services.Catalog;
using Nop.Services.Directory;
using Nop.Services.Helpers;
using Nop.Services.Tax;
using Nop.Web.Models.Catalog;
using NUnit.Framework;

namespace Nop.Plugin.Misc.AttributeCombinationPricing.Tests
{
    [TestFixture]
    public class AttributeCombinationProductModelFactoryTests
    {
        private static AttributeCombinationProductModelFactory Build(
            Mock<IPriceCalculationService> priceCalculationService,
            Mock<IProductAttributeService> productAttributeService,
            Mock<IProductAttributeParser> productAttributeParser,
            Mock<ITaxService> taxService,
            Mock<ICurrencyService> currencyService,
            Mock<IPriceFormatter> priceFormatter,
            Mock<IWorkContext> workContext)
        {
            //Every base-class dependency this test's code path never reaches is null.
            //Only the seven mocked above are touched by ApplyCorrectedAdjustmentsAsync.
            return new AttributeCombinationProductModelFactory(
                captchaSettings: null,
                catalogSettings: null,
                customerSettings: null,
                categoryService: null,
                currencyService: currencyService.Object,
                customerService: null,
                dateRangeService: null,
                dateTimeHelper: null,
                downloadService: null,
                genericAttributeService: null,
                localizationService: null,
                manufacturerService: null,
                permissionService: null,
                pictureService: null,
                priceCalculationService: priceCalculationService.Object,
                priceFormatter: priceFormatter.Object,
                productAttributeParser: productAttributeParser.Object,
                productAttributeService: productAttributeService.Object,
                productService: null,
                productTagService: null,
                productTemplateService: null,
                reviewTypeService: null,
                specificationAttributeService: null,
                staticCacheManager: null,
                storeContext: null,
                shoppingCartModelFactory: null,
                taxService: taxService.Object,
                urlRecordService: null,
                vendorService: null,
                webHelper: null,
                workContext: workContext.Object,
                mediaSettings: null,
                orderSettings: null,
                seoSettings: null,
                shippingSettings: null,
                vendorSettings: null);
        }

        [Test]
        public async Task Corrects_only_the_value_backed_by_an_override_combination()
        {
            var product = new Product { Id = 1 };
            var customer = new Customer { Id = 1 };
            var mapping = new ProductAttributeMapping { Id = 10, ProductId = product.Id };

            //Right-handed mount: has a matching combination with an Overridden price of 320,
            //against a base price of 299 -> corrected adjustment should be +21.
            var overriddenValue = new ProductAttributeValue
            {
                Id = 100,
                ProductAttributeMappingId = mapping.Id,
                AttributeValueTypeId = (int)AttributeValueType.AssociatedToProduct,
                AssociatedProductId = 200
            };

            //Left-handed mount: also Associated-to-Product, but no matching combination exists ->
            //must be left exactly as core computed it.
            var plainValue = new ProductAttributeValue
            {
                Id = 101,
                ProductAttributeMappingId = mapping.Id,
                AttributeValueTypeId = (int)AttributeValueType.AssociatedToProduct,
                AssociatedProductId = 201
            };

            //A Simple-type value on the same attribute -> must never be looked up at all.
            var simpleValue = new ProductAttributeValue
            {
                Id = 102,
                ProductAttributeMappingId = mapping.Id,
                AttributeValueTypeId = (int)AttributeValueType.Simple,
                PriceAdjustment = 5m
            };

            var combination = new ProductAttributeCombination { OverriddenPrice = 320m };

            var priceCalculationService = new Mock<IPriceCalculationService>();
            priceCalculationService.Setup(x => x.GetFinalPriceAsync(product, customer))
                .ReturnsAsync((299m, 299m, 0m, new List<Discount>()));

            var productAttributeService = new Mock<IProductAttributeService>();
            productAttributeService.Setup(x => x.GetProductAttributeValuesAsync(mapping.Id))
                .ReturnsAsync(new List<ProductAttributeValue> { overriddenValue, plainValue, simpleValue });
            productAttributeService.Setup(x => x.GetProductAttributeMappingByIdAsync(mapping.Id))
                .ReturnsAsync(mapping);

            var productAttributeParser = new Mock<IProductAttributeParser>();
            productAttributeParser
                .Setup(x => x.AddProductAttribute(string.Empty, mapping, overriddenValue.Id.ToString()))
                .Returns("xml-overridden");
            productAttributeParser
                .Setup(x => x.AddProductAttribute(string.Empty, mapping, plainValue.Id.ToString()))
                .Returns("xml-plain");
            productAttributeParser
                .Setup(x => x.FindProductAttributeCombinationAsync(product, "xml-overridden"))
                .ReturnsAsync(combination);
            productAttributeParser
                .Setup(x => x.FindProductAttributeCombinationAsync(product, "xml-plain"))
                .ReturnsAsync((ProductAttributeCombination)null);

            //tax and currency pass through unchanged, so the assertions below can check exact figures
            var taxService = new Mock<ITaxService>();
            taxService.Setup(x => x.GetProductPriceAsync(product, It.IsAny<decimal>()))
                .Returns((Product _, decimal price) => Task.FromResult((price, 0m)));

            var currency = new Currency { Id = 1 };
            var workContext = new Mock<IWorkContext>();
            workContext.Setup(x => x.GetWorkingCurrencyAsync()).ReturnsAsync(currency);

            var currencyService = new Mock<ICurrencyService>();
            currencyService.Setup(x => x.ConvertFromPrimaryStoreCurrencyAsync(It.IsAny<decimal>(), currency))
                .Returns((decimal amount, Currency _) => Task.FromResult(amount));

            var priceFormatter = new Mock<IPriceFormatter>();
            priceFormatter.Setup(x => x.FormatPriceAsync(It.IsAny<decimal>(), false, false))
                .Returns((decimal amount, bool _, bool __) => Task.FromResult($"${amount:0.00}"));

            var model = new List<ProductDetailsModel.ProductAttributeModel>
            {
                new ProductDetailsModel.ProductAttributeModel
                {
                    Id = mapping.Id,
                    ProductId = product.Id,
                    ProductAttributeId = 5,
                    Values =
                    {
                        new ProductDetailsModel.ProductAttributeValueModel
                            { Id = overriddenValue.Id, Name = "Right-handed mount", PriceAdjustment = "+$50.00", PriceAdjustmentValue = 50m },
                        new ProductDetailsModel.ProductAttributeValueModel
                            { Id = plainValue.Id, Name = "Left-handed mount", PriceAdjustment = "+$45.00", PriceAdjustmentValue = 45m },
                        new ProductDetailsModel.ProductAttributeValueModel
                            { Id = simpleValue.Id, Name = "No mount", PriceAdjustment = "+$5.00", PriceAdjustmentValue = 5m }
                    }
                }
            };

            var factory = Build(priceCalculationService, productAttributeService, productAttributeParser,
                taxService, currencyService, priceFormatter, workContext);

            await factory.ApplyCorrectedAdjustmentsAsync(product, model, customer);

            var values = model[0].Values;
            Assert.That(values[0].PriceAdjustment, Is.EqualTo("+$21.00"));
            Assert.That(values[0].PriceAdjustmentValue, Is.EqualTo(21m));
            Assert.That(values[1].PriceAdjustment, Is.EqualTo("+$45.00"));
            Assert.That(values[1].PriceAdjustmentValue, Is.EqualTo(45m));
            Assert.That(values[2].PriceAdjustment, Is.EqualTo("+$5.00"));
            Assert.That(values[2].PriceAdjustmentValue, Is.EqualTo(5m));
        }

        [Test]
        public async Task A_negative_corrected_delta_gets_a_minus_prefix()
        {
            var product = new Product { Id = 1 };
            var customer = new Customer { Id = 1 };
            var mapping = new ProductAttributeMapping { Id = 10, ProductId = product.Id };

            var value = new ProductAttributeValue
            {
                Id = 100,
                ProductAttributeMappingId = mapping.Id,
                AttributeValueTypeId = (int)AttributeValueType.AssociatedToProduct,
                AssociatedProductId = 200
            };

            //base price 299, override 285 -> corrected adjustment is -14
            var combination = new ProductAttributeCombination { OverriddenPrice = 285m };

            var priceCalculationService = new Mock<IPriceCalculationService>();
            priceCalculationService.Setup(x => x.GetFinalPriceAsync(product, customer))
                .ReturnsAsync((299m, 299m, 0m, new List<Discount>()));

            var productAttributeService = new Mock<IProductAttributeService>();
            productAttributeService.Setup(x => x.GetProductAttributeValuesAsync(mapping.Id))
                .ReturnsAsync(new List<ProductAttributeValue> { value });
            productAttributeService.Setup(x => x.GetProductAttributeMappingByIdAsync(mapping.Id))
                .ReturnsAsync(mapping);

            var productAttributeParser = new Mock<IProductAttributeParser>();
            productAttributeParser
                .Setup(x => x.AddProductAttribute(string.Empty, mapping, value.Id.ToString()))
                .Returns("xml-value");
            productAttributeParser
                .Setup(x => x.FindProductAttributeCombinationAsync(product, "xml-value"))
                .ReturnsAsync(combination);

            var taxService = new Mock<ITaxService>();
            taxService.Setup(x => x.GetProductPriceAsync(product, It.IsAny<decimal>()))
                .Returns((Product _, decimal price) => Task.FromResult((price, 0m)));

            var currency = new Currency { Id = 1 };
            var workContext = new Mock<IWorkContext>();
            workContext.Setup(x => x.GetWorkingCurrencyAsync()).ReturnsAsync(currency);

            var currencyService = new Mock<ICurrencyService>();
            currencyService.Setup(x => x.ConvertFromPrimaryStoreCurrencyAsync(It.IsAny<decimal>(), currency))
                .Returns((decimal amount, Currency _) => Task.FromResult(amount));

            var priceFormatter = new Mock<IPriceFormatter>();
            priceFormatter.Setup(x => x.FormatPriceAsync(It.IsAny<decimal>(), false, false))
                .Returns((decimal amount, bool _, bool __) => Task.FromResult($"${amount:0.00}"));

            var model = new List<ProductDetailsModel.ProductAttributeModel>
            {
                new ProductDetailsModel.ProductAttributeModel
                {
                    Id = mapping.Id,
                    Values = { new ProductDetailsModel.ProductAttributeValueModel { Id = value.Id, PriceAdjustment = "+$50.00", PriceAdjustmentValue = 50m } }
                }
            };

            var factory = Build(priceCalculationService, productAttributeService, productAttributeParser,
                taxService, currencyService, priceFormatter, workContext);

            await factory.ApplyCorrectedAdjustmentsAsync(product, model, customer);

            Assert.That(model[0].Values[0].PriceAdjustment, Is.EqualTo("-$14.00"));
            Assert.That(model[0].Values[0].PriceAdjustmentValue, Is.EqualTo(-14m));
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
rm -rf "$NOP/src/Tests/Nop.Plugin.Misc.AttributeCombinationPricing.Tests"
cp -R "$PROJ/AttributeCombinationPricing/Tests/Nop.Plugin.Misc.AttributeCombinationPricing.Tests" "$NOP/src/Tests/"
cd "$NOP/src/Tests/Nop.Plugin.Misc.AttributeCombinationPricing.Tests"
DOTNET_ROLL_FORWARD=Major dotnet test -nologo 2>&1 | tail -20
```
Expected: FAIL to build — `AttributeCombinationProductModelFactory` does not exist.

> If any mocked call fails to compile because the parameter list doesn't match, open the cited nopCommerce interface file directly under `$NOP/src/Libraries/Nop.Services/` and match the signature exactly. Do not guess — several of these interfaces have multiple overloads with the same name.

- [ ] **Step 3: Write the implementation**

Create `Factories/AttributeCombinationProductModelFactory.cs`. The base constructor's 36 parameters are copied verbatim from `$NOP/src/Presentation/Nop.Web/Factories/ProductModelFactory.cs:85-120` — re-open that file if anything below doesn't match it exactly:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Media;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Security;
using Nop.Core.Domain.Seo;
using Nop.Core.Domain.Shipping;
using Nop.Core.Domain.Vendors;
using Nop.Plugin.Misc.AttributeCombinationPricing.Services;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Security;
using Nop.Services.Seo;
using Nop.Services.Shipping.Date;
using Nop.Services.Tax;
using Nop.Services.Vendors;
using Nop.Web.Factories;
using Nop.Web.Models.Catalog;

namespace Nop.Plugin.Misc.AttributeCombinationPricing.Factories
{
    /// <summary>
    /// Corrects the storefront price-adjustment label shown next to an "Associated to product"
    /// attribute value, so it reflects the matching attribute combination's Overridden price rather
    /// than the associated product's own standalone price. See
    /// docs/superpowers/specs/2026-09-20-attribute-combination-pricing-design.md.
    /// </summary>
    public class AttributeCombinationProductModelFactory : ProductModelFactory
    {
        private readonly ICurrencyService _currencyService;
        private readonly ICustomerService _customerService;
        private readonly IPermissionService _permissionService;
        private readonly IPriceCalculationService _priceCalculationService;
        private readonly IPriceFormatter _priceFormatter;
        private readonly IProductAttributeParser _productAttributeParser;
        private readonly IProductAttributeService _productAttributeService;
        private readonly ITaxService _taxService;
        private readonly IWorkContext _workContext;

        public AttributeCombinationProductModelFactory(CaptchaSettings captchaSettings,
            CatalogSettings catalogSettings,
            CustomerSettings customerSettings,
            ICategoryService categoryService,
            ICurrencyService currencyService,
            ICustomerService customerService,
            IDateRangeService dateRangeService,
            IDateTimeHelper dateTimeHelper,
            IDownloadService downloadService,
            IGenericAttributeService genericAttributeService,
            ILocalizationService localizationService,
            IManufacturerService manufacturerService,
            IPermissionService permissionService,
            IPictureService pictureService,
            IPriceCalculationService priceCalculationService,
            IPriceFormatter priceFormatter,
            IProductAttributeParser productAttributeParser,
            IProductAttributeService productAttributeService,
            IProductService productService,
            IProductTagService productTagService,
            IProductTemplateService productTemplateService,
            IReviewTypeService reviewTypeService,
            ISpecificationAttributeService specificationAttributeService,
            IStaticCacheManager staticCacheManager,
            IStoreContext storeContext,
            IShoppingCartModelFactory shoppingCartModelFactory,
            ITaxService taxService,
            IUrlRecordService urlRecordService,
            IVendorService vendorService,
            IWebHelper webHelper,
            IWorkContext workContext,
            MediaSettings mediaSettings,
            OrderSettings orderSettings,
            SeoSettings seoSettings,
            ShippingSettings shippingSettings,
            VendorSettings vendorSettings)
            : base(captchaSettings, catalogSettings, customerSettings, categoryService, currencyService,
                customerService, dateRangeService, dateTimeHelper, downloadService, genericAttributeService,
                localizationService, manufacturerService, permissionService, pictureService,
                priceCalculationService, priceFormatter, productAttributeParser, productAttributeService,
                productService, productTagService, productTemplateService, reviewTypeService,
                specificationAttributeService, staticCacheManager, storeContext, shoppingCartModelFactory,
                taxService, urlRecordService, vendorService, webHelper, workContext, mediaSettings,
                orderSettings, seoSettings, shippingSettings, vendorSettings)
        {
            _currencyService = currencyService;
            _customerService = customerService;
            _permissionService = permissionService;
            _priceCalculationService = priceCalculationService;
            _priceFormatter = priceFormatter;
            _productAttributeParser = productAttributeParser;
            _productAttributeService = productAttributeService;
            _taxService = taxService;
            _workContext = workContext;
        }

        protected override async Task<IList<ProductDetailsModel.ProductAttributeModel>> PrepareProductAttributeModelsAsync(Product product, ShoppingCartItem updatecartitem)
        {
            var model = await base.PrepareProductAttributeModelsAsync(product, updatecartitem);

            //mirrors the same permission check core's own PrepareProductAttributeModelsAsync uses before
            //it builds any price label - if prices aren't shown, there's nothing to correct
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.DisplayPrices))
                return model;

            //mirrors core's own customer resolution for the same method
            var customer = updatecartitem?.CustomerId is null
                ? await _workContext.GetCurrentCustomerAsync()
                : await _customerService.GetCustomerByIdAsync(updatecartitem.CustomerId);

            await ApplyCorrectedAdjustmentsAsync(product, model, customer);

            return model;
        }

        /// <summary>
        /// For every "Associated to product" value in <paramref name="model"/>, replaces the price
        /// adjustment label with the delta the matching attribute combination's Overridden price makes
        /// against the product's own base price, when such a combination exists. Values with no matching
        /// override combination, and every <see cref="AttributeValueType.Simple"/> value, are left exactly
        /// as <see cref="ProductModelFactory"/> produced them.
        /// </summary>
        public virtual async Task ApplyCorrectedAdjustmentsAsync(Product product,
            IList<ProductDetailsModel.ProductAttributeModel> model, Customer customer)
        {
            var (_, basePrice, _, _) = await _priceCalculationService.GetFinalPriceAsync(product, customer);

            foreach (var attributeModel in model)
            {
                var rawValues = await _productAttributeService.GetProductAttributeValuesAsync(attributeModel.Id);
                var associatedToProductValues = rawValues
                    .Where(v => v.AttributeValueType == AttributeValueType.AssociatedToProduct)
                    .ToList();

                if (!associatedToProductValues.Any())
                    continue;

                var mapping = await _productAttributeService.GetProductAttributeMappingByIdAsync(attributeModel.Id);

                foreach (var valueModel in attributeModel.Values)
                {
                    var rawValue = associatedToProductValues.FirstOrDefault(v => v.Id == valueModel.Id);
                    if (rawValue == null)
                        continue;

                    var candidateXml = _productAttributeParser.AddProductAttribute(string.Empty, mapping, rawValue.Id.ToString());
                    var combination = await _productAttributeParser.FindProductAttributeCombinationAsync(product, candidateXml);

                    var correctedAdjustment = AttributeCombinationPriceAdjustmentCalculator
                        .TryGetCorrectedAdjustment(basePrice, combination?.OverriddenPrice);

                    if (!correctedAdjustment.HasValue)
                        continue;

                    var (priceAdjustmentBase, _) = await _taxService.GetProductPriceAsync(product, correctedAdjustment.Value);
                    var priceAdjustment = await _currencyService.ConvertFromPrimaryStoreCurrencyAsync(
                        priceAdjustmentBase, await _workContext.GetWorkingCurrencyAsync());

                    valueModel.PriceAdjustment = priceAdjustmentBase switch
                    {
                        > 0 => "+" + await _priceFormatter.FormatPriceAsync(priceAdjustment, false, false),
                        < 0 => "-" + await _priceFormatter.FormatPriceAsync(-priceAdjustment, false, false),
                        _ => string.Empty
                    };
                    valueModel.PriceAdjustmentValue = priceAdjustment;
                }
            }
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
rm -rf "$NOP/src/Plugins/Nop.Plugin.Misc.AttributeCombinationPricing"
cp -R "$PROJ/AttributeCombinationPricing/nopCommerce 4.50/Nop.Plugin.Misc.AttributeCombinationPricing" "$NOP/src/Plugins/"
rm -rf "$NOP/src/Tests/Nop.Plugin.Misc.AttributeCombinationPricing.Tests"
cp -R "$PROJ/AttributeCombinationPricing/Tests/Nop.Plugin.Misc.AttributeCombinationPricing.Tests" "$NOP/src/Tests/"
cd "$NOP/src/Tests/Nop.Plugin.Misc.AttributeCombinationPricing.Tests"
DOTNET_ROLL_FORWARD=Major dotnet test -nologo 2>&1 | tail -10
```
Expected: `Passed!` — 6 tests passed (4 from Task 2, 2 from this task).

- [ ] **Step 5: Commit**

```bash
cd "$PROJ" && git add -A && git commit -m "feat: correct attribute combination price labels in the product model factory"
```

---

### Task 4: DI registration

Core registers `IProductModelFactory` in `Nop.Web.Infrastructure.NopStartup` at `Order => 2002` (verified: `$NOP/src/Presentation/Nop.Web/Infrastructure/NopStartup.cs:67,127`). `HandlingFee`'s `NopStartup` already establishes that `Order => 3000` beats it — it replaces `ICheckoutModelFactory`, registered by core at the same `Order => 2002`, and that override is confirmed working in the shipped `HandlingFee` plugin. The same mechanism applies here.

**Files:**
- Create: `.../Infrastructure/NopStartup.cs`

**Interfaces:**
- Consumes: `AttributeCombinationProductModelFactory` from Task 3
- Produces: `IProductModelFactory` resolves to `AttributeCombinationProductModelFactory` throughout the storefront

- [ ] **Step 1: Write NopStartup.cs**

Create `Infrastructure/NopStartup.cs`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Plugin.Misc.AttributeCombinationPricing.Factories;
using Nop.Web.Factories;

namespace Nop.Plugin.Misc.AttributeCombinationPricing.Infrastructure
{
    /// <summary>
    /// Replaces the storefront product model factory so "Associated to product" attribute value
    /// labels reflect attribute combination overrides. Order is above core NopStartup's 2002 so this
    /// registration wins.
    /// </summary>
    public class NopStartup : INopStartup
    {
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped<IProductModelFactory, AttributeCombinationProductModelFactory>();
        }

        public void Configure(IApplicationBuilder application)
        {
        }

        public int Order => 3000;
    }
}
```

- [ ] **Step 2: Copy into the tree and build**

```bash
rm -rf "$NOP/src/Plugins/Nop.Plugin.Misc.AttributeCombinationPricing"
cp -R "$PROJ/AttributeCombinationPricing/nopCommerce 4.50/Nop.Plugin.Misc.AttributeCombinationPricing" "$NOP/src/Plugins/"
cd "$NOP/src"
DOTNET_ROLL_FORWARD=Major dotnet build Plugins/Nop.Plugin.Misc.AttributeCombinationPricing/Nop.Plugin.Misc.AttributeCombinationPricing.csproj -c Release -nologo 2>&1 | grep -E "error|Build succeeded|Build FAILED"
```
Expected: `Build succeeded.`

- [ ] **Step 3: Re-run the full test suite as a final regression check**

```bash
rm -rf "$NOP/src/Tests/Nop.Plugin.Misc.AttributeCombinationPricing.Tests"
cp -R "$PROJ/AttributeCombinationPricing/Tests/Nop.Plugin.Misc.AttributeCombinationPricing.Tests" "$NOP/src/Tests/"
cd "$NOP/src/Tests/Nop.Plugin.Misc.AttributeCombinationPricing.Tests"
DOTNET_ROLL_FORWARD=Major dotnet test -nologo 2>&1 | tail -10
```
Expected: `Passed!` — 6 tests passed.

- [ ] **Step 4: Commit**

```bash
cd "$PROJ" && git add -A && git commit -m "feat: register the corrected product model factory"
```

---

### Task 5: Package for deployment and document

**Files:**
- Create: `$PROJ/AttributeCombinationPricing/nopCommerce 4.50/Misc.AttributeCombinationPricing/` (built output)
- Create: `$PROJ/AttributeCombinationPricing/Readme.txt`
- Modify: `$PROJ/README.md`

**Interfaces:**
- Consumes: everything above
- Produces: a deployable plugin folder, ready to drop into `\Plugins` on a live store

- [ ] **Step 1: Build Release**

```bash
rm -rf "$NOP/src/Presentation/Nop.Web/Plugins/Misc.AttributeCombinationPricing"
cd "$NOP/src"
DOTNET_ROLL_FORWARD=Major dotnet build Plugins/Nop.Plugin.Misc.AttributeCombinationPricing/Nop.Plugin.Misc.AttributeCombinationPricing.csproj -c Release -nologo 2>&1 | grep -E "error|warning CS|Build succeeded"
```
Expected: `Build succeeded.` with no warnings.

- [ ] **Step 2: Assemble the deployable folder, excluding build leakage**

A newer SDK copies `Nop.Web` host files into plugin output that the post-build helper does not clean up (documented in `README.md`'s Building section for the other two plugins). Copy only the plugin's own files:

```bash
OUT="$NOP/src/Presentation/Nop.Web/Plugins/Misc.AttributeCombinationPricing"
DEST="$PROJ/AttributeCombinationPricing/nopCommerce 4.50/Misc.AttributeCombinationPricing"
rm -rf "$DEST"; mkdir -p "$DEST"
cp "$OUT/Nop.Plugin.Misc.AttributeCombinationPricing.dll" \
   "$OUT/Nop.Plugin.Misc.AttributeCombinationPricing.deps.json" \
   "$OUT/Nop.Plugin.Misc.AttributeCombinationPricing.pdb" \
   "$OUT/plugin.json" "$OUT/logo.png" "$DEST/"
cp "$PROJ/LICENSE.md" "$DEST/"
find "$DEST" -type f | sed "s|$DEST/||" | sort
```
Expected exactly: `LICENSE.md`, `Nop.Plugin.Misc.AttributeCombinationPricing.deps.json`, `Nop.Plugin.Misc.AttributeCombinationPricing.dll`, `Nop.Plugin.Misc.AttributeCombinationPricing.pdb`, `logo.png`, `plugin.json`

- [ ] **Step 3: Verify no stray Nop.Web files shipped**

```bash
ls "$DEST" | grep -i "^Nop.Web" && echo "FAIL: host files leaked" || echo "OK: no host files"
```
Expected: `OK: no host files`

- [ ] **Step 4: Write the Readme**

Create `$PROJ/AttributeCombinationPricing/Readme.txt` covering: what the plugin fixes (the `[+$X]` label on "Associated to product" attribute values not reflecting an attribute combination's Overridden price), a worked example matching the spec (holster at $299, mount value associated to a $50 product, combination Overridden price $320 → label now reads `+$21.00` instead of `+$50.00`), that it applies automatically with no configuration, the known limitation that the label is computed once at page load and does not recompute if the customer changes a *different* attribute afterward (only matters for products with more than one interacting attribute), and install/upgrade steps matching the other two `Readme.txt` files in this repo (drop the folder into `\Plugins`, reload the plugin list, install, restart).

- [ ] **Step 5: Update the top-level README**

Add a row to the plugin table and a short section in `$PROJ/README.md`, following the existing style for `BetterSearch` and `HandlingFee`'s entries — link to `AttributeCombinationPricing/Readme.txt`, system name `Misc.AttributeCombinationPricing`, version `1.00`, and a one-line "Does" summary: "Corrects the storefront price label on an Associated-to-Product attribute value so it reflects the attribute combination's Overridden price instead of the associated product's own price." Add the "Accurate attribute combination pricing" section under the same heading pattern as the other two, summarizing the bug and the fix in a short paragraph.

- [ ] **Step 6: Commit**

```bash
cd "$PROJ" && git add -A && git commit -m "build: package attribute combination pricing plugin for deployment"
```

---

## Manual verification on a live store

Automated tests cover the label arithmetic against fakes. This requires a running store with the plugin installed and cannot be automated here.

Set up a product with an attribute (e.g. "Mount") whose values are each "Associated to product", and an attribute combination for at least one value with an Overridden price set, then confirm:

| Scenario | Expected |
| --- | --- |
| Product page loads, before selecting anything | Every "Associated to product" option backed by a combination override shows the corrected delta, not the associated product's own price |
| Select the option backed by the override | The total price shown updates to the combination's Overridden price (this already worked before the plugin — confirms the label now agrees with it) |
| An "Associated to product" value with no matching combination | Label unchanged from stock behavior — the associated product's own price |
| A `Simple`-type value (plain price adjustment) on the same product | Label unchanged from stock behavior |
| Disable/remove the plugin | Labels revert to stock (wrong) behavior — confirms the plugin, not something else, is the source of the correction |
| A customer without `DisplayPrices` permission | No price labels shown at all, same as stock behavior |

## Self-review notes

- **Spec coverage:** the corrected-delta rule maps to Task 2; the display-layer override, its fallback behavior for unmatched combinations, and the `Simple`-type exclusion map to Task 3; the DI registration and its verified `Order` precedence map to Task 4; packaging and documentation map to Task 5. The spec's "no configuration screen" decision is honored by the File Structure having no `Models/`, `Controllers/`, or `Views/` at all.
- **Placeholder scan:** none found — every step has runnable code or an exact file-write.
- **Type consistency:** `AttributeCombinationPriceAdjustmentCalculator.TryGetCorrectedAdjustment(decimal, decimal?) : decimal?` is used identically in Task 2's tests and Task 3's implementation. `ApplyCorrectedAdjustmentsAsync(Product, IList<ProductDetailsModel.ProductAttributeModel>, Customer)` is used identically in Task 3's tests and its own implementation. The 36-parameter constructor order in Task 3's implementation and the named-argument `Build` helper in Task 3's tests were both checked against the same source citation (`ProductModelFactory.cs:85-120`) and against each other.
- **Verified against the actual build target**, not just upstream: every cited file/line (`PriceCalculationService.cs:455`, `ProductModelFactory.cs:836` and `:85-120`, `ProductAttributeParser.cs:462,679`, `ShoppingCartService.cs:~1390`, `Nop.Web/Infrastructure/NopStartup.cs:67,127`) was grepped directly in `$NOP` (nopCommerce 4.50.2), not only in the upstream `release-4.50.4` tag used during design research. The two matched exactly except for a few lines of natural drift in `ShoppingCartService.cs`, which doesn't affect anything this plan depends on.
