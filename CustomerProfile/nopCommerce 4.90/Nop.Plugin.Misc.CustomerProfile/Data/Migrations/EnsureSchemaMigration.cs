using Nop.Data.Migrations;

namespace Nop.Plugin.Misc.CustomerProfile.Data.Migrations;

/// <summary>
/// 1.0.1: creates the tables on stores where 1.0.0 installed without them (see <see cref="SchemaMigration"/>).
/// Runs when an installed 1.0.0 is upgraded; on a fresh install nopCommerce marks it applied without running it.
/// </summary>
[NopMigration("2026/10/12 15:37:22", "Misc.CustomerProfile create missing tables", MigrationProcessType.Update)]
public class EnsureSchemaMigration : CustomerProfileSchemaBase
{
    public override void Up() => CreateMissingTables();

    // Uninstall keeps the data (see SchemaMigration.Down)
    public override void Down()
    {
    }
}
