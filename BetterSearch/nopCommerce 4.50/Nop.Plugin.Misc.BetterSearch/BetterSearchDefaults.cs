namespace Nop.Plugin.Misc.BetterSearch
{
    /// <summary>
    /// Constants for the better search plugin
    /// </summary>
    public static class BetterSearchDefaults
    {
        public const string SYSTEM_NAME = "Misc.BetterSearch";

        /// <summary>
        /// Index location, relative to the application's App_Data folder
        /// </summary>
        public const string INDEX_FOLDER = "BetterSearch/index";

        /// <summary>
        /// The scheduled rebuild task, registered on install
        /// </summary>
        public const string REBUILD_TASK_NAME = "Rebuild the product search index";
        public const string REBUILD_TASK_TYPE = "Nop.Plugin.Misc.BetterSearch.Tasks.RebuildSearchIndexTask, Nop.Plugin.Misc.BetterSearch";
        public const int REBUILD_TASK_PERIOD_SECONDS = 900;

        //Lucene field names. Every consumer uses these constants rather than string literals,
        //because a typo in a field name produces silently empty results rather than an error.
        public const string FIELD_PRODUCT_ID = "productid";
        public const string FIELD_NAME = "name";
        public const string FIELD_SHORT_DESCRIPTION = "shortdescription";
        public const string FIELD_FULL_DESCRIPTION = "fulldescription";
        public const string FIELD_TAGS = "tags";
        public const string FIELD_CATEGORIES = "categories";
        public const string FIELD_MANUFACTURERS = "manufacturers";
        public const string FIELD_GTIN = "gtin";

        //identifiers are indexed three ways; see the spec's "SKU matching" section
        public const string FIELD_SKU_RAW = "sku_raw";
        public const string FIELD_SKU_SEGMENT = "sku_segment";
        public const string FIELD_SKU_NGRAM = "sku_ngram";
        public const string FIELD_MPN_RAW = "mpn_raw";
        public const string FIELD_MPN_SEGMENT = "mpn_segment";
        public const string FIELD_MPN_NGRAM = "mpn_ngram";
    }
}
