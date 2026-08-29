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
                "PDFInvoice.PaymentMethodAdditionalFee",
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
        }

        [Test]
        public void Invoice_and_email_resources_get_a_trailing_colon()
        {
            //the PDF invoice and email templates carry the colon inside the resource value;
            //the views that render them do not supply one
            var values = HandlingFeeLabelDefaults.BuildResourceValues("Small Order Handling Charge");

            values["PDFInvoice.PaymentMethodAdditionalFee"].Should().Be("Small Order Handling Charge:");
            values["Messages.Order.PaymentMethodAdditionalFee"].Should().Be("Small Order Handling Charge:");
        }

        [Test]
        public void A_label_typed_with_its_own_colon_does_not_get_a_second_one()
        {
            var values = HandlingFeeLabelDefaults.BuildResourceValues("Small Order Handling Charge:");

            values["PDFInvoice.PaymentMethodAdditionalFee"].Should().Be("Small Order Handling Charge:");
            values["ShoppingCart.Totals.PaymentMethodAdditionalFee"].Should().Be("Small Order Handling Charge");
        }

        [Test]
        public void Surrounding_whitespace_is_trimmed()
        {
            var values = HandlingFeeLabelDefaults.BuildResourceValues("  Small Order Handling Charge  ");

            values["ShoppingCart.Totals.PaymentMethodAdditionalFee"].Should().Be("Small Order Handling Charge");
            values["PDFInvoice.PaymentMethodAdditionalFee"].Should().Be("Small Order Handling Charge:");
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
                new LabelBackupEntry { LanguageId = 2, ResourceName = "PDFInvoice.PaymentMethodAdditionalFee", Value = "Frais:" }
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
