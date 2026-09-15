namespace Orbita.Application.Ai;

/// <summary>
/// What a given model charges. ORB-C01 requires every call to record its cost, and
/// <c>ai_runs.cost_usd</c> is what ORB-A13's usage metering and ORB-A12's billing are
/// eventually computed from — so an unpriced model silently under-reports real spend.
///
/// Prices live in configuration rather than in code because they change often (OpenAI
/// cut GPT-5.6 Luna by 80% within three weeks of release) and because each model has
/// its own rate: a single per-provider price stops being meaningful the moment one
/// gateway serves several models, which is exactly how this project uses it.
/// </summary>
public interface ILlmPricing
{
    /// <summary>
    /// Cost in USD for a call. Returns <c>0</c> for a model with no configured price,
    /// which is correct for a locally hosted model and a silent under-count for a paid
    /// one — see <c>ConfigurationLlmPricing</c> for how that is surfaced.
    /// </summary>
    decimal CostFor(string model, int tokensIn, int tokensOut);
}
