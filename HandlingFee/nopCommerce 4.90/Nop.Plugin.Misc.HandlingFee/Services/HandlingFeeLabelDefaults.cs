using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Nop.Plugin.Misc.HandlingFee.Services
{
    /// <summary>
    /// Represents a backed-up locale resource value, captured before the plugin overwrote it
    /// </summary>
    public class LabelBackupEntry
    {
        public int LanguageId { get; set; }

        public string ResourceName { get; set; }

        public string Value { get; set; }
    }

    /// <summary>
    /// The core locale resources through which the handling fee is displayed, and the rules
    /// for writing a custom label into them.
    ///
    /// The fee rides nopCommerce's payment-method-fee channel, so its label lives in core
    /// resources rather than in this plugin. There is no per-line label hook: the views call
    /// @T(...) on fixed resource names.
    /// </summary>
    public static class HandlingFeeLabelDefaults
    {
        public const string CART_TOTALS = "ShoppingCart.Totals.PaymentMethodAdditionalFee";
        public const string ORDER_DETAILS = "Order.PaymentMethodAdditionalFee";
        public const string PDF_INVOICE = "PDFInvoice.PaymentMethodAdditionalFee";
        public const string ORDER_EMAIL = "Messages.Order.PaymentMethodAdditionalFee";
        public const string ADMIN_ORDER = "Admin.Orders.Fields.PaymentMethodAdditionalFee";
        public const string ADMIN_ORDER_EDIT = "Admin.Orders.Fields.Edit.PaymentMethodAdditionalFee";

        /// <summary>
        /// These two carry the trailing colon inside the resource value itself, because the
        /// PDF and email builders do not add one. The other four are rendered by views that
        /// supply their own colon, so adding one here would double it.
        /// </summary>
        private static readonly string[] _resourcesCarryingTheirOwnColon = { PDF_INVOICE, ORDER_EMAIL };

        /// <summary>
        /// Every resource this plugin will overwrite when a custom label is set.
        /// Deliberately excludes Admin.Configuration.Settings.Tax.* — those describe
        /// nopCommerce's tax setting, which genuinely is the payment method fee setting.
        /// </summary>
        public static IReadOnlyList<string> ManagedResources { get; } = new[]
        {
            CART_TOTALS,
            ORDER_DETAILS,
            PDF_INVOICE,
            ORDER_EMAIL,
            ADMIN_ORDER,
            ADMIN_ORDER_EDIT
        };

        /// <summary>
        /// Build the value to store in each managed resource for the given label
        /// </summary>
        /// <param name="label">The label as typed by the store owner</param>
        /// <returns>Resource name to resource value</returns>
        public static IDictionary<string, string> BuildResourceValues(string label)
        {
            //tolerate a label typed with its own trailing colon, so the invoice does not end up with two
            var trimmed = (label ?? string.Empty).Trim().TrimEnd(':').Trim();

            return ManagedResources.ToDictionary(
                resourceName => resourceName,
                resourceName => _resourcesCarryingTheirOwnColon.Contains(resourceName)
                    ? $"{trimmed}:"
                    : trimmed);
        }

        /// <summary>
        /// Serialise captured originals for storage in a setting
        /// </summary>
        public static string SerialiseBackup(IEnumerable<LabelBackupEntry> entries)
        {
            return JsonSerializer.Serialize(entries ?? Enumerable.Empty<LabelBackupEntry>());
        }

        /// <summary>
        /// Read back captured originals. Never throws: a corrupt or absent backup must not
        /// be able to block an uninstall.
        /// </summary>
        public static IList<LabelBackupEntry> DeserialiseBackup(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<LabelBackupEntry>();

            try
            {
                return JsonSerializer.Deserialize<List<LabelBackupEntry>>(json) ?? new List<LabelBackupEntry>();
            }
            catch (JsonException)
            {
                return new List<LabelBackupEntry>();
            }
        }
    }
}
