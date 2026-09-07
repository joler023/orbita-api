using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
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
            MemberNotFoundException => (StatusCodes.Status404NotFound, "Member not found"),
            CannotRemoveLastOwnerException => (StatusCodes.Status409Conflict, "Cannot remove last owner"),
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
