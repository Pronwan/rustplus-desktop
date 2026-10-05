using System;
using System.Threading;
using System.Threading.Tasks;

namespace RustPlusDesk.Services
{
    public enum RustPlusTokenStatus
    {
        Valid,
        LoggedOutOrInvalid, // 403 Forbidden from Facepunch
        Expired,            // exp claim in JWT is in past
        Corrupt,            // 500 / unparseable JWT
        NetworkError,       // Offline / DNS error
        Missing             // No token on disk
    }

    public sealed record RustPlusTokenCheckResult(
        RustPlusTokenStatus Status,
        string Message,
        ulong SteamId,
        int Version,
        DateTime? IssuedAtUtc,
        DateTime? ExpiresAtUtc
    );

    public interface IRustCompanionAuthProvider
    {
        bool IsAvailable { get; }
        Task<bool> RunRegistrationFlowAsync(Action<string> log, CancellationToken ct = default);
        Task<RustPlusTokenCheckResult> CheckTokenStatusAsync(string? token = null, CancellationToken ct = default);
        Task<(bool Success, string Message)> LogoutCurrentDeviceAsync(string? deviceId = null, CancellationToken ct = default);
        Task<(bool Success, string Message)> LogoutAllDevicesAsync(CancellationToken ct = default);
    }
}
