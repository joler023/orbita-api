using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Orbita.Domain.Ai;

/// <summary>
/// ORB-C12: everything a cached answer depended on, hashed. Two moments with the same
/// fingerprint would have produced the same prompt for the same question, so reusing the
/// answer between them is safe; any difference — a published instruction, a tool switched
/// on, a document uploaded, reindexed or deleted — produces a new fingerprint and retires
/// every answer given before it.
/// </summary>
public static class AgentAnswerFingerprint
{
    public static string Compute(AiAgent agent, int documentCount, DateTimeOffset? latestDocumentChange)
    {
        ArgumentNullException.ThrowIfNull(agent);

        var parts = new[]
        {
            agent.SystemPrompt,
            agent.Temperature.ToString(CultureInfo.InvariantCulture),
            agent.MaxTokens.ToString(CultureInfo.InvariantCulture),
            string.Join('\u001f', agent.Tools.Order(StringComparer.Ordinal)),
            string.Join('\u001f', agent.BlockedTopics.Order(StringComparer.Ordinal)),
            documentCount.ToString(CultureInfo.InvariantCulture),
            latestDocumentChange?.UtcTicks.ToString(CultureInfo.InvariantCulture) ?? "-",
        };

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001e', parts)));

        return Convert.ToHexStringLower(hash);
    }
}
