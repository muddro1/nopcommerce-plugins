using Nop.Core;

namespace Nop.Plugin.Misc.CustomerProfile.Domain;

/// <summary>Payment terms recorded when a Net 30 order is created</summary>
public class Net30Order : BaseEntity
{
    public int OrderId { get; set; }
    public int CustomerId { get; set; }
    public int TermsDays { get; set; }

    /// <summary>Date only, store time zone</summary>
    public DateTime DueOn { get; set; }

    public DateTime? OverdueNoticeSentOnUtc { get; set; }
}
