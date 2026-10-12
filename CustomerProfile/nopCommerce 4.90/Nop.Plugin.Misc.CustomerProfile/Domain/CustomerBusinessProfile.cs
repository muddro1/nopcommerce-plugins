using Nop.Core;

namespace Nop.Plugin.Misc.CustomerProfile.Domain;

/// <summary>B2B details for one customer; created on first save</summary>
public class CustomerBusinessProfile : BaseEntity
{
    public int CustomerId { get; set; }
    public string? BmwDealership { get; set; }
    public bool Net30Enabled { get; set; }
    public int Net30TermsDays { get; set; }
    public DateTime UpdatedOnUtc { get; set; }
}
