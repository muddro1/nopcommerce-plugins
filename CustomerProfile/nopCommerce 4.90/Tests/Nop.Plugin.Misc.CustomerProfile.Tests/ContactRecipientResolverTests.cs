using FluentAssertions;
using Nop.Plugin.Misc.CustomerProfile.Domain;
using Nop.Plugin.Misc.CustomerProfile.Logic;
using NUnit.Framework;

namespace Nop.Plugin.Misc.CustomerProfile.Tests;

public class ContactRecipientResolverTests
{
    private static CustomerContact Contact(string email, MessageCategory categories, bool active = true, string name = "Pat") =>
        new() { Email = email, Name = name, Categories = categories, Active = active };

    [Test]
    public void Includes_active_subscribed_contact()
    {
        var result = ContactRecipientResolver.Resolve(new[] { Contact("ap@dealer.com", MessageCategory.Shipping, name: "AP") },
            MessageCategory.Shipping, "owner@dealer.com");

        result.Should().ContainSingle().Which.Should().Be(new Recipient("ap@dealer.com", "AP"));
    }

    [Test]
    public void Skips_inactive_contact()
    {
        ContactRecipientResolver.Resolve(new[] { Contact("ap@dealer.com", MessageCategory.Shipping, active: false) },
            MessageCategory.Shipping, "owner@dealer.com").Should().BeEmpty();
    }

    [Test]
    public void Skips_contact_not_subscribed_to_category()
    {
        ContactRecipientResolver.Resolve(new[] { Contact("ap@dealer.com", MessageCategory.InvoicesPayments) },
            MessageCategory.Shipping, "owner@dealer.com").Should().BeEmpty();
    }

    [Test]
    public void Skips_contact_with_the_primary_address_ignoring_case_and_spaces()
    {
        ContactRecipientResolver.Resolve(new[] { Contact(" Owner@Dealer.com ", MessageCategory.Shipping) },
            MessageCategory.Shipping, "owner@dealer.com").Should().BeEmpty();
    }

    [Test]
    public void Deduplicates_contacts_sharing_an_address()
    {
        var result = ContactRecipientResolver.Resolve(new[]
        {
            Contact("ap@dealer.com", MessageCategory.Shipping, name: "First"),
            Contact("AP@dealer.com", MessageCategory.Shipping, name: "Second")
        }, MessageCategory.Shipping, "owner@dealer.com");

        result.Should().ContainSingle().Which.Name.Should().Be("First");
    }

    [Test]
    public void None_category_sends_nothing()
    {
        ContactRecipientResolver.Resolve(new[] { Contact("ap@dealer.com", MessageCategory.Shipping) },
            MessageCategory.None, "owner@dealer.com").Should().BeEmpty();
    }

    [Test]
    public void Multi_category_contact_matches_each_of_its_categories()
    {
        var contacts = new[] { Contact("ap@dealer.com", MessageCategory.Shipping | MessageCategory.InvoicesPayments) };

        ContactRecipientResolver.Resolve(contacts, MessageCategory.Shipping, null).Should().HaveCount(1);
        ContactRecipientResolver.Resolve(contacts, MessageCategory.InvoicesPayments, null).Should().HaveCount(1);
        ContactRecipientResolver.Resolve(contacts, MessageCategory.Returns, null).Should().BeEmpty();
    }

    [Test]
    public void Skips_blank_email()
    {
        ContactRecipientResolver.Resolve(new[] { Contact("  ", MessageCategory.Shipping) },
            MessageCategory.Shipping, null).Should().BeEmpty();
    }
}
