using System.Diagnostics;
using System.Net.Mime;
using System.Text.Json;
using System.Text.Json.Serialization;
using BookingEngine.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BookingEngine.Api.Middlewares;

/// <summary>
/// Turns thrown exceptions into the standard <see cref="ErrorResponse" /> envelope.
/// </summary>
/// <remarks>
/// Endpoints signal failure by throwing rather than by returning a result, so this is the one
/// place that decides status codes.
/// </remarks>
public sealed partial record ExceptionHandlingMiddleware(
    ILogger<ExceptionHandlingMiddleware> Logger,
    IHostEnvironment Environment
) : IMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
            {
                throw;
            }

            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        int statusCode = ResolveStatusCode(ex);
        string traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

        if (statusCode >= 500)
        {
            LogError(Logger, traceId, ex);
        }
        else
        {
            LogWarning(Logger, statusCode, traceId, ex);
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = MediaTypeNames.Application.Json;

        ErrorResponse response = new(
            traceId,
            ResolveMessage(statusCode),
            Environment.IsDevelopment() ? ex.ToString() : null
        );

        await context.Response.WriteAsJsonAsync(response, JsonOptions);
    }

    private static int ResolveStatusCode(Exception ex)
    {
        // A serialization failure that outlived the execution strategy's retries means the
        // caller genuinely lost the race, so it is a conflict rather than a server error.
        return ex switch
        {
            _ when IsSerializationFailure(ex) => StatusCodes.Status409Conflict,
            BookingConflictException or DbUpdateConcurrencyException =>
                StatusCodes.Status409Conflict,
            ForbiddenException => StatusCodes.Status403Forbidden,
            ArgumentException => StatusCodes.Status400BadRequest,
            KeyNotFoundException or EntityNotFoundException =>
                StatusCodes.Status404NotFound,
            _ => StatusCodes.Status500InternalServerError,
        };
    }

    private static bool IsSerializationFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (
                current is PostgresException
                {
                    SqlState: PostgresErrorCodes.SerializationFailure
                        or PostgresErrorCodes.DeadlockDetected,
                }
            )
            {
                return true;
            }
        }

        return false;
    }

    private static string ResolveMessage(int statusCode)
    {
        return statusCode switch
        {
            StatusCodes.Status400BadRequest => "The request contains invalid data.",
            StatusCodes.Status403Forbidden =>
                "You are not allowed to perform this operation.",
            StatusCodes.Status404NotFound => "The requested resource was not found.",
            StatusCodes.Status409Conflict =>
                "The request conflicts with the current state of the resource.",
            _ => "An unexpected error occurred.",
        };
    }

    [LoggerMessage(
        LogLevel.Warning,
        "Request failed with status {StatusCode}. TraceId: {TraceId}"
    )]
    static partial void LogWarning(
        ILogger<ExceptionHandlingMiddleware> logger,
        int statusCode,
        string traceId,
        Exception exception
    );

    [LoggerMessage(LogLevel.Error, "Unhandled exception. TraceId: {TraceId}")]
    static partial void LogError(
        ILogger<ExceptionHandlingMiddleware> logger,
        string traceId,
        Exception exception
    );
}
