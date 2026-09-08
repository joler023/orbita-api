using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Orbita.Application.Billing;
using Orbita.Application.Channels;
using Orbita.Application.Crm;
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
