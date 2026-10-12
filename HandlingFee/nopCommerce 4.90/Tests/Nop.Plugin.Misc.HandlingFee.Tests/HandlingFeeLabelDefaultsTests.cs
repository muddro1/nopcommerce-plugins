using System.Linq;
using FluentAssertions;
using Nop.Plugin.Misc.HandlingFee.Services;
using NUnit.Framework;

namespace Nop.Plugin.Misc.HandlingFee.Tests
{
    [TestFixture]
    public class HandlingFeeLabelDefaultsTests
    {
        [Test]
        public void Manages_all_six_resources()
        {
            HandlingFeeLabelDefaults.ManagedResources.Should().BeEquivalentTo(new[]
            {
                "ShoppingCart.Totals.PaymentMethodAdditionalFee",
                "Order.PaymentMethodAdditionalFee",
                "Pdf.PaymentMethodAdditionalFee",
                "Messages.Order.PaymentMethodAdditionalFee",
                "Admin.Orders.Fields.PaymentMethodAdditionalFee",
                "Admin.Orders.Fields.Edit.PaymentMethodAdditionalFee"
            });
        }

        [Test]
        public void Plain_resources_get_the_label_verbatim()
        {
            var values = HandlingFeeLabelDefaults.BuildResourceValues("Small Order Handling Charge");

            values["ShoppingCart.Totals.PaymentMethodAdditionalFee"].Should().Be("Small Order Handling Charge");
            values["Order.PaymentMethodAdditionalFee"].Should().Be("Small Order Handling Charge");
            values["Admin.Orders.Fields.PaymentMethodAdditionalFee"].Should().Be("Small Order Handling Charge");
            values["Admin.Orders.Fields.Edit.PaymentMethodAdditionalFee"].Should().Be("Small Order Handling Charge");

            //the 4.90 PDF builder appends its own colon after the label
            values["Pdf.PaymentMethodAdditionalFee"].Should().Be("Small Order Handling Charge");
        }

        [Test]
        public void Email_resource_gets_a_trailing_colon()
        {
            //the email template carries the colon inside the resource value;
            //the code that renders it does not supply one
            var values = HandlingFeeLabelDefaults.BuildResourceValues("Small Order Handling Charge");

            values["Messages.Order.PaymentMethodAdditionalFee"].Should().Be("Small Order Handling Charge:");
        }

        [Test]
        public void The_legacy_4_50_pdf_resource_is_not_managed()
        {
            HandlingFeeLabelDefaults.ManagedResources.Should().NotContain("PDFInvoice.PaymentMethodAdditionalFee");
        }

        [Test]
        public void Migrating_a_backup_drops_resources_no_longer_managed()
        {
            var entries = new[]
            {
                new LabelBackupEntry { LanguageId = 1, ResourceName = "Order.PaymentMethodAdditionalFee", Value = "Payment method additional fee" },
                new LabelBackupEntry { LanguageId = 1, ResourceName = "PDFInvoice.PaymentMethodAdditionalFee", Value = "Payment method additional fee:" }
            };

            var retained = HandlingFeeLabelDefaults.RetainManaged(entries);

            retained.Should().ContainSingle()
                .Which.ResourceName.Should().Be("Order.PaymentMethodAdditionalFee");
        }

        [Test]
        public void Migrating_a_missing_backup_yields_an_empty_list()
        {
            HandlingFeeLabelDefaults.RetainManaged(null).Should().BeEmpty();
        }

        [Test]
        public void A_label_typed_with_its_own_colon_does_not_get_a_second_one()
        {
            var values = HandlingFeeLabelDefaults.BuildResourceValues("Small Order Handling Charge:");

            values["Messages.Order.PaymentMethodAdditionalFee"].Should().Be("Small Order Handling Charge:");
            values["ShoppingCart.Totals.PaymentMethodAdditionalFee"].Should().Be("Small Order Handling Charge");
        }

        [Test]
        public void Surrounding_whitespace_is_trimmed()
        {
            var values = HandlingFeeLabelDefaults.BuildResourceValues("  Small Order Handling Charge  ");

            values["ShoppingCart.Totals.PaymentMethodAdditionalFee"].Should().Be("Small Order Handling Charge");
            values["Messages.Order.PaymentMethodAdditionalFee"].Should().Be("Small Order Handling Charge:");
        }

        [Test]
        public void Every_managed_resource_gets_a_value()
        {
            var values = HandlingFeeLabelDefaults.BuildResourceValues("Anything");

            values.Keys.Should().BeEquivalentTo(HandlingFeeLabelDefaults.ManagedResources);
            values.Values.Should().OnlyContain(v => !string.IsNullOrWhiteSpace(v));
        }

        [Test]
        public void Backup_round_trips_through_serialisation()
        {
            var entries = new[]
            {
                new LabelBackupEntry { LanguageId = 1, ResourceName = "Order.PaymentMethodAdditionalFee", Value = "Payment method additional fee" },
                new LabelBackupEntry { LanguageId = 2, ResourceName = "Messages.Order.PaymentMethodAdditionalFee", Value = "Frais:" }
            };

            var restored = HandlingFeeLabelDefaults.DeserialiseBackup(HandlingFeeLabelDefaults.SerialiseBackup(entries));

            restored.Should().HaveCount(2);
            restored.First(e => e.LanguageId == 1).Value.Should().Be("Payment method additional fee");
            restored.First(e => e.LanguageId == 2).Value.Should().Be("Frais:");
        }

        [Test]
        public void Deserialising_missing_or_broken_backup_yields_an_empty_list_rather_than_throwing()
        {
            //a corrupt backup must never block an uninstall
            HandlingFeeLabelDefaults.DeserialiseBackup(null).Should().BeEmpty();
            HandlingFeeLabelDefaults.DeserialiseBackup(string.Empty).Should().BeEmpty();
            HandlingFeeLabelDefaults.DeserialiseBackup("not json").Should().BeEmpty();
        }
    }
}
