using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFDiscordBot
{
    public class IGDBClient
    {
        HttpClient _hc;
        string _clientId;
        string _clientSecret;

        AccessToken token;

        public IGDBClient(string client_id, string client_secret, HttpClient client)
        {
            _clientId = client_id;
            _clientSecret = client_secret;

            _hc = client;
            _hc.BaseAddress = new Uri("https://api.igdb.com/");
        }

        public async Task<JsonElement> SearchGamesAsync(string query)
        {
            await GetTwitchToken();

            var response = await _hc.PostAsync("/v4/games/", new StringContent(query));

            return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        }

        private async Task GetTwitchToken()
        {
            if (token == null || (token.Expires - DateTimeOffset.Now).TotalMinutes < 5)
            {
                var response = await _hc.PostAsync($"https://id.twitch.tv/oauth2/token?client_id={_clientId}&client_secret={_clientSecret}&grant_type=client_credentials", null);
                var str = await response.Content.ReadAsStringAsync();
                token = JsonSerializer.Deserialize<AccessToken>(str);
            }

            _hc.DefaultRequestHeaders.Remove("Authorization");
            _hc.DefaultRequestHeaders.Remove("Client-ID");

            _hc.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {token.Token}");
            _hc.DefaultRequestHeaders.TryAddWithoutValidation("Client-ID", _clientId);
        }

        private class AccessToken
        {
            [JsonPropertyName("access_token")]
            public string Token { get; set; }
            [JsonPropertyName("token_type")]
            public string Type { get; set; }
            [JsonPropertyName("expires_in")]
            public long ExpiresIn { get; set; }
            private DateTimeOffset? _expires { get; set; }
            public DateTimeOffset Expires
            {
                get
                {
                    if (_expires == null)
                    {
                        _expires = DateTimeOffset.Now.AddSeconds(ExpiresIn);
                    }

                    return _expires.Value;
                }
            }
        }
    }
}
