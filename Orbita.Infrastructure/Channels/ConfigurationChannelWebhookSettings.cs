using Microsoft.Extensions.Configuration;
using Orbita.Application.Channels;

namespace Orbita.Infrastructure.Channels;

/// <summary>Reads the webhook settings from <c>Channels:Webhooks:PublicBaseUrl</c> and <c>Channels:Meta:VerifyToken</c>.</summary>
public sealed class ConfigurationChannelWebhookSettings(IConfiguration configuration) : IChannelWebhookSettings
{
    public string PublicBaseUrl => (configuration["Channels:Webhooks:PublicBaseUrl"] ?? string.Empty).TrimEnd('/');

    public string GlobalVerifyToken => configuration["Channels:Meta:VerifyToken"] ?? string.Empty;
}
