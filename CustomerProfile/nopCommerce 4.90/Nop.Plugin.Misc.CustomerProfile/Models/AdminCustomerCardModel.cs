using Nop.Plugin.Misc.CustomerProfile.Domain;

namespace Nop.Plugin.Misc.CustomerProfile.Models;

public class AdminCustomerCardModel
{
    public int CustomerId { get; set; }
    public bool CanEdit { get; set; }
    public string? BmwDealership { get; set; }
    public bool Net30Enabled { get; set; }
    public int Net30TermsDays { get; set; }
    public int MaxUploadSizeMb { get; set; }
}

public class AdminContactListModel
{
    public int CustomerId { get; set; }
    public bool CanEdit { get; set; }
    public IList<CustomerContact> Contacts { get; set; } = new List<CustomerContact>();
}

public class AdminCertificateListModel
{
    public int CustomerId { get; set; }
    public bool CanEdit { get; set; }
    public bool IsTaxExempt { get; set; }
    public TaxExemptionCertificate? CurrentValid { get; set; }
    public bool HasPending { get; set; }
    public IList<TaxExemptionCertificate> Certificates { get; set; } = new List<TaxExemptionCertificate>();
    public DateTime Today { get; set; }
}
