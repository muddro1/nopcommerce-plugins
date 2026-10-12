using Nop.Data.Migrations;

namespace Nop.Plugin.Misc.CustomerProfile.Data.Migrations;

/// <summary>1.0.2: TaxExemptionCertificate.ExemptionRemovedOnExpiry. SchemaMigration creates it on fresh installs.</summary>
[NopMigration("2026/10/12 18:42:07", "Misc.CustomerProfile add ExemptionRemovedOnExpiry", MigrationProcessType.Update)]
public class AddExemptionRemovedMigration : CustomerProfileSchemaBase
{
    public override void Up()
    {
        CreateMissingTables();
        AddMissingColumns();
    }

    // Uninstall keeps the data (see SchemaMigration.Down)
    public override void Down()
    {
    }
}
