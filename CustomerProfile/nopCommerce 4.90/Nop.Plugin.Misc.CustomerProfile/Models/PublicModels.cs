using Nop.Plugin.Misc.CustomerProfile.Domain;

namespace Nop.Plugin.Misc.CustomerProfile.Models;

public class BusinessDetailsModel
{
    public string? BmwDealership { get; set; }
    public IList<CustomerContact> Contacts { get; set; } = new List<CustomerContact>();
    public ContactModel NewContact { get; set; } = new();
    public bool CanAddContact { get; set; }
}

public class TaxExemptionPageModel
{
    public TaxExemptionCertificate? CurrentValid { get; set; }
    public TaxExemptionCertificate? LatestExpired { get; set; }
    public bool HasPending { get; set; }
    public IList<TaxExemptionCertificate> Certificates { get; set; } = new List<TaxExemptionCertificate>();

    /// <summary>Preselected in the upload form: what was just posted, else the latest certificate's, for renewals</summary>
    public ExemptionReason ExemptionReason { get; set; }

    public int MaxUploadSizeMb { get; set; }
    public DateTime Today { get; set; }
}
