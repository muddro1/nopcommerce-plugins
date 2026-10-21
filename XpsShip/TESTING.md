# XPS Ship plugin: staging test script

**Plugin:** Misc.XpsShip 2.02 (nopCommerce 4.90) and 1.02 (nopCommerce 4.50)
**Purpose:** confirm on a copy of the live store, with the real XPS account, what the automated tests and a local run could only confirm against sample data.

The automated tests (117 on 4.90, 119 on 4.50, which adds bins) cover the selection rules, mapping, tracking-number parsing and links, the Update Order parser and planner, the request guard, and the Update Order flow against mocked nopCommerce services. That flow includes undoing a failed post and recovering on the retry. The 4.50 build has not been run on a live site; on 4.50 only **Sales → XPS Ship** exists. The 4.90 plugin was also run end to end on a local nopCommerce 4.90 with sample data. That run covered:
- install;
- List Orders, Get Order and Update Order over HTTPS;
- duplicate and unknown-line posts;
- a three-package label;
- hold and release, and the automatic do-not-ship hold;
- void and re-ship;
- the `%Shipment.XpsTrackingUrl%` token in a queued email.

It has not run against the live store's custom code (checkout, order customisations) or against XPS itself. This script closes that gap.

The test numbers (T1…T22) are the spec's (`docs/superpowers/specs/2026-09-24-xps-ship-design.md`). The order numbers there were open on 2026-09-24. Use orders of the same shape from the staging copy.

**Run this on staging, not production.** Take a database backup first.

You need:
- an admin login;
- the staging site reachable over HTTPS (XPS must be able to reach it for Part 4);
- a terminal with `curl`;
- **System → Message queue** to check emails.

In the curl lines, `$KEY` is the secret key from the Settings tab, `$SITE` is the staging URL, and `$OG` is an order's GUID. To find an order's GUID: `select "OrderGuid" from "Order" where "CustomOrderNumber"='8109'` (SQL Server: `select OrderGuid from [Order] where CustomOrderNumber='8109'`).

**Use a browser user agent if you script the admin pages with curl.** nopCommerce treats `curl`'s user agent as a search engine, which is never logged in. The webhooks don't care.

---

## Part 1: Install

1. Copy `Misc.XpsShip` into `\Plugins`, then **Configuration → Local plugins → Reload list of plugins → Install**.
   - [ ] Tables `XpsShip_TrackingMap`, `XpsShip_OrderFlag`, `XpsShip_WebhookLog` exist.
   - [ ] **System → Schedule tasks** has "XPS Ship: purge webhook log", 86400 seconds.
   - [ ] **Sales → XPS Ship** and **FMS Plugins → XPS Ship** both open the page; all six tabs open.
   - [ ] Settings shows three `https://…/xps/…` URLs and a 32-character key.
2. Still disabled: `curl -s -H "x-rsis-key: $KEY" $SITE/xps/orders`
   - [ ] `503 {"error":"integration is disabled"}`.
3. Settings: confirm the sender address, then tick **Enabled** and Save.

## Part 2: Selection (Preview tab)

For each case, open **Preview List Orders** and check where the order lands.

| # | Case | Expect |
|---|---|---|
| T1/T20 | Paid order, FedEx Standard Overnight (#8109 shape) | Sent; `shippingService` `EXP-UPS-NEXTDAY`; `customerNotes` starts "Customer selected: FedEx Standard Overnight". |
| T3 | Open Payment, Pending, mixed stock (#8108 shape) | Sent despite unpaid; backordered lines titled `BACKORDERED: …`. If it's left out as "unpaid (Payments.X)", put that system name in *Ship before payment*. |
| T4 | Every line tracked with stock ≤ 0 (#7611 shape) | Left out, "backordered". Untick *Hide fully backordered orders*, then it is sent. Tick it again. |
| T5 | A `-EXCH` line (#8103 shape) | Left out, "exchange order, not released". Release it on **Hold & release**, then it is sent. |
| T6 | Special instruction "PO# 36591…" with a linked mount (#8102 shape) | `shipperReference` "8102 PO 36591"; title has the mount SKU in brackets; weight e.g. "2.9100". |
| T7 | Special instruction contains "do not ship" (#8090 shape) | Left out; **Hold & release** shows a Hold by "auto". Take off hold, then it is sent and stays sent on the next Preview. |
| T8 | A line with no SKU (#8086 shape) | Sent with `"sku": null`; **Warnings** tab lists "No SKU: …" once. |
| T14 | PayPal / card order left Pending | Left out, "unpaid (…)". |
| – | Give T1's customer an extra role "Ship UPS 2-Day" and add `Ship UPS 2-Day = EXP-UPS-2DAY` under *Service keys by customer role* | `shippingService` `EXP-UPS-2DAY` though they chose FedEx Standard Overnight; `customerNotes` still says what they chose. Remove the role: back to `EXP-UPS-NEXTDAY`. |
| – | Pickup in store, or a virtual gift card only | Left out. |
| – | T3's order with *Mark backordered lines* unticked | Same lines, no `BACKORDERED: ` prefix. Tick it again. |
| – | 4.50: T6's order with *Send item bin*, then *Send component part bins*, unticked | The title loses its own bin, then the mount's; with both off, no `[BIN …]`. Tick both again. |

- [ ] The Preview list matches the "Waiting to ship" section of that day's daily report, apart from the deliberate exclusions above.

## Part 3: Webhooks with curl

```sh
curl -s -H "x-rsis-key: $KEY" $SITE/xps/orders | head -c 2000
curl -s -H "x-rsis-key: $KEY" "$SITE/xps/order?orderId=$OG"
curl -s -H "x-rsis-key: $KEY" $SITE/xps/update \
  --data-urlencode "orderId=$OG" --data-urlencode "carrier=ups" \
  --data-urlencode "trackingNumbers=1ZTEST0000000001" \
  --data-urlencode "items[0][id]=<productId from the list>" --data-urlencode "items[0][quantity]=2"
```

| # | Do | Expect |
|---|---|---|
| T11 | List with a wrong key; then with `http://` | 401 `wrong key`; 403 `HTTPS required`. Both in the **Webhook log** tab. |
| T1 | Post the full order | 200 "shipped: …". On the order: one shipment, Shipped date set, tracking `1ZTEST…`, shipping status Shipped. It's gone from List Orders. |
| T9 | Repeat the same post | 200 "duplicate: …"; still one shipment. |
| T2 | Two lines, 17 each (#8106 shape) | One shipment, 2 items, 34 units. |
| T3 | Post only the in-stock lines of the Open Payment order | Partially shipped. Get Order and List Orders show only the remaining lines. |
| T10/T22 | `trackingNumbers=1zc602x70219206948, 1ZC602X70220017391,1ZC602X70224542806` | Tracking `1ZC602X70219206948`. The Labels tab shows all 3. A customer-visible note lists all 3. The message queue has one "shipped" email and no "new order note" email. |
| T12 | `items[0][id]=` a random GUID | 422 naming the id; no shipment. |
| T13 | Quantity 5 on a ×2 line | Shipment for 2; **Warnings** shows the cap. |
| T15 | Three posts, one line each, on a 3-line order | Partially shipped, Partially shipped, then Shipped. It leaves List Orders only after the third. |
| – | `carrier=ontrac`; a 120-character tracking number | 422 for each. |
| – | `carrier=dhl`, `trackingNumbers=JD014600006281230704` (with the Part 5 template active) | The queued shipped email links to DHL's tracking page, not the order page. |

| – | Ship the rest of T3's Open Payment order, with *Complete order when fully shipped* ticked | Shipping status Shipped, order status **Complete**, payment status still Pending, private note "XPS Ship: fully shipped, order status set to Complete". The message queue has **one** "Order completed" email to the customer (4.90: and one to the store owner), and a note lists its id. Then mark the order paid: no second completed email. |
| – | With a send delay on the OrderCompleted templates, Void that order's label before the emails go | Order back to Processing; the unsent "Order completed" emails are gone from the message queue; the void note says "Deleted 2 unsent order completed email(s)" (1 on 4.50). |
| – | Ship a paid order in full | nopCommerce completes it itself; exactly one "Order completed" email, none from the plugin. |
| – | The same on another unpaid order with the setting unticked | Order status stays Processing (nopCommerce only completes paid orders). |

## Part 4: Live XPS against staging

1. In XPS, add a second Webhooks integration pointing at the staging URLs, with the staging key. Turn on Support Partial Shipments and paste the Get Order URL.
2. Map the four service keys.
3. Pull orders.
   - [ ] The orders match the Preview.
   - [ ] Addresses, PO references and backordered titles look right on the XPS ship screen.
4. Create real labels on 2–3 test orders, including one multi-box UPS shipment and one partial.
   - [ ] Shipments appear on the staging orders with the right numbers.
   - [ ] On the multi-box label, the master number is first: it is the shipment's tracking number.
   - [ ] The Labels tab shows the label cost and billed weight.
5. Ask XPS support, or watch the log: what does Webship show when a post gets 422?
6. Void the labels in XPS, then click **Void** on each in the Labels tab (T18).
   - [ ] The shipment is gone, the label is Voided, there's a private note "XPS label … voided", the shipping status is back to Not yet shipped / Partially shipped, a Complete order is back to Processing, and the order is back in List Orders.
   - [ ] A new label for the same order then posts normally.

## Part 5: Emails and tracking links (T16, T21)

1. Activate **ShipmentSent.CustomerNotification**. Replace the second cell of its Tracking Number row with the snippet in `Readme.txt`.
2. Add `%Shipment.XpsShipToAddress%` to the same template (snippet in `Readme.txt`).
3. Post a UPS label and a USPS label.
   - [ ] In the message queue, the links go to the UPS and USPS tracking pages for those numbers.
   - [ ] Change the street in XPS before buying one of the labels: that email shows the XPS street, not the order's.
4. For old hand-made shipments ("UPS 1ZC602X71321659005", "USPS 9334611045000004779320", "FEDEX 876435110017", "Hand Deliver"), use the template's *Test template* button with the order.
   - [ ] Carrier links with the typed text stripped; "Hand Deliver" links to the customer's order page. The address is the order's shipping address.

## Part 6: Daily report (T17)

- [ ] New shipments count by Shipped date in items shipped.
- [ ] No new order trips C3 (Complete but not shipped) or C7 (no tracking).

## Rollback

Untick **Enabled**: XPS gets 503 and sees no orders, and nothing else changes. Uninstalling drops the three tables. Shipments already made stay.
