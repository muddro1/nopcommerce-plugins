using Nop.Data.Migrations;

namespace Nop.Plugin.Misc.CustomerProfile.Data.Migrations;

/// <summary>1.0.8: TaxExemptionCertificate.ExemptionReasonId. SchemaMigration creates it on fresh installs.</summary>
[NopMigration("2026/10/12 19:31:46", "Misc.CustomerProfile add ExemptionReasonId", MigrationProcessType.Update)]
public class AddExemptionReasonMigration : CustomerProfileSchemaBase
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
