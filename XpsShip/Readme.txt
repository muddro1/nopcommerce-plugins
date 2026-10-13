XPS Ship webhooks
=================
For nopCommerce 4.90 (version 2.00). A 4.50 build is planned but not yet made.

Lets XPS Ship (eCommerce Webship) pull ready-to-ship orders from the store
and post each label back. Every label becomes a real nopCommerce shipment
with its tracking number, marked shipped through nopCommerce's own
pipeline. Nobody types addresses or tracking numbers by hand.

  System name: Misc.XpsShip
  Spec: docs/superpowers/specs/2026-09-24-xps-ship-design.md


How it works
------------
XPS polls the store; the store never calls XPS. Three URLs, all HTTPS and
all requiring the secret key in the x-rsis-key header:

  /xps/orders   List Orders: every order ready to ship, as XPS JSON
  /xps/order    Get Order (?orderId=): one order, only lines still to ship.
                XPS calls it for partial shipments.
  /xps/update   Update Order: XPS posts carrier, tracking numbers and the
                items shipped. The plugin creates one shipment, calls
                ShipAsync (shipped date, shipping status, order note,
                "Shipment sent" email), and records the label.

The orderId XPS sees is the order's OrderGuid, and each item's productId is
the order line's OrderItemGuid. XPS echoes these back, so the plugin knows
exactly which line shipped, even when one SKU appears twice.


Installing
----------
Copy the Misc.XpsShip folder into \Plugins, then Configuration > Local
plugins > Reload list of plugins > Install. Installing:

  - creates the tables XpsShip_TrackingMap, XpsShip_OrderFlag and
    XpsShip_WebhookLog (dropped again on uninstall)
  - generates a 32-character secret key
  - creates the schedule task "XPS Ship: purge webhook log" (daily)
  - leaves the integration switched OFF: the URLs answer 503 until you
    enable it

The page is under Sales > XPS Ship and FMS Plugins > XPS Ship, and on the
plugin's Configure button. Viewing needs "Orders. View"; saving, holding,
releasing, voiding and regenerating the key need "Orders. Create, edit,
delete". Vendors never see it.


Connecting XPS
--------------
  1. Settings tab: check the ship-from address and the service keys.
  2. Preview List Orders tab: compare the list with what is waiting to ship.
     It shows the orders left out and why.
  3. Tick Enabled and save.
  4. In eCommerce Webship: gear icon > eCommerce Integrations > Add
     eCommerce Integration > Webhooks. Paste the List Orders URL, Update
     Order URL and secret key from the Settings tab. Turn on "Support
     Partial Shipments" and paste the Get Order URL.
  5. In XPS's service mapping, map each service key to a carrier service.
     The defaults:
       FREE              UPS Ground
       EXP-UPS-NEXTDAY   UPS Next Day Air (or Next Day Air Saver)
       EXP-UPS-2DAY      UPS 2nd Day Air
       INTL-UPS          UPS Worldwide, staff pick
  6. To email customers a working tracking link, activate the message
     template ShipmentSent.CustomerNotification and use the token
     %Shipment.XpsTrackingUrl% (snippet below). The plugin changes no
     templates itself.


Which orders XPS sees
---------------------
An order is listed only when all of these hold:

  1. Order status Pending or Processing.
  2. Shipping status Not yet shipped or Partially shipped.
  3. Not pickup in store, and it has a shipping address.
  4. Paid, or its payment method is in "Ship before payment" (agency POs).
     That setting matches the payment method's system name, ignoring case,
     spaces and the "Payments." prefix. The Preview shows the system name
     of every unpaid order it leaves out, so you can confirm the match.
  5. At least one physical, ship-enabled line still has quantity that is
     not in a shipment. Virtual gift cards and downloads never go to XPS,
     even when marked ship-enabled.
  6. Not on hold. An order whose special instruction (checkout attributes)
     says "do not ship" is put on hold automatically the first time it is
     seen. Taking it off hold on the Hold & release tab is remembered, so
     the rule doesn't put it straight back.
  7. Exchange orders (a line whose SKU ends in "-EXCH") wait until
     released on the Hold & release tab.
  8. "Hide fully backordered orders" (on by default): left out when every
     line still to ship is a tracked product with stock at or below 0.
     Stock of 9998, 9999 or 10000 counts as unlimited. Products tracked by
     attributes use the matching attribute combination's stock.

Lines with stock at or below 0 on orders that are sent get the title prefix
"BACKORDERED: ", so staff can set them to 0 when shipping a partial.

The list is oldest first and capped at "Most orders per list" (250).


What is sent
------------
  orderNumber       the custom order number
  customerNotes     "Customer selected: <shipping method>", then the
                    checkout attributes as plain text
  shipperReference  order number, plus "PO <n>" when the special
                    instruction has a PO number; cut to 35 characters
  shippingService   service key from the Service keys map (else the default)
  shipping_total    shipping paid, incl. tax
  sender            the ship-from settings
  receiver          the shipping address; state as its abbreviation,
                    country as its 2-letter code, phone as its last 10
                    digits, email from the address or else the customer
  items             SKU (of the attribute combination when it has one),
                    product name | attributes, unit price excl. tax,
                    quantity still to ship, unit weight in lb, 100px image

A line with no SKU or no weight is still sent, and logged once per order on
the Warnings tab so the catalog gets fixed. Packages are not sent: staff
pick boxes and weights in XPS.


When a label posts
------------------
  - Unknown order: 404. Bad carrier, no tracking number, or an item id
    that isn't a line on the order: 422, and nothing is created.
  - A tracking number already recorded for the order (by XPS, or typed on
    a shipment) is a duplicate: 200, nothing changes. Retries are safe.
  - Quantities above what's left are capped, with a warning.
  - No item list posted: everything left is shipped.
  - Shipment: first tracking number in Tracking number. Total weight is
    XPS's billed weight when given in lb, else the items' weight. The admin
    comment reads "XPS: UPS, 2 packages: 1Z..., 1Z...; label $14.82".
  - With more than one package, a note visible to the customer lists every
    number, without a "new order note" email.
  - Two posts for one order are handled one at a time.

Every call is in the Webhook log tab (bodies up to 8 KB, kept
"Keep log (days)", 90 by default). Failed calls are also written to
nopCommerce's own log.


Voided labels
-------------
XPS doesn't tell the store about voids. After voiding a label in XPS, click
Void on its row in the Labels tab. That:

  - deletes the shipment and gives back any booked inventory, as the admin
    Delete shipment button does
  - sets the shipping status back to Partially shipped or Not yet shipped.
    nopCommerce's own Delete shipment doesn't do this, so the order would
    otherwise never return to XPS. A Complete order goes back to
    Processing.
  - marks the label Voided and adds a private order note

The customer is not told. The replacement label sends a new shipped email.
Re-posting a voided tracking number is treated as a duplicate.


Tracking link token
-------------------
%Shipment.XpsTrackingUrl% links to the carrier's tracking page. The carrier
comes from the label XPS posted, or else from the number's format: 1Z... is
UPS, 12 or 15 digits FedEx, 92-95 with 20-22 digits USPS. Carrier text typed
by hand ("UPS 1Z...") is stripped first. When neither works, it links to the
customer's order page. For the Tracking Number row of
ShipmentSent.CustomerNotification:

  <td style="font-family: Arial,sans-serif; font-size: 13px; padding: 8px 0; border-bottom: 1px solid #f0f0f0;"><a style="color: #2e5fa3; font-weight: bold; text-decoration: none;" href="%Shipment.XpsTrackingUrl%">%Shipment.TrackingNumber%</a></td>


Security
--------
  - HTTPS only. Plain http gets 403, never a redirect.
  - The key is compared in constant time. Regenerate it on the Settings tab
    if it leaks, then paste the new one into XPS.
  - Optional IP allowlist (XPS's outbound IPs, once XPS support confirms
    them).
  - More than 30 wrong-key requests from one IP within 10 minutes block
    that IP for an hour, and an error goes to the nopCommerce log.
  - Responses carry X-Robots-Tag: noindex and Cache-Control: no-store.
  - The webhooks don't go through nopCommerce's storefront controller
    filters, so no guest customer, cookie or visitor tracking is created.


Known limitations
-----------------
  - The failed-key counts, IP blocks and per-order lock live in the web
    process's memory. That's right for one web server; in a web farm each
    node counts separately.
  - No delivery dates: XPS webhooks don't send delivery events.
  - nopCommerce holds one tracking number per shipment. Extra package
    numbers are in the admin comment, the customer note and the Labels tab.
  - Whether a fully shipped, paid order becomes Complete is nopCommerce's
    order status rule (and "Complete order when delivered"); the plugin
    doesn't set order status except when a void undoes a Complete.
  - Open questions for XPS support: what Webship does with a non-200 reply
    (shown to staff? retried?), and its outbound IP addresses.


Files in the plugin folder
--------------------------
  Nop.Plugin.Misc.XpsShip.dll, .pdb, .deps.json, plugin.json, logo.png,
  LICENSE.md, Views\_ViewImports.cshtml, Views\Admin\*.cshtml
