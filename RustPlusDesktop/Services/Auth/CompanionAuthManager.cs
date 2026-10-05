using System;
using System.Threading;
using System.Threading.Tasks;

namespace RustPlusDesk.Services
{
    /// <summary>
    /// Manages the active Rust+ companion authentication provider.
    /// Automatically resolves the proprietary native provider if the RustPlus.Auth submodule/DLL is present,
    /// or falls back to the open-source stub provider.
    /// </summary>
    public static class CompanionAuthManager
    {
        private static IRustCompanionAuthProvider? s_provider;

        public static IRustCompanionAuthProvider Provider
        {
            get => s_provider ??= ResolveProvider();
            set => s_provider = value;
        }

        private static IRustCompanionAuthProvider ResolveProvider()
        {
            try
            {
                // 1. Check if NativeCompanionAuthProvider is in the referenced RustPlus.Auth assembly
                var type = Type.GetType("RustPlusDesk.Services.NativeCompanionAuthProvider, RustPlus.Auth")
                        ?? Type.GetType("RustPlusDesk.Services.NativeCompanionAuthProvider");

                if (type != null && Activator.CreateInstance(type) is IRustCompanionAuthProvider nativeProvider)
                {
                    return nativeProvider;
                }
            }
            catch { }

            // 2. Fallback for public open-source builds without the submodule
            return new FallbackAuthProvider();
        }

        private sealed class FallbackAuthProvider : IRustCompanionAuthProvider
        {
            public bool IsAvailable => false;

            public Task<bool> RunRegistrationFlowAsync(Action<string> log, CancellationToken ct = default)
            {
                log("[auth-fallback] Proprietary OAuth registration submodule is not present in this open-source build.");
                log("[auth-fallback] Please pair via official mobile app or configure rustplusjs-config.json manually.");
                return Task.FromResult(false);
            }

            public Task<RustPlusTokenCheckResult> CheckTokenStatusAsync(string? token = null, CancellationToken ct = default)
            {
                return Task.FromResult(new RustPlusTokenCheckResult(
                    RustPlusTokenStatus.Missing,
                    "Authentication verification module is not available in this build.",
                    0, 0, null, null));
            }

            public Task<(bool Success, string Message)> LogoutCurrentDeviceAsync(string? deviceId = null, CancellationToken ct = default)
            {
                return Task.FromResult((true, "Logged out locally (fallback)."));
            }

            public Task<(bool Success, string Message)> LogoutAllDevicesAsync(CancellationToken ct = default)
            {
                return Task.FromResult((false, "Global invalidation module is not available in this build."));
            }
        }
    }
}
