using System;
using System.Threading;
using System.Threading.Tasks;

namespace RustPlusDesk.Services
{
    public interface IRustCompanionAuthProvider
    {
        bool IsAvailable { get; }
        Task<bool> RunRegistrationFlowAsync(Action<string> log, CancellationToken ct = default);
        Task<RustPlusTokenCheckResult> CheckTokenStatusAsync(string? token = null, CancellationToken ct = default);
        Task<(bool Success, string Message)> LogoutCurrentDeviceAsync(string? deviceId = null, CancellationToken ct = default);
        Task<(bool Success, string Message)> LogoutAllDevicesAsync(CancellationToken ct = default);
    }
}
