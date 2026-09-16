using System.Data.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Orbita.Api.ErrorHandling;
using Orbita.Application.Ai;

namespace Orbita.IntegrationTests.ErrorHandling;

/// <summary>
/// A dropped Postgres connection during login (see HANDOFF, "500 intermitente en el
/// login") looked identical to a real bug: same 500, same generic detail, and nothing in
/// the log said which one happened. These pin the two things that changed: a transient
/// <see cref="DbException"/> answers 503 instead of 500, and every 5xx now leaves a trace.
/// </summary>
public sealed class GlobalExceptionHandlerTests
{
    private sealed class TransientDbException : DbException
    {
        public override bool IsTransient => true;
    }

    private sealed class NonTransientDbException : DbException
    {
        public override bool IsTransient => false;
    }

    private sealed class RecordingProblemDetailsService : IProblemDetailsService
    {
        public Microsoft.AspNetCore.Mvc.ProblemDetails? Written { get; private set; }

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            Written = context.ProblemDetails;
            return ValueTask.FromResult(true);
        }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Written = context.ProblemDetails;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingLogger : ILogger<GlobalExceptionHandler>
    {
        public LogLevel? LoggedLevel { get; private set; }
        public Exception? LoggedException { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            LoggedLevel = logLevel;
            LoggedException = exception;
        }
    }

    private static async Task<(Microsoft.AspNetCore.Mvc.ProblemDetails Problem, int StatusCode, LogLevel? LoggedAt, Exception? LoggedException)> HandleAsync(
        Exception exception)
    {
        var problemDetailsService = new RecordingProblemDetailsService();
        var logger = new RecordingLogger();
        var handler = new GlobalExceptionHandler(problemDetailsService, logger);
        var httpContext = new DefaultHttpContext();

        await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        return (problemDetailsService.Written!, httpContext.Response.StatusCode, logger.LoggedLevel, logger.LoggedException);
    }

    [Fact]
    public async Task A_transient_db_exception_answers_503_not_500()
    {
        var (problem, statusCode, _, _) = await HandleAsync(new TransientDbException());

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, statusCode);
        Assert.Equal("Database unavailable", problem.Title);
    }

    [Fact]
    public async Task A_non_transient_db_exception_is_still_an_unexpected_error()
    {
        var (problem, statusCode, _, _) = await HandleAsync(new NonTransientDbException());

        Assert.Equal(StatusCodes.Status500InternalServerError, statusCode);
        Assert.Equal("Unexpected error", problem.Title);
    }

    [Fact]
    public async Task A_5xx_is_logged_with_its_exception_so_it_leaves_a_trace()
    {
        var exception = new InvalidOperationException("boom");

        var (_, _, loggedAt, loggedException) = await HandleAsync(exception);

        Assert.Equal(LogLevel.Error, loggedAt);
        Assert.Same(exception, loggedException);
    }

    [Fact]
    public async Task A_4xx_is_logged_below_error_since_it_is_not_a_bug()
    {
        var (_, _, loggedAt, _) = await HandleAsync(new AiAgentNotFoundException());

        Assert.NotEqual(LogLevel.Error, loggedAt);
    }
}
