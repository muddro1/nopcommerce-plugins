# Customer profile plugin — design

**Date:** 2026-10-12
**Target:** nopCommerce 4.90, net9.0
**Plugin:** `Nop.Plugin.Misc.CustomerProfile`, system name `Misc.CustomerProfile` (working name)
**Status:** draft, decisions recorded; one item (TaxJar) to verify

## Purpose

Extend the customer record for B2B customers with five things nopCommerce does
not do on its own:

1. A **BMW dealership** field on the customer.
2. A **tax exemption form** the customer or an admin uploads and ties to the
   account, with an **expiration date**.
3. **Reminders** before and when that form expires, to the customer and to us,
   and automatic removal of tax exemption when it lapses.
4. Two admin-only switches: **tax exempt** and **Net 30** (pay on account).
5. **Secondary contacts** (name + email) per customer, each choosing which kinds
   of emails they receive.

## Decisions

| Topic | Decision |
|---|---|
| Dealership | Free text. The customer sets it themselves (My Account and registration); admins can edit it too. |
| Open payment | Net 30: the order is placed now, invoiced, paid within terms. The plugin provides its own payment method (see 4). |
| Expired form | Tax exemption switches off automatically on the expiry date. |
| Tax provider | TaxJar (not Avalara). See "To verify" below. |
| Our notifications | Store owner email, plus an optional list of CC addresses in settings. |
| Secondary contacts | Added by the customer or an admin; each picks which message categories they get. |
| Certificates | One active form per customer; history kept. |

## To verify

**V1. Does our TaxJar plugin honour `Customer.IsTaxExempt`?** In 4.90 the order
tax total comes from the tax provider's own `GetTaxTotalAsync`
(`TaxService.cs:750`). The built-in providers go through
`TaxService.IsTaxExemptAsync`, which checks the flag, but a TaxJar plugin that
sends the cart to TaxJar's API decides for itself. Test on staging: tick
*Is tax exempt* on a customer, build a cart to a taxable address, and check the
cart, checkout and placed order all show zero tax. If they don't, the plugin
either needs a TaxJar-side exemption sync (TaxJar's customer API with
`exemption_type`) or the TaxJar plugin needs a fix. Which TaxJar plugin and
version is installed?

**V2. Resolved:** no other installed plugin touches email, so decorating
`IWorkflowMessageService` (see 5) has no conflict.

## What nopCommerce already gives us

Verified against the local 4.90 source tree.

- **`Customer.IsTaxExempt`** already exists, is an admin-only checkbox on the
  customer edit page, and `TaxService.IsTaxExemptAsync` honours it. The plugin
  reads and writes this flag rather than adding its own.
- **`IPaymentMethod.HidePaymentMethodAsync(cart)`** lets a payment method hide
  itself per customer at checkout.
- **`AdminWidgetZones.CustomerDetailsBlock`** renders a widget as its own card on
  the admin customer edit page, inside the main customer form.
- **`ModelReceivedEvent<BaseNopModel>`** fires when the admin customer form is
  posted, so the plugin can save its card's fields on the page's normal Save
  button (Avalara and PayPal Commerce do the same).
- **`PublicWidgetZones.AccountNavigationAfter`** for the My Account menu and
  **`RegisterBottom`** for registration. Note: `RegisterBottom` and
  `CustomerInfoBottom` render *after* their page's `</form>`
  (`Register.cshtml:456-460`, `Info.cshtml:487-488`), so a field placed there
  does not post with the form.
- **`WorkflowMessageService`**: every customer-facing email has its own public
  virtual method (`SendOrderPlacedCustomerNotificationAsync`,
  `SendShipmentSentCustomerNotificationAsync`, …) and they all funnel through
  the public virtual `SendNotificationAsync` (`WorkflowMessageService.cs:2891`).
  Registered as scoped in `Nop.Web.Framework/Infrastructure/NopStartup.cs:192`.
- **`Download` table / `IDownloadService`** for storing uploaded files in the
  database.
- **Schedule tasks** and **message templates** for daily checks and editable
  emails.

## The pieces

### 1. BMW dealership

Free text (max 200), stored in the plugin's `CustomerProfile` table (one row per
customer, created on first save) so the admin lists can filter on it.

- Customer: field on the plugin's own My Account → **Business details** page
  (with the contacts list), which posts to the plugin's controller. Optional.
- Registration: the `RegisterBottom` widget renders the field plus a few lines
  of script that move it inside the registration form, before the register
  button. A `CustomerRegisteredEvent` consumer reads it from the posted form.
  If a theme changes the form markup so the script can't find it, the field
  stays hidden and the customer can fill it in later on Business details.
- Admin: field on the plugin's card on the customer edit page, saved with the
  page's Save button.
- Shown in the admin Tax exemptions and Net 30 grids, and available as a
  `%Customer.BmwDealership%` token in emails.

### 2. Tax exemption form

Table `TaxExemptionCertificate`, one row per uploaded form. Old rows are kept,
never overwritten: tax authorities expect certificates retained for years, so
"replace" means "add a new one and supersede the old one."

| Column | Notes |
|---|---|
| `Id` | |
| `CustomerId` | indexed |
| `DownloadId` | the file, in nopCommerce's `Download` table |
| `FileName`, `ContentType` | as uploaded |
| `State` | `PendingReview`, `Approved`, `Rejected`, `Superseded`, `Expired` |
| `ExpiresOn` | date only, valid through the end of that day in the store time zone; nullable while pending |
| `CertificateNumber`, `IssuingState` | optional, admin reference |
| `UploadedByCustomerId`, `UploadedOnUtc` | customer or admin |
| `ReviewedByCustomerId`, `ReviewedOnUtc`, `ReviewNote` | admin review |
| `ReminderSentOnUtc`, `ExpiredNoticeSentOnUtc` | so each notice is sent once |

**Upload.** PDF, JPG or PNG, max size a setting (default 10 MB). The file's first
bytes are checked, not just the extension.

**Customer upload.** My Account → Tax exemption. The customer may enter the
expiration date printed on the form. The row starts `PendingReview` and their
tax status does not change. We get a "new form to review" email.

**Admin review.** On the customer's card the admin opens the file, sets or
corrects the expiration date, and approves or rejects with a note the customer
sees. Approving supersedes the previous approved form and ticks `IsTaxExempt`
(setting "Approve also marks tax exempt", default on). Admins can upload on the
customer's behalf; those rows start `Approved` and still need an expiration
date.

**Tax exempt stays the admin's call.** The `IsTaxExempt` checkbox stays where
nopCommerce puts it and stays admin-only. The card shows beside it whether an
approved, unexpired form is on file, and warns when tax exempt is ticked without
one.

**File access.** Tax forms carry EINs and addresses. Files are served only
through the plugin's controller, which checks the requester is the owning
customer or an admin with *Customers. View*. nopCommerce's public
`/download/getfileupload/{guid}` route is never used for them.

### 3. Expiry and reminders

A daily schedule task (`Misc.CustomerProfile.DailyTasks`, installed with the
plugin, removed on uninstall) runs over `Approved` forms:

1. **Reminder.** Expires within *N* days (setting, default 30) and no reminder
   sent → email the customer and us; stamp `ReminderSentOnUtc`.
2. **Expired.** Expiry date passed → `State = Expired`; unless the customer has
   another valid form, untick `IsTaxExempt` and add a customer note recording
   why; email the customer and us; stamp `ExpiredNoticeSentOnUtc`.
3. **Pending nudge** (setting, default 3 days): forms still `PendingReview`
   after that long → one email to us.

The storefront account pages also show a banner inside the reminder window and
after expiry, linking to the upload page.

"Us" = the default email account's address (where nopCommerce sends every store
owner notification), with the settings CC list (comma-separated) written into
the queued email's CC field.

### 4. Net 30 payment method

The plugin implements `IPaymentMethod` as **"Net 30"** (title editable in
locale resources). The plugin class is both a widget plugin and a payment
method; it must be activated once under Configuration → Payment methods.

- **Who sees it.** `HidePaymentMethodAsync` hides it unless the customer's
  `Net30Enabled` flag is on. Guests never see it.
- **Admin switch.** "Net 30 enabled" checkbox and "Terms (days)" (default 30) on
  the customer card. Admin-only; the customer never sees the switch, only the
  method at checkout.
- **Placing the order.** `ProcessPaymentAsync` returns payment status
  **Pending**, so the order is placed normally and stays unpaid. The plugin
  records `DueOnUtc = order date + terms` in a `Net30Order` table (`OrderId`,
  `TermsDays`, `DueOnUtc`) and adds an order note "Net 30, payment due
  {date}". The due date is available as a token in the order placed email
  and on the order details page via a widget.
- **Getting paid.** The admin uses nopCommerce's existing *Mark as paid* on the
  order. No capture, no card data.
- **Admin grid.** FMS Plugins → Customer Profile → *Net 30 orders*: unpaid Net
  30 orders with customer, dealership, total, due date; overdue rows
  highlighted; filter by overdue.
- **Overdue reminder** (setting, default off): the daily task emails the
  customer (and contacts subscribed to Billing) once when an order passes its
  due date unpaid, with a CC to us.

Credit limits are out of scope for v1. The table and task leave room for a
"block Net 30 when unpaid balance exceeds X" rule later.

### 5. Secondary contacts and message preferences

Table `CustomerContact`:

| Column | Notes |
|---|---|
| `Id`, `CustomerId` | indexed on `CustomerId` |
| `Name`, `Email` | required; email validated |
| `Title` | optional ("Parts manager", "Accounts payable") |
| `Categories` | flags of the message categories below |
| `Active` | lets them pause without deleting |
| `CreatedOnUtc`, `CreatedByCustomerId` | |

Contacts are email recipients only. They do not log in; that would be a
different feature (multiple accounts under one company).

**Message categories.** Each groups nopCommerce's customer emails:

| Category | Emails |
|---|---|
| Order confirmations | Order placed, order processing |
| Invoices & payments | Order paid, order completed, order refunded, Net 30 overdue reminder |
| Shipping | Shipment sent, ready for pickup, delivered |
| Cancellations | Order cancelled |
| Order notes | New order note added (only notes flagged to notify the customer) |
| Returns | Return request submitted, status changed |
| Tax exemption | Expiring, expired, reviewed |

Account emails (password recovery, email validation, welcome, failed logins)
are never copied. They belong to the account holder.

The primary account email keeps getting everything it gets today. Per-category
opt-out for the primary is out of scope for v1.

**Who manages contacts.** The customer, on My Account → Business details (add,
edit, pause, delete, tick categories). Admins, on the customer card. Changes
record who made them (`CreatedByCustomerId`, `UpdatedByCustomerId`,
`UpdatedOnUtc`).

**How the copies are sent.** The plugin registers a subclass of
`WorkflowMessageService` in its `INopStartup` (order after core) that:

1. Overrides each customer-facing `Send…CustomerNotificationAsync` method in the
   table above. Each override sets an async-local "fan-out context" (the
   customer and the category), calls `base`, and clears the context.
2. Overrides `SendNotificationAsync`. When a fan-out context is set, it calls
   `base` for the original recipient, then calls `base` again once for each
   active contact subscribed to that category, with the contact's name and
   email as recipient.

Each copy has the template's BCC cleared after queueing, so a store that BCCs
itself on order emails gets one BCC per order, not one per contact.

So each contact gets **their own copy** of the exact email the customer got:
same template, same tokens, same PDF invoice attachment when the store attaches
one. No CC, so contacts never see each other's addresses. The emails go through
the normal queue and show up under System → Message queue.

The plugin's own emails (tax exemption, Net 30 overdue) use the same lookup
directly.

### 6. Admin screens

- **Customer edit page card** (`CustomerDetailsBlock`): dealership; Net 30
  switch and terms; tax-exemption status with expiry date and the list of forms
  (view, approve, reject, upload); contacts list (add, edit, remove, categories).
  Dealership and Net 30 save with the page's Save button via
  `ModelReceivedEvent`. File upload, review actions and contact edits are AJAX
  calls with their own buttons, since the card sits inside the customer form
  and can't nest a second form.
- **FMS Plugins → Customer Profile**, using the shared `FmsPluginsMenuConsumer`
  pattern:
  - *Tax exemptions*: forms by state (pending review, expiring within 30 days,
    expired), with dealership and customer filters. The working queue.
  - *Net 30 orders*: unpaid Net 30 orders, overdue first.
  - *Settings*: reminder days, pending nudge days, approve marks exempt, CC
    addresses for our notifications, max file size, overdue reminder on/off.

Permissions: viewing needs *Customers. View*; changing customers needs
*Customers. Create, edit, delete*; Net 30 orders grid needs *Orders. View*;
settings need *Manage plugins*. Closed to vendors.

### 7. Storefront screens

Registered customers only. Two new links in the My Account menu
(`AccountNavigationAfter`):

- **Tax exemption**: status, expiry date, upload, history with any rejection
  note.
- **Business details**: dealership, and the secondary contacts with their
  categories.

Order details for a Net 30 order show "Payment due {date}".

## Message templates created on install

All editable under Content management → Message templates.

- `CustomerProfile.TaxExemption.Submitted.StoreOwnerNotification`
- `CustomerProfile.TaxExemption.Reviewed.CustomerNotification`
- `CustomerProfile.TaxExemption.Expiring.CustomerNotification`
- `CustomerProfile.TaxExemption.Expiring.StoreOwnerNotification`
- `CustomerProfile.TaxExemption.Expired.CustomerNotification`
- `CustomerProfile.TaxExemption.Expired.StoreOwnerNotification`
- `CustomerProfile.TaxExemption.PendingReview.StoreOwnerNotification`
- `CustomerProfile.Net30.Overdue.CustomerNotification`

Store owner templates go to the store owner email with the settings CC list.
Tokens: customer name/email/company, dealership, expiry date, days left, review
note, order number, due date, and links to the customer's upload page and the
admin customer page.

## Install / uninstall

- Install: tables (`CustomerProfile`, `TaxExemptionCertificate`,
  `CustomerContact`, `Net30Order`), settings, message templates, schedule task,
  locale resources.
- Uninstall: drops the tables and the `Download` rows they own; removes
  settings, templates and the task. Leaves customers' `IsTaxExempt` values
  alone. Readme warns that forms and contacts are deleted on uninstall.
- Follows the Estimate Builder rule: the Installation migration creates the full
  current schema, and later Update migrations are mirrored into it.

## Repository layout

4.90 only, building against prebuilt nopCommerce assemblies via
`-p:NopRefs=` like Estimate Builder, in `CustomerProfile/nopCommerce 4.90/`
here with `Tests/`, `Readme.txt` and `TESTING.md`.

## Testing

- **Unit (NUnit):**
  - Daily task decisions as a pure function over (forms, Net 30 orders, today,
    settings) → actions. Edge cases: expires today, no expiry date, second valid
    form on file, reminder already sent, overdue already reminded, settings off.
  - Fan-out: given a context and contacts, which recipients get a copy (inactive
    contact, unsubscribed category, duplicate of the primary email, no context
    set → no copies).
  - File validation (magic bytes vs extension, size limit).
- **Manual (`TESTING.md`):**
  - Customer uploads → we get the email → approve → order has zero tax
    **through TaxJar** (V1).
  - Set expiry to yesterday, run the task from System → Schedule tasks →
    exemption removed, customer note added, emails queued.
  - A different customer can't open the file by URL.
  - Net 30 hidden for a customer without the flag; shown with it; order placed
    as Pending with due date note; Mark as paid clears it from the grid.
  - Contact subscribed to Shipping only: ships an order → contact gets the
    shipment email with the same content; places an order → contact gets
    nothing.

## Out of scope for v1

- Multiple concurrent tax certificates (one per state).
- Credit limits on Net 30.
- Logins for secondary contacts.
- Per-category opt-out for the primary account email.
- Sync with TaxJar's exemption API (unless V1 forces it in).

## Suggested build order

1. Skeleton, schema, settings, FMS menu, install/uninstall.
2. Dealership field (admin card, Business details page, registration).
3. Net 30 payment method, customer switch, order due date, grid.
4. Secondary contacts: storage, admin + customer UI, message fan-out.
5. Tax certificate upload, secure download, review, customer page.
6. Daily task, message templates, banners, overdue reminder.
7. Readme, TESTING.md, package.

Contacts come before the tax form so the tax-exemption emails can go to
subscribed contacts from the start.
