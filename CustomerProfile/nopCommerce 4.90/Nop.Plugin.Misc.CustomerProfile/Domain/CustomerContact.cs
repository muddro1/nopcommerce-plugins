using Nop.Core;

namespace Nop.Plugin.Misc.CustomerProfile.Domain;

/// <summary>An extra email recipient for a customer's account. Not a login.</summary>
public class CustomerContact : BaseEntity
{
    public int CustomerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Title { get; set; }
    public int CategoriesId { get; set; }
    public bool Active { get; set; } = true;
    public DateTime CreatedOnUtc { get; set; }
    public int CreatedByCustomerId { get; set; }
    public DateTime? UpdatedOnUtc { get; set; }
    public int? UpdatedByCustomerId { get; set; }

    public MessageCategory Categories
    {
        get => (MessageCategory)CategoriesId;
        set => CategoriesId = (int)value;
    }
}
