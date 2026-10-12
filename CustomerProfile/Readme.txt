Customer profile
================
For nopCommerce 4.90 (version 1.0.2).

Extends the customer record for B2B customers:

  - a BMW dealership field
  - tax exemption certificates, uploaded by the customer or an admin, with an
    expiration date, review, reminders, and automatic removal of tax
    exemption when a certificate lapses
  - a Net 30 payment method that only customers you switch on can see
  - additional contacts per customer, each choosing which kinds of order
    email they receive a copy of

  System name: Misc.CustomerProfile


Installing
----------
Copy the Misc.CustomerProfile folder into \Plugins, then Configuration >
Local plugins > Reload list of plugins > Install. Installing also:

  - activates the plugin as a widget and as a payment method. Net 30 stays
    invisible at checkout until you switch it on for a customer.
  - creates eight message templates (Content management > Message
    templates, all starting "CustomerProfile.")
  - creates the schedule task "Customer profile: tax exemption and Net 30
    reminders" (System > Schedule tasks), which runs every 4 hours

Settings: FMS Plugins > Customer Profile > Settings.


Where things are
----------------
Admin
  Customers > edit a customer > "Business profile" card
      Dealership, Net 30 switch and terms, tax exemption certificates
      (view, approve, reject, upload), additional contacts.
      Dealership and Net 30 save with the page's Save button. Certificate
      and contact buttons save immediately.
  FMS Plugins > Customer Profile > Tax exemptions
      Certificates by status: pending review (the work queue), expiring
      soon, expired, rejected.
  FMS Plugins > Customer Profile > Net 30 orders
      Unpaid Net 30 orders, soonest due first, overdue rows in red.

Storefront (My Account menu, registered customers only)
  Business details   Dealership and additional contacts
  Tax exemption      Status, upload, certificate history
  Registration page  Optional dealership field


Tax exemption
-------------
The plugin does not add its own tax-exempt flag. It uses nopCommerce's
"Is tax exempt" checkbox on the customer, which tax calculation already
honours. The checkbox stays admin-only.

  1. The customer uploads a certificate (PDF, JPG or PNG) on My Account >
     Tax exemption, optionally entering its expiration date. Nothing about
     their tax status changes yet. You get an email. While it waits for
     review they can't upload another.
  2. On the customer's Business profile card you open the file, set or
     correct the expiration date, and Approve, or Reject with a note the
     customer sees. Approving replaces any previous approved certificate
     and, with "Approving marks the customer tax exempt" on (default),
     ticks "Is tax exempt". The customer and their Tax exemption contacts
     are emailed either way.
     You can also upload a certificate yourself from the card; it is
     approved straight away and needs an expiration date.
  3. Reminder days before expiry (default 30): the customer, their
     contacts and you are emailed once. Their account pages show a notice.
  4. The day after the expiration date the certificate is marked Expired.
     With "Remove tax exemption on expiry" on (default), "Is tax exempt"
     is unticked, unless another valid certificate is on file, and a line
     is added to the customer's admin comment saying why. Everyone is
     emailed again. If that email fails, the next run of the task sends it.

A certificate is valid through the whole of its expiration date, in the
store's time zone (Configuration > Settings > General settings).

Certificates are kept as history. They are never overwritten; a replaced
one shows as "Replaced". Files are stored in the database (the Download
table) and can only be opened by the customer who uploaded them or by an
admin with "Customers. View".

Your notifications go to the store's default email account, with the
addresses in "CC on store notifications" copied.

TaxJar: check on staging that an exempt customer is charged $0 tax at cart,
checkout and on the placed order. nopCommerce asks the active tax plugin for
the order's tax, so it is up to the TaxJar plugin whether it respects
"Is tax exempt". See TESTING.md, part 3.


Net 30
------
Switch it on per customer on the Business profile card, with their terms in
days (default from settings, 30).

  - Only those customers see "Net 30" at checkout ("Pay within N days of
    invoice"). Guests never do.
  - The order is placed with payment status Pending. Its due date (order
    date + terms) is saved, added as an order note the customer can see,
    and shown on their order details page.
  - When the invoice is paid, use nopCommerce's own "Mark as paid" on the
    order. Refunds: "Refund (offline)".
  - Optional overdue reminder (setting, off by default): one email to the
    customer and their "Invoices and payments" contacts when an order passes
    its due date unpaid, copied to you.
  - "Unpaid" means payment status Pending. Paid, refunded, partially
    refunded, voided and cancelled orders drop off the Net 30 list and never
    get an overdue reminder.

To show the due date in nopCommerce's own order emails, add
%Order.Net30DueDate% (and %Order.Net30Terms% for the number of days) to the
"Order placed" customer template. Both are blank on non-Net 30 orders. The
plugin does not edit core templates. %Customer.BmwDealership% is also
available on order emails.


Additional contacts
-------------------
Name, email, optional job title, and the kinds of email they get. They do
not get a login. Up to 20 per customer. Customers manage them on Business
details; admins on the Business profile card. Untick Active to pause one.

  Order confirmations     Order placed, order processing
  Invoices and payments   Order paid, order completed, order refunded,
                          Net 30 overdue reminder
  Shipping updates        Shipment sent, ready for pickup, delivered
  Cancellations           Order cancelled
  Order notes             Order notes you send to the customer
  Returns                 Return request submitted, status changed
  Tax exemption           Certificate reviewed, expiring, expired

Each contact gets their own copy of exactly the email the customer got,
including an attached PDF invoice, so contacts never see each other's
addresses. If the template BCCs the store, only the customer's copy keeps
the BCC. The customer's own email is unchanged. Account emails (password
recovery, welcome, email validation) are never copied.

How: the plugin replaces nopCommerce's workflow message service with a
subclass. After upgrading nopCommerce, re-check that the plugin still
starts and that contact copies still arrive, because the subclass repeats
core's constructor. Another plugin that also replaces that service would
conflict with this one.


Upgrading
---------
Upload the new version over the installed one (Local plugins > Upload plugin
or theme) and restart. Don't uninstall first: that deletes every certificate.

1.0.2 adds one column to TaxExemptionCertificate, created on upgrade.

From 1.0.0:
1.0.0 created no tables on stores that also run Estimate Builder: its
install step had the same migration timestamp as one of Estimate Builder's,
and nopCommerce skipped it as already done. The admin lists then failed with
"Invalid object name 'Net30Order'" or "'TaxExemptionCertificate'".

Upgrading to 1.0.1 or later creates any missing tables. Settings,
templates and the schedule task from 1.0.0 are kept. Don't install 1.0.0
anywhere else: on a store whose Estimate Builder is older than 1.2.0, 1.0.0
records a migration that makes Estimate Builder's later upgrade skip adding
its Note column.


Uninstalling
------------
Deletes the plugin's tables - dealerships, Net 30 settings and due dates,
contacts, and every tax exemption certificate including the files - plus
its settings, message templates and schedule task. Customers' "Is tax
exempt" values and Net 30 orders themselves are left as they are.
Download any certificates you need to keep first.


Known limitations
-----------------
  - One active certificate per customer, not one per state.
  - No credit limit on Net 30.
  - The registration dealership field is moved into the form by a script
    that looks for ".registration-page form". A theme with different markup
    hides the field; customers can still fill it in on Business details.
