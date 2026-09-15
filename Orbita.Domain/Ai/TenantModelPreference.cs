using Orbita.Domain.Common;

namespace Orbita.Domain.Ai;

/// <summary>
/// One tenant's choice of model for one task on one provider (ORB-C13's "configurable por
/// tenant").
///
/// <para><b>Why the provider is part of the key.</b> Model ids are not portable:
/// <c>openai/gpt-5.6-luna</c> means something to a hosted gateway and nothing to a local
/// Ollama. A preference that named only a task would break the moment the resilience layer
/// failed over, which is the exact bug ORB-C01 was refactored to avoid.</para>
///
/// <para><b>Why a row rather than a column on <c>tenants</c>.</b> The number of tasks grows
/// (ORB-C01 has three today), and a tenant only overrides the ones it cares about — an
/// absent row means "use the deployment default", which is different from "use null".</para>
///
/// Nothing here decides <em>which</em> model is cheap or good: that judgement is the
/// owner's, and the saving it produces shows up in <c>ai_runs.cost_usd</c>, which already
/// records the model each call actually used.
/// </summary>
public sealed class TenantModelPreference : Entity
{
    private TenantModelPreference(
        Guid id,
        Guid tenantId,
        string providerName,
        LlmTask task,
        string model,
        DateTimeOffset updatedAt)
        : base(id)
    {
        TenantId = tenantId;
        ProviderName = providerName;
        Task = task;
        Model = model;
        UpdatedAt = updatedAt;
    }

    public Guid TenantId { get; }

    /// <summary>Matches <c>ILlmProvider.Name</c> — the provider this model id is valid for.</summary>
    public string ProviderName { get; }

    public LlmTask Task { get; }

    public string Model { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public const int ProviderNameMaxLength = 60;

    public const int ModelMaxLength = 120;

    public static TenantModelPreference Create(
        Guid tenantId,
        string providerName,
        LlmTask task,
        string model,
        DateTimeOffset now)
    {
        ValidateProviderName(providerName);
        ValidateModel(model);

        return new TenantModelPreference(Guid.NewGuid(), tenantId, providerName.Trim(), task, model.Trim(), now);
    }

    public void ChangeModel(string model, DateTimeOffset now)
    {
        ValidateModel(model);

        Model = model.Trim();
        UpdatedAt = now;
    }

    private static void ValidateProviderName(string providerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        if (providerName.Trim().Length > ProviderNameMaxLength)
        {
            throw new ArgumentException($"Must be at most {ProviderNameMaxLength} characters.", nameof(providerName));
        }
    }

    private static void ValidateModel(string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        if (model.Trim().Length > ModelMaxLength)
        {
            throw new ArgumentException($"Must be at most {ModelMaxLength} characters.", nameof(model));
        }
    }
}
