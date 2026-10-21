using FluentMigrator;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.XpsShip.Logic;

namespace Nop.Plugin.Misc.XpsShip.Data;

/// <summary>1.02/2.02: XpsShip_TrackingMap.ShipToAddress. SchemaMigration creates it on fresh installs.</summary>
[NopMigration("2026/10/21 09:42:18", "Misc.XpsShip add ShipToAddress", MigrationProcessType.Update)]
public class AddShipToAddressMigration : Migration
{
    public override void Up()
    {
        if (!Schema.Table(XpsShipDefaults.TrackingMapTable).Column("ShipToAddress").Exists())
            Alter.Table(XpsShipDefaults.TrackingMapTable).AddColumn("ShipToAddress").AsString(ShipTo.MaxLength).Nullable();
    }

    // SchemaMigration.Down drops the table on uninstall
    public override void Down()
    {
    }
}
