using RustPlusDesk.Models;
using System.Threading;
using System.Threading.Tasks;
// using static RustPlusDesk.Services.RustPlusClientReal;

namespace RustPlusDesk.Services;


    public interface IRustPlusClient
{
    Task ConnectAsync(ServerProfile profile, CancellationToken ct = default);
    Task DisconnectAsync();
    Task ToggleSmartSwitchAsync(long entityId, bool on, CancellationToken ct = default);
    Task<bool?> GetSmartSwitchStateAsync(uint entityId);

    // NEU:
    Task<EntityProbeResult> ProbeEntityAsync(uint entityId, CancellationToken ct = default);
}
