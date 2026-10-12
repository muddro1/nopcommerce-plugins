using Nop.Plugin.Misc.CustomerProfile.Services;

namespace Nop.Plugin.Misc.CustomerProfile.Models;

public abstract class PagedListModel
{
    public int PageIndex { get; set; }
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
}

public class Net30OrdersModel : PagedListModel
{
    public bool OverdueOnly { get; set; }
    public DateTime Today { get; set; }
    public IList<Net30Row> Rows { get; set; } = new List<Net30Row>();
}

public class TaxExemptionsModel : PagedListModel
{
    public CertificateFilter Filter { get; set; }
    public string? Dealership { get; set; }
    public string? Customer { get; set; }
    public IList<CertificateRow> Rows { get; set; } = new List<CertificateRow>();
}
