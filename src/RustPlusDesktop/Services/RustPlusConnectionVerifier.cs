using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using RustPlusApi;
using RustPlusDesk.Models;

namespace RustPlusDesk.Services;

public static class RustPlusConnectionVerifier
{
    public sealed record VerificationResult(
        bool Reachable,
        bool DevicesVerified,
        int VerifiedCount,
        int TestedCount,
        string? ServerName,
        string? ErrorMessage);

    /// <summary>
    /// Attempts to connect to a Rust+ companion server with the given credentials and verifies:
    /// 1. Whether the server companion socket is reachable and accepts the connection.
    /// 2. Whether any of the specified existing entity IDs (switches, alarms, storage monitors) exist and respond on the server.
    /// </summary>
    public static async Task<VerificationResult> VerifyServerAndDevicesAsync(
        string host,
        int port,
        ulong steamId,
        int playerToken,
        IEnumerable<uint>? entityIdsToCheck = null,
        bool useProxy = false,
        TimeSpan timeout = default)
    {
        if (string.IsNullOrWhiteSpace(host) || port <= 0 || steamId == 0)
        {
            return new VerificationResult(false, false, 0, 0, null, "Invalid connection parameters.");
        }

        if (timeout == default)
        {
            timeout = TimeSpan.FromSeconds(5);
        }

        using var cts = new CancellationTokenSource(timeout);

        // Direct only. The companion /game/{ip}/{port} relay answers 418 for every server now,
        // so falling back to it can only turn an unreachable server into a false denial.
        // useProxy is kept for callers but no longer changes the route.
        return await TryConnectAndVerifyInternalAsync(host, port, steamId, playerToken, entityIdsToCheck, false, cts.Token);
    }

    private static async Task<VerificationResult> TryConnectAndVerifyInternalAsync(
        string host,
        int port,
        ulong steamId,
        int playerToken,
        IEnumerable<uint>? entityIdsToCheck,
        bool useProxy,
        CancellationToken ct)
    {
        RustPlus? api = null;
        try
        {
            var conn = new RustPlusConnection(host, port, steamId, playerToken, useProxy);
            api = new RustPlus(conn);

            var mConnect = api.GetType().GetMethod("ConnectAsync", new[] { typeof(CancellationToken) })
                          ?? api.GetType().GetMethod("ConnectAsync", Type.EmptyTypes);

            if (mConnect != null)
            {
                var res = mConnect.GetParameters().Length == 1
                    ? mConnect.Invoke(api, new object[] { ct })
                    : mConnect.Invoke(api, Array.Empty<object>());
                if (res is Task t) await t.WaitAsync(ct);
            }

            string? serverName = null;
            try
            {
                var infoTask = api.GetInfoAsync();
                var infoRes = await infoTask.WaitAsync(TimeSpan.FromSeconds(3), ct);
                serverName = infoRes?.Data?.Name;
            }
            catch
            {
                // Server info probe can occasionally be slow or fail on some modded servers, continue
            }

            int verifiedCount = 0;
            int testedCount = 0;
            bool devicesVerified = false;

            var entities = entityIdsToCheck?.Where(id => id > 0).Take(5).ToList();
            if (entities != null && entities.Count > 0)
            {
                foreach (var entityId in entities)
                {
                    if (ct.IsCancellationRequested) break;
                    testedCount++;
                    bool found = await ProbeSingleEntityAsync(api, entityId, ct);
                    if (found)
                    {
                        verifiedCount++;
                        devicesVerified = true;
                        // Once we have verified at least one entity exists on this NetID, we have positive proof
                        break;
                    }
                }
            }

            return new VerificationResult(
                Reachable: true,
                DevicesVerified: devicesVerified,
                VerifiedCount: verifiedCount,
                TestedCount: testedCount,
                ServerName: serverName,
                ErrorMessage: null);
        }
        catch (OperationCanceledException)
        {
            return new VerificationResult(false, false, 0, 0, null, "Connection timed out.");
        }
        catch (Exception ex)
        {
            return new VerificationResult(false, false, 0, 0, null, ex.Message);
        }
        finally
        {
            if (api != null)
            {
                try
                {
                    var mDisc = api.GetType().GetMethod("DisconnectAsync", Type.EmptyTypes);
                    if (mDisc != null && mDisc.Invoke(api, null) is Task dt)
                    {
                        await dt.WaitAsync(TimeSpan.FromMilliseconds(500));
                    }
                }
                catch
                {
                    // Ignore disconnect errors during teardown
                }
            }
        }
    }

    private static async Task<bool> ProbeSingleEntityAsync(RustPlus api, uint entityId, CancellationToken ct)
    {
        // 1) Test smart switch query
        try
        {
            var sw = await api.GetSmartSwitchInfoAsync(entityId).WaitAsync(TimeSpan.FromSeconds(2), ct);
            if (sw?.IsSuccess == true) return true;
        }
        catch
        {
            // Fall through to generic AppRequest
        }

        // 2) Test AppRequest GetEntityInfo
        try
        {
            var asm = typeof(RustPlus).Assembly;
            var reqType = asm.GetTypes().FirstOrDefault(t => t.Name == "AppRequest");
            var emptyType = asm.GetTypes().FirstOrDefault(t => t.Name == "AppEmpty");
            if (reqType != null && emptyType != null)
            {
                var req = Activator.CreateInstance(reqType)!;
                var empty = Activator.CreateInstance(emptyType)!;
                reqType.GetProperty("EntityId")?.SetValue(req, entityId);
                reqType.GetProperty("GetEntityInfo")?.SetValue(req, empty);

                var send = api.GetType().GetMethods().FirstOrDefault(mi =>
                    mi.Name == "SendRequestAsync" &&
                    mi.GetParameters().Length >= 1 &&
                    mi.GetParameters()[0].ParameterType.IsAssignableFrom(reqType));

                if (send != null)
                {
                    var args = new object?[send.GetParameters().Length];
                    args[0] = req;
                    for (int i = 1; i < args.Length; i++)
                    {
                        var p = send.GetParameters()[i];
                        args[i] = p.HasDefaultValue ? p.DefaultValue : (p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null);
                    }

                    var taskObj = send.Invoke(api, args);
                    if (taskObj is Task task)
                    {
                        await task.WaitAsync(TimeSpan.FromSeconds(2), ct);
                        var result = task.GetType().GetProperty("Result")?.GetValue(task);
                        var resp = result?.GetType().GetProperty("Response")?.GetValue(result) ?? result;
                        var ent = resp?.GetType().GetProperty("EntityInfo")?.GetValue(resp);
                        if (ent != null) return true;
                    }
                }
            }
        }
        catch
        {
            // Device does not exist on this server or unreachable
        }

        return false;
    }
}
