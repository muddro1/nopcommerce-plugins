namespace Nop.Plugin.Misc.CustomerProfile.Domain;

public enum CertificateState
{
    PendingReview = 0,
    Approved = 10,
    Rejected = 20,

    /// <summary>A newer certificate was approved</summary>
    Superseded = 30,

    Expired = 40
}
