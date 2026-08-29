using System.Collections.Generic;

namespace Nop.Plugin.Misc.BetterSearch.Services
{
    /// <summary>
    /// Everything <see cref="ProductDocumentBuilder"/> needs to build a Lucene document for a
    /// product, decoupled from the nopCommerce <c>Product</c> entity so the builder - and its
    /// tests - never need a database.
    /// </summary>
    public record ProductIndexInput
    {
        public int ProductId { get; init; }
        public string Name { get; init; }
        public string Sku { get; init; }
        public string ManufacturerPartNumber { get; init; }
        public string Gtin { get; init; }
        public string ShortDescription { get; init; }
        public string FullDescription { get; init; }
        public IList<string> Tags { get; init; } = new List<string>();
        public IList<string> Categories { get; init; } = new List<string>();
        public IList<string> Manufacturers { get; init; } = new List<string>();
    }
}
