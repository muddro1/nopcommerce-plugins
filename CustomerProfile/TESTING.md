# Customer profile plugin — staging test script

**Plugin:** Misc.CustomerProfile 1.0.8, built against nopCommerce 4.90
**Purpose:** confirm on a real store what the automated tests could only confirm against mocks.

The 49 automated tests cover the decision rules (expiry, overdue, which contacts get a copy,
upload checks) and the contact-copy mechanics against mocked nopCommerce services. Nothing
has run against a real database, real checkout or TaxJar. This script closes that gap.

**Run this on staging, not production.** Take a database backup first. Budget about an hour.

You need: an admin login, two registered test customers (**A** and **B**) with real mailboxes
or a mail catcher, a test PDF, and a renamed non-image file (e.g. a `.txt` renamed `form.pdf`).
Check emails under **System → Message queue** rather than waiting for delivery.

---

## Part 1 — Install

1. Copy `Misc.CustomerProfile` into `\Plugins`, **Configuration → Local plugins → Reload list
   of plugins → Install**.
2. Check:
   - [ ] **Configuration → Widgets** and **Configuration → Payment methods** both list the plugin as active.
   - [ ] **Content management → Message templates**: eight templates starting `CustomerProfile.`.
   - [ ] **System → Schedule tasks**: "Customer profile: tax exemption and Net 30 reminders", every 14400 seconds.
   - [ ] **FMS Plugins → Customer Profile** has Tax exemptions, Net 30 orders, Settings, and each page opens.
   - [ ] In the database, the tables `CustomerBusinessProfile`, `CustomerContact`, `TaxExemptionCertificate` and `Net30Order` exist.
   - [ ] **FMS Plugins → Customer Profile → Net 30 orders** and **Tax exemptions** open without an error.
3. **Settings**: put your own address in *CC on store notifications*. Save. Reopen: it stuck.

**Upgrading a store that already runs 1.0.7 or earlier** (instead of step 1): upload the new zip over it and restart.
   - [ ] `TaxExemptionCertificate` has an `ExemptionReasonId` column; existing certificates show *Not given*.
   - [ ] The "certificate to review" template (`CustomerProfile.TaxExemption.Submitted.StoreOwnerNotification`) has a
     "Tax exempt category: %TaxExemption.Reason%" line, unless it had been edited.
   - [ ] An existing pending certificate can't be approved until a category is chosen.

## Part 2 — Dealership and admin card

1. Admin → Customers → edit **A**. A **Business profile** card is near the bottom.
2. Enter a dealership, tick **Net 30 enabled**, **Save and continue**.
   - [ ] Both are still there after reload.
3. Untick every customer role (forcing a core validation error), change the dealership and untick Net 30, Save.
   - [ ] The page shows the core error. Reload without saving: dealership is the **old** value and Net 30 still ticked.
4. Log in as a vendor admin, if you have one.
   - [ ] No Business profile card, no FMS Plugins → Customer Profile items.
5. Log in to the storefront as **A** → My Account.
   - [ ] Menu has **Business details** and **Tax exemption**.
   - [ ] Business details shows the dealership; change it, Save, the admin card shows the new value.
6. Log out, register a new customer, fill in the **Business details** dealership field on the registration page.
   - [ ] The field appears inside the form, above the Register button.
   - [ ] The new customer's admin card shows the dealership.

## Part 3 — Tax exemption and TaxJar

1. As **A**, My Account → Tax exemption, upload the renamed `.txt` file.
   - [ ] Refused: "Please upload a PDF, JPG or PNG file."
2. Upload the test PDF with no tax exempt category chosen.
   - [ ] Refused: "Choose a tax exempt category." The page keeps the form.
   Choose **Wholesale or resale exempt**, set an expiration date ~10 days out, and upload.
   - [ ] "Thank you…" message; history shows *Pending review* and *Wholesale or resale exempt*.
   - [ ] Message queue: "certificate to review" email to the store account, with your CC address and a "Tax exempt category: Wholesale or resale exempt" line.
3. Admin → customer A → card: the certificate is listed *Pending review*. Click **View**.
   - [ ] The PDF opens.
4. Copy the View link. Log in to the storefront as **B** and open A's storefront file link
   (`/customer/tax-exemption/file/{id}`).
   - [ ] 404. Logged out: redirected to login.
5. Admin: the pending row's category drop-down shows *Wholesale or resale exempt*. Change it to
   **Government entity exempt** and **Approve** with the date prefilled.
   - [ ] The certificate row and the status line show *Government entity exempt*, as does A's history.
   - [ ] The core *Is tax exempt* checkbox on the page ticks itself.
   - [ ] Click the page's **Save**. Reload: *Is tax exempt* is still ticked.
   - [ ] Reviewed email to A.
6. **TaxJar check:** as A, add a taxable product, ship to a taxable address, go through checkout.
   - [ ] Cart tax $0.00
   - [ ] Checkout confirm tax $0.00
   - [ ] Placed order tax $0.00 (admin order page and PDF invoice)
   - If any shows tax, **stop**: TaxJar isn't respecting "Is tax exempt" and needs follow-up before go-live.
7. As **A**, upload a second certificate, then try to upload a third.
   - [ ] After the second, the upload form is replaced by "waiting for review"; posting a third anyway is refused.
8. Admin **Rejects** the second certificate with no note.
   - [ ] Refused: a note is required. Add a note, Reject: A gets the email with the note, A's history shows it.

## Part 4 — Reminders and expiry

1. **System → Schedule tasks → Run now** on the Customer profile task.
   - [ ] (Certificate from part 3 expires within 30 days.) Message queue: expiring email to A and to the store with CC.
   - [ ] A's My Account menu shows the orange expiring notice.
2. Run the task again.
   - [ ] No new emails.
3. In the database, set that certificate's `ExpiresOn` to yesterday:
   `UPDATE TaxExemptionCertificate SET ExpiresOn = DATEADD(day, -1, CAST(GETDATE() AS date)) WHERE Id = <id>;`
   Run the task.
   - [ ] Certificate shows *Expired*; A's *Is tax exempt* is unticked; A's admin comment has a line explaining why.
   - [ ] Expired emails to A and to the store. The store's says tax exemption was removed.
   - [ ] A's account pages show the expired notice; checkout charges tax again.
4. Leave a fresh upload pending, set its `UploadedOnUtc` to 4 days ago, run the task.
   - [ ] One "still waiting for review" email to the store. Running again sends nothing.

## Part 5 — Net 30

1. As **B** (Net 30 off), check out.
   - [ ] No "Net 30" payment method.
2. Admin → B → card: tick **Net 30 enabled**, terms 30, Save. Check out as B again.
   - [ ] "Net 30" listed, description "Pay within 30 days of invoice".
   - [ ] Place the order. Payment status *Pending*. Order notes include "Net 30: payment due {date}" (visible to customer).
   - [ ] B's storefront order details show "Payment due {date}".
   - [ ] FMS Plugins → Net 30 orders lists it.
3. Admin: **Mark as paid**.
   - [ ] Gone from Net 30 orders; due line gone from the storefront order page.
4. Start a checkout as B up to the confirm step, untick Net 30 for B in admin, then confirm the order.
   - [ ] Order refused: "Net 30 is not available on this account…"
5. Mark a Net 30 order paid, then **Refund (offline)** it.
   - [ ] It doesn't reappear in Net 30 orders; B's order page shows no "payment due" line.
6. Turn on *Email overdue Net 30 reminders*. Place another Net 30 order, set its `Net30Order.DueOn` to yesterday, run the task.
   - [ ] One overdue email to B, CC the store. Running again sends nothing. Overdue row is red in the list.

## Part 6 — Additional contacts

1. As **A**, Business details → add contact "AP", subscribed to **Shipping updates** only.
   Add contact "Accounts", subscribed to **Invoices and payments**, then untick its Active.
2. Place an order as A.
   - [ ] Message queue: Order placed email to A only.
3. Admin: ship the order (mark shipped).
   - [ ] Two "Shipment sent" emails: A, and AP. Same subject and body. AP's has no BCC even if the template has one.
4. Mark the order paid.
   - [ ] Only A gets "Order paid" (Accounts is paused).
5. Tick Accounts active again, add a refund or another payment event.
   - [ ] Accounts now gets the Invoices and payments email.
6. Trigger a password recovery for A.
   - [ ] Only A gets it.
7. As **B**, open `/customer/business-details/contact/{id of A's contact}`.
   - [ ] 404.
8. Admin card: edit AP's categories (pencil, change the ticks, **Save contact**), delete Accounts.
   - [ ] A's Business details reflects both changes.
9. Admin card: pencil on AP, change its categories, then press the page's **Save and continue** instead of Save contact.
   - [ ] After the reload AP shows the new categories.
10. Admin card: pencil on AP, tick three or more categories, **Save contact**, then press F5.
   - [ ] AP lists every ticked category, not only the first, one per line.
   - [ ] A's Business details page lists AP's categories one per line too.

## Part 7 — Export

1. **FMS Plugins → Customer Profile → Settings → Export (zip)**.
   - [ ] The zip has `certificates.csv`, `business-profiles.csv`, `contacts.csv`, `net30-orders.csv` and a `certificates/` folder.
   - [ ] Each certificate file opens; its path matches the `File` column.
   - [ ] The CSVs open in Excel with names and dealerships readable. A dealership typed as `=1+1` shows as text, not 2.
2. **Tax exemptions**, filter *Expired*, **Export (zip)**.
   - [ ] `certificates.csv` and the folder hold only expired certificates.

## Part 8 — Uninstall keeps the data (on a staging copy)

1. Note how many rows `TaxExemptionCertificate` and `CustomerContact` have. **Uninstall** the plugin.
   - [ ] The four tables still exist with the same row counts; the certificates' `Download` rows are still there.
   - [ ] Message templates and the schedule task are gone.
2. **Install** again.
   - [ ] Customer A's card shows the same dealership, contacts and certificates; their files open.
   - [ ] Settings are back to defaults (re-enter the CC list).
3. Settings → **Delete all plugin data** with the box empty.
   - [ ] "Nothing was deleted".
4. Type `DELETE`, submit.
   - [ ] The message gives the counts; the four tables are empty; the certificates' `Download` rows are gone.
   - [ ] Customer A's *Is tax exempt* is unchanged.
