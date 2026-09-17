using System.Data.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Orbita.Application.Ai;
using Orbita.Application.Billing;
using Orbita.Application.Channels;
using Orbita.Application.Crm;
using Orbita.Application.Identity;
using Orbita.Application.Inbox;
using Orbita.Application.Media;
using Orbita.Application.Tenants;

namespace Orbita.Api.ErrorHandling;

public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title) = exception switch
        {
            TenantSlugAlreadyExistsException => (StatusCodes.Status409Conflict, "Tenant slug already exists"),
            EmailAlreadyRegisteredException => (StatusCodes.Status409Conflict, "Email already registered"),
            InvalidCredentialsException => (StatusCodes.Status401Unauthorized, "Invalid credentials"),
            InvalidRefreshTokenException => (StatusCodes.Status401Unauthorized, "Invalid refresh token"),
            MembershipAlreadyExistsException => (StatusCodes.Status409Conflict, "Membership already exists"),
            ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden"),
            InvitationNotFoundException => (StatusCodes.Status404NotFound, "Invitation not found"),
            InvalidInvitationException => (StatusCodes.Status400BadRequest, "Invalid invitation"),
            InvalidPasswordResetException => (StatusCodes.Status400BadRequest, "Invalid password reset"),
            MemberNotFoundException => (StatusCodes.Status404NotFound, "Member not found"),
            CannotRemoveLastOwnerException => (StatusCodes.Status409Conflict, "Cannot remove last owner"),
            PlanNotFoundException => (StatusCodes.Status404NotFound, "Plan not found"),
            SubscriptionNotFoundException => (StatusCodes.Status404NotFound, "Subscription not found"),
            SubscriptionAlreadyExistsException => (StatusCodes.Status409Conflict, "Subscription already exists"),
            // 401, not 400: a signature mismatch means "prove you're Meta/Stripe/Wompi",
            // the same concept as any other failed authentication (ORB-B02).
            InvalidWebhookSignatureException => (StatusCodes.Status401Unauthorized, "Invalid webhook signature"),
            TwoFactorRequiredException => (StatusCodes.Status401Unauthorized, "Two-factor code required"),
            InvalidTwoFactorCodeException => (StatusCodes.Status400BadRequest, "Invalid two-factor code"),
            TwoFactorSetupNotStartedException => (StatusCodes.Status400BadRequest, "Two-factor setup not started"),
            TwoFactorNotEnabledException => (StatusCodes.Status409Conflict, "Two-factor not enabled"),
            ChannelAccountNotFoundException => (StatusCodes.Status404NotFound, "Channel account not found"),
            ChannelAlreadyConnectedException => (StatusCodes.Status409Conflict, "Channel already connected"),
            ChannelConnectionFailedException => (StatusCodes.Status502BadGateway, "Channel connection failed"),
            WebhookVerificationFailedException => (StatusCodes.Status403Forbidden, "Webhook verification failed"),
            PipelineNotFoundException => (StatusCodes.Status404NotFound, "Pipeline not found"),
            StageNotFoundException => (StatusCodes.Status404NotFound, "Pipeline stage not found"),
            CannotDeleteLastPipelineException => (StatusCodes.Status409Conflict, "Cannot delete last pipeline"),
            CannotDeleteLastStageException => (StatusCodes.Status409Conflict, "Cannot delete last stage"),
            StageHasOpportunitiesException => (StatusCodes.Status409Conflict, "Stage has opportunities"),
            PipelineHasOpportunitiesException => (StatusCodes.Status409Conflict, "Pipeline has opportunities"),
            InvalidStageRelocateException => (StatusCodes.Status400BadRequest, "Invalid stage relocate"),
            OpportunityNotFoundException => (StatusCodes.Status404NotFound, "Opportunity not found"),
            AssigneeNotInTenantException => (StatusCodes.Status400BadRequest, "Assignee not in tenant"),
            ContactNotFoundException => (StatusCodes.Status404NotFound, "Contact not found"),
            ContactAlreadyExistsException => (StatusCodes.Status409Conflict, "Contact already exists"),
            ContactFieldAlreadyExistsException => (StatusCodes.Status409Conflict, "Contact field already exists"),
            ConversationNotFoundException => (StatusCodes.Status404NotFound, "Conversation not found"),
            ChannelNotConnectedException => (StatusCodes.Status409Conflict, "Channel not connected"),
            MessageNotFoundException => (StatusCodes.Status404NotFound, "Message not found"),
            InvalidMediaSignatureException => (StatusCodes.Status403Forbidden, "Invalid media link"),
            MediaTooLargeException => (StatusCodes.Status413PayloadTooLarge, "Media too large"),
            UnsupportedMediaTypeException => (StatusCodes.Status415UnsupportedMediaType, "Unsupported media type"),
            MediaKeyNotFoundException => (StatusCodes.Status404NotFound, "Media not found"),
            ServiceWindowClosedException => (StatusCodes.Status409Conflict, "Service window closed"),
            TemplateNotApprovedException => (StatusCodes.Status409Conflict, "Template not approved"),
            TemplateNotFoundException => (StatusCodes.Status404NotFound, "Template not found"),
            TemplateAlreadyExistsException => (StatusCodes.Status409Conflict, "Template already exists"),
            MessageNotRetryableException => (StatusCodes.Status409Conflict, "Message cannot be retried"),
            AiAgentNotFoundException => (StatusCodes.Status404NotFound, "AI agent not found"),
            AgentHasHistoryException => (StatusCodes.Status409Conflict, "Assistant has history"),
            CannotDeleteLastAgentException => (StatusCodes.Status409Conflict, "Cannot delete last agent"),
            // The title is the key the dashboard maps its copy by, so renaming it degrades
            // that copy to the generic error in silence. AgentTestCasesApiTests pins it.
            TooManyTestCasesException => (StatusCodes.Status409Conflict, "Too many test cases"),
            NothingToPublishException => (StatusCodes.Status409Conflict, "Nothing to publish"),
            KnowledgeDocumentNotFoundException => (StatusCodes.Status404NotFound, "Knowledge document not found"),
            UnsupportedDocumentTypeException => (StatusCodes.Status400BadRequest, "Unsupported document type"),
            // 413 rather than 400: the request was well formed, it was just too big.
            DocumentTooLargeException => (StatusCodes.Status413PayloadTooLarge, "Document too large"),
            // 502 rather than 500: the request was fine and this API is fine — the model
            // provider behind it is not, and every configured one was already tried
            // (ResilientLlmProvider). The caller can meaningfully retry.
            LlmProviderException => (StatusCodes.Status502BadGateway, "Model provider unavailable"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request"),
            // 503, not 500: a dropped connection or a database that went away is "try
            // again", not "there is a bug here". Npgsql already classifies these
            // (DbException.IsTransient), and the distinction is what tells an operator
            // whether to look at the code or at the network.
            DbException { IsTransient: true } => (StatusCodes.Status503ServiceUnavailable, "Database unavailable"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error"),
        };

        // Nothing logged the exception before this: a 500 left the caller with "Unexpected
        // error" and left the log with nothing to read, so the only evidence of a real
        // failure was whatever EF happened to print on its way out.
        logger.Log(
            statusCode >= StatusCodes.Status500InternalServerError ? LogLevel.Error : LogLevel.Debug,
            exception,
            "{Method} {Path} failed with {StatusCode} ({Title}).",
            httpContext.Request.Method,
            httpContext.Request.Path.Value,
            statusCode,
            title);

        httpContext.Response.StatusCode = statusCode;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = DetailFor(exception),
            },
        });
    }

    /// <summary>
    /// .NET appends " (Parameter 'name')", in English, to an <see cref="ArgumentException"/>'s
    /// message whenever a parameter name was given — which is always, in this codebase. The
    /// Spanish messages the domain writes for the dashboard would otherwise reach it with an
    /// English developer note glued on. Stripped here, once, rather than by giving up
    /// <c>nameof</c> everywhere; if the runtime ever localizes the suffix, the match simply
    /// misses and the message goes out unchanged.
    /// </summary>
    internal static string DetailFor(Exception exception)
        => exception is ArgumentException { ParamName: { } parameter } argument
            ? argument.Message.Replace($" (Parameter '{parameter}')", string.Empty, StringComparison.Ordinal)
            : exception.Message;
}
