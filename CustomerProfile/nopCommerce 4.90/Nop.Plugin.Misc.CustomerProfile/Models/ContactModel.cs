using Nop.Plugin.Misc.CustomerProfile.Domain;

namespace Nop.Plugin.Misc.CustomerProfile.Models;

/// <summary>Posted from both the admin card and the storefront Business details page</summary>
public class ContactModel
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Title { get; set; }
    public bool Active { get; set; } = true;
    public List<int> Categories { get; set; } = new();

    public MessageCategory CategoryFlags =>
        Categories.Aggregate(MessageCategory.None, (all, c) => all | ((MessageCategory)c & AllFlags));

    private static readonly MessageCategory AllFlags = MessageCategories.All.Aggregate(MessageCategory.None, (a, c) => a | c);

    public static ContactModel From(CustomerContact contact) => new()
    {
        Id = contact.Id,
        Name = contact.Name,
        Email = contact.Email,
        Title = contact.Title,
        Active = contact.Active,
        Categories = MessageCategories.All.Where(c => contact.Categories.HasFlag(c)).Select(c => (int)c).ToList()
    };
}
