# Customer Profile Plugin Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a nopCommerce 4.90 plugin for B2B customers: a free-text BMW dealership field, tax exemption certificates with review, expiry, reminders and automatic removal of tax exemption, an admin-only Net 30 payment method, and secondary contacts who receive copies of chosen customer emails.

**Architecture:** One plugin class that is both an `IWidgetPlugin` (admin customer card, My Account pages, registration field, order details, banner) and an `IPaymentMethod` (Net 30). Four plugin tables. Decisions with edge cases (expiry, overdue, who gets a copy, upload validity) are pure classes with unit tests; services and controllers are thin around them. Contact copies come from a subclass of `WorkflowMessageService` registered over core's. A schedule task drives reminders. Tax exemption itself is nopCommerce's own `Customer.IsTaxExempt`.

**Tech Stack:** C# 13, net9.0, nopCommerce 4.90, FluentMigrator, linq2db, NUnit 3.14, Moq 4.20, FluentAssertions 6.12.

**Spec:** `docs/superpowers/specs/2026-10-12-customer-profile-design.md`

## Global Constraints

- Build against **nopCommerce 4.90** at `$NOP`. `plugin.json` declares `"SupportedVersions": [ "4.90" ]`.
- Only the .NET 10 SDK is installed. Prefix every `dotnet build` / `dotnet test` with `DOTNET_ROLL_FORWARD=Major` (nopCommerce's `ClearPluginAssemblies.dll` post-build helper targets an older runtime). `$NOP/global.json` already rolls forward; leave it alone.
- Plugin system name `Misc.CustomerProfile`. Assembly and namespace root `Nop.Plugin.Misc.CustomerProfile`.
- All locale resource keys are prefixed `Plugins.Misc.CustomerProfile.`. Register them from **both** `InstallAsync` and `UpdateAsync` through one shared method (the Handling Fee 2.11 lesson: install-only registration shows raw keys after an upgrade).
- The Installation migration creates the full current schema. Any later Update migration must be mirrored into it (Estimate Builder rule: nopCommerce marks Update migrations applied on install without running them).
- Never add a second tax-exempt flag. Read and write `Customer.IsTaxExempt` only.
- Tax certificate files are never served through nopCommerce's public `/download/getfileupload/{guid}` route. Only the plugin's controllers serve them, after an ownership or permission check.
- Account emails (welcome, password recovery, email validation, failed login) are never copied to contacts.
- All admin pages are closed to vendors (`await _workContext.GetCurrentVendorAsync() != null` → access denied).
- Expiry dates are dates, not instants. A certificate with `ExpiresOn = 2026-12-31` is valid through the end of 31 December in `IDateTimeHelper.DefaultStoreTimeZone`. "Today" always means today in that time zone.

## Paths

```bash
PROJ="/Users/zach/Claude/Projects/NOPCommercePluginDiscount"
NOP="/Users/zach/Claude/Projects/nopCommerce-4.90.x"
SRC="$PROJ/CustomerProfile/nopCommerce 4.90/Nop.Plugin.Misc.CustomerProfile"
TESTS="$PROJ/CustomerProfile/nopCommerce 4.90/Tests/Nop.Plugin.Misc.CustomerProfile.Tests"
```

`$PROJ` is the repository and holds the source of record. `$NOP` is the nopCommerce 4.90 tree used to compile and test (the Handling Fee and Better Search 4.90 builds already live in `$NOP/src/Plugins`). Edit in `$PROJ`, then sync into `$NOP` to build:

```bash
sync_in() {
  rsync -a --delete "$SRC/" "$NOP/src/Plugins/Nop.Plugin.Misc.CustomerProfile/"
  rsync -a --delete --exclude bin --exclude obj "$TESTS/" "$NOP/src/Tests/Nop.Plugin.Misc.CustomerProfile.Tests/"
}
build() { (cd "$NOP/src" && DOTNET_ROLL_FORWARD=Major dotnet build Plugins/Nop.Plugin.Misc.CustomerProfile/Nop.Plugin.Misc.CustomerProfile.csproj -c Release -nologo 2>&1 | grep -E "error|warn CS|Build succeeded|Build FAILED"); }
test_all() { (cd "$NOP/src" && DOTNET_ROLL_FORWARD=Major dotnet test Tests/Nop.Plugin.Misc.CustomerProfile.Tests -nologo 2>&1 | tail -15); }
```

## File Structure

Under `$SRC`:

| File | Responsibility |
|---|---|
| `Nop.Plugin.Misc.CustomerProfile.csproj` | Project; output to `Presentation/Nop.Web/Plugins/Misc.CustomerProfile` |
| `plugin.json`, `logo.png` | Manifest, logo (copy Estimate Builder's) |
| `CustomerProfileDefaults.cs` | System name, table names, routes, menu names, view paths, template names, form field names |
| `CustomerProfileSettings.cs` | `ISettings` |
| `CustomerProfilePlugin.cs` | `BasePlugin, IWidgetPlugin, IPaymentMethod`: install/uninstall, widget zones, Net 30 |
| `Domain/CustomerBusinessProfile.cs` | Dealership, Net 30 flag and terms per customer |
| `Domain/CustomerContact.cs` | Secondary contact |
| `Domain/MessageCategory.cs` | `[Flags]` enum of email categories |
| `Domain/TaxExemptionCertificate.cs`, `Domain/CertificateState.cs` | Certificate and its state |
| `Domain/Net30Order.cs` | Due date per Net 30 order |
| `Data/Migrations/SchemaMigration.cs` | Full schema on install |
| `Logic/ContactRecipientResolver.cs` | Pure: which contacts get a copy |
| `Logic/ExpiryPlanner.cs` | Pure: reminder / expire / nudge decisions |
| `Logic/OverduePlanner.cs` | Pure: Net 30 overdue decisions |
| `Logic/UploadValidator.cs` | Pure: file type and size |
| `Logic/StoreClock.cs` | "Today" in the store time zone (thin wrapper, injectable) |
| `Services/IBusinessProfileService.cs` + impl | Profile get/save |
| `Services/ICustomerContactService.cs` + impl | Contacts CRUD |
| `Services/ITaxExemptionService.cs` + impl | Upload, approve, reject, expire, queries |
| `Services/INet30Service.cs` + impl | Net 30 orders, due dates, grid query |
| `Services/ICustomerProfileMessageService.cs` + impl | The plugin's own emails |
| `Services/CustomerProfileWorkflowMessageService.cs` | Core subclass: contact copies |
| `Services/MessageTemplateInstaller.cs` | Creates and removes the plugin's message templates |
| `Services/DailyTask.cs` | `IScheduleTask` |
| `Infrastructure/NopStartup.cs` | DI |
| `Infrastructure/RouteProvider.cs` | Storefront routes |
| `Infrastructure/FmsPluginsMenuConsumer.cs` | FMS Plugins menu group (copy of the shared pattern) |
| `Infrastructure/EventConsumers.cs` | `ModelReceivedEvent`, `CustomerRegisteredEvent`, `EntityInsertedEvent<Order>`, `MessageTokensAddedEvent` |
| `Controllers/CustomerProfileAdminController.cs` | Admin card AJAX, grids, settings |
| `Controllers/CustomerProfilePublicController.cs` | Business details, tax exemption pages, file download |
| `Components/*.cs` | One view component per widget zone, plus Net 30 payment info |
| `Models/*.cs` | View models |
| `Views/**` | Razor views |
| `Content/customer-profile-admin.js` | Admin card AJAX |

Under `$TESTS`: `Nop.Plugin.Misc.CustomerProfile.Tests.csproj`, `ContactRecipientResolverTests.cs`, `ExpiryPlannerTests.cs`, `OverduePlannerTests.cs`, `UploadValidatorTests.cs`, `WorkflowFanOutTests.cs`.

Repo-level: `CustomerProfile/Readme.txt`, `CustomerProfile/TESTING.md`, a row in `README.md`.

---

### Task 0: TaxJar check on staging (gate for Task 12's tax step)

Not code. Run it first because the answer can add work.

- [ ] **Step 1:** On staging, pick a test customer with a taxable shipping address. Admin → Customers → edit → tick **Is tax exempt** → Save.
- [ ] **Step 2:** Log in as that customer, add a taxable product, go through checkout. Record tax shown on the cart, on checkout confirm, and on the placed order (admin order page and PDF invoice).
- [ ] **Step 3:** Record the TaxJar plugin's name and version (Configuration → Local plugins).
- [ ] **Step 4:** If all three show **$0.00 tax**, write "TaxJar honours IsTaxExempt (plugin X vX.Y, checked YYYY-MM-DD)" into the spec's V1 section and continue. If any shows tax, stop and add a task before Task 12: sync exemption to TaxJar (customer API, `exemption_type: "wholesale"`, exempt regions from the certificate's issuing state) or patch the TaxJar plugin. Confirm the approach with the user before building it.

---

### Task 1: Skeleton

**Files:** create `$SRC/Nop.Plugin.Misc.CustomerProfile.csproj`, `plugin.json`, `logo.png`, `CustomerProfileDefaults.cs`, `CustomerProfileSettings.cs`, a stub `CustomerProfilePlugin.cs`; test csproj.

- [ ] **Step 1: csproj.** Copy the shape of `$PROJ/HandlingFee/nopCommerce 4.90/Nop.Plugin.Misc.HandlingFee/Nop.Plugin.Misc.HandlingFee.csproj` (project reference to `..\..\Presentation\Nop.Web\Nop.Web.csproj`, `OutputPath`/`OutDir` to `..\..\Presentation\Nop.Web\Plugins\Misc.CustomerProfile`, the `ClearPluginAssemblies` target). Add:

```xml
<ItemGroup>
  <None Update="plugin.json" CopyToOutputDirectory="PreserveNewest" />
  <None Update="logo.png" CopyToOutputDirectory="PreserveNewest" />
  <None Update="Views\**\*.cshtml" CopyToOutputDirectory="PreserveNewest" />
  <None Update="Content\**\*" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
<ItemGroup>
  <InternalsVisibleTo Include="Nop.Plugin.Misc.CustomerProfile.Tests" />
</ItemGroup>
```

- [ ] **Step 2: plugin.json.**

```json
{
  "Group": "Misc",
  "FriendlyName": "Customer profile (dealership, tax exemption, Net 30, contacts)",
  "SystemName": "Misc.CustomerProfile",
  "Version": "1.0.0",
  "SupportedVersions": [ "4.90" ],
  "Author": "Custom",
  "DisplayOrder": 1,
  "FileName": "Nop.Plugin.Misc.CustomerProfile.dll",
  "Description": "BMW dealership field, tax exemption certificates with expiry reminders, Net 30 payment for selected customers, and secondary contacts who receive chosen customer emails",
  "LimitedToStores": [],
  "LimitedToCustomerRoles": [],
  "DependsOnSystemNames": []
}
```

- [ ] **Step 3: Defaults.**

```csharp
namespace Nop.Plugin.Misc.CustomerProfile;

public static class CustomerProfileDefaults
{
    public const string SystemName = "Misc.CustomerProfile";

    public const string ProfileTable = "CustomerBusinessProfile";
    public const string ContactTable = "CustomerContact";
    public const string CertificateTable = "TaxExemptionCertificate";
    public const string Net30OrderTable = "Net30Order";

    public const int DealershipMaxLength = 200;
    public const int ContactNameMaxLength = 200;
    public const int ContactEmailMaxLength = 254;
    public const int ContactTitleMaxLength = 100;
    public const int NoteMaxLength = 1000;

    public const string DailyTaskName = "Customer profile: tax exemption and Net 30 reminders";
    public const string DailyTaskType = "Nop.Plugin.Misc.CustomerProfile.Services.DailyTask, Nop.Plugin.Misc.CustomerProfile";
    public const int DailyTaskSeconds = 4 * 60 * 60; // idempotent; frequency only affects latency

    // Form field names posted from widgets that sit inside core forms
    public const string FieldDealership = "CustomerProfile.BmwDealership";
    public const string FieldNet30Enabled = "CustomerProfile.Net30Enabled";
    public const string FieldNet30TermsDays = "CustomerProfile.Net30TermsDays";

    // Message templates
    public const string TplSubmittedOwner = "CustomerProfile.TaxExemption.Submitted.StoreOwnerNotification";
    public const string TplReviewedCustomer = "CustomerProfile.TaxExemption.Reviewed.CustomerNotification";
    public const string TplExpiringCustomer = "CustomerProfile.TaxExemption.Expiring.CustomerNotification";
    public const string TplExpiringOwner = "CustomerProfile.TaxExemption.Expiring.StoreOwnerNotification";
    public const string TplExpiredCustomer = "CustomerProfile.TaxExemption.Expired.CustomerNotification";
    public const string TplExpiredOwner = "CustomerProfile.TaxExemption.Expired.StoreOwnerNotification";
    public const string TplPendingOwner = "CustomerProfile.TaxExemption.PendingReview.StoreOwnerNotification";
    public const string TplOverdueCustomer = "CustomerProfile.Net30.Overdue.CustomerNotification";

    // Storefront routes
    public const string RouteBusinessDetails = "CustomerProfile.BusinessDetails";
    public const string RouteContactEdit = "CustomerProfile.ContactEdit";
    public const string RouteTaxExemption = "CustomerProfile.TaxExemption";
    public const string RouteCertificateFile = "CustomerProfile.CertificateFile";

    // Admin menu entries under "FMS Plugins"
    public const string MenuGroup = "FmsPlugins.CustomerProfile";
    public const string MenuTaxExemptions = "FmsPlugins.CustomerProfile.TaxExemptions";
    public const string MenuNet30Orders = "FmsPlugins.CustomerProfile.Net30Orders";
    public const string MenuSettings = "FmsPlugins.CustomerProfile.Settings";

    public const string ViewsPath = "~/Plugins/Misc.CustomerProfile/Views";
    public const string AdminScriptPath = "~/Plugins/Misc.CustomerProfile/Content/customer-profile-admin.js";
}
```

- [ ] **Step 4: Settings.**

```csharp
using Nop.Core.Configuration;

namespace Nop.Plugin.Misc.CustomerProfile;

public class CustomerProfileSettings : ISettings
{
    public int ReminderDaysBeforeExpiry { get; set; } = 30;
    public int PendingReviewNudgeDays { get; set; } = 3;
    public bool ApproveMarksTaxExempt { get; set; } = true;
    public bool RemoveExemptionOnExpiry { get; set; } = true;
    /// <summary>Comma-separated; copied on every store-owner notification from this plugin</summary>
    public string NotificationCcEmails { get; set; } = string.Empty;
    public int MaxUploadSizeMb { get; set; } = 10;
    public int Net30DefaultTermsDays { get; set; } = 30;
    public bool Net30OverdueReminderEnabled { get; set; }
}
```

- [ ] **Step 5:** Stub `CustomerProfilePlugin : BasePlugin` (filled in Task 5). Test csproj: copy `$PROJ/HandlingFee/nopCommerce 4.90/Tests/Nop.Plugin.Misc.HandlingFee.Tests/*.csproj`, point the `ProjectReference` at `..\..\Plugins\Nop.Plugin.Misc.CustomerProfile\Nop.Plugin.Misc.CustomerProfile.csproj`. Add one trivial passing test.
- [ ] **Step 6:** `sync_in && build && test_all` → build succeeds, 1 test passes.
- [ ] **Step 7:** Commit: `feat(customer-profile): plugin skeleton`.

---

### Task 2: Domain and schema

**Files:** `Domain/*.cs`, `Data/Migrations/SchemaMigration.cs`.

- [ ] **Step 1: Entities.** Table names equal class names (linq2db convention, as in Estimate Builder).

```csharp
// Domain/CustomerBusinessProfile.cs
public class CustomerBusinessProfile : BaseEntity
{
    public int CustomerId { get; set; }
    public string? BmwDealership { get; set; }
    public bool Net30Enabled { get; set; }
    public int Net30TermsDays { get; set; }
    public DateTime UpdatedOnUtc { get; set; }
}

// Domain/MessageCategory.cs
[Flags]
public enum MessageCategory
{
    None = 0,
    OrderConfirmations = 1,   // placed, processing
    InvoicesPayments = 2,     // paid, completed, refunded, Net 30 overdue
    Shipping = 4,             // sent, ready for pickup, delivered
    Cancellations = 8,
    OrderNotes = 16,
    Returns = 32,
    TaxExemption = 64
}

// Domain/CustomerContact.cs
public class CustomerContact : BaseEntity
{
    public int CustomerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Title { get; set; }
    public int CategoriesId { get; set; }
    public bool Active { get; set; } = true;
    public DateTime CreatedOnUtc { get; set; }
    public int CreatedByCustomerId { get; set; }
    public DateTime? UpdatedOnUtc { get; set; }
    public int? UpdatedByCustomerId { get; set; }

    public MessageCategory Categories
    {
        get => (MessageCategory)CategoriesId;
        set => CategoriesId = (int)value;
    }
}

// Domain/CertificateState.cs
public enum CertificateState { PendingReview = 0, Approved = 10, Rejected = 20, Superseded = 30, Expired = 40 }

// Domain/TaxExemptionCertificate.cs
public class TaxExemptionCertificate : BaseEntity
{
    public int CustomerId { get; set; }
    public int DownloadId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public int StateId { get; set; }
    /// <summary>Date only; valid through the end of this day in the store time zone</summary>
    public DateTime? ExpiresOn { get; set; }
    public string? CertificateNumber { get; set; }
    public string? IssuingState { get; set; }
    public int UploadedByCustomerId { get; set; }
    public DateTime UploadedOnUtc { get; set; }
    public int? ReviewedByCustomerId { get; set; }
    public DateTime? ReviewedOnUtc { get; set; }
    public string? ReviewNote { get; set; }
    public DateTime? ReminderSentOnUtc { get; set; }
    public DateTime? ExpiredNoticeSentOnUtc { get; set; }
    public DateTime? PendingNudgeSentOnUtc { get; set; }

    public CertificateState State
    {
        get => (CertificateState)StateId;
        set => StateId = (int)value;
    }
}

// Domain/Net30Order.cs
public class Net30Order : BaseEntity
{
    public int OrderId { get; set; }
    public int CustomerId { get; set; }
    public int TermsDays { get; set; }
    /// <summary>Date only, store time zone</summary>
    public DateTime DueOn { get; set; }
    public DateTime? OverdueNoticeSentOnUtc { get; set; }
}
```

The `Categories` and `State` wrappers need no attribute. nopCommerce maps a property only when the entity descriptor has a column of the same name (`Nop.Data/Mapping/FluentMigratorMetadataReader.cs:99-102`), the same way core's `Order.OrderStatus` wraps `OrderStatusId`.

- [ ] **Step 2: Migration.**

```csharp
[NopMigration("2026/10/12 12:00:00", "Misc.CustomerProfile schema", MigrationProcessType.Installation)]
public class SchemaMigration : Migration
{
    public override void Up()
    {
        Create.Table(CustomerProfileDefaults.ProfileTable)
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("CustomerId").AsInt32().NotNullable().Unique("UX_CustomerBusinessProfile_CustomerId")
            .WithColumn("BmwDealership").AsString(CustomerProfileDefaults.DealershipMaxLength).Nullable()
            .WithColumn("Net30Enabled").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("Net30TermsDays").AsInt32().NotNullable().WithDefaultValue(30)
            .WithColumn("UpdatedOnUtc").AsDateTime2().NotNullable();

        Create.Table(CustomerProfileDefaults.ContactTable)
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("CustomerId").AsInt32().NotNullable().Indexed("IX_CustomerContact_CustomerId")
            .WithColumn("Name").AsString(CustomerProfileDefaults.ContactNameMaxLength).NotNullable()
            .WithColumn("Email").AsString(CustomerProfileDefaults.ContactEmailMaxLength).NotNullable()
            .WithColumn("Title").AsString(CustomerProfileDefaults.ContactTitleMaxLength).Nullable()
            .WithColumn("CategoriesId").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("Active").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("CreatedOnUtc").AsDateTime2().NotNullable()
            .WithColumn("CreatedByCustomerId").AsInt32().NotNullable()
            .WithColumn("UpdatedOnUtc").AsDateTime2().Nullable()
            .WithColumn("UpdatedByCustomerId").AsInt32().Nullable();

        Create.Table(CustomerProfileDefaults.CertificateTable)
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("CustomerId").AsInt32().NotNullable().Indexed("IX_TaxExemptionCertificate_CustomerId")
            .WithColumn("DownloadId").AsInt32().NotNullable()
            .WithColumn("FileName").AsString(400).NotNullable()
            .WithColumn("ContentType").AsString(100).NotNullable()
            .WithColumn("StateId").AsInt32().NotNullable().Indexed("IX_TaxExemptionCertificate_StateId")
            .WithColumn("ExpiresOn").AsDateTime2().Nullable()
            .WithColumn("CertificateNumber").AsString(100).Nullable()
            .WithColumn("IssuingState").AsString(100).Nullable()
            .WithColumn("UploadedByCustomerId").AsInt32().NotNullable()
            .WithColumn("UploadedOnUtc").AsDateTime2().NotNullable()
            .WithColumn("ReviewedByCustomerId").AsInt32().Nullable()
            .WithColumn("ReviewedOnUtc").AsDateTime2().Nullable()
            .WithColumn("ReviewNote").AsString(CustomerProfileDefaults.NoteMaxLength).Nullable()
            .WithColumn("ReminderSentOnUtc").AsDateTime2().Nullable()
            .WithColumn("ExpiredNoticeSentOnUtc").AsDateTime2().Nullable()
            .WithColumn("PendingNudgeSentOnUtc").AsDateTime2().Nullable();

        Create.Table(CustomerProfileDefaults.Net30OrderTable)
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("OrderId").AsInt32().NotNullable().Unique("UX_Net30Order_OrderId")
            .WithColumn("CustomerId").AsInt32().NotNullable().Indexed("IX_Net30Order_CustomerId")
            .WithColumn("TermsDays").AsInt32().NotNullable()
            .WithColumn("DueOn").AsDateTime2().NotNullable()
            .WithColumn("OverdueNoticeSentOnUtc").AsDateTime2().Nullable();
    }

    public override void Down()
    {
        foreach (var table in new[] { CustomerProfileDefaults.Net30OrderTable, CustomerProfileDefaults.CertificateTable,
                     CustomerProfileDefaults.ContactTable, CustomerProfileDefaults.ProfileTable })
            if (Schema.Table(table).Exists())
                Delete.Table(table);
    }
}
```

No foreign keys to core tables, matching Estimate Builder. Customer deletion in nopCommerce is a soft delete, so rows stay valid.

- [ ] **Step 3:** `sync_in && build`.
- [ ] **Step 4:** Install on a local 4.90 site (run `$NOP/src/Presentation/Nop.Web` against a scratch database). Check the four tables exist with the columns above. Uninstall and check they are gone.
- [ ] **Step 5:** Commit: `feat(customer-profile): domain and schema`.

---

### Task 3: Pure logic, test-first

**Files:** `Logic/*.cs`, `$TESTS/*Tests.cs`. Write each test file first, watch it fail, then implement.

- [ ] **Step 1: `ContactRecipientResolver`.**

```csharp
public sealed record Recipient(string Email, string Name);

public static class ContactRecipientResolver
{
    /// <summary>
    /// Contacts that should get their own copy of an email in <paramref name="category"/>.
    /// Skips inactive contacts, contacts not subscribed to the category, the original recipient, and duplicates.
    /// </summary>
    public static IReadOnlyList<Recipient> Resolve(IEnumerable<CustomerContact> contacts, MessageCategory category, string? primaryEmail)
    {
        if (category == MessageCategory.None)
            return Array.Empty<Recipient>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(primaryEmail))
            seen.Add(primaryEmail.Trim());

        return contacts
            .Where(c => c.Active && c.Categories.HasFlag(category) && !string.IsNullOrWhiteSpace(c.Email))
            .Where(c => seen.Add(c.Email.Trim()))
            .Select(c => new Recipient(c.Email.Trim(), c.Name))
            .ToList();
    }
}
```

Tests: subscribed active contact included; inactive excluded; other category excluded; contact with the primary's email (different case, surrounding spaces) excluded; two contacts with the same email → one recipient; `None` → empty; multi-flag contact matches each of its flags.

- [ ] **Step 2: `ExpiryPlanner`.**

```csharp
public sealed record CertificateSnapshot(int Id, int CustomerId, CertificateState State, DateTime? ExpiresOn,
    DateTime UploadedOnUtc, DateTime? ReminderSentOnUtc, DateTime? ExpiredNoticeSentOnUtc, DateTime? PendingNudgeSentOnUtc);

public enum ExpiryActionKind { SendReminder, Expire, NudgePendingReview }

/// <param name="RemoveExemption">Only for Expire: untick IsTaxExempt</param>
public sealed record ExpiryAction(ExpiryActionKind Kind, int CertificateId, int CustomerId, bool RemoveExemption = false);

public static class ExpiryPlanner
{
    public static IReadOnlyList<ExpiryAction> Plan(IReadOnlyCollection<CertificateSnapshot> certificates, DateTime today,
        DateTime utcNow, int reminderDays, int pendingNudgeDays, bool removeExemptionOnExpiry)
    {
        today = today.Date;
        var actions = new List<ExpiryAction>();

        // A customer keeps exemption if any approved certificate is still valid today
        bool HasOtherValid(CertificateSnapshot cert) => certificates.Any(c =>
            c.Id != cert.Id && c.CustomerId == cert.CustomerId && c.State == CertificateState.Approved
            && c.ExpiresOn.HasValue && c.ExpiresOn.Value.Date >= today);

        foreach (var cert in certificates)
        {
            switch (cert.State)
            {
                case CertificateState.Approved when cert.ExpiresOn.HasValue:
                    var expires = cert.ExpiresOn.Value.Date;
                    if (expires < today)
                        actions.Add(new ExpiryAction(ExpiryActionKind.Expire, cert.Id, cert.CustomerId,
                            removeExemptionOnExpiry && !HasOtherValid(cert)));
                    else if (cert.ReminderSentOnUtc == null && reminderDays > 0 && (expires - today).TotalDays <= reminderDays
                             && !HasLaterValid(certificates, cert))
                        actions.Add(new ExpiryAction(ExpiryActionKind.SendReminder, cert.Id, cert.CustomerId));
                    break;

                case CertificateState.PendingReview when pendingNudgeDays > 0 && cert.PendingNudgeSentOnUtc == null
                                                        && (utcNow - cert.UploadedOnUtc).TotalDays >= pendingNudgeDays:
                    actions.Add(new ExpiryAction(ExpiryActionKind.NudgePendingReview, cert.Id, cert.CustomerId));
                    break;
            }
        }

        return actions;
    }

    // No reminder if the customer already has an approved certificate expiring later than this one
    private static bool HasLaterValid(IEnumerable<CertificateSnapshot> all, CertificateSnapshot cert) => all.Any(c =>
        c.Id != cert.Id && c.CustomerId == cert.CustomerId && c.State == CertificateState.Approved
        && c.ExpiresOn.HasValue && c.ExpiresOn.Value.Date > cert.ExpiresOn!.Value.Date);
}
```

Tests (today = 2026-10-12):
- expires 2026-10-12 (today) → not expired; it is inside the 30-day window, so `SendReminder` if not yet sent. Expires 2026-10-11 → `Expire` with `RemoveExemption = true`.
- expires 2026-11-11 (30 days) → reminder; 2026-11-12 (31 days) → nothing.
- reminder already sent → nothing until expiry.
- `reminderDays = 0` → never a reminder.
- expired, but a second approved certificate valid until 2027 → `Expire` with `RemoveExemption = false`.
- `removeExemptionOnExpiry = false` → `Expire` with `RemoveExemption = false`.
- approved with no `ExpiresOn` → nothing (admin is forced to set one on approve; defensive).
- pending uploaded 3 days ago, nudge days 3 → nudge; 2 days → nothing; nudge already sent → nothing.
- rejected / superseded / expired states → nothing.

- [ ] **Step 3: `OverduePlanner`.**

```csharp
public sealed record Net30Snapshot(int OrderId, int CustomerId, DateTime DueOn, bool IsPaid, bool IsCancelled, DateTime? OverdueNoticeSentOnUtc);

public static class OverduePlanner
{
    public static IReadOnlyList<int> OrdersToRemind(IEnumerable<Net30Snapshot> orders, DateTime today, bool enabled) =>
        !enabled ? Array.Empty<int>() : orders
            .Where(o => !o.IsPaid && !o.IsCancelled && o.OverdueNoticeSentOnUtc == null && o.DueOn.Date < today.Date)
            .Select(o => o.OrderId)
            .ToList();
}
```

Tests: due yesterday unpaid → remind; due today → not yet; paid / cancelled / already reminded → no; disabled → empty.

- [ ] **Step 4: `UploadValidator`.**

```csharp
public enum UploadProblem { None, Empty, TooLarge, UnsupportedType }

public static class UploadValidator
{
    private static readonly (string Ext, string ContentType, byte[] Magic)[] Allowed =
    {
        (".pdf", "application/pdf", "%PDF"u8.ToArray()),
        (".png", "image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47 }),
        (".jpg", "image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF }),
        (".jpeg", "image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF })
    };

    /// <returns>The problem, and the content type to store (taken from the bytes, not from the browser)</returns>
    public static (UploadProblem Problem, string? ContentType) Validate(string fileName, ReadOnlySpan<byte> bytes, int maxSizeMb)
    {
        if (bytes.Length == 0)
            return (UploadProblem.Empty, null);
        if (bytes.Length > maxSizeMb * 1024L * 1024L)
            return (UploadProblem.TooLarge, null);

        var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        foreach (var (allowedExt, contentType, magic) in Allowed)
            if (ext == allowedExt && bytes.StartsWith(magic))
                return (UploadProblem.None, contentType);

        return (UploadProblem.UnsupportedType, null);
    }
}
```

Tests: real PDF header with `.pdf` → ok, `application/pdf`; PDF bytes named `.png` → unsupported; `.exe` renamed `.pdf` (MZ header) → unsupported; `.JPG` upper case → ok; empty → `Empty`; 10 MB + 1 byte at limit 10 → `TooLarge`; exactly 10 MB → ok.

- [ ] **Step 5: `StoreClock`.**

```csharp
public interface IStoreClock { DateTime Today { get; } DateTime UtcNow { get; } }

public class StoreClock : IStoreClock
{
    private readonly IDateTimeHelper _dateTimeHelper;
    public StoreClock(IDateTimeHelper dateTimeHelper) => _dateTimeHelper = dateTimeHelper;
    public DateTime UtcNow => DateTime.UtcNow;
    public DateTime Today => _dateTimeHelper.ConvertToUserTime(DateTime.UtcNow, TimeZoneInfo.Utc, _dateTimeHelper.DefaultStoreTimeZone).Date;
}
```

- [ ] **Step 6:** `sync_in && test_all` → all pass.
- [ ] **Step 7:** Commit: `feat(customer-profile): expiry, overdue, recipient and upload rules with tests`.

---

### Task 4: Data services

**Files:** `Services/IBusinessProfileService.cs`, `ICustomerContactService.cs`, `ITaxExemptionService.cs` (queries only for now), `INet30Service.cs` (queries only), implementations. All take `IRepository<T>`.

- [ ] **Step 1: `BusinessProfileService`.**
  - `Task<CustomerBusinessProfile?> GetAsync(int customerId)`
  - `Task<CustomerBusinessProfile> GetOrNewAsync(int customerId)` returns an unsaved instance with `Net30TermsDays = settings.Net30DefaultTermsDays` when missing.
  - `Task SaveAsync(CustomerBusinessProfile profile)` inserts or updates, sets `UpdatedOnUtc`, trims dealership to max length, clamps terms to 1–365.
  - `Task<IList<CustomerBusinessProfile>> GetByCustomerIdsAsync(int[] ids)` for the grids.
- [ ] **Step 2: `CustomerContactService`.** `GetForCustomerAsync(customerId, activeOnly)`, `GetByIdAsync`, `InsertAsync`, `UpdateAsync`, `DeleteAsync`. Validation lives in the controllers via a shared `ContactInput.Validate()` (name required, email via `CommonHelper.IsValidEmail`, lengths, max 20 contacts per customer).
- [ ] **Step 3: `TaxExemptionService` queries.** `GetByIdAsync`, `GetForCustomerAsync(customerId)` (newest first), `GetCurrentValidAsync(customerId, today)` (approved, `ExpiresOn >= today`), `SearchAsync(states?, expiringWithinDays?, dealership?, customerEmailOrName?, pageIndex, pageSize)` joining `Customer` and `CustomerBusinessProfile` via `IRepository<Customer>.Table`.
- [ ] **Step 4: `Net30Service` queries.** `GetByOrderIdAsync`, `SearchUnpaidAsync(overdueOnly, today, pageIndex, pageSize)` joining `Order` where `PaymentStatusId != Paid` and `OrderStatusId != Cancelled` and `!Deleted`.
- [ ] **Step 5:** Register all in `Infrastructure/NopStartup.cs` (`Order => 3000`) as scoped, plus `IStoreClock`.
- [ ] **Step 6:** `build`. Commit: `feat(customer-profile): data services`.

---

### Task 5: Plugin class, install and uninstall

**Files:** `CustomerProfilePlugin.cs`, `Services/MessageTemplateInstaller.cs`.

- [ ] **Step 1: Class declaration and widget zones.**

```csharp
public class CustomerProfilePlugin : BasePlugin, IWidgetPlugin, IPaymentMethod
{
    public bool HideInWidgetList => false;

    public Task<IList<string>> GetWidgetZonesAsync() => Task.FromResult<IList<string>>(new List<string>
    {
        AdminWidgetZones.CustomerDetailsBlock,
        PublicWidgetZones.AccountNavigationAfter,
        PublicWidgetZones.AccountNavigationBefore,   // expiry banner
        PublicWidgetZones.RegisterBottom,
        PublicWidgetZones.OrderDetailsPageOverview   // "Payment due"
    });

    public Type GetWidgetViewComponent(string widgetZone) => widgetZone switch
    {
        _ when widgetZone == AdminWidgetZones.CustomerDetailsBlock => typeof(AdminCustomerCardViewComponent),
        _ when widgetZone == PublicWidgetZones.AccountNavigationAfter => typeof(AccountNavigationViewComponent),
        _ when widgetZone == PublicWidgetZones.AccountNavigationBefore => typeof(ExpiryBannerViewComponent),
        _ when widgetZone == PublicWidgetZones.RegisterBottom => typeof(RegisterDealershipViewComponent),
        _ => typeof(OrderDueDateViewComponent)
    };
    // ...
}
```

(`AdminWidgetZones`/`PublicWidgetZones` members are static properties, not constants, so `switch` needs guards.)

- [ ] **Step 2: Install.**

```csharp
public override async Task InstallAsync()
{
    await _settingService.SaveSettingAsync(new CustomerProfileSettings());
    await AddLocalesAsync();
    await _templateInstaller.InstallAsync();

    if (await _scheduleTaskService.GetTaskByTypeAsync(CustomerProfileDefaults.DailyTaskType) == null)
        await _scheduleTaskService.InsertTaskAsync(new ScheduleTask
        {
            Name = CustomerProfileDefaults.DailyTaskName,
            Type = CustomerProfileDefaults.DailyTaskType,
            Seconds = CustomerProfileDefaults.DailyTaskSeconds,
            Enabled = true,
            LastEnabledUtc = DateTime.UtcNow,
            StopOnError = false
        });

    // Widgets and payment method both need activating; Net 30 stays invisible until a customer is switched on
    if (!_widgetSettings.ActiveWidgetSystemNames.Contains(CustomerProfileDefaults.SystemName))
    {
        _widgetSettings.ActiveWidgetSystemNames.Add(CustomerProfileDefaults.SystemName);
        await _settingService.SaveSettingAsync(_widgetSettings);
    }
    if (!_paymentSettings.ActivePaymentMethodSystemNames.Contains(CustomerProfileDefaults.SystemName))
    {
        _paymentSettings.ActivePaymentMethodSystemNames.Add(CustomerProfileDefaults.SystemName);
        await _settingService.SaveSettingAsync(_paymentSettings);
    }

    await base.InstallAsync();
}

public override async Task UpdateAsync(string currentVersion, string targetVersion)
{
    await AddLocalesAsync();
    await base.UpdateAsync(currentVersion, targetVersion);
}
```

- [ ] **Step 3: Uninstall.** Delete settings; `_templateInstaller.UninstallAsync()`; delete the task; delete every `Download` row referenced by `TaxExemptionCertificate.DownloadId` **before** `base.UninstallAsync()` drops the tables (the migration `Down` runs inside base); delete locale resources by prefix. Leave `Customer.IsTaxExempt` untouched.
- [ ] **Step 4: `MessageTemplateInstaller`.** For each of the eight template names in Defaults: if `GetMessageTemplatesByNameAsync(name)` is empty, insert `new MessageTemplate { Name, Subject, Body, IsActive = true, EmailAccountId = _emailAccountSettings.DefaultEmailAccountId, LimitedToStores = false }`. Bodies are short HTML using the tokens defined in Task 11 (write them out in the installer; example below). Uninstall deletes every template whose name starts with `CustomerProfile.`.

```text
Subject: %Store.Name%: your tax exemption certificate expires on %TaxExemption.ExpiresOn%
Body:
<p>Hello %Customer.FullName%,</p>
<p>The tax exemption certificate on file for your account expires on <strong>%TaxExemption.ExpiresOn%</strong>
(%TaxExemption.DaysLeft% days). To keep tax-exempt pricing after that date, upload a current certificate:</p>
<p><a href="%TaxExemption.UploadUrl%">%TaxExemption.UploadUrl%</a></p>
<p>%Store.Name%</p>
```

- [ ] **Step 5: Locales.** One dictionary with every label used by views, validation messages, the payment method name `Plugins.Misc.CustomerProfile.Net30.Name` = "Net 30" and description `...Net30.Description` = "Pay within {0} days of invoice". Category labels `...Category.OrderConfirmations` etc.
- [ ] **Step 6:** `build`, reinstall locally, check: settings saved, eight templates under Content management → Message templates, task under System → Schedule tasks, plugin listed under Widgets and Payment methods as active. Uninstall and check all removed.
- [ ] **Step 7:** Commit: `feat(customer-profile): install, uninstall, templates, task registration`.

---

### Task 6: FMS menu and settings page

**Files:** `Infrastructure/FmsPluginsMenuConsumer.cs`, `Controllers/CustomerProfileAdminController.cs` (settings actions), `Models/SettingsModel.cs`, `Views/Admin/Configure.cshtml`.

- [ ] **Step 1:** Copy `estimate-builder/Infrastructure/FmsPluginsMenuConsumer.cs` verbatim, keeping the shared constants identical. Group "Customer Profile", icon `fas fa-id-card`. Children:
  - Tax exemptions → `~/Admin/CustomerProfileAdmin/TaxExemptions`, `CUSTOMERS_VIEW`
  - Net 30 orders → `~/Admin/CustomerProfileAdmin/Net30Orders`, `ORDERS_VIEW`
  - Settings → `~/Admin/CustomerProfileAdmin/Configure`, `MANAGE_PLUGINS`
  - `Visible = !isVendor` on the first two.
- [ ] **Step 2:** Controller attributes as in `AdminEstimateController`: `[AuthorizeAdmin] [Area(AreaNames.ADMIN)] [AutoValidateAntiforgeryToken]`, `BasePluginController`. Private helpers `CanViewCustomersAsync`, `CanEditCustomersAsync`, `CanViewOrdersAsync`, `CanConfigureAsync`, each false for vendors.
- [ ] **Step 3:** `Configure` GET/POST binding every setting. Validate: reminder days 0–365, nudge days 0–60, max upload 1–25 MB, terms 1–365, each CC address valid. `GetConfigurationPageUrl()` returns `{store}Admin/CustomerProfileAdmin/Configure`.
- [ ] **Step 4:** Build, check menu, save settings round-trip. Commit: `feat(customer-profile): FMS menu and settings`.

---

### Task 7: Admin customer card, dealership and Net 30 switch

**Files:** `Components/AdminCustomerCardViewComponent.cs`, `Models/AdminCustomerCardModel.cs`, `Views/Admin/CustomerCard.cshtml`, `Infrastructure/EventConsumers.cs` (ModelReceived part).

- [ ] **Step 1: View component.** `additionalData` is `Nop.Web.Areas.Admin.Models.Customers.CustomerModel`. Return empty content for vendors and for admins without `CUSTOMERS_VIEW`. When `Model.Id == 0` (Create page) render a one-line "Save the customer first" card. Otherwise load profile, contacts, certificates and render.
- [ ] **Step 2: Card markup.** Use `<nop-card asp-name="customer-profile" asp-icon="fas fa-id-card" asp-title="Business profile">`, consistent with the other cards on the page. Sections:
  1. **Dealership**: text input `name="CustomerProfile.BmwDealership"`.
  2. **Net 30**: checkbox `name="CustomerProfile.Net30Enabled" value="true"` plus hidden `value="false"` after it; terms input `name="CustomerProfile.Net30TermsDays"`.
  3. **Tax exemption**: status line (approved until X / expired on X / pending review / none), warning if `Model.IsTaxExempt` is true and no valid certificate, certificate table (Task 12 fills in actions).
  4. **Contacts**: table (Task 9 fills in actions).
  These inputs are inside the core customer `<form>`, so sections 1–2 post with **Save**. Sections 3–4 use buttons with `type="button"` and AJAX only. **No nested `<form>` elements.**
- [ ] **Step 3: ModelReceived consumer.**

```csharp
public async Task HandleEventAsync(ModelReceivedEvent<BaseNopModel> eventMessage)
{
    if (eventMessage.Model is not CustomerModel model || model.Id == 0 || !eventMessage.ModelState.IsValid)
        return;
    if (await _workContext.GetCurrentVendorAsync() != null
        || !await _permissionService.AuthorizeAsync(StandardPermission.Customers.CUSTOMERS_CREATE_EDIT_DELETE))
        return;

    var request = _httpContextAccessor.HttpContext!.Request;
    var (hasDealer, dealer) = await request.TryGetFormValueAsync(CustomerProfileDefaults.FieldDealership);
    if (!hasDealer)
        return; // posted from a form that doesn't carry the card (e.g. a different customer action)

    var (_, net30) = await request.TryGetFormValueAsync(CustomerProfileDefaults.FieldNet30Enabled);
    var (_, terms) = await request.TryGetFormValueAsync(CustomerProfileDefaults.FieldNet30TermsDays);

    var profile = await _profileService.GetOrNewAsync(model.Id);
    profile.BmwDealership = string.IsNullOrWhiteSpace(dealer) ? null : dealer.ToString().Trim();
    profile.Net30Enabled = net30.FirstOrDefault() == "true";   // checkbox posts "true","false" when ticked
    if (int.TryParse(terms, out var days))
        profile.Net30TermsDays = days;
    await _profileService.SaveAsync(profile);
}
```

- [ ] **Step 4:** Manual check: edit a customer, set dealership and Net 30, Save → reload shows values. Force a validation error on the core form (blank email for a registered customer) → plugin values not saved. Vendor login → card absent.
- [ ] **Step 5:** Commit: `feat(customer-profile): admin card with dealership and Net 30 switch`.

---

### Task 8: Net 30 payment method

**Files:** `CustomerProfilePlugin.cs` (IPaymentMethod members), `Components/Net30PaymentInfoViewComponent.cs`, `Views/Net30PaymentInfo.cshtml`, `Infrastructure/EventConsumers.cs` (Order inserted + tokens), `Components/OrderDueDateViewComponent.cs`, admin grid action + view.

- [ ] **Step 1: IPaymentMethod members.** Base on `$NOP/src/Plugins/Nop.Plugin.Payments.CheckMoneyOrder/CheckMoneyOrderPaymentProcessor.cs`. Differences:

```csharp
public async Task<bool> HidePaymentMethodAsync(IList<ShoppingCartItem> cart)
{
    var customer = await _workContext.GetCurrentCustomerAsync();
    if (await _customerService.IsGuestAsync(customer))
        return true;
    var profile = await _profileService.GetAsync(customer.Id);
    return profile is not { Net30Enabled: true };
}

public async Task<ProcessPaymentResult> ProcessPaymentAsync(ProcessPaymentRequest request)
{
    // Checkout doesn't re-run HidePaymentMethodAsync at place-order time, so check again here
    var profile = await _profileService.GetAsync(request.CustomerId);
    if (profile is not { Net30Enabled: true })
        return new ProcessPaymentResult { Errors = new[] { await _localizationService.GetResourceAsync("Plugins.Misc.CustomerProfile.Net30.NotAvailable") } };

    return new ProcessPaymentResult { NewPaymentStatus = PaymentStatus.Pending };
}

public async Task<string> GetPaymentMethodDescriptionAsync()
{
    var customer = await _workContext.GetCurrentCustomerAsync();
    var profile = await _profileService.GetAsync(customer.Id);
    var days = profile?.Net30TermsDays ?? _settings.Net30DefaultTermsDays;
    return string.Format(await _localizationService.GetResourceAsync("Plugins.Misc.CustomerProfile.Net30.Description"), days);
}

public bool SkipPaymentInfo => true;
public Type GetPublicViewComponent() => typeof(Net30PaymentInfoViewComponent); // required by the interface; shows the terms line
public Task<decimal> GetAdditionalHandlingFeeAsync(IList<ShoppingCartItem> cart) => Task.FromResult(0m);
```

Capture/refund/void/recurring return "not supported" errors exactly as CheckMoneyOrder does. Refunds on Net 30 orders are done with nopCommerce's offline "Refund (offline)" button, which doesn't call the plugin.

**Name at checkout.** Checkout shows `ILocalizationService.GetLocalizedFriendlyNameAsync(plugin)` (`CheckoutModelFactory.cs:492`), which reads the resource `Plugins.FriendlyName.Misc.CustomerProfile` (`LocalizationService.cs:941-944`) and falls back to `plugin.json`'s `FriendlyName`. In `AddLocalesAsync`, set that resource to **"Net 30"**. Customers see "Net 30" at checkout, and the `plugin.json` name stays as the fallback. There is one name per plugin, so any admin screen that uses the localized name will also say "Net 30". Check Local plugins, Payment methods and Widgets after install. If "Net 30" there is too confusing, change `plugin.json`'s `Description` to start with "Net 30 payment method, …" rather than splitting the plugin.

- [ ] **Step 2: Due date at order insert.** Order emails are sent **before** `OrderPlacedEvent` (`OrderProcessingService.cs:1580` vs `:1590`), so the due date row must exist earlier. Consume `EntityInsertedEvent<Order>`:

```csharp
public async Task HandleEventAsync(EntityInsertedEvent<Order> eventMessage)
{
    var order = eventMessage.Entity;
    if (order.PaymentMethodSystemName != CustomerProfileDefaults.SystemName)
        return;

    var profile = await _profileService.GetAsync(order.CustomerId);
    var terms = profile?.Net30TermsDays ?? _settings.Net30DefaultTermsDays;
    var orderDay = _dateTimeHelper.ConvertToUserTime(order.CreatedOnUtc, TimeZoneInfo.Utc, _dateTimeHelper.DefaultStoreTimeZone).Date;

    await _net30Service.InsertAsync(new Net30Order
    {
        OrderId = order.Id, CustomerId = order.CustomerId, TermsDays = terms, DueOn = orderDay.AddDays(terms)
    });
}
```

Also on `OrderPlacedEvent`, add an order note "Net 30: payment due {date}" (`DisplayToCustomer = true`), so the order history carries it independent of the plugin.

- [ ] **Step 3: Token.** Consume `MessageTokensAddedEvent`: if the tokens include `Order.OrderId`/`Order.OrderNumber` and that order has a `Net30Order` row, add `%Order.Net30DueDate%` (formatted short date) and `%Order.Net30Terms%`. Otherwise add both as empty strings, so templates can include them unconditionally. Note in Readme: add `%Order.Net30DueDate%` to the Order placed / Order paid customer templates if wanted; the plugin doesn't edit core templates.
- [ ] **Step 4: Order details widget.** `OrderDueDateViewComponent` on `OrderDetailsPageOverview`: `additionalData` is the storefront `OrderDetailsModel`; if a `Net30Order` exists and the order isn't paid, render "Payment due {date}" (red "Overdue since {date}" when past).
- [ ] **Step 5: Admin grid.** `Net30Orders` action + view: DataTables grid like `AdminEstimateList.cshtml`. Columns: order #, customer (link), dealership, order total, created, due, days overdue. Filters: overdue only. Rows link to `~/Admin/Order/Edit/{id}`. Paid or cancelled orders drop off.
- [ ] **Step 6: Manual check.** Customer without Net 30: method absent at checkout. Switch on: method shows with "Pay within 30 days of invoice"; place order → payment status Pending, order note with due date, grid shows it; Mark as paid → gone from grid. Switch off with the method preselected in a stale checkout → place order fails with the "not available" error.
- [ ] **Step 7:** Commit: `feat(customer-profile): Net 30 payment method`.

---

### Task 9: Secondary contacts UI

**Files:** admin AJAX actions + `Content/customer-profile-admin.js`; `Controllers/CustomerProfilePublicController.cs`; `Infrastructure/RouteProvider.cs`; `Views/Public/BusinessDetails.cshtml`, `Views/Public/ContactEdit.cshtml`; `Components/AccountNavigationViewComponent.cs`; `Components/RegisterDealershipViewComponent.cs`; `Models/ContactModel.cs`, `Models/BusinessDetailsModel.cs`.

- [ ] **Step 1: Shared input model.** `ContactModel { Id, Name, Email, Title, Active, List<MessageCategory> Categories }` with `Validate()` returning localized errors. `AllCategories` = every flag except `None`, with locale labels.
- [ ] **Step 2: Admin AJAX.** `ContactList(customerId)` → partial table; `ContactSave(customerId, ContactModel)` → JSON `{ success, errors }`; `ContactDelete(id)`. Require `CanEditCustomersAsync()`. Check `contact.CustomerId == customerId` on save/delete. JS posts with nopCommerce's `addAntiForgeryToken(data)` helper and refreshes the table; edit opens an inline row form.
- [ ] **Step 3: Routes.**

```csharp
endpointRouteBuilder.MapControllerRoute(CustomerProfileDefaults.RouteBusinessDetails, "customer/business-details",
    new { controller = "CustomerProfilePublic", action = "BusinessDetails" });
endpointRouteBuilder.MapControllerRoute(CustomerProfileDefaults.RouteContactEdit, "customer/business-details/contact/{id:int}",
    new { controller = "CustomerProfilePublic", action = "ContactEdit" });
endpointRouteBuilder.MapControllerRoute(CustomerProfileDefaults.RouteTaxExemption, "customer/tax-exemption",
    new { controller = "CustomerProfilePublic", action = "TaxExemption" });
endpointRouteBuilder.MapControllerRoute(CustomerProfileDefaults.RouteCertificateFile, "customer/tax-exemption/file/{id:int}",
    new { controller = "CustomerProfilePublic", action = "CertificateFile" });
```

- [ ] **Step 4: Public controller.** `[AutoValidateAntiforgeryToken]`, `BasePluginController`. Every action starts with: current customer is registered, else `Challenge()`. Pages use the account layout (`Layout = "_ColumnsTwo"` with the customer navigation, as core's `Info.cshtml` does).
  - `BusinessDetails` GET: dealership field + contacts list + "Add contact" form. POST `BusinessDetails` (dealership) and POST `ContactAdd`.
  - `ContactEdit` GET/POST, `ContactDelete` POST; every contact lookup checks `contact.CustomerId == currentCustomer.Id`, else 404.
  - Success messages through `INotificationService`.
- [ ] **Step 5: Account navigation.** `AccountNavigationViewComponent` renders two `<li>` links (Business details, Tax exemption) matching the theme's `CustomerNavigation` markup; mark the active one by comparing the current route name. Registered customers only.
- [ ] **Step 6: Registration field.** `RegisterDealershipViewComponent` renders:

```html
<div id="customer-profile-register" class="fieldset" style="display:none">
  <div class="title"><strong>@T("Plugins.Misc.CustomerProfile.Register.Title")</strong></div>
  <div class="form-fields"><div class="inputs">
    <label for="cp-dealer">@T("Plugins.Misc.CustomerProfile.Dealership")</label>
    <input id="cp-dealer" name="@CustomerProfileDefaults.FieldDealership" maxlength="200" />
  </div></div>
</div>
<script asp-location="Footer">
  $(function () {
    // RegisterBottom renders after </form>; move the field inside so it posts
    var block = $('#customer-profile-register');
    var form = $('.registration-page form').first();
    if (!form.length) return;
    var buttons = form.find('.buttons').first();
    buttons.length ? block.insertBefore(buttons) : block.appendTo(form);
    block.show();
  });
</script>
```

`CustomerRegisteredEvent` consumer reads the field with `TryGetFormValueAsync` and saves the profile when non-empty.

- [ ] **Step 7: Manual check.** Register with a dealership → saved. Business details: edit dealership, add two contacts, edit categories, pause one, delete one. Try another customer's contact id in the URL → 404. Admin card shows and edits the same contacts.
- [ ] **Step 8:** Commit: `feat(customer-profile): business details page, contacts, registration field`.

---

### Task 10: Contact copies of customer emails

**Files:** `Services/CustomerProfileWorkflowMessageService.cs`, `Infrastructure/NopStartup.cs`, `$TESTS/WorkflowFanOutTests.cs`.

- [ ] **Step 1: Write `WorkflowFanOutTests` first.** Construct the subclass with Moq'd dependencies (all 18 base ctor arguments plus `ICustomerContactService`). Arrange `IQueuedEmailService.InsertQueuedEmailAsync` to capture emails and `GetQueuedEmailByIdAsync` to return them. Call `SendNotificationAsync` directly inside and outside a fan-out scope (expose `internal Task<T> RunInScopeAsync<T>(int customerId, MessageCategory category, Func<Task<T>> send)` for the test). Assert:
  - outside a scope → 1 email;
  - inside a scope with two subscribed contacts → 3 emails, contact copies have the contact's address and name, same subject/body, same attachment path, **empty Bcc**;
  - contact equal to the primary address → not duplicated;
  - the original email keeps the template's Bcc.
- [ ] **Step 2: Implement.**

```csharp
public class CustomerProfileWorkflowMessageService : WorkflowMessageService
{
    private static readonly AsyncLocal<FanOutScope?> _scope = new();
    private sealed record FanOutScope(int CustomerId, MessageCategory Category);

    private readonly ICustomerContactService _contactService;

    public CustomerProfileWorkflowMessageService(/* the 18 base parameters, same order */, ICustomerContactService contactService)
        : base(/* ... */) => _contactService = contactService;

    internal async Task<T> RunInScopeAsync<T>(int customerId, MessageCategory category, Func<Task<T>> send)
    {
        var previous = _scope.Value;
        _scope.Value = new FanOutScope(customerId, category);
        try { return await send(); }
        finally { _scope.Value = previous; }
    }

    public override async Task<int> SendNotificationAsync(MessageTemplate messageTemplate, EmailAccount emailAccount, int languageId,
        IList<Token> tokens, string toEmailAddress, string toName, string attachmentFilePath = null, string attachmentFileName = null,
        string replyToEmailAddress = null, string replyToName = null, string fromEmail = null, string fromName = null,
        string subject = null, bool ignoreDelayBeforeSend = false)
    {
        var id = await base.SendNotificationAsync(messageTemplate, emailAccount, languageId, tokens, toEmailAddress, toName,
            attachmentFilePath, attachmentFileName, replyToEmailAddress, replyToName, fromEmail, fromName, subject, ignoreDelayBeforeSend);

        var scope = _scope.Value;
        if (scope == null)
            return id;

        var contacts = await _contactService.GetForCustomerAsync(scope.CustomerId, activeOnly: true);
        foreach (var recipient in ContactRecipientResolver.Resolve(contacts, scope.Category, toEmailAddress))
        {
            var copyId = await base.SendNotificationAsync(messageTemplate, emailAccount, languageId, tokens, recipient.Email, recipient.Name,
                attachmentFilePath, attachmentFileName, replyToEmailAddress, replyToName, fromEmail, fromName, subject, ignoreDelayBeforeSend);

            // One store BCC per message, not one per contact
            var copy = await _queuedEmailService.GetQueuedEmailByIdAsync(copyId);
            if (copy != null && !string.IsNullOrEmpty(copy.Bcc))
            {
                copy.Bcc = null;
                await _queuedEmailService.UpdateQueuedEmailAsync(copy);
            }
        }

        return id;
    }

    // One override per customer-facing email; each just opens a scope around base

    public override Task<IList<int>> SendOrderPlacedCustomerNotificationAsync(Order order, int languageId,
        string attachmentFilePath = null, string attachmentFileName = null) =>
        RunInScopeAsync(order.CustomerId, MessageCategory.OrderConfirmations,
            () => base.SendOrderPlacedCustomerNotificationAsync(order, languageId, attachmentFilePath, attachmentFileName));

    // SendOrderProcessingCustomerNotificationAsync  -> OrderConfirmations
    // SendOrderPaidCustomerNotificationAsync        -> InvoicesPayments
    // SendOrderCompletedCustomerNotificationAsync   -> InvoicesPayments
    // SendOrderRefundedCustomerNotificationAsync    -> InvoicesPayments
    // SendOrderCancelledCustomerNotificationAsync   -> Cancellations
    // SendShipmentSentCustomerNotificationAsync     -> Shipping   (customer id via _orderService.GetOrderByIdAsync(shipment.OrderId))
    // SendShipmentReadyForPickupNotificationAsync   -> Shipping
    // SendShipmentDeliveredCustomerNotificationAsync-> Shipping
    // SendNewOrderNoteAddedCustomerNotificationAsync-> OrderNotes (order via orderNote.OrderId)
    // SendNewReturnRequestCustomerNotificationAsync -> Returns    (order parameter)
    // SendReturnRequestStatusChangedCustomerNotificationAsync -> Returns
}
```

Signatures to match exactly are at `WorkflowMessageService.cs` lines 774, 869, 916, 964, 1010, 1060, 1109, 1200, 1382, 1428, 1855, 1912. Re-read them before writing each override.

The *store owner* variants (e.g. `SendOrderPlacedStoreOwnerNotificationAsync`) are not overridden, so no scope is open when they run and nothing is copied.

- [ ] **Step 3: Register.** In `NopStartup.ConfigureServices` (Order 3000, after core's 2000): `services.AddScoped<IWorkflowMessageService, CustomerProfileWorkflowMessageService>();`. The last registration wins.
- [ ] **Step 4:** `sync_in && test_all` → fan-out tests pass.
- [ ] **Step 5: Manual check.** Contact subscribed to Shipping only. Place an order → System → Message queue shows one Order placed email (customer). Mark shipped → two Shipment sent emails (customer, contact), contact's has no Bcc. Order placed with "attach PDF invoice" on → contact copy (when subscribed to Order confirmations) carries the PDF. Password recovery → never copied.
- [ ] **Step 6:** Commit: `feat(customer-profile): copy customer emails to subscribed contacts`.

---

### Task 11: The plugin's own emails

**Files:** `Services/ICustomerProfileMessageService.cs` + `CustomerProfileMessageService.cs`.

- [ ] **Step 1:** Methods:
  - `SendCertificateSubmittedAsync(cert)` → owner
  - `SendCertificateReviewedAsync(cert)` → customer + TaxExemption contacts
  - `SendExpiringAsync(cert)` → customer + contacts, and owner
  - `SendExpiredAsync(cert, exemptionRemoved)` → customer + contacts, and owner
  - `SendPendingNudgeAsync(cert)` → owner
  - `SendNet30OverdueAsync(net30Order, order)` → customer + InvoicesPayments contacts, CC owner list
- [ ] **Step 2: Mechanics.** For each send: load active templates by name for the store (`GetMessageTemplatesByNameAsync(name, storeId)`, skip inactive); language = customer's language or store default; email account = template's account or default; tokens = `AddStoreTokensAsync` + `AddCustomerTokensAsync` + the plugin tokens below; publish `MessageTokensAddedAsync`; then `IWorkflowMessageService.SendNotificationAsync`.
  - Customer sends go to `customer.Email`, then once per `ContactRecipientResolver.Resolve(contacts, category, customer.Email)` recipient.
  - Owner sends go to `emailAccount.Email` / `emailAccount.DisplayName`; then load the queued email and set `CC` to the validated `NotificationCcEmails` joined with `;`. The send task splits CC on `;` only (`QueuedMessagesSendTask.cs:17`), so split the setting on `,` and `;`, trim, drop invalid entries, and re-join with `;`.
- [ ] **Step 3: Tokens.** `%TaxExemption.ExpiresOn%`, `%TaxExemption.DaysLeft%`, `%TaxExemption.State%`, `%TaxExemption.ReviewNote%`, `%TaxExemption.CertificateNumber%`, `%TaxExemption.UploadUrl%` (storefront absolute URL to `customer/tax-exemption`), `%TaxExemption.AdminCustomerUrl%` (`{store}Admin/Customer/Edit/{id}`), `%Customer.BmwDealership%`, `%Order.Net30DueDate%`, `%Order.Net30Terms%`. Also add `%Customer.BmwDealership%` to every message with customer tokens via the `MessageTokensAddedEvent` consumer from Task 8.
- [ ] **Step 4:** Build. Commit: `feat(customer-profile): plugin message service`.

---

### Task 12: Tax exemption certificates

**Files:** `TaxExemptionService` write methods; admin AJAX actions and the card's certificate section; public `TaxExemption` page and `CertificateFile`; `Views/Public/TaxExemption.cshtml`.

**Prerequisite:** Task 0 passed, or its follow-up task is done.

- [ ] **Step 1: Service writes.**
  - `UploadAsync(customerId, IFormFile file, expiresOn?, certNumber?, issuingState?, uploadedBy, approveImmediately)`: read bits via `IDownloadService.GetDownloadBitsAsync`; `UploadValidator.Validate`; insert `Download { DownloadGuid = Guid.NewGuid(), UseDownloadUrl = false, DownloadBinary = bytes, ContentType = validatedType, Filename = name without extension, Extension = ext, IsNew = true }`; insert certificate `PendingReview`, or go through `ApproveAsync` when `approveImmediately` (admin upload). Customer upload → `SendCertificateSubmittedAsync`.
  - `ApproveAsync(certId, expiresOn, reviewerId, note)`: require `expiresOn >= clock.Today`; mark every other `Approved` certificate for the customer `Superseded`; set `Approved`, review fields, `ExpiresOn`, clear reminder stamps; if `ApproveMarksTaxExempt` set `customer.IsTaxExempt = true` and update; `SendCertificateReviewedAsync`.
  - `RejectAsync(certId, reviewerId, note)`: note required; `Rejected`; `SendCertificateReviewedAsync`.
  - `ExpireAsync(certId, removeExemption)`: `Expired`; if `removeExemption` set `IsTaxExempt = false` and append to `customer.AdminComment`: `"{date}: tax exemption removed automatically, certificate #{id} expired {ExpiresOn:d}."`; `SendExpiredAsync`; stamp `ExpiredNoticeSentOnUtc`.
- [ ] **Step 2: Admin card section.** Certificate table: uploaded date, uploaded by (customer/admin), state badge, expires, cert #/state, actions: **View** (opens `Admin/CustomerProfileAdmin/CertificateFile/{id}` in a new tab), **Approve** (inline date input, prefilled from the customer's entry), **Reject** (note). Upload form: file + expiry + cert # + issuing state, posted as `FormData` via AJAX.
  - **Gotcha:** the core form's *Is tax exempt* checkbox is on the same page. After a successful approve that set `IsTaxExempt`, the JS must also tick `#IsTaxExempt`; otherwise clicking the core Save afterwards writes `false` back. Same in reverse is not needed (reject never changes the flag).
- [ ] **Step 3: Admin file action.** `CertificateFile(id)`: require `CanViewCustomersAsync()`; load certificate and download; `return File(download.DownloadBinary, cert.ContentType, cert.FileName)`. Add `Response.Headers["X-Content-Type-Options"] = "nosniff"`.
- [ ] **Step 4: Public page.** `TaxExemption` GET: current status, expiry, upload form (file + optional expiry date + cert # + issuing state), history table with state and any rejection note. POST `TaxExemptionUpload` with `[RequestSizeLimit]` set from settings (+ small overhead). `CertificateFile(id)`: certificate must belong to the current customer, else 404 (not 403, to avoid confirming ids exist).
- [ ] **Step 5: Manual check.** Customer uploads a PDF → owner email queued (with CC list) → admin sees pending → approve with date → customer `IsTaxExempt` true, page checkbox ticked, Reviewed email to customer and TaxExemption contacts → checkout shows zero tax. Upload a renamed `.exe` → rejected with a message. Another customer requests `/customer/tax-exemption/file/{id}` → 404. Logged-out → login page.
- [ ] **Step 6:** Commit: `feat(customer-profile): tax exemption certificates`.

---

### Task 13: Daily task and expiry banner

**Files:** `Services/DailyTask.cs`, `Components/ExpiryBannerViewComponent.cs`, `Views/Public/ExpiryBanner.cshtml`.

- [ ] **Step 1: Task.**

```csharp
public class DailyTask : IScheduleTask
{
    public async Task ExecuteAsync()
    {
        var today = _clock.Today;
        var utcNow = _clock.UtcNow;

        var certs = await _taxExemptionService.GetForPlanningAsync(); // Approved + PendingReview only
        foreach (var action in ExpiryPlanner.Plan(certs.Select(ToSnapshot).ToList(), today, utcNow,
                     _settings.ReminderDaysBeforeExpiry, _settings.PendingReviewNudgeDays, _settings.RemoveExemptionOnExpiry))
        {
            try
            {
                switch (action.Kind)
                {
                    case ExpiryActionKind.SendReminder: await _taxExemptionService.SendReminderAsync(action.CertificateId); break;
                    case ExpiryActionKind.Expire: await _taxExemptionService.ExpireAsync(action.CertificateId, action.RemoveExemption); break;
                    case ExpiryActionKind.NudgePendingReview: await _taxExemptionService.SendPendingNudgeAsync(action.CertificateId); break;
                }
            }
            catch (Exception ex)
            {
                // One bad row must not stop the rest; the stamp isn't written, so it retries next run
                await _logger.ErrorAsync($"Customer profile: {action.Kind} failed for certificate {action.CertificateId}", ex);
            }
        }

        var net30 = await _net30Service.GetUnpaidSnapshotsAsync();
        foreach (var orderId in OverduePlanner.OrdersToRemind(net30, today, _settings.Net30OverdueReminderEnabled))
        {
            try { await _net30Service.SendOverdueAsync(orderId); }
            catch (Exception ex) { await _logger.ErrorAsync($"Customer profile: overdue reminder failed for order {orderId}", ex); }
        }
    }
}
```

`SendReminderAsync` / `SendPendingNudgeAsync` / `SendOverdueAsync` send then stamp, in that order: a crash between them re-sends once rather than never sending.

- [ ] **Step 2: Banner.** On `AccountNavigationBefore`, for the current customer: if their latest approved certificate is within the reminder window, show "Your tax exemption certificate expires on {date}. Upload a new one" linking to the Tax exemption page; if they have an expired one and no valid one, show "Your tax exemption certificate expired on {date}…". Nothing otherwise. Cache nothing; it's one indexed query on account pages only.
- [ ] **Step 3: Manual check.** Approve a certificate expiring in 10 days → System → Schedule tasks → Run now → reminder emails queued (customer, contacts, owner with CC), banner shows. Run again → no duplicates. Set `ExpiresOn` to yesterday in the database → run → state Expired, `IsTaxExempt` false, admin comment appended, Expired emails queued. Net 30 order with `DueOn` yesterday and overdue reminders on → one overdue email; off → none.
- [ ] **Step 4:** Commit: `feat(customer-profile): daily reminders, expiry, overdue, banner`.

---

### Task 14: Tax exemptions admin grid

**Files:** admin `TaxExemptions` action + list JSON action, `Views/Admin/TaxExemptions.cshtml`.

- [ ] **Step 1:** Grid like `AdminEstimateList.cshtml`. Filters: state (Pending review default, Approved, Expiring within N days, Expired, Rejected, All), dealership contains, customer email/name contains. Columns: customer (link to customer edit, which holds the actions), dealership, state, expires, uploaded, uploaded by. Default sort: pending oldest first, then expiring soonest.
- [ ] **Step 2:** Manual check with a mix of states. Commit: `feat(customer-profile): tax exemptions admin grid`.

---

### Task 15: Documentation, packaging, repo

- [ ] **Step 1: `CustomerProfile/Readme.txt`.** Install steps; settings; how Net 30 works (Mark as paid, offline refund); adding `%Order.Net30DueDate%` to core templates; contact categories table; that uninstall deletes certificates and contacts; that tax exemption stays nopCommerce's own flag; TaxJar result from Task 0.
- [ ] **Step 2: `CustomerProfile/TESTING.md`.** Collect the manual checks from Tasks 7–14 into one script, in the Handling Fee `TESTING.md` style.
- [ ] **Step 3: `README.md`.** Add the table row (`Misc.CustomerProfile`, `— / 1.0.0`) and a short section in the style of the others. Update the "Five plugins" sentence.
- [ ] **Step 4: Package.** `build` in Release, then copy `$NOP/src/Presentation/Nop.Web/Plugins/Misc.CustomerProfile/` to `$PROJ/CustomerProfile/nopCommerce 4.90/Misc.CustomerProfile/` (compiled folder is committed, zips are not). Confirm it contains the DLL, `plugin.json`, `logo.png`, `Views/`, `Content/` and no nopCommerce assemblies.
- [ ] **Step 5:** `test_all` one last time. Commit: `docs(customer-profile): readme, test script, package 1.0.0`.
- [ ] **Step 6:** Staging deploy: upload, install, run `TESTING.md` end to end, including the TaxJar zero-tax check with a certificate-driven exemption.

---

## Risks to watch during implementation

- **`WorkflowMessageService` constructor drift.** The subclass repeats core's 18 constructor parameters. A nopCommerce patch release that changes them breaks the plugin at startup. Readme notes: re-check after nopCommerce upgrades.
- **Theme markup.** The registration field script looks for `.registration-page form` and `.buttons`. A custom theme may differ; the field then stays hidden and customers use Business details.
- **Core form overwriting `IsTaxExempt`.** Covered by the checkbox sync in Task 12 Step 2; test it explicitly.
