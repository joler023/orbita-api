using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Orbita.Application.Ai;
using Orbita.Application.Billing;
using Orbita.Application.Identity;
using Orbita.Application.Tenants;

namespace Orbita.Api.ErrorHandling;

public sealed class GlobalExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
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
            InvalidWebhookSignatureException => (StatusCodes.Status400BadRequest, "Invalid webhook signature"),
            TwoFactorRequiredException => (StatusCodes.Status401Unauthorized, "Two-factor code required"),
            InvalidTwoFactorCodeException => (StatusCodes.Status400BadRequest, "Invalid two-factor code"),
            TwoFactorSetupNotStartedException => (StatusCodes.Status400BadRequest, "Two-factor setup not started"),
            TwoFactorNotEnabledException => (StatusCodes.Status409Conflict, "Two-factor not enabled"),
            AiAgentNotFoundException => (StatusCodes.Status404NotFound, "AI agent not found"),
            CannotDeleteLastAgentException => (StatusCodes.Status409Conflict, "Cannot delete last agent"),
            KnowledgeDocumentNotFoundException => (StatusCodes.Status404NotFound, "Knowledge document not found"),
            UnsupportedDocumentTypeException => (StatusCodes.Status400BadRequest, "Unsupported document type"),
            // 413 rather than 400: the request was well formed, it was just too big.
            DocumentTooLargeException => (StatusCodes.Status413PayloadTooLarge, "Document too large"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error"),
        };

        httpContext.Response.StatusCode = statusCode;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = exception.Message,
            },
        });
    }
}
