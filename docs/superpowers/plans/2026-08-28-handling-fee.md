# Handling Fee Plugin Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a nopCommerce 4.50.2 plugin that charges a configurable handling fee on physical orders at or below a threshold that ship for free.

**Architecture:** A pure calculator function decides the fee. Two core services are subclassed and re-registered from the plugin's `INopStartup`: `PaymentService` adds the fee to the existing payment-fee channel (which brings tax, persistence and display for free), and `OrderTotalCalculationService` has one guard removed so the fee is visible before a payment method is selected.

**Tech Stack:** C# 10, net6.0, nopCommerce 4.50.2, NUnit 3.13.2, Moq 4.16.1, FluentAssertions 6.2.0.

**Spec:** `docs/superpowers/specs/2026-08-28-handling-fee-design.md`

## Global Constraints

- Target framework is `net6.0`. Do not upgrade it.
- Build against **nopCommerce 4.50.2** specifically. `plugin.json` declares `"SupportedVersions": [ "4.50" ]`.
- The machine has only the .NET 10 SDK. Two workarounds are mandatory for every build and test command:
  - nopCommerce's `global.json` pins SDK 6.0.101 — set `"rollForward": "latestMajor"` in the nopCommerce tree.
  - Prefix every `dotnet build`, `dotnet test` and `dotnet run` with `DOTNET_ROLL_FORWARD=Major`, because nopCommerce's post-build helper `ClearPluginAssemblies.dll` is a net6.0 app and the .NET 6 runtime is not installed.
- Plugin system name: `Misc.HandlingFee`. Assembly/namespace root: `Nop.Plugin.Misc.HandlingFee`.
- All locale resource keys are prefixed `Plugins.Misc.HandlingFee.`.
- Locale resources MUST be registered from **both** `InstallAsync` and `UpdateAsync` via a single shared private method. Registering only in `InstallAsync` causes raw resource keys to display after an in-place upgrade.
- The `Enabled` setting check must be the first thing the calculator evaluates, so a disabled or uninstalled-but-present plugin is a true no-op.
- Threshold comparison is `<=` — a subtotal exactly at the threshold attracts the fee.
- A null shipping total counts as zero, not as unknown.

## Paths

Two locations are used throughout. Define them once per shell session:

```bash
PROJ="/Users/zach/Claude/Projects/NOPCommercePluginDiscount"
NOP="$HOME/Claude/Projects/nopCommerce-4.50.2"
```

`$PROJ` is the deliverable repository — plugin source lives here and is the thing under version control. `$NOP` is a working nopCommerce source tree used only to compile and test against; it is not a deliverable.

## File Structure

Source of record, under `$PROJ/HandlingFee/nopCommerce 4.50/Nop.Plugin.Misc.HandlingFee/`:

| File | Responsibility |
| --- | --- |
| `Nop.Plugin.Misc.HandlingFee.csproj` | Project definition, output path, view content |
| `plugin.json` | Plugin manifest |
| `logo.jpg` | Plugin logo |
| `HandlingFeeDefaults.cs` | System name and locale-key constants |
| `HandlingFeeSettings.cs` | The four settings |
| `HandlingFeePlugin.cs` | `BasePlugin`, install/update/uninstall, config URL |
| `Services/HandlingFeeCalculator.cs` | The fee decision — pure, no dependencies |
| `Services/HandlingFeePaymentService.cs` | Adds the fee to the payment-fee channel |
| `Services/HandlingFeeOrderTotalCalculationService.cs` | Removes the payment-method guard |
| `Infrastructure/NopStartup.cs` | DI registration of both overrides |
| `Models/ConfigurationModel.cs` | Admin config model |
| `Controllers/MiscHandlingFeeController.cs` | Admin config GET/POST |
| `Views/Configure.cshtml` | Admin config page |
| `Views/_ViewImports.cshtml` | Razor imports |

Tests, under `$PROJ/HandlingFee/Tests/Nop.Plugin.Misc.HandlingFee.Tests/`:

| File | Responsibility |
| --- | --- |
| `Nop.Plugin.Misc.HandlingFee.Tests.csproj` | Test project |
| `HandlingFeeCalculatorTests.cs` | Unit tests for the pure calculator |
| `HandlingFeePaymentServiceTests.cs` | Fee arrives through the payment channel |
| `HandlingFeeTotalsTests.cs` | End-to-end totals through the overridden service |

---

### Task 0: Workspace, nopCommerce tree, version control

**Files:**
- Create: `$PROJ/.gitignore`
- Create: `$PROJ/HandlingFee/` directory tree

**Interfaces:**
- Consumes: nothing
- Produces: `$PROJ` is a git repository; `$NOP` is a buildable nopCommerce 4.50.2 tree

> **Confirm with the user before running Step 1.** `$PROJ` is not currently a git repository. Initialising one is a change to their workspace. If they decline, skip Step 1 and every `git commit` step in this plan.

- [ ] **Step 1: Initialise version control**

```bash
cd "$PROJ"
git init
cat > .gitignore <<'EOF'
.DS_Store
bin/
obj/
*.user
EOF
git add -A
git commit -m "chore: initial commit of existing plugin work and specs"
```

- [ ] **Step 2: Obtain a permanent nopCommerce 4.50.2 tree**

The tree used during design was in a temporary scratchpad and will be cleaned up. Create a durable one:

```bash
mkdir -p "$NOP" && cd "$NOP"
curl -sL -o nop.tar.gz https://codeload.github.com/nopSolutions/nopCommerce/tar.gz/refs/tags/release-4.50.2
tar xzf nop.tar.gz --strip-components=1 && rm nop.tar.gz
```

- [ ] **Step 3: Relax the SDK pin**

```bash
cd "$NOP"
python3 - <<'PY'
import json
d = json.load(open("global.json"))
d["sdk"]["rollForward"] = "latestMajor"
json.dump(d, open("global.json", "w"), indent=2)
PY
```

- [ ] **Step 4: Verify the tree builds**

```bash
cd "$NOP/src"
DOTNET_ROLL_FORWARD=Major dotnet build Presentation/Nop.Web/Nop.Web.csproj -c Release -nologo 2>&1 | tail -5
```
Expected: `Build succeeded.`

- [ ] **Step 5: Create the plugin folder structure**

```bash
mkdir -p "$PROJ/HandlingFee/nopCommerce 4.50/Nop.Plugin.Misc.HandlingFee"/{Services,Infrastructure,Models,Controllers,Views}
mkdir -p "$PROJ/HandlingFee/Tests/Nop.Plugin.Misc.HandlingFee.Tests"
cp "$PROJ/HasOnlyProducts/nopCommerce 4.50/Nop.Plugin.DiscountRules.HasOnlyProducts/logo.jpg" \
   "$PROJ/HandlingFee/nopCommerce 4.50/Nop.Plugin.Misc.HandlingFee/logo.jpg"
```

- [ ] **Step 6: Commit**

```bash
cd "$PROJ" && git add -A && git commit -m "chore: scaffold handling fee plugin folders"
```

---

### Task 1: Project skeleton that compiles

**Files:**
- Create: `$PROJ/HandlingFee/nopCommerce 4.50/Nop.Plugin.Misc.HandlingFee/Nop.Plugin.Misc.HandlingFee.csproj`
- Create: `.../plugin.json`
- Create: `.../HandlingFeeDefaults.cs`
- Create: `.../Views/_ViewImports.cshtml`

**Interfaces:**
- Consumes: Task 0's folder structure
- Produces: `HandlingFeeDefaults.SYSTEM_NAME` (string const `"Misc.HandlingFee"`), `HandlingFeeDefaults.CONFIGURATION_ROUTE` (string const `"Plugin.Misc.HandlingFee.Configure"`)

- [ ] **Step 1: Write the csproj**

Create `Nop.Plugin.Misc.HandlingFee.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <OutputPath>..\..\Presentation\Nop.Web\Plugins\Misc.HandlingFee</OutputPath>
    <OutDir>$(OutputPath)</OutDir>
    <CopyLocalLockFileAssemblies>false</CopyLocalLockFileAssemblies>
  </PropertyGroup>

  <ItemGroup>
    <None Remove="logo.jpg" />
    <None Remove="plugin.json" />
    <None Remove="Views\Configure.cshtml" />
    <None Remove="Views\_ViewImports.cshtml" />
  </ItemGroup>

  <ItemGroup>
    <Content Include="logo.jpg">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Include="plugin.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Include="Views\Configure.cshtml">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Include="Views\_ViewImports.cshtml">
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
  "FriendlyName": "Handling fee for small orders",
  "SystemName": "Misc.HandlingFee",
  "Version": "1.00",
  "SupportedVersions": [ "4.50" ],
  "Author": "muddro1",
  "DisplayOrder": 1,
  "FileName": "Nop.Plugin.Misc.HandlingFee.dll",
  "Description": "Adds a configurable handling fee to physical orders at or below a threshold value that ship for free."
}
```

- [ ] **Step 3: Write HandlingFeeDefaults.cs**

```csharp
namespace Nop.Plugin.Misc.HandlingFee
{
    /// <summary>
    /// Represents constants for the handling fee plugin
    /// </summary>
    public static class HandlingFeeDefaults
    {
        /// <summary>
        /// The system name of the plugin
        /// </summary>
        public const string SYSTEM_NAME = "Misc.HandlingFee";

        /// <summary>
        /// The name of the configuration route
        /// </summary>
        public const string CONFIGURATION_ROUTE = "Plugin.Misc.HandlingFee.Configure";
    }
}
```

- [ ] **Step 4: Write Views/_ViewImports.cshtml**

```razor
@inherits Nop.Web.Framework.Mvc.Razor.NopRazorPage<TModel>
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
@addTagHelper *, Nop.Web.Framework

@using Nop.Web.Framework.UI
@using Nop.Web.Framework.Extensions
```

- [ ] **Step 5: Create a placeholder Configure.cshtml so the csproj content reference resolves**

```razor
@* replaced in Task 6 *@
<div></div>
```

- [ ] **Step 6: Copy into the nopCommerce tree and build**

```bash
rm -rf "$NOP/src/Plugins/Nop.Plugin.Misc.HandlingFee"
cp -R "$PROJ/HandlingFee/nopCommerce 4.50/Nop.Plugin.Misc.HandlingFee" "$NOP/src/Plugins/"
cd "$NOP/src"
DOTNET_ROLL_FORWARD=Major dotnet build Plugins/Nop.Plugin.Misc.HandlingFee/Nop.Plugin.Misc.HandlingFee.csproj -c Release -nologo 2>&1 | grep -E "error|Build succeeded|Build FAILED"
```
Expected: `Build succeeded.`

> Repeat this copy-then-build sequence at the end of every subsequent task. `$PROJ` is the source of record; `$NOP` is a build scratch area.

- [ ] **Step 7: Commit**

```bash
cd "$PROJ" && git add -A && git commit -m "feat: handling fee plugin skeleton"
```

---

### Task 2: Settings and the pure fee calculator

**Files:**
- Create: `.../HandlingFeeSettings.cs`
- Create: `.../Services/HandlingFeeCalculator.cs`
- Create: `$PROJ/HandlingFee/Tests/Nop.Plugin.Misc.HandlingFee.Tests/Nop.Plugin.Misc.HandlingFee.Tests.csproj`
- Test: `$PROJ/HandlingFee/Tests/Nop.Plugin.Misc.HandlingFee.Tests/HandlingFeeCalculatorTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks
- Produces:
  - `HandlingFeeSettings` with `bool Enabled`, `decimal ThresholdAmount`, `decimal FeeAmount`, `bool SuppressWhenShippingCharged`
  - `static decimal HandlingFeeCalculator.Calculate(HandlingFeeSettings settings, decimal goodsSubtotalAfterDiscounts, decimal? shippingTotal, bool cartRequiresShipping)`

- [ ] **Step 1: Write the settings class**

```csharp
using Nop.Core.Configuration;

namespace Nop.Plugin.Misc.HandlingFee
{
    /// <summary>
    /// Represents the handling fee settings
    /// </summary>
    public class HandlingFeeSettings : ISettings
    {
        /// <summary>
        /// Gets or sets a value indicating whether the handling fee is active
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Gets or sets the goods subtotal at or below which the fee applies
        /// </summary>
        public decimal ThresholdAmount { get; set; }

        /// <summary>
        /// Gets or sets the fee charged
        /// </summary>
        public decimal FeeAmount { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether a shipping charge suppresses the fee
        /// </summary>
        public bool SuppressWhenShippingCharged { get; set; }
    }
}
```

- [ ] **Step 2: Write the test project file**

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
    <ProjectReference Include="..\..\Plugins\Nop.Plugin.Misc.HandlingFee\Nop.Plugin.Misc.HandlingFee.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Write the failing calculator tests**

Create `HandlingFeeCalculatorTests.cs`:

```csharp
using FluentAssertions;
using Nop.Plugin.Misc.HandlingFee;
using Nop.Plugin.Misc.HandlingFee.Services;
using NUnit.Framework;

namespace Nop.Plugin.Misc.HandlingFee.Tests
{
    [TestFixture]
    public class HandlingFeeCalculatorTests
    {
        private static HandlingFeeSettings Settings(bool enabled = true, bool suppress = true)
        {
            return new HandlingFeeSettings
            {
                Enabled = enabled,
                ThresholdAmount = 50m,
                FeeAmount = 4.95m,
                SuppressWhenShippingCharged = suppress
            };
        }

        [Test]
        public void Charges_the_fee_below_the_threshold_with_free_shipping()
        {
            HandlingFeeCalculator.Calculate(Settings(), 30m, 0m, true).Should().Be(4.95m);
        }

        [Test]
        public void Charges_the_fee_exactly_at_the_threshold()
        {
            HandlingFeeCalculator.Calculate(Settings(), 50m, 0m, true).Should().Be(4.95m);
        }

        [Test]
        public void No_fee_above_the_threshold()
        {
            HandlingFeeCalculator.Calculate(Settings(), 50.01m, 0m, true).Should().Be(0m);
        }

        [Test]
        public void No_fee_when_any_shipping_is_charged()
        {
            HandlingFeeCalculator.Calculate(Settings(), 30m, 1.50m, true).Should().Be(0m);
        }

        [Test]
        public void Null_shipping_counts_as_free_shipping()
        {
            HandlingFeeCalculator.Calculate(Settings(), 30m, null, true).Should().Be(4.95m);
        }

        [Test]
        public void No_fee_when_the_cart_needs_no_shipping()
        {
            HandlingFeeCalculator.Calculate(Settings(), 30m, 0m, false).Should().Be(0m);
        }

        [Test]
        public void No_fee_when_disabled()
        {
            HandlingFeeCalculator.Calculate(Settings(enabled: false), 30m, 0m, true).Should().Be(0m);
        }

        [Test]
        public void Suppression_can_be_turned_off()
        {
            HandlingFeeCalculator.Calculate(Settings(suppress: false), 30m, 8m, true).Should().Be(4.95m);
        }

        [Test]
        public void Zero_value_goods_are_below_the_threshold()
        {
            HandlingFeeCalculator.Calculate(Settings(), 0m, 0m, true).Should().Be(4.95m);
        }

        [Test]
        public void Null_settings_yield_no_fee()
        {
            HandlingFeeCalculator.Calculate(null, 30m, 0m, true).Should().Be(0m);
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

```bash
rm -rf "$NOP/src/Tests/Nop.Plugin.Misc.HandlingFee.Tests"
cp -R "$PROJ/HandlingFee/Tests/Nop.Plugin.Misc.HandlingFee.Tests" "$NOP/src/Tests/"
rm -rf "$NOP/src/Plugins/Nop.Plugin.Misc.HandlingFee"
cp -R "$PROJ/HandlingFee/nopCommerce 4.50/Nop.Plugin.Misc.HandlingFee" "$NOP/src/Plugins/"
cd "$NOP/src/Tests/Nop.Plugin.Misc.HandlingFee.Tests"
DOTNET_ROLL_FORWARD=Major dotnet test -nologo 2>&1 | tail -20
```
Expected: FAIL — `The name 'HandlingFeeCalculator' does not exist` / `CS0246`.

- [ ] **Step 5: Write the calculator**

Create `Services/HandlingFeeCalculator.cs`:

```csharp
namespace Nop.Plugin.Misc.HandlingFee.Services
{
    /// <summary>
    /// Decides whether a handling fee applies, and how much it is.
    /// Deliberately pure: it takes every figure it needs as a parameter so that it
    /// has no dependency on nopCommerce services and cannot take part in a DI cycle.
    /// </summary>
    public static class HandlingFeeCalculator
    {
        /// <summary>
        /// Calculate the handling fee
        /// </summary>
        /// <param name="settings">Handling fee settings</param>
        /// <param name="goodsSubtotalAfterDiscounts">Goods subtotal once item and subtotal discounts are applied, excluding shipping and tax</param>
        /// <param name="shippingTotal">Shipping charge; null when no shipping method has been selected yet, which counts as zero</param>
        /// <param name="cartRequiresShipping">Whether any item in the cart is ship-enabled</param>
        /// <returns>The fee, or zero</returns>
        public static decimal Calculate(HandlingFeeSettings settings,
            decimal goodsSubtotalAfterDiscounts,
            decimal? shippingTotal,
            bool cartRequiresShipping)
        {
            //a disabled or absent configuration must be a complete no-op
            if (settings == null || !settings.Enabled)
                return decimal.Zero;

            //the fee pays for physical handling, so downloadable and virtual orders are exempt
            if (!cartRequiresShipping)
                return decimal.Zero;

            //"at or below" the threshold
            if (goodsSubtotalAfterDiscounts > settings.ThresholdAmount)
                return decimal.Zero;

            //a shipping charge of any size absorbs the fee entirely
            //a null shipping total means no method chosen yet, which counts as no charge
            if (settings.SuppressWhenShippingCharged && (shippingTotal ?? decimal.Zero) > decimal.Zero)
                return decimal.Zero;

            return settings.FeeAmount;
        }
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

```bash
rm -rf "$NOP/src/Plugins/Nop.Plugin.Misc.HandlingFee"
cp -R "$PROJ/HandlingFee/nopCommerce 4.50/Nop.Plugin.Misc.HandlingFee" "$NOP/src/Plugins/"
cd "$NOP/src/Tests/Nop.Plugin.Misc.HandlingFee.Tests"
DOTNET_ROLL_FORWARD=Major dotnet test -nologo 2>&1 | tail -10
```
Expected: `Passed! - Failed: 0, Passed: 10`

- [ ] **Step 7: Commit**

```bash
cd "$PROJ" && git add -A && git commit -m "feat: handling fee settings and pure fee calculator"
```

---

### Task 3: Payment service override

**Files:**
- Create: `.../Services/HandlingFeePaymentService.cs`
- Test: `$PROJ/HandlingFee/Tests/Nop.Plugin.Misc.HandlingFee.Tests/HandlingFeePaymentServiceTests.cs`

**Interfaces:**
- Consumes: `HandlingFeeCalculator.Calculate`, `HandlingFeeSettings` from Task 2
- Produces: `HandlingFeePaymentService : PaymentService` overriding `Task<decimal> GetAdditionalHandlingFeeAsync(IList<ShoppingCartItem> cart, string paymentMethodSystemName)`

**Why `IServiceProvider` and not constructor injection:** `HandlingFeeOrderTotalCalculationService` (Task 4) depends on `IPaymentService`. If this class constructor-injected `IOrderTotalCalculationService`, the container would see a cycle and throw at startup. Resolving it lazily at call time breaks the cycle. There is no runtime recursion, because `GetShoppingCartSubTotalAsync` and `GetShoppingCartShippingTotalAsync` contain no reference to the payment service.

- [ ] **Step 1: Write the failing test**

Create `HandlingFeePaymentServiceTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Nop.Core;
using Nop.Core.Domain.Discounts;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Domain.Stores;
using Nop.Plugin.Misc.HandlingFee.Services;
using Nop.Services.Catalog;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Orders;
using Nop.Services.Payments;
using NUnit.Framework;

namespace Nop.Plugin.Misc.HandlingFee.Tests
{
    [TestFixture]
    public class HandlingFeePaymentServiceTests
    {
        private static HandlingFeePaymentService Build(HandlingFeeSettings settings,
            decimal subtotal, decimal? shipping, bool requiresShipping)
        {
            var totals = new Mock<IOrderTotalCalculationService>();
            totals.Setup(x => x.GetShoppingCartSubTotalAsync(It.IsAny<IList<ShoppingCartItem>>(), false))
                .ReturnsAsync((0m, new List<Discount>(), subtotal, subtotal,
                    new SortedDictionary<decimal, decimal>()));
            totals.Setup(x => x.GetShoppingCartShippingTotalAsync(It.IsAny<IList<ShoppingCartItem>>(), false))
                .ReturnsAsync((shipping, 0m, new List<Discount>()));

            var provider = new Mock<IServiceProvider>();
            provider.Setup(x => x.GetService(typeof(IOrderTotalCalculationService))).Returns(totals.Object);

            var cartService = new Mock<IShoppingCartService>();
            cartService.Setup(x => x.ShoppingCartRequiresShippingAsync(It.IsAny<IList<ShoppingCartItem>>()))
                .ReturnsAsync(requiresShipping);

            var settingService = new Mock<ISettingService>();
            settingService.Setup(x => x.LoadSettingAsync<HandlingFeeSettings>(It.IsAny<int>()))
                .ReturnsAsync(settings);

            var storeContext = new Mock<IStoreContext>();
            storeContext.Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(new Store { Id = 1 });

            return new HandlingFeePaymentService(
                new Mock<ICustomerService>().Object,
                new Mock<IHttpContextAccessor>().Object,
                new Mock<IPaymentPluginManager>().Object,
                new Mock<IPriceCalculationService>().Object,
                new PaymentSettings(),
                new ShoppingCartSettings(),
                provider.Object,
                cartService.Object,
                settingService.Object,
                storeContext.Object);
        }

        private static HandlingFeeSettings Settings(bool enabled = true)
        {
            return new HandlingFeeSettings
            {
                Enabled = enabled,
                ThresholdAmount = 50m,
                FeeAmount = 4.95m,
                SuppressWhenShippingCharged = true
            };
        }

        [Test]
        public async Task Adds_the_fee_for_a_small_physical_order_with_free_shipping()
        {
            var service = Build(Settings(), subtotal: 30m, shipping: 0m, requiresShipping: true);
            var fee = await service.GetAdditionalHandlingFeeAsync(new List<ShoppingCartItem>(), string.Empty);
            fee.Should().Be(4.95m);
        }

        [Test]
        public async Task Adds_nothing_when_shipping_is_charged()
        {
            var service = Build(Settings(), subtotal: 30m, shipping: 8m, requiresShipping: true);
            var fee = await service.GetAdditionalHandlingFeeAsync(new List<ShoppingCartItem>(), string.Empty);
            fee.Should().Be(0m);
        }

        [Test]
        public async Task Adds_nothing_for_a_downloadable_only_order()
        {
            var service = Build(Settings(), subtotal: 30m, shipping: 0m, requiresShipping: false);
            var fee = await service.GetAdditionalHandlingFeeAsync(new List<ShoppingCartItem>(), string.Empty);
            fee.Should().Be(0m);
        }

        [Test]
        public async Task Does_not_touch_the_totals_service_when_disabled()
        {
            var service = Build(Settings(enabled: false), subtotal: 30m, shipping: 0m, requiresShipping: true);
            var fee = await service.GetAdditionalHandlingFeeAsync(new List<ShoppingCartItem>(), string.Empty);
            fee.Should().Be(0m);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
rm -rf "$NOP/src/Tests/Nop.Plugin.Misc.HandlingFee.Tests"
cp -R "$PROJ/HandlingFee/Tests/Nop.Plugin.Misc.HandlingFee.Tests" "$NOP/src/Tests/"
cd "$NOP/src/Tests/Nop.Plugin.Misc.HandlingFee.Tests"
DOTNET_ROLL_FORWARD=Major dotnet test -nologo 2>&1 | tail -20
```
Expected: FAIL — `HandlingFeePaymentService` does not exist.

> If the mocked `GetShoppingCartSubTotalAsync` setup fails to compile, open `$NOP/src/Libraries/Nop.Services/Orders/IOrderTotalCalculationService.cs` line 23 and match the parameter list exactly. Do not guess.

- [ ] **Step 3: Write the payment service**

Create `Services/HandlingFeePaymentService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Services.Catalog;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Orders;
using Nop.Services.Payments;

namespace Nop.Plugin.Misc.HandlingFee.Services
{
    /// <summary>
    /// Adds the handling fee to nopCommerce's payment method additional fee.
    /// Riding that channel means tax treatment, persistence on the order and display
    /// in the cart, admin, emails and invoices all work with no further code.
    /// </summary>
    public class HandlingFeePaymentService : PaymentService
    {
        #region Fields

        private readonly IServiceProvider _serviceProvider;
        private readonly IShoppingCartService _shoppingCartService;
        private readonly ISettingService _settingService;
        private readonly IStoreContext _storeContext;

        #endregion

        #region Ctor

        public HandlingFeePaymentService(ICustomerService customerService,
            IHttpContextAccessor httpContextAccessor,
            IPaymentPluginManager paymentPluginManager,
            IPriceCalculationService priceCalculationService,
            PaymentSettings paymentSettings,
            ShoppingCartSettings shoppingCartSettings,
            IServiceProvider serviceProvider,
            IShoppingCartService shoppingCartService,
            ISettingService settingService,
            IStoreContext storeContext)
            : base(customerService, httpContextAccessor, paymentPluginManager,
                priceCalculationService, paymentSettings, shoppingCartSettings)
        {
            _serviceProvider = serviceProvider;
            _shoppingCartService = shoppingCartService;
            _settingService = settingService;
            _storeContext = storeContext;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Gets the additional handling fee, with our handling fee added to it
        /// </summary>
        /// <param name="cart">Shopping cart</param>
        /// <param name="paymentMethodSystemName">Payment method system name</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the fee</returns>
        public override async Task<decimal> GetAdditionalHandlingFeeAsync(IList<ShoppingCartItem> cart, string paymentMethodSystemName)
        {
            var fee = await base.GetAdditionalHandlingFeeAsync(cart, paymentMethodSystemName);

            var store = await _storeContext.GetCurrentStoreAsync();
            var settings = await _settingService.LoadSettingAsync<HandlingFeeSettings>(store?.Id ?? 0);

            //bail out before doing any work at all when the plugin is off,
            //so that a disabled or uninstalled-but-present plugin costs nothing
            if (settings == null || !settings.Enabled)
                return fee;

            //resolved lazily rather than injected, to avoid a DI cycle with
            //HandlingFeeOrderTotalCalculationService, which depends on IPaymentService
            var orderTotalCalculationService = _serviceProvider.GetRequiredService<IOrderTotalCalculationService>();

            var (_, _, _, subTotalWithDiscount, _) = await orderTotalCalculationService
                .GetShoppingCartSubTotalAsync(cart, false);
            var shippingTotal = (await orderTotalCalculationService
                .GetShoppingCartShippingTotalAsync(cart, false)).shippingTotal;
            var requiresShipping = await _shoppingCartService.ShoppingCartRequiresShippingAsync(cart);

            return fee + HandlingFeeCalculator.Calculate(settings, subTotalWithDiscount, shippingTotal, requiresShipping);
        }

        #endregion
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
rm -rf "$NOP/src/Plugins/Nop.Plugin.Misc.HandlingFee"
cp -R "$PROJ/HandlingFee/nopCommerce 4.50/Nop.Plugin.Misc.HandlingFee" "$NOP/src/Plugins/"
cd "$NOP/src/Tests/Nop.Plugin.Misc.HandlingFee.Tests"
DOTNET_ROLL_FORWARD=Major dotnet test -nologo 2>&1 | tail -10
```
Expected: `Failed: 0, Passed: 14`

- [ ] **Step 5: Commit**

```bash
cd "$PROJ" && git add -A && git commit -m "feat: add handling fee to the payment fee channel"
```

---

### Task 4: Order total service override

**Files:**
- Create: `.../Services/HandlingFeeOrderTotalCalculationService.cs`
- Read for reference: `$NOP/src/Libraries/Nop.Services/Orders/OrderTotalCalculationService.cs:1190-1281`
- Test: `$PROJ/HandlingFee/Tests/Nop.Plugin.Misc.HandlingFee.Tests/HandlingFeeTotalsTests.cs`

**Interfaces:**
- Consumes: `HandlingFeePaymentService` behaviour from Task 3 via `IPaymentService`
- Produces: `HandlingFeeOrderTotalCalculationService : OrderTotalCalculationService` overriding `GetShoppingCartTotalAsync`

**Why this class exists:** core hides the payment fee whenever no payment method has been selected, which is the case on the cart page. Removing that guard is the only change.

**Two constraints discovered in the source, both mandatory:**

1. Every backing field in `OrderTotalCalculationService` is `private readonly`, so the copied method **cannot** reuse them. This subclass must inject its own copies of the seven services the method body touches, *in addition* to passing the full parameter list to `base`.
2. The three helpers the method calls — `GetOrderTotalDiscountAsync`, `AppliedGiftCardsAsync` and `SetRewardPointsAsync` — are `protected virtual`, so they **are** reachable from the subclass and must not be reimplemented.

- [ ] **Step 1: Write the failing totals test**

Create `HandlingFeeTotalsTests.cs`:

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Misc.HandlingFee.Services;
using Nop.Services.Orders;
using NUnit.Framework;

namespace Nop.Plugin.Misc.HandlingFee.Tests
{
    /// <summary>
    /// Asserts the FINAL ORDER TOTAL rather than just the fee, so that the interaction
    /// with tax and gift cards is covered rather than assumed.
    /// </summary>
    [TestFixture]
    public class HandlingFeeTotalsTests
    {
        [Test]
        public async Task Fee_reaches_the_total_when_no_payment_method_is_selected()
        {
            //the cart page selects no payment method; core would hide the fee here
            var (total, _) = await TotalsHarness.ComputeAsync(
                subtotal: 30m, shipping: 0m, requiresShipping: true, paymentMethodSystemName: string.Empty);

            total.Should().Be(34.95m);
        }

        [Test]
        public async Task Fee_is_absent_once_paid_shipping_is_chosen()
        {
            var (total, _) = await TotalsHarness.ComputeAsync(
                subtotal: 30m, shipping: 8m, requiresShipping: true, paymentMethodSystemName: string.Empty);

            total.Should().Be(38m);
        }

        [Test]
        public async Task Large_order_paid_mostly_by_gift_card_still_pays_no_fee()
        {
            var (total, _) = await TotalsHarness.ComputeAsync(
                subtotal: 100m, shipping: 0m, requiresShipping: true,
                paymentMethodSystemName: string.Empty, giftCardBalance: 80m);

            //threshold saw £100, so no fee; gift card then pays £80 of it
            total.Should().Be(20m);
        }

        [Test]
        public async Task Small_order_paid_by_gift_card_still_pays_the_fee()
        {
            var (total, fee) = await TotalsHarness.ComputeAsync(
                subtotal: 30m, shipping: 0m, requiresShipping: true,
                paymentMethodSystemName: string.Empty, giftCardBalance: 80m);

            fee.Should().Be(4.95m);
            total.Should().Be(0m);
        }

        [Test]
        public async Task Downloadable_only_order_pays_no_fee()
        {
            var (total, _) = await TotalsHarness.ComputeAsync(
                subtotal: 30m, shipping: 0m, requiresShipping: false, paymentMethodSystemName: string.Empty);

            total.Should().Be(30m);
        }
    }
}
```

- [ ] **Step 2: Write the test double**

The real `GetShoppingCartSubTotalAsync`, `GetShoppingCartShippingTotalAsync` and `GetTaxTotalAsync` would drag in the whole pricing and shipping stack. All three are `public virtual`, and `GetOrderTotalDiscountAsync`, `AppliedGiftCardsAsync` and `SetRewardPointsAsync` are `protected virtual`, so a test double overrides all six with fixed values. What remains executing is exactly the copied method under test.

Overriding `AppliedGiftCardsAsync` with a balance-subtracting stub is deliberate and still meaningful: the assertion it supports is that **our** copied method applies gift cards *after* the fee, which is our code. Core's gift card logic is untouched by this plugin and is not what these tests are for.

Create `TestableTotalsService.cs`:

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Discounts;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Misc.HandlingFee.Services;
using Nop.Services.Discounts;
using Nop.Services.Payments;

namespace Nop.Plugin.Misc.HandlingFee.Tests
{
    /// <summary>
    /// Feeds fixed figures into the copied GetShoppingCartTotalAsync so that only the
    /// plugin's own logic is under test.
    /// </summary>
    public class TestableTotalsService : HandlingFeeOrderTotalCalculationService
    {
        private readonly decimal _subtotal;
        private readonly decimal? _shipping;
        private readonly decimal _giftCardBalance;

        public TestableTotalsService(decimal subtotal, decimal? shipping, decimal giftCardBalance,
            /* then the full 22-parameter list copied verbatim from
               OrderTotalCalculationService.cs:58, passed straight through to base */)
            : base(/* the same 22 arguments in the same order */)
        {
            _subtotal = subtotal;
            _shipping = shipping;
            _giftCardBalance = giftCardBalance;
        }

        //NOTE: tuple element names must match the base signatures exactly, or the compiler
        //rejects the override with CS8139. Copy the return types verbatim from core.

        public override Task<(decimal discountAmount, List<Discount> appliedDiscounts, decimal subTotalWithoutDiscount, decimal subTotalWithDiscount, SortedDictionary<decimal, decimal> taxRates)>
            GetShoppingCartSubTotalAsync(IList<ShoppingCartItem> cart, bool includingTax)
        {
            return Task.FromResult((decimal.Zero, new List<Discount>(), _subtotal, _subtotal,
                new SortedDictionary<decimal, decimal>()));
        }

        public override Task<(decimal? shippingTotal, decimal taxRate, List<Discount> appliedDiscounts)>
            GetShoppingCartShippingTotalAsync(IList<ShoppingCartItem> cart, bool includingTax)
        {
            return Task.FromResult((_shipping, decimal.Zero, new List<Discount>()));
        }

        public override Task<(decimal taxTotal, SortedDictionary<decimal, decimal> taxRates)>
            GetTaxTotalAsync(IList<ShoppingCartItem> cart, bool usePaymentMethodAdditionalFee = true)
        {
            return Task.FromResult((decimal.Zero, new SortedDictionary<decimal, decimal>()));
        }

        protected override Task<(decimal orderDiscount, List<Discount> appliedDiscounts)>
            GetOrderTotalDiscountAsync(Customer customer, decimal orderTotal)
        {
            return Task.FromResult((decimal.Zero, new List<Discount>()));
        }

        protected override Task<decimal> AppliedGiftCardsAsync(IList<ShoppingCartItem> cart,
            List<AppliedGiftCard> appliedGiftCards, Customer customer, decimal resultTemp)
        {
            var used = resultTemp > _giftCardBalance ? _giftCardBalance : resultTemp;
            return Task.FromResult(resultTemp - used);
        }

        protected override Task<(int redeemedRewardPoints, decimal redeemedRewardPointsAmount)>
            SetRewardPointsAsync(int redeemedRewardPoints, decimal redeemedRewardPointsAmount,
                bool? useRewardPoints, Customer customer, decimal orderTotal)
        {
            return Task.FromResult((0, decimal.Zero));
        }
    }
}
```

> Confirm the `SetRewardPointsAsync` parameter list against `OrderTotalCalculationService.cs:667` before writing it; it spans two lines in the source.

- [ ] **Step 2b: Write the harness**

Create `TotalsHarness.cs`:

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Moq;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Domain.Stores;
using Nop.Plugin.Misc.HandlingFee.Services;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Tax;

namespace Nop.Plugin.Misc.HandlingFee.Tests
{
    public static class TotalsHarness
    {
        public static async Task<(decimal total, decimal fee)> ComputeAsync(
            decimal subtotal, decimal? shipping, bool requiresShipping,
            string paymentMethodSystemName, decimal giftCardBalance = 0m)
        {
            var settings = new HandlingFeeSettings
            {
                Enabled = true,
                ThresholdAmount = 50m,
                FeeAmount = 4.95m,
                SuppressWhenShippingCharged = true
            };

            var customer = new Customer();
            var store = new Store { Id = 1 };

            var customerService = new Mock<ICustomerService>();
            customerService.Setup(x => x.GetShoppingCartCustomerAsync(It.IsAny<IList<ShoppingCartItem>>()))
                .ReturnsAsync(customer);

            var storeContext = new Mock<IStoreContext>();
            storeContext.Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(store);

            var genericAttributeService = new Mock<IGenericAttributeService>();
            genericAttributeService.Setup(x => x.GetAttributeAsync<string>(
                    It.IsAny<Customer>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
                .ReturnsAsync(paymentMethodSystemName);

            //taxable is off for these assertions, so the fee passes through unchanged
            var taxService = new Mock<ITaxService>();
            taxService.Setup(x => x.GetPaymentMethodAdditionalFeeAsync(
                    It.IsAny<decimal>(), It.IsAny<bool>(), It.IsAny<Customer>()))
                .ReturnsAsync((decimal price, bool _, Customer _) => (price, decimal.Zero));

            var cartService = new Mock<IShoppingCartService>();
            cartService.Setup(x => x.ShoppingCartRequiresShippingAsync(It.IsAny<IList<ShoppingCartItem>>()))
                .ReturnsAsync(requiresShipping);

            var settingService = new Mock<ISettingService>();
            settingService.Setup(x => x.LoadSettingAsync<HandlingFeeSettings>(It.IsAny<int>()))
                .ReturnsAsync(settings);

            //the inner totals service the payment service consults for subtotal and shipping
            var innerTotals = new Mock<IOrderTotalCalculationService>();
            innerTotals.Setup(x => x.GetShoppingCartSubTotalAsync(It.IsAny<IList<ShoppingCartItem>>(), false))
                .ReturnsAsync((0m, new List<Nop.Core.Domain.Discounts.Discount>(), subtotal, subtotal,
                    new SortedDictionary<decimal, decimal>()));
            innerTotals.Setup(x => x.GetShoppingCartShippingTotalAsync(It.IsAny<IList<ShoppingCartItem>>(), false))
                .ReturnsAsync((shipping, 0m, new List<Nop.Core.Domain.Discounts.Discount>()));

            var provider = new Mock<System.IServiceProvider>();
            provider.Setup(x => x.GetService(typeof(IOrderTotalCalculationService)))
                .Returns(innerTotals.Object);

            var paymentService = new HandlingFeePaymentService(
                customerService.Object,
                new Mock<IHttpContextAccessor>().Object,
                new Mock<IPaymentPluginManager>().Object,
                new Mock<IPriceCalculationService>().Object,
                new PaymentSettings(),
                new ShoppingCartSettings(),
                provider.Object,
                cartService.Object,
                settingService.Object,
                storeContext.Object);

            //Pass the mocks above for customerService, genericAttributeService, paymentService,
            //storeContext and taxService. Pass new ShoppingCartSettings { RoundPricesDuringCalculation = false }.
            //Every other base constructor parameter may be null: those dependencies are only
            //reached through the six methods TestableTotalsService overrides.
            var service = new TestableTotalsService(subtotal, shipping, giftCardBalance,
                /* 22 base arguments as described above */);

            var cart = new List<ShoppingCartItem>();
            var fee = await paymentService.GetAdditionalHandlingFeeAsync(cart, paymentMethodSystemName);
            var (total, _, _, _, _, _) = await service.GetShoppingCartTotalAsync(cart);

            return (total ?? decimal.Zero, fee);
        }
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

```bash
rm -rf "$NOP/src/Tests/Nop.Plugin.Misc.HandlingFee.Tests"
cp -R "$PROJ/HandlingFee/Tests/Nop.Plugin.Misc.HandlingFee.Tests" "$NOP/src/Tests/"
cd "$NOP/src/Tests/Nop.Plugin.Misc.HandlingFee.Tests"
DOTNET_ROLL_FORWARD=Major dotnet test -nologo 2>&1 | tail -20
```
Expected: FAIL — `NotImplementedException`, and `HandlingFeeOrderTotalCalculationService` does not exist.

- [ ] **Step 4: Create the subclass with its constructor**

Copy the full constructor parameter list from `OrderTotalCalculationService.cs:58` onward. Pass every parameter through to `base(...)`, and additionally store these seven in private fields of the subclass, because the base class's own fields are private and unreachable:

`ICustomerService`, `IGenericAttributeService`, `IPaymentService`, `IPriceCalculationService`, `IStoreContext`, `ITaxService`, `ShoppingCartSettings`.

```csharp
public class HandlingFeeOrderTotalCalculationService : OrderTotalCalculationService
{
    private readonly ICustomerService _customerService;
    private readonly IGenericAttributeService _genericAttributeService;
    private readonly IPaymentService _paymentService;
    private readonly IPriceCalculationService _priceCalculationService;
    private readonly IStoreContext _storeContext;
    private readonly ITaxService _taxService;
    private readonly ShoppingCartSettings _shoppingCartSettings;

    // constructor: copy the parameter list from OrderTotalCalculationService.cs:58,
    // call : base(...) with all of them, then assign the seven fields above
}
```

- [ ] **Step 5: Copy the method body and remove the guard**

Copy `OrderTotalCalculationService.cs` lines **1190–1281** verbatim into the subclass as `public override async Task<...> GetShoppingCartTotalAsync(...)`. Then make exactly one change. Find:

```csharp
            if (usePaymentMethodAdditionalFee && !string.IsNullOrEmpty(paymentMethodSystemName))
```

Replace with:

```csharp
            //the guard on paymentMethodSystemName is deliberately dropped: it hides the fee
            //on the cart page, where no payment method has been selected yet
            if (usePaymentMethodAdditionalFee)
```

Change nothing else. Leave the calls to `GetOrderTotalDiscountAsync`, `AppliedGiftCardsAsync` and `SetRewardPointsAsync` as they are — they resolve to the protected base members.

- [ ] **Step 6: Implement the harness body and run the tests**

```bash
rm -rf "$NOP/src/Plugins/Nop.Plugin.Misc.HandlingFee" "$NOP/src/Tests/Nop.Plugin.Misc.HandlingFee.Tests"
cp -R "$PROJ/HandlingFee/nopCommerce 4.50/Nop.Plugin.Misc.HandlingFee" "$NOP/src/Plugins/"
cp -R "$PROJ/HandlingFee/Tests/Nop.Plugin.Misc.HandlingFee.Tests" "$NOP/src/Tests/"
cd "$NOP/src/Tests/Nop.Plugin.Misc.HandlingFee.Tests"
DOTNET_ROLL_FORWARD=Major dotnet test -nologo 2>&1 | tail -10
```
Expected: `Failed: 0, Passed: 19`

- [ ] **Step 7: Record the provenance of the copied code**

Add this comment immediately above the overridden method, so a future upgrade knows what to re-diff:

```csharp
// Copied from Nop.Services.Orders.OrderTotalCalculationService.GetShoppingCartTotalAsync
// (nopCommerce 4.50.2, lines 1190-1281) with one change: the !string.IsNullOrEmpty(
// paymentMethodSystemName) condition is removed so the handling fee is visible on the
// cart page. RE-DIFF THIS METHOD AGAINST CORE ON ANY NOPCOMMERCE UPGRADE.
```

- [ ] **Step 8: Commit**

```bash
cd "$PROJ" && git add -A && git commit -m "feat: show handling fee before a payment method is selected"
```

---

### Task 5: Plugin class, DI registration, locales

**Files:**
- Create: `.../HandlingFeePlugin.cs`
- Create: `.../Infrastructure/NopStartup.cs`

**Interfaces:**
- Consumes: `HandlingFeeDefaults`, `HandlingFeeSettings`, both services
- Produces: `HandlingFeePlugin : BasePlugin, IMiscPlugin`; DI registration of both overrides

- [ ] **Step 1: Write the startup registration**

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Plugin.Misc.HandlingFee.Services;
using Nop.Services.Orders;
using Nop.Services.Payments;

namespace Nop.Plugin.Misc.HandlingFee.Infrastructure
{
    /// <summary>
    /// Replaces two core services so the handling fee joins the order total.
    /// Order is above NopStartup's 2000 so these registrations win.
    /// </summary>
    public class NopStartup : INopStartup
    {
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped<IPaymentService, HandlingFeePaymentService>();
            services.AddScoped<IOrderTotalCalculationService, HandlingFeeOrderTotalCalculationService>();
        }

        public void Configure(IApplicationBuilder application)
        {
        }

        public int Order => 3000;
    }
}
```

- [ ] **Step 2: Write the plugin class**

Locale registration lives in one private method called from both `InstallAsync` and `UpdateAsync`. This is not optional — installing the locales only on first install produces raw resource keys such as `Plugins.Misc.HandlingFee.Fields.FeeAmount` in the admin UI after an in-place upgrade.

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using Nop.Core;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Plugins;

namespace Nop.Plugin.Misc.HandlingFee
{
    public class HandlingFeePlugin : BasePlugin, IMiscPlugin
    {
        #region Fields

        private readonly ILocalizationService _localizationService;
        private readonly ISettingService _settingService;
        private readonly IWebHelper _webHelper;

        #endregion

        #region Ctor

        public HandlingFeePlugin(ILocalizationService localizationService,
            ISettingService settingService,
            IWebHelper webHelper)
        {
            _localizationService = localizationService;
            _settingService = settingService;
            _webHelper = webHelper;
        }

        #endregion

        #region Methods

        public override string GetConfigurationPageUrl()
        {
            return $"{_webHelper.GetStoreLocation()}Admin/MiscHandlingFee/Configure";
        }

        public override async Task InstallAsync()
        {
            await _settingService.SaveSettingAsync(new HandlingFeeSettings
            {
                Enabled = false,
                ThresholdAmount = decimal.Zero,
                FeeAmount = decimal.Zero,
                SuppressWhenShippingCharged = true
            });

            await AddOrUpdateLocalesAsync();

            await base.InstallAsync();
        }

        public override async Task UpdateAsync(string currentVersion, string targetVersion)
        {
            //locale resources added in later versions are missing on sites that installed
            //an earlier one, so re-register them all on every upgrade
            await AddOrUpdateLocalesAsync();

            await base.UpdateAsync(currentVersion, targetVersion);
        }

        public override async Task UninstallAsync()
        {
            await _settingService.DeleteSettingAsync<HandlingFeeSettings>();
            await _localizationService.DeleteLocaleResourcesAsync("Plugins.Misc.HandlingFee");

            await base.UninstallAsync();
        }

        #endregion

        #region Utilities

        private async Task AddOrUpdateLocalesAsync()
        {
            await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
            {
                ["Plugins.Misc.HandlingFee.Fields.Enabled"] = "Enabled",
                ["Plugins.Misc.HandlingFee.Fields.Enabled.Hint"] = "Charge a handling fee on qualifying orders.",
                ["Plugins.Misc.HandlingFee.Fields.ThresholdAmount"] = "Order threshold",
                ["Plugins.Misc.HandlingFee.Fields.ThresholdAmount.Hint"] = "The fee applies when the goods subtotal, after discounts, is at or below this amount. Shipping, tax, gift cards and reward points are not counted.",
                ["Plugins.Misc.HandlingFee.Fields.FeeAmount"] = "Handling fee",
                ["Plugins.Misc.HandlingFee.Fields.FeeAmount.Hint"] = "The amount charged, in the primary store currency.",
                ["Plugins.Misc.HandlingFee.Fields.SuppressWhenShippingCharged"] = "No fee when shipping is charged",
                ["Plugins.Misc.HandlingFee.Fields.SuppressWhenShippingCharged.Hint"] = "When ticked, any shipping charge above zero removes the handling fee entirely. Orders that need no shipping at all never attract the fee.",
                ["Plugins.Misc.HandlingFee.Configuration.Saved"] = "The settings have been saved."
            });
        }

        #endregion
    }
}
```

- [ ] **Step 3: Build and confirm the existing tests still pass**

```bash
rm -rf "$NOP/src/Plugins/Nop.Plugin.Misc.HandlingFee"
cp -R "$PROJ/HandlingFee/nopCommerce 4.50/Nop.Plugin.Misc.HandlingFee" "$NOP/src/Plugins/"
cd "$NOP/src/Tests/Nop.Plugin.Misc.HandlingFee.Tests"
DOTNET_ROLL_FORWARD=Major dotnet test -nologo 2>&1 | tail -10
```
Expected: `Failed: 0, Passed: 19`

- [ ] **Step 4: Commit**

```bash
cd "$PROJ" && git add -A && git commit -m "feat: plugin lifecycle, locales and DI registration"
```

---

### Task 6: Admin configuration page

**Files:**
- Create: `.../Models/ConfigurationModel.cs`
- Create: `.../Controllers/MiscHandlingFeeController.cs`
- Modify: `.../Views/Configure.cshtml` (replaces the Task 1 placeholder)

**Interfaces:**
- Consumes: `HandlingFeeSettings`, the locale keys from Task 5
- Produces: an admin page at `Admin/MiscHandlingFee/Configure`

- [ ] **Step 1: Write the model**

```csharp
using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Misc.HandlingFee.Models
{
    public record ConfigurationModel : BaseNopModel
    {
        public int ActiveStoreScopeConfiguration { get; set; }

        [NopResourceDisplayName("Plugins.Misc.HandlingFee.Fields.Enabled")]
        public bool Enabled { get; set; }
        public bool Enabled_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Misc.HandlingFee.Fields.ThresholdAmount")]
        public decimal ThresholdAmount { get; set; }
        public bool ThresholdAmount_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Misc.HandlingFee.Fields.FeeAmount")]
        public decimal FeeAmount { get; set; }
        public bool FeeAmount_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Misc.HandlingFee.Fields.SuppressWhenShippingCharged")]
        public bool SuppressWhenShippingCharged { get; set; }
        public bool SuppressWhenShippingCharged_OverrideForStore { get; set; }
    }
}
```

- [ ] **Step 2: Write the controller**

```csharp
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Misc.HandlingFee.Models;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Misc.HandlingFee.Controllers
{
    [AuthorizeAdmin]
    [Area(AreaNames.Admin)]
    [AutoValidateAntiforgeryToken]
    public class MiscHandlingFeeController : BasePluginController
    {
        private readonly ILocalizationService _localizationService;
        private readonly INotificationService _notificationService;
        private readonly IPermissionService _permissionService;
        private readonly ISettingService _settingService;
        private readonly IStoreContext _storeContext;

        public MiscHandlingFeeController(ILocalizationService localizationService,
            INotificationService notificationService,
            IPermissionService permissionService,
            ISettingService settingService,
            IStoreContext storeContext)
        {
            _localizationService = localizationService;
            _notificationService = notificationService;
            _permissionService = permissionService;
            _settingService = settingService;
            _storeContext = storeContext;
        }

        public async Task<IActionResult> Configure()
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePlugins))
                return AccessDeniedView();

            var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
            var settings = await _settingService.LoadSettingAsync<HandlingFeeSettings>(storeScope);

            var model = new ConfigurationModel
            {
                ActiveStoreScopeConfiguration = storeScope,
                Enabled = settings.Enabled,
                ThresholdAmount = settings.ThresholdAmount,
                FeeAmount = settings.FeeAmount,
                SuppressWhenShippingCharged = settings.SuppressWhenShippingCharged
            };

            if (storeScope > 0)
            {
                model.Enabled_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.Enabled, storeScope);
                model.ThresholdAmount_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.ThresholdAmount, storeScope);
                model.FeeAmount_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.FeeAmount, storeScope);
                model.SuppressWhenShippingCharged_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.SuppressWhenShippingCharged, storeScope);
            }

            return View("~/Plugins/Misc.HandlingFee/Views/Configure.cshtml", model);
        }

        [HttpPost]
        public async Task<IActionResult> Configure(ConfigurationModel model)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePlugins))
                return AccessDeniedView();

            if (!ModelState.IsValid)
                return await Configure();

            var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
            var settings = await _settingService.LoadSettingAsync<HandlingFeeSettings>(storeScope);

            settings.Enabled = model.Enabled;
            settings.ThresholdAmount = model.ThresholdAmount;
            settings.FeeAmount = model.FeeAmount;
            settings.SuppressWhenShippingCharged = model.SuppressWhenShippingCharged;

            await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.Enabled, model.Enabled_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.ThresholdAmount, model.ThresholdAmount_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.FeeAmount, model.FeeAmount_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.SuppressWhenShippingCharged, model.SuppressWhenShippingCharged_OverrideForStore, storeScope, false);

            await _settingService.ClearCacheAsync();

            _notificationService.SuccessNotification(
                await _localizationService.GetResourceAsync("Plugins.Misc.HandlingFee.Configuration.Saved"));

            return await Configure();
        }
    }
}
```

- [ ] **Step 3: Write the view**

```razor
@model Nop.Plugin.Misc.HandlingFee.Models.ConfigurationModel

@{
    Layout = "_ConfigurePlugin";
}

<form asp-controller="MiscHandlingFee" asp-action="Configure" method="post">
    <div class="cards-group">
        <div class="card card-default">
            <div class="card-body">
                <div class="form-group row">
                    <div class="col-md-3">
                        <nop-override-store-checkbox asp-for="Enabled_OverrideForStore" asp-input="Enabled" asp-store-scope="@Model.ActiveStoreScopeConfiguration" />
                        <nop-label asp-for="Enabled" />
                    </div>
                    <div class="col-md-9">
                        <nop-editor asp-for="Enabled" />
                        <span asp-validation-for="Enabled"></span>
                    </div>
                </div>
                <div class="form-group row">
                    <div class="col-md-3">
                        <nop-override-store-checkbox asp-for="ThresholdAmount_OverrideForStore" asp-input="ThresholdAmount" asp-store-scope="@Model.ActiveStoreScopeConfiguration" />
                        <nop-label asp-for="ThresholdAmount" />
                    </div>
                    <div class="col-md-9">
                        <nop-editor asp-for="ThresholdAmount" />
                        <span asp-validation-for="ThresholdAmount"></span>
                    </div>
                </div>
                <div class="form-group row">
                    <div class="col-md-3">
                        <nop-override-store-checkbox asp-for="FeeAmount_OverrideForStore" asp-input="FeeAmount" asp-store-scope="@Model.ActiveStoreScopeConfiguration" />
                        <nop-label asp-for="FeeAmount" />
                    </div>
                    <div class="col-md-9">
                        <nop-editor asp-for="FeeAmount" />
                        <span asp-validation-for="FeeAmount"></span>
                    </div>
                </div>
                <div class="form-group row">
                    <div class="col-md-3">
                        <nop-override-store-checkbox asp-for="SuppressWhenShippingCharged_OverrideForStore" asp-input="SuppressWhenShippingCharged" asp-store-scope="@Model.ActiveStoreScopeConfiguration" />
                        <nop-label asp-for="SuppressWhenShippingCharged" />
                    </div>
                    <div class="col-md-9">
                        <nop-editor asp-for="SuppressWhenShippingCharged" />
                        <span asp-validation-for="SuppressWhenShippingCharged"></span>
                    </div>
                </div>
                <div class="form-group row">
                    <div class="col-md-9 offset-md-3">
                        <button type="submit" name="save" class="btn btn-primary">@T("Admin.Common.Save")</button>
                    </div>
                </div>
            </div>
        </div>
    </div>
</form>
```

- [ ] **Step 4: Build and run all tests**

```bash
rm -rf "$NOP/src/Plugins/Nop.Plugin.Misc.HandlingFee"
cp -R "$PROJ/HandlingFee/nopCommerce 4.50/Nop.Plugin.Misc.HandlingFee" "$NOP/src/Plugins/"
cd "$NOP/src/Tests/Nop.Plugin.Misc.HandlingFee.Tests"
DOTNET_ROLL_FORWARD=Major dotnet test -nologo 2>&1 | tail -10
```
Expected: `Failed: 0, Passed: 19`

- [ ] **Step 5: Commit**

```bash
cd "$PROJ" && git add -A && git commit -m "feat: handling fee admin configuration page"
```

---

### Task 7: Package for deployment

**Files:**
- Create: `$PROJ/HandlingFee/nopCommerce 4.50/Misc.HandlingFee/` (built output)
- Create: `$PROJ/HandlingFee/Readme.txt`

**Interfaces:**
- Consumes: everything above
- Produces: a deployable folder and zip

- [ ] **Step 1: Build Release**

```bash
rm -rf "$NOP/src/Presentation/Nop.Web/Plugins/Misc.HandlingFee"
cd "$NOP/src"
DOTNET_ROLL_FORWARD=Major dotnet build Plugins/Nop.Plugin.Misc.HandlingFee/Nop.Plugin.Misc.HandlingFee.csproj -c Release -nologo 2>&1 | grep -E "error|warning CS|Build succeeded"
```
Expected: `Build succeeded.` with no warnings.

- [ ] **Step 2: Assemble the deployable folder, excluding build leakage**

A newer SDK copies `Nop.Web` host files into plugin output that the post-build helper does not clean up. The bare `Nop.Web` file is a Mac executable and must never ship. Copy only the plugin's own files:

```bash
OUT="$NOP/src/Presentation/Nop.Web/Plugins/Misc.HandlingFee"
DEST="$PROJ/HandlingFee/nopCommerce 4.50/Misc.HandlingFee"
rm -rf "$DEST"; mkdir -p "$DEST/Views"
cp "$OUT/Nop.Plugin.Misc.HandlingFee.dll" "$OUT/Nop.Plugin.Misc.HandlingFee.deps.json" \
   "$OUT/Nop.Plugin.Misc.HandlingFee.pdb" "$OUT/plugin.json" "$OUT/logo.jpg" "$DEST/"
cp "$OUT/Views/"*.cshtml "$DEST/Views/"
find "$DEST" -type f | sed "s|$DEST/||" | sort
```
Expected exactly: `logo.jpg`, `Nop.Plugin.Misc.HandlingFee.deps.json`, `Nop.Plugin.Misc.HandlingFee.dll`, `Nop.Plugin.Misc.HandlingFee.pdb`, `plugin.json`, `Views/Configure.cshtml`, `Views/_ViewImports.cshtml`

- [ ] **Step 3: Verify no stray Nop.Web files shipped**

```bash
ls "$DEST" | grep -i "^Nop.Web" && echo "FAIL: host files leaked" || echo "OK: no host files"
```
Expected: `OK: no host files`

- [ ] **Step 4: Write the Readme**

Cover: what the plugin does, the exact rule (physical order, at or below threshold, free shipping), the four settings, the worked examples from the spec's edge-case table, install steps (drop folder into `\Plugins`, reload plugin list, install, restart), the upgrade path (replace the folder and restart, never uninstall first), and the note that taxability is controlled from **Configuration → Tax settings**, not from this plugin.

- [ ] **Step 5: Zip and commit**

```bash
cd "$PROJ/HandlingFee/nopCommerce 4.50"
rm -f "$PROJ/HandlingFee/Misc.HandlingFee.zip"
zip -qr "$PROJ/HandlingFee/Misc.HandlingFee.zip" Misc.HandlingFee -x "*.DS_Store"
cd "$PROJ" && git add -A && git commit -m "build: package handling fee plugin for deployment"
```

---

## Manual verification on a live store

Automated tests cover the arithmetic. These require a running store and cannot be automated here. Configure threshold 50, fee 4.95, suppression on, then confirm:

| Scenario | Expected |
| --- | --- |
| Physical £30 in cart, before checkout | Fee line shows, total £34.95 |
| Choose a paid shipping method | Fee line disappears |
| Choose a free shipping method | Fee line remains |
| Physical £60 | No fee at any point |
| Downloadable only, £30 | No fee |
| Complete the order | Admin order page shows the fee and components sum to the total |
| Order confirmation email and PDF invoice | Fee appears |
| £30 order paid with a gift card | Fee charged, absorbed by the card |
| Disable the plugin, reload cart | No fee anywhere |

## Self-review notes

- **Spec coverage:** every spec section maps to a task — the rule and settings to Task 2, the payment rail to Task 3, cart-page visibility to Task 4, lifecycle and locales to Task 5, configuration to Task 6, packaging to Task 7. The recurring-order behaviour is inherited from core and needs no code, so it appears only in manual verification.
- **Placeholder scan:** an earlier draft left the Task 4 harness as a `NotImplementedException` with a "work it out" note. That was a plan defect and has been replaced with the complete test double and harness.
- **Type consistency:** `GetShoppingCartSubTotalAsync` takes exactly two parameters, `(IList<ShoppingCartItem> cart, bool includingTax)`. An earlier draft mocked it with three. Both occurrences are now correct. Tuple element names in every override must match core verbatim or the compiler rejects them with CS8139.
- **Remaining transcription points**, deliberately left as "copy from source" rather than reproduced here, because transcribing them by hand is how errors get introduced:
  - the 22-parameter base constructor list (`OrderTotalCalculationService.cs:58`)
  - the copied method body (`OrderTotalCalculationService.cs:1190-1281`)
  - the `SetRewardPointsAsync` parameter list (`OrderTotalCalculationService.cs:667`)

  Each carries an exact file and line reference, and the compiler catches any slip immediately.
