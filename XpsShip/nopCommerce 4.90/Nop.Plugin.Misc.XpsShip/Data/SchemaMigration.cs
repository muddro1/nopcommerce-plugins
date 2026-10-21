using FluentMigrator;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.XpsShip.Logic;

namespace Nop.Plugin.Misc.XpsShip.Data;

/// <summary>
/// Creates the plugin's three tables on install and drops them on uninstall.
/// nopCommerce identifies a migration only by its timestamp, across every plugin, so it must be unique:
/// check with grep -rn 'NopMigration("' before adding another.
/// </summary>
[NopMigration("2026/10/13 09:14:27", "Misc.XpsShip schema", MigrationProcessType.Installation)]
public class SchemaMigration : Migration
{
    public override void Up()
    {
        if (!Schema.Table(XpsShipDefaults.TrackingMapTable).Exists())
        {
            Create.Table(XpsShipDefaults.TrackingMapTable)
                .WithColumn("Id").AsInt32().PrimaryKey().Identity()
                .WithColumn("OrderGuid").AsGuid().NotNullable().Indexed("IX_XpsShip_TrackingMap_OrderGuid")
                .WithColumn("CustomOrderNumber").AsString(100).NotNullable()
                .WithColumn("PrimaryTrackingNumber").AsString(XpsShipDefaults.TrackingNumberMaxLength).NotNullable()
                .WithColumn("Carrier").AsString(20).NotNullable()
                .WithColumn("TrackingNumbers").AsString(int.MaxValue).NotNullable()
                .WithColumn("PackageCount").AsInt32().NotNullable()
                .WithColumn("LabelCost").AsDecimal(18, 4).Nullable()
                .WithColumn("BilledWeightLb").AsDecimal(18, 4).Nullable()
                .WithColumn("StatusId").AsInt32().NotNullable()
                .WithColumn("VoidedOnUtc").AsDateTime2().Nullable()
                .WithColumn("CreatedOnUtc").AsDateTime2().NotNullable()
                .WithColumn("ShipToAddress").AsString(ShipTo.MaxLength).Nullable();
        }

        if (!Schema.Table(XpsShipDefaults.OrderFlagTable).Exists())
        {
            Create.Table(XpsShipDefaults.OrderFlagTable)
                .WithColumn("Id").AsInt32().PrimaryKey().Identity()
                .WithColumn("OrderGuid").AsGuid().NotNullable().Indexed("IX_XpsShip_OrderFlag_OrderGuid")
                .WithColumn("CustomOrderNumber").AsString(100).NotNullable()
                .WithColumn("Flag").AsString(30).NotNullable()
                .WithColumn("Reason").AsString(1000).Nullable()
                .WithColumn("CreatedBy").AsString(200).NotNullable()
                .WithColumn("CreatedOnUtc").AsDateTime2().NotNullable();
        }

        if (!Schema.Table(XpsShipDefaults.WebhookLogTable).Exists())
        {
            Create.Table(XpsShipDefaults.WebhookLogTable)
                .WithColumn("Id").AsInt32().PrimaryKey().Identity()
                .WithColumn("Endpoint").AsString(30).NotNullable()
                .WithColumn("HttpStatus").AsInt32().NotNullable()
                .WithColumn("OrderGuid").AsGuid().Nullable()
                .WithColumn("CustomOrderNumber").AsString(100).Nullable()
                .WithColumn("RemoteIp").AsString(100).Nullable()
                .WithColumn("RequestBody").AsString(int.MaxValue).Nullable()
                .WithColumn("ResponseSummary").AsString(1000).Nullable()
                .WithColumn("Message").AsString(4000).Nullable()
                .WithColumn("CreatedOnUtc").AsDateTime2().NotNullable().Indexed("IX_XpsShip_WebhookLog_CreatedOnUtc");
        }
    }

    /// <summary>Runs on uninstall: the spec has the plugin's tables dropped with it</summary>
    public override void Down()
    {
        foreach (var table in new[] { XpsShipDefaults.TrackingMapTable, XpsShipDefaults.OrderFlagTable, XpsShipDefaults.WebhookLogTable })
        {
            if (Schema.Table(table).Exists())
                Delete.Table(table);
        }
    }
}
