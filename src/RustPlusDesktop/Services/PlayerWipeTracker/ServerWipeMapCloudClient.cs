using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RustPlusDesk.Services.PlayerWipeTracker;

/// <summary>
/// Uploads/queries server wipe maps. Decoupled from the player wipe tracker: a
/// wipe map (image + geometry + monuments) belongs to the server/wipe, not to a
/// player's tracked archive. Targets the dedicated <c>/server-wipe-maps</c> API.
/// </summary>
public sealed class ServerWipeMapCloudClient
{
    private static readonly HttpClient Http = new(new TrafficTrackingHttpMessageHandler("Cloud API")) { Timeout = TimeSpan.FromSeconds(20) };
    private const string BaseUrl = "https://rustplusdesktop.cloud/api/v1";
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    /// <summary>Current server state for a wipe, so callers can skip needless uploads.</summary>
    public sealed record WipeMapStatus(bool Exists, bool HasImage, bool HasExtraMonuments);

    public async Task<int> UploadWipeMapAsync(
        string serverKey,
        string wipeKey,
        byte[] mapPngBytes,
        TrackerWipeMap mapMeta,
        DateTime? wipeStartedAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(serverKey), "server_key");
        content.Add(new StringContent(wipeKey), "wipe_key");
        content.Add(new StringContent(mapMeta.WorldSize.ToString(CultureInfo.InvariantCulture)), "world_size");
        content.Add(new StringContent(mapMeta.WorldRectX.ToString(CultureInfo.InvariantCulture)), "world_rect_x");
        content.Add(new StringContent(mapMeta.WorldRectY.ToString(CultureInfo.InvariantCulture)), "world_rect_y");
        content.Add(new StringContent(mapMeta.WorldRectWidth.ToString(CultureInfo.InvariantCulture)), "world_rect_width");
        content.Add(new StringContent(mapMeta.WorldRectHeight.ToString(CultureInfo.InvariantCulture)), "world_rect_height");
        content.Add(new StringContent(mapMeta.OceanMargin.ToString(CultureInfo.InvariantCulture)), "ocean_margin");

        if (mapMeta.Monuments is { Count: > 0 })
        {
            content.Add(new StringContent(SerializeMonuments(mapMeta.Monuments)), "monuments");
        }

        if (wipeStartedAtUtc.HasValue)
        {
            content.Add(new StringContent(wipeStartedAtUtc.Value.ToString("o", CultureInfo.InvariantCulture)), "wipe_started_at");
        }

        if (mapPngBytes is { Length: > 0 })
        {
            var imageContent = new ByteArrayContent(mapPngBytes);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Add(imageContent, "map_image", "map.png");
        }

        using var request = BuildRequest(HttpMethod.Post, "server-wipe-maps");
        request.Content = content;
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        return (int)response.StatusCode;
    }

    /// <summary>Uploads (replaces) the 3D-parsed extra monuments for an existing wipe.</summary>
    public async Task<int> UploadExtraMonumentsAsync(
        string serverKey,
        string wipeKey,
        IReadOnlyList<TrackerMonument> extraMonuments,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            extra_monuments = extraMonuments.Select(m => new
            {
                name = m.Name,
                x = m.X,
                y = m.Y,
                size = m.Size,
            }),
        };

        using var request = BuildRequest(
            HttpMethod.Post,
            $"server-wipe-maps/{Uri.EscapeDataString(serverKey)}/{Uri.EscapeDataString(wipeKey)}/extra-monuments");
        request.Content = new StringContent(JsonSerializer.Serialize(payload, _json), System.Text.Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        return (int)response.StatusCode;
    }

    /// <summary>
    /// Fetches what the server already has for a wipe, so the client can avoid
    /// spending quota re-uploading the base map or extra monuments.
    /// </summary>
    public async Task<WipeMapStatus> GetStatusAsync(string serverKey, string wipeKey, CancellationToken cancellationToken = default)
    {
        using var request = BuildRequest(
            HttpMethod.Get,
            $"server-wipe-maps/{Uri.EscapeDataString(serverKey)}/{Uri.EscapeDataString(wipeKey)}");
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new WipeMapStatus(false, false, false);
        }

        if (!response.IsSuccessStatusCode)
        {
            // Unknown — treat as "exists" so we don't hammer uploads on transient errors.
            return new WipeMapStatus(true, true, true);
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var data = document.RootElement.TryGetProperty("data", out var d) ? d : document.RootElement;

        var hasImage = data.TryGetProperty("has_map_image", out var img) && img.ValueKind == JsonValueKind.True;
        var hasExtra = data.TryGetProperty("extra_monuments_uploaded_at", out var ex)
            && ex.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;

        return new WipeMapStatus(true, hasImage, hasExtra);
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, $"{BaseUrl.TrimEnd('/')}/{path}");
        var token = RustPlusDesk.Services.Cloud.CloudAuthManager.CurrentToken;
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        request.Headers.Add("X-Client-Version", Helpers.VersionHelper.GetClientVersion());
        return request;
    }

    private string SerializeMonuments(IReadOnlyList<TrackerMonument> monuments)
        => JsonSerializer.Serialize(
            monuments.Select(m => new { name = m.Name, x = m.X, y = m.Y, size = m.Size }),
            _json);
}
