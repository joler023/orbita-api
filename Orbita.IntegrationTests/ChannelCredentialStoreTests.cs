using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orbita.Application.Channels;
using Orbita.Infrastructure.Channels;
using Orbita.Infrastructure.Persistence;

namespace Orbita.IntegrationTests;

/// <summary>
/// The local stand-in for Secrets Manager (ORB-B01): what goes in must come back out
/// unchanged, and what sits in the table must never be the plaintext.
/// </summary>
public sealed class ChannelCredentialStoreTests : IClassFixture<TenantsApiFixture>
{
    private readonly TenantsApiFixture _fixture;

    public ChannelCredentialStoreTests(TenantsApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task StoreThenGet_RoundTripsThePlaintextWithoutPersistingIt()
    {
        using var scope = _fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IChannelCredentialStore>();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrbitaDbContext>();
        const string secret = "EAAB-very-secret-access-token";

        var reference = await store.StoreAsync(secret, CancellationToken.None);

        Assert.StartsWith(DataProtectionChannelCredentialStore.ReferencePrefix, reference);
        var id = Guid.Parse(reference[DataProtectionChannelCredentialStore.ReferencePrefix.Length..]);
        var row = await dbContext.ChannelCredentials.AsNoTracking().SingleAsync(c => c.Id == id);
        Assert.NotEqual(secret, row.Ciphertext);
        Assert.DoesNotContain(secret, row.Ciphertext);
        Assert.Equal(secret, await store.GetAsync(reference, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_RemovesTheRowAndIsIdempotent()
    {
        using var scope = _fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IChannelCredentialStore>();
        var reference = await store.StoreAsync("to-be-deleted", CancellationToken.None);

        await store.DeleteAsync(reference, CancellationToken.None);
        await store.DeleteAsync(reference, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetAsync(reference, CancellationToken.None));
    }

    [Fact]
    public async Task Get_WithAForeignReference_ThrowsInsteadOfGuessing()
    {
        using var scope = _fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IChannelCredentialStore>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.GetAsync("arn:aws:secretsmanager:us-east-1:123456789012:secret:orbita/channel", CancellationToken.None));
    }
}
