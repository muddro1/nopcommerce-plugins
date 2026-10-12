using FluentAssertions;
using Moq;
using Nop.Plugin.Misc.CustomerProfile.Domain;
using Nop.Plugin.Misc.CustomerProfile.Models;
using Nop.Plugin.Misc.CustomerProfile.Services;
using Nop.Services.Localization;
using NUnit.Framework;

namespace Nop.Plugin.Misc.CustomerProfile.Tests;

public class ContactEditorTests
{
    private const int CustomerId = 5;
    private CustomerContact _contact;
    private CustomerContact? _updated;
    private ContactEditor _editor;

    [SetUp]
    public void SetUp()
    {
        _updated = null;
        _contact = new CustomerContact
        {
            Id = 12, CustomerId = CustomerId, Name = "AP", Email = "ap@example.com",
            Categories = MessageCategory.OrderConfirmations | MessageCategory.Shipping
        };

        var contacts = new Mock<ICustomerContactService>();
        contacts.Setup(c => c.GetByIdAsync(12)).ReturnsAsync(_contact);
        contacts.Setup(c => c.UpdateAsync(It.IsAny<CustomerContact>())).Callback((CustomerContact c) => _updated = c).Returns(Task.CompletedTask);

        var localization = new Mock<ILocalizationService>();
        localization.Setup(l => l.GetResourceAsync(It.IsAny<string>())).ReturnsAsync((string key) => key);

        _editor = new ContactEditor(contacts.Object, localization.Object);
    }

    [Test]
    public async Task Update_replaces_the_categories()
    {
        // As the admin card posts it: Categories=4&Categories=8
        var errors = await _editor.SaveAsync(CustomerId, new ContactModel
        {
            Id = 12, Name = "AP", Email = "ap@example.com", Active = true, Categories = new List<int> { 4, 8 }
        }, editorCustomerId: 1);

        errors.Should().BeEmpty();
        _updated.Should().NotBeNull();
        _updated!.Categories.Should().Be(MessageCategory.Shipping | MessageCategory.Cancellations);
        _updated.CategoriesId.Should().Be(12);
    }

    [Test]
    public async Task Update_of_another_customers_contact_is_refused()
    {
        var errors = await _editor.SaveAsync(CustomerId + 1, new ContactModel
        {
            Id = 12, Name = "AP", Email = "ap@example.com", Categories = new List<int> { 4 }
        }, editorCustomerId: 1);

        errors.Should().ContainSingle();
        _updated.Should().BeNull();
    }
}
