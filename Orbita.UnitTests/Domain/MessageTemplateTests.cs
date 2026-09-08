using Orbita.Domain.Channels;
using Orbita.Domain.Inbox;

namespace Orbita.UnitTests.Domain;

public sealed class MessageTemplateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_StartsAsDraft()
    {
        var template = MessageTemplate.Create(Guid.NewGuid(), Guid.NewGuid(), "order_confirmation", MessageCategory.Utility, "es", "Hola {{1}}, tu pedido {{2}} está listo.", Now);

        Assert.Equal(TemplateStatus.Draft, template.Status);
        Assert.False(template.IsApproved);
        Assert.Equal("es", template.Language);
    }

    [Fact]
    public void Create_WithNoLanguage_DefaultsToSpanish()
    {
        var template = MessageTemplate.Create(Guid.NewGuid(), Guid.NewGuid(), "greeting", MessageCategory.Utility, null, "Hola", Now);

        Assert.Equal("es", template.Language);
    }

    [Fact]
    public void ApplyMetaStatus_ToApproved_SetsApprovedAtOnce()
    {
        var template = MessageTemplate.Create(Guid.NewGuid(), Guid.NewGuid(), "greeting", MessageCategory.Utility, "es", "Hola", Now);

        template.ApplyMetaStatus(TemplateStatus.Approved, null, Now);
        template.ApplyMetaStatus(TemplateStatus.Approved, null, Now.AddDays(1));

        Assert.True(template.IsApproved);
        Assert.Equal(Now, template.ApprovedAt);
    }

    [Fact]
    public void ApplyMetaStatus_ToRejected_StoresTheReason()
    {
        var template = MessageTemplate.Create(Guid.NewGuid(), Guid.NewGuid(), "greeting", MessageCategory.Utility, "es", "Hola", Now);

        template.ApplyMetaStatus(TemplateStatus.Rejected, "Contenido promocional en categoría utility", Now);

        Assert.Equal(TemplateStatus.Rejected, template.Status);
        Assert.Equal("Contenido promocional en categoría utility", template.RejectedReason);
    }

    [Fact]
    public void Render_SubstitutesPositionalPlaceholders()
    {
        var template = MessageTemplate.Create(Guid.NewGuid(), Guid.NewGuid(), "order_confirmation", MessageCategory.Utility, "es", "Hola {{1}}, tu pedido {{2}} está listo.", Now);

        var rendered = template.Render(["Ana", "#123"]);

        Assert.Equal("Hola Ana, tu pedido #123 está listo.", rendered);
    }

    [Fact]
    public void Render_WithWrongVariableCount_Throws()
    {
        var template = MessageTemplate.Create(Guid.NewGuid(), Guid.NewGuid(), "greeting", MessageCategory.Utility, "es", "Hola {{1}}", Now);

        Assert.Throws<ArgumentException>(() => template.Render(["Ana", "extra"]));
        Assert.Throws<ArgumentException>(() => template.Render([]));
    }

    [Fact]
    public void Render_WithNoPlaceholders_AcceptsNoVariables()
    {
        var template = MessageTemplate.Create(Guid.NewGuid(), Guid.NewGuid(), "greeting", MessageCategory.Utility, "es", "Hola, gracias por escribirnos.", Now);

        var rendered = template.Render([]);

        Assert.Equal("Hola, gracias por escribirnos.", rendered);
    }
}
