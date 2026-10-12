using FluentMigrator;
using Nop.Data.Migrations;

namespace Nop.Plugin.Misc.CustomerProfile.Data.Migrations;

/// <summary>
/// Creates the full current schema on a fresh install.
/// nopCommerce only marks Update migrations as applied when a plugin is installed (it does not run them),
/// so every column and index added by a later Update migration must also be created here.
/// </summary>
[NopMigration("2026/10/12 12:00:00", "Misc.CustomerProfile schema", MigrationProcessType.Installation)]
public class SchemaMigration : Migration
{
    public override void Up()
    {
        Create.Table(CustomerProfileDefaults.ProfileTable)
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("CustomerId").AsInt32().NotNullable().Unique("UX_CustomerBusinessProfile_CustomerId")
            .WithColumn("BmwDealership").AsString(CustomerProfileDefaults.DealershipMaxLength).Nullable()
            .WithColumn("Net30Enabled").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("Net30TermsDays").AsInt32().NotNullable().WithDefaultValue(30)
            .WithColumn("UpdatedOnUtc").AsDateTime2().NotNullable();

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
            .WithColumn("PendingNudgeSentOnUtc").AsDateTime2().Nullable();

        Create.Table(CustomerProfileDefaults.Net30OrderTable)
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("OrderId").AsInt32().NotNullable().Unique("UX_Net30Order_OrderId")
            .WithColumn("CustomerId").AsInt32().NotNullable().Indexed("IX_Net30Order_CustomerId")
            .WithColumn("TermsDays").AsInt32().NotNullable()
            .WithColumn("DueOn").AsDateTime2().NotNullable()
            .WithColumn("OverdueNoticeSentOnUtc").AsDateTime2().Nullable();
    }

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
