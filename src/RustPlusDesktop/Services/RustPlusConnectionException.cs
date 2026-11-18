using System;
using System.Reflection;
using System.Linq;
using System.Text.RegularExpressions;
using RustPlusDesk.Models;

namespace RustPlusDesk.Services;

public sealed class RustPlusConnectionException(ConnectionFailureKind kind, string message) : Exception(message)
{
    public ConnectionFailureKind Kind { get; } = kind;

    public static ConnectionFailureKind Classify(Exception error)
    {
        if (error is RustPlusConnectionException connection) return connection.Kind;
        if (error is TargetInvocationException { InnerException: { } inner }) return Classify(inner);
        if (error is AggregateException aggregate) return aggregate.Flatten().InnerExceptions.Select(Classify).Max();
        if (RustPlusClientReal.IsAccessDeniedError(error)) return ConnectionFailureKind.RePairRequired;
        return ConnectionFailureKind.Unavailable;
    }

    internal static bool IsLoginRejection(Exception error) =>
        Regex.IsMatch(error.ToString(), @"(?<!\d)(401|403)(?!\d)");

    internal static ConnectionFailureKind FromServerError(string code) => code.Trim().ToLowerInvariant() switch
    {
        "not_found" => ConnectionFailureKind.RePairRequired,
        "unhandled" => ConnectionFailureKind.Unsupported,
        "timed_out" or "timeout" or "connection_error" => ConnectionFailureKind.Unavailable,
        _ => ConnectionFailureKind.ServerError
    };

    internal static Guid RequireCompanionCredentials(string? serverId, string? authToken)
    {
        if (!Guid.TryParse(serverId, out var id) || id == Guid.Empty)
            throw new RustPlusConnectionException(ConnectionFailureKind.MissingCompanionId, "Server pairing has no companion ID.");
        if (string.IsNullOrWhiteSpace(authToken))
            throw new RustPlusConnectionException(ConnectionFailureKind.SignInRequired, "Rust+ companion login is missing.");
        return id;
    }

    public static bool CanRetry(ConnectionFailureKind? kind) =>
        kind is ConnectionFailureKind.Unavailable or ConnectionFailureKind.ServerError;

    internal static async System.Threading.Tasks.Task<bool> RetryAsync(
        Func<System.Threading.Tasks.Task<bool>> attempt, Func<bool> isCurrent,
        Func<ConnectionFailureKind?> failure, Func<int, System.Threading.Tasks.Task> wait)
    {
        for (int index = 0; index < 3 && isCurrent(); index++)
        {
            if (index > 0)
            {
                await wait(index * 1000);
                if (!isCurrent()) return false;
            }
            if (await attempt()) return true;
            if (!CanRetry(failure())) return false;
        }
        return false;
    }
}
