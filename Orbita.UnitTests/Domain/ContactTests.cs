using Orbita.Domain.Crm;

namespace Orbita.UnitTests.Domain;

public sealed class ContactTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_NormalizesPhoneInstagramAndChannel()
    {
        var contact = Contact.Create(
            Guid.NewGuid(),
            "  Ana Pérez  ",
            Now,
            phone: "+57 300 123 4567",
            instagramUsername: "@Ana.Shop",
            email: "Ana@Shop.com",
            channel: "WhatsApp");

        Assert.Equal("Ana Pérez", contact.DisplayName);
        Assert.Equal("+573001234567", contact.Phone);
        Assert.Equal("ana.shop", contact.InstagramUsername);
        Assert.Equal("ana@shop.com", contact.Email);
        Assert.Equal("whatsapp", contact.Channel);
    }

    [Fact]
    public void Create_RejectsUnknownChannel()
    {
        Assert.Throws<ArgumentException>(() =>
            Contact.Create(Guid.NewGuid(), "Ana", Now, channel: "tiktok"));
    }

    [Fact]
    public void SetCustomField_StoresAndClearsByKey()
    {
        var contact = Contact.Create(Guid.NewGuid(), "Ana", Now);
        contact.SetCustomField("City", " Bogotá ", Now);
        contact.SetCustomField("city", "", Now.AddMinutes(1));

        Assert.Empty(contact.CustomFields);
    }

    [Fact]
    public void LinkContact_OnOpportunity_StoresId()
    {
        var tenantId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var deal = Opportunity.Create(tenantId, Guid.NewGuid(), Guid.NewGuid(), "Sitio", 10m, Now);
        deal.LinkContact(contactId, Now);

        Assert.Equal(contactId, deal.ContactId);
    }
}
