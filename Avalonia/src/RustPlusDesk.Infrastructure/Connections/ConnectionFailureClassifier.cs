using System.Text.RegularExpressions;
using RustPlusDesk.Core.Connections;

namespace RustPlusDesk.Infrastructure.Connections;

internal static partial class ConnectionFailureClassifier
{
    [GeneratedRegex(@"(?<!\d)418(?!\d)")]
    private static partial Regex StandaloneTeapot();

    [GeneratedRegex(@"(?<!\d)403(?!\d)")]
    private static partial Regex StandaloneForbidden();

    public static ConnectionFailure Response(string? message)
    {
        if (message is null) return ConnectionFailure.Unreachable;
        return message.Trim().Equals("not_found", StringComparison.OrdinalIgnoreCase) || IsDenied(message)
            ? ConnectionFailure.RePairRequired : ConnectionFailure.Unreachable;
    }

    public static ConnectionFailure Exception(Exception exception, bool companionSocket)
    {
        if (exception is ServerConnectionException classified) return classified.Failure;
        if (companionSocket && StandaloneForbidden().IsMatch(exception.Message)) return ConnectionFailure.CompanionLoginRequired;
        if (IsDenied(exception.Message)) return ConnectionFailure.RePairRequired;
        return exception.InnerException is not null ? Exception(exception.InnerException, companionSocket) : ConnectionFailure.Unreachable;
    }

    private static bool IsDenied(string message) => StandaloneTeapot().IsMatch(message)
        || message.Contains("Access Denied", StringComparison.OrdinalIgnoreCase)
        || message.Contains("Zugriff verweigert", StringComparison.OrdinalIgnoreCase)
        || message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)
        || message.Contains("Invalid Player Token", StringComparison.OrdinalIgnoreCase);
}
