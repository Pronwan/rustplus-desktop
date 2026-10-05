using System;
using System.Reflection;
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

                if (type != null)
                {
                    var instance = Activator.CreateInstance(type);
                    if (instance != null)
                    {
                        if (instance is IRustCompanionAuthProvider direct)
                            return direct;

                        return new ReflectedCompanionAuthProvider(instance);
                    }
                }
            }
            catch { }

            // 2. Fallback for public open-source builds without the submodule
            return new FallbackAuthProvider();
        }

        private sealed class ReflectedCompanionAuthProvider : IRustCompanionAuthProvider
        {
            private readonly object _instance;
            private readonly PropertyInfo? _isAvailableProp;
            private readonly MethodInfo? _runRegistrationFlowMethod;
            private readonly MethodInfo? _checkTokenStatusMethod;
            private readonly MethodInfo? _logoutCurrentDeviceMethod;
            private readonly MethodInfo? _logoutAllDevicesMethod;

            public ReflectedCompanionAuthProvider(object instance)
            {
                _instance = instance;
                var t = instance.GetType();
                _isAvailableProp = t.GetProperty("IsAvailable");
                _runRegistrationFlowMethod = t.GetMethod("RunRegistrationFlowAsync");
                _checkTokenStatusMethod = t.GetMethod("CheckTokenStatusAsync");
                _logoutCurrentDeviceMethod = t.GetMethod("LogoutCurrentDeviceAsync");
                _logoutAllDevicesMethod = t.GetMethod("LogoutAllDevicesAsync");
            }

            public bool IsAvailable => (bool)(_isAvailableProp?.GetValue(_instance) ?? true);

            public async Task<bool> RunRegistrationFlowAsync(Action<string> log, CancellationToken ct = default)
            {
                if (_runRegistrationFlowMethod == null) return false;
                var task = (Task<bool>)_runRegistrationFlowMethod.Invoke(_instance, new object[] { log, ct })!;
                return await task.ConfigureAwait(false);
            }

            public async Task<RustPlusTokenCheckResult> CheckTokenStatusAsync(string? token = null, CancellationToken ct = default)
            {
                if (_checkTokenStatusMethod == null)
                    return new RustPlusTokenCheckResult(RustPlusTokenStatus.Missing, "Not supported", 0, 0, null, null);

                var task = (Task)_checkTokenStatusMethod.Invoke(_instance, new object?[] { token, ct })!;
                await task.ConfigureAwait(false);
                var resultProp = task.GetType().GetProperty("Result");
                var res = resultProp?.GetValue(task);
                if (res == null) return new RustPlusTokenCheckResult(RustPlusTokenStatus.Missing, "Unknown", 0, 0, null, null);

                var resType = res.GetType();
                var status = (RustPlusTokenStatus)(int)(resType.GetProperty("Status")?.GetValue(res) ?? 0);
                var message = (string)(resType.GetProperty("Message")?.GetValue(res) ?? "");
                var steamId = (ulong)(resType.GetProperty("SteamId")?.GetValue(res) ?? 0UL);
                var version = (int)(resType.GetProperty("Version")?.GetValue(res) ?? 0);
                var issuedAt = (DateTime?)(resType.GetProperty("IssuedAtUtc")?.GetValue(res));
                var expiresAt = (DateTime?)(resType.GetProperty("ExpiresAtUtc")?.GetValue(res));

                return new RustPlusTokenCheckResult(status, message, steamId, version, issuedAt, expiresAt);
            }

            public async Task<(bool Success, string Message)> LogoutCurrentDeviceAsync(string? deviceId = null, CancellationToken ct = default)
            {
                if (_logoutCurrentDeviceMethod == null) return (false, "Not supported");
                var task = (Task<(bool Success, string Message)>)_logoutCurrentDeviceMethod.Invoke(_instance, new object?[] { deviceId, ct })!;
                return await task.ConfigureAwait(false);
            }

            public async Task<(bool Success, string Message)> LogoutAllDevicesAsync(CancellationToken ct = default)
            {
                if (_logoutAllDevicesMethod == null) return (false, "Not supported");
                var task = (Task<(bool Success, string Message)>)_logoutAllDevicesMethod.Invoke(_instance, new object?[] { ct })!;
                return await task.ConfigureAwait(false);
            }
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

