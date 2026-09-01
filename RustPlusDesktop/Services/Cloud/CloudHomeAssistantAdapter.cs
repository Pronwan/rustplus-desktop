using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace RustPlusDesk.Services.Cloud
{
    /// <summary>
    /// Manages the per-user Home Assistant API token against the Cloud API.
    ///
    /// The token authenticates the cloud worker's <c>/api/ha</c> REST-switch
    /// endpoints, so this is effectively API-key management: the user views it
    /// (to paste into their configuration.yaml), regenerates it, or revokes it.
    /// Platform-only — there is no Supabase equivalent.
    /// </summary>
    public static class CloudHomeAssistantAdapter
    {
        /// <summary>The current Home Assistant token, or null when none has been generated.</summary>
        public static async Task<string?> GetTokenAsync()
        {
            var body = await CloudApiClient.CallApiAsync("me/home-assistant", HttpMethod.Get);
            return ExtractToken(body);
        }

        /// <summary>Generate a fresh token (invalidating any previous one) and return it.</summary>
        public static async Task<string?> RegenerateTokenAsync()
        {
            var body = await CloudApiClient.CallApiAsync("me/home-assistant/regenerate", HttpMethod.Post);
            return ExtractToken(body);
        }

        /// <summary>Remove the token, disabling the worker's Home Assistant endpoints for this account.</summary>
        public static Task RevokeAsync() =>
            CloudApiClient.CallApiAsync("me/home-assistant", HttpMethod.Delete);

        private static string? ExtractToken(string body)
        {
            using var doc = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                return null;

            return data.TryGetProperty("api_token", out var token) && token.ValueKind == JsonValueKind.String
                ? token.GetString()
                : null;
        }
    }
}
