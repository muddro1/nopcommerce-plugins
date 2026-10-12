namespace Nop.Plugin.Misc.CustomerProfile.Domain;

/// <summary>Why a customer is tax exempt, as claimed on a certificate</summary>
public enum ExemptionReason
{
    /// <summary>Certificates uploaded before 1.0.8 had no reason</summary>
    NotSet = 0,

    WholesaleResale = 10,
    Government = 20,
    Other = 30
}

public static class ExemptionReasons
{
    /// <summary>The choices offered in the drop-downs, in display order</summary>
    public static readonly ExemptionReason[] All =
    {
        ExemptionReason.WholesaleResale,
        ExemptionReason.Government,
        ExemptionReason.Other
    };

    /// <summary>Whether <paramref name="id"/> is one of <see cref="All"/>; NotSet and unknown ids are not</summary>
    public static bool IsChoice(int id) => All.Any(r => (int)r == id);
}
