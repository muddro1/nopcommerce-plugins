using Nop.Plugin.Misc.CustomerProfile.Domain;

namespace Nop.Plugin.Misc.CustomerProfile.Logic;

public sealed record Recipient(string Email, string Name);

public static class ContactRecipientResolver
{
    /// <summary>
    /// Contacts that should get their own copy of an email in <paramref name="category"/>.
    /// Skips inactive contacts, contacts not subscribed to the category, the original recipient, and duplicates.
    /// </summary>
    public static IReadOnlyList<Recipient> Resolve(IEnumerable<CustomerContact> contacts, MessageCategory category, string? primaryEmail)
    {
        if (category == MessageCategory.None)
            return Array.Empty<Recipient>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(primaryEmail))
            seen.Add(primaryEmail.Trim());

        return contacts
            .Where(c => c.Active && c.Categories.HasFlag(category) && !string.IsNullOrWhiteSpace(c.Email))
            .Where(c => seen.Add(c.Email.Trim()))
            .Select(c => new Recipient(c.Email.Trim(), c.Name))
            .ToList();
    }
}
