using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace RustPlusDesk.Services
{
    /// <summary>
    /// Calls the public Steam Web API to retrieve VAC/game-ban status.
    /// Only uses the ISteamUser/GetPlayerBans endpoint — no private data.
    /// Requires a Steam Web API key stored in app settings (user-provided).
    /// </summary>
    public class SteamBanLookupService
    {
        private readonly HttpClient _http;
        private readonly string _apiKey;

        public SteamBanLookupService(string steamApiKey)
        {
            _apiKey = steamApiKey;
            _http = new HttpClient();
        }

        public async Task<(bool VacBanned, bool GameBanned, int DaysSinceLastBan)>
            GetBanStatusAsync(string steamId)
        {
            var url = $"https://api.steampowered.com/ISteamUser/GetPlayerBans/v1/" +
                      $"?key={_apiKey}&steamids={steamId}";
            var response = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);
            var player = doc.RootElement
                            .GetProperty("players")[0];
            return (
                player.GetProperty("VACBanned").GetBoolean(),
                player.GetProperty("NumberOfGameBans").GetInt32() > 0,
                player.GetProperty("DaysSinceLastBan").GetInt32()
            );
        }
    }
}
