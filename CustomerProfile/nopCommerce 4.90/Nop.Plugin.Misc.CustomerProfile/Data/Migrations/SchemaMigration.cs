using FluentMigrator;
using Nop.Data.Migrations;

namespace Nop.Plugin.Misc.CustomerProfile.Data.Migrations;

/// <summary>Creates whichever of the plugin's tables don't exist yet</summary>
public abstract class CustomerProfileSchemaBase : Migration
{
    /// <summary>Columns added after 1.0.0, for tables that already existed</summary>
    protected void AddMissingColumns()
    {
        if (Schema.Table(CustomerProfileDefaults.CertificateTable).Exists()
            && !Schema.Table(CustomerProfileDefaults.CertificateTable).Column("ExemptionRemovedOnExpiry").Exists())
        {
            Alter.Table(CustomerProfileDefaults.CertificateTable)
                .AddColumn("ExemptionRemovedOnExpiry").AsBoolean().NotNullable().WithDefaultValue(false);
        }
    }

    protected void CreateMissingTables()
    {
        if (!Schema.Table(CustomerProfileDefaults.ProfileTable).Exists())
        {
            Create.Table(CustomerProfileDefaults.ProfileTable)
                .WithColumn("Id").AsInt32().PrimaryKey().Identity()
                .WithColumn("CustomerId").AsInt32().NotNullable().Unique("UX_CustomerBusinessProfile_CustomerId")
                .WithColumn("BmwDealership").AsString(CustomerProfileDefaults.DealershipMaxLength).Nullable()
                .WithColumn("Net30Enabled").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("Net30TermsDays").AsInt32().NotNullable().WithDefaultValue(30)
                .WithColumn("UpdatedOnUtc").AsDateTime2().NotNullable();
        }

        if (!Schema.Table(CustomerProfileDefaults.ContactTable).Exists())
        {
            Create.Table(CustomerProfileDefaults.ContactTable)
                .WithColumn("Id").AsInt32().PrimaryKey().Identity()
                .WithColumn("CustomerId").AsInt32().NotNullable().Indexed("IX_CustomerContact_CustomerId")
                .WithColumn("Name").AsString(CustomerProfileDefaults.ContactNameMaxLength).NotNullable()
                .WithColumn("Email").AsString(CustomerProfileDefaults.ContactEmailMaxLength).NotNullable()
                .WithColumn("Title").AsString(CustomerProfileDefaults.ContactTitleMaxLength).Nullable()
                .WithColumn("CategoriesId").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("Active").AsBoolean().NotNullable().WithDefaultValue(true)
                .WithColumn("CreatedOnUtc").AsDateTime2().NotNullable()
                .WithColumn("CreatedByCustomerId").AsInt32().NotNullable()
                .WithColumn("UpdatedOnUtc").AsDateTime2().Nullable()
                .WithColumn("UpdatedByCustomerId").AsInt32().Nullable();
        }

        if (!Schema.Table(CustomerProfileDefaults.CertificateTable).Exists())
        {
            Create.Table(CustomerProfileDefaults.CertificateTable)
                .WithColumn("Id").AsInt32().PrimaryKey().Identity()
                .WithColumn("CustomerId").AsInt32().NotNullable().Indexed("IX_TaxExemptionCertificate_CustomerId")
                .WithColumn("DownloadId").AsInt32().NotNullable()
                .WithColumn("FileName").AsString(400).NotNullable()
                .WithColumn("ContentType").AsString(100).NotNullable()
                .WithColumn("StateId").AsInt32().NotNullable().Indexed("IX_TaxExemptionCertificate_StateId")
                .WithColumn("ExpiresOn").AsDateTime2().Nullable()
                .WithColumn("CertificateNumber").AsString(100).Nullable()
                .WithColumn("IssuingState").AsString(100).Nullable()
                .WithColumn("UploadedByCustomerId").AsInt32().NotNullable()
                .WithColumn("UploadedOnUtc").AsDateTime2().NotNullable()
                .WithColumn("ReviewedByCustomerId").AsInt32().Nullable()
                .WithColumn("ReviewedOnUtc").AsDateTime2().Nullable()
                .WithColumn("ReviewNote").AsString(CustomerProfileDefaults.NoteMaxLength).Nullable()
                .WithColumn("ReminderSentOnUtc").AsDateTime2().Nullable()
                .WithColumn("ExpiredNoticeSentOnUtc").AsDateTime2().Nullable()
                .WithColumn("PendingNudgeSentOnUtc").AsDateTime2().Nullable()
                .WithColumn("ExemptionRemovedOnExpiry").AsBoolean().NotNullable().WithDefaultValue(false);
        }

        if (!Schema.Table(CustomerProfileDefaults.Net30OrderTable).Exists())
        {
            Create.Table(CustomerProfileDefaults.Net30OrderTable)
                .WithColumn("Id").AsInt32().PrimaryKey().Identity()
                .WithColumn("OrderId").AsInt32().NotNullable().Unique("UX_Net30Order_OrderId")
                .WithColumn("CustomerId").AsInt32().NotNullable().Indexed("IX_Net30Order_CustomerId")
                .WithColumn("TermsDays").AsInt32().NotNullable()
                .WithColumn("DueOn").AsDateTime2().NotNullable()
                .WithColumn("OverdueNoticeSentOnUtc").AsDateTime2().Nullable();
        }
    }
}

/// <summary>
/// Creates the full current schema on a fresh install.
/// nopCommerce only marks Update migrations as applied when a plugin is installed (it does not run them),
/// so every column and index added by a later Update migration must also be created here.
/// </summary>
/// <remarks>
/// nopCommerce identifies a migration only by its timestamp, across every plugin, so the timestamp must be unique.
/// 1.0.0 used "2026/10/12 12:00:00", which Estimate Builder's AddNoteMigration already used: on stores with
/// Estimate Builder the install was skipped as already applied and no tables were created. Never reuse that value.
/// </remarks>
[NopMigration("2026/10/12 15:37:21", "Misc.CustomerProfile schema", MigrationProcessType.Installation)]
public class SchemaMigration : CustomerProfileSchemaBase
{
    public override void Up() => CreateMissingTables();

    public override void Down()
    {
        // Indexes go away with their tables
        foreach (var table in new[]
                 {
                     CustomerProfileDefaults.Net30OrderTable, CustomerProfileDefaults.CertificateTable,
                     CustomerProfileDefaults.ContactTable, CustomerProfileDefaults.ProfileTable
                 })
        {
            if (Schema.Table(table).Exists())
                Delete.Table(table);
        }
    }
}
