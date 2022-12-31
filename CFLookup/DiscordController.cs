using CurseForge.APIClient;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using NSec.Cryptography;
using StackExchange.Redis;
using System.Text;

namespace CFLookup
{
    [Route("api/[controller]")]
    [ApiController]
    public class DiscordController : ControllerBase
    {
        private readonly ApiClient _cfApiClient;
        private readonly IDatabaseAsync _redis;
        private readonly IHttpClientFactory _httpClientFactory;

        public DiscordController(ApiClient cfApiClient, ConnectionMultiplexer connectionMultiplexer, IHttpClientFactory httpClientFactory)
        {
            _cfApiClient = cfApiClient;
            _redis = connectionMultiplexer.GetDatabase(5);
            _httpClientFactory = httpClientFactory;
        }

        [HttpPost("interactions")]
        public async Task<IActionResult> InteractionsAsync()
        {
            using var sr = new StreamReader(Request.Body);

            var json = await sr.ReadToEndAsync();

            var discordAppId = Environment.GetEnvironmentVariable("DISCORD_APP_ID");

            var publicDiscordKey = Environment.GetEnvironmentVariable("DISCORD_PUBLIC_KEY");
            Request.Headers.TryGetValue("X-Signature-Ed25519", out var signatureValue);
            Request.Headers.TryGetValue("X-Signature-Timestamp", out var signatureTimestamp);

            if (!signatureValue.Any() || !signatureTimestamp.Any())
            {
                return new UnauthorizedResult();
            }

            var pubKey = PublicKey.Import(SignatureAlgorithm.Ed25519, GetBytesFromHexString(publicDiscordKey), KeyBlobFormat.RawPublicKey);
            var dataToValidate = Encoding.UTF8.GetBytes(signatureTimestamp.ToString() + json);

            var isValidRequest = SignatureAlgorithm.Ed25519.Verify(pubKey, dataToValidate, GetBytesFromHexString(signatureValue.ToString()));

            if (!isValidRequest)
            {
                return new UnauthorizedResult();
            }

            var requestObject = JsonConvert.DeserializeObject<DiscordInteractionRequest>(json);

            if (requestObject == null)
            {
                return new BadRequestResult();
            }

            switch (requestObject.Type)
            {
                case DiscordInteractionType.Ping:
                    {
                        _ = RegisterDiscordCommandsAsync(discordAppId);
                        return new JsonResult(new DiscordPongResult());
                    }
                case DiscordInteractionType.ApplicationCommand:
                    return new JsonResult(await HandleDiscordCommandAsync(requestObject));
            }

            return new JsonResult(requestObject);
        }

        private async Task<object> HandleDiscordCommandAsync(DiscordInteractionRequest request)
        {
            if (request.Data is null)
            {
                return null;
            }

            if (request.Data.Name == "cflookup")
            {
                return await ProjectLookupAsync(request);
            }

            return null;
        }

        private async Task<object> ProjectLookupAsync(DiscordInteractionRequest request)
        {
            var projectId = request?.Data?.Options?.FirstOrDefault(o => o.Name == "projectid");

            if (projectId == null)
            {
                return new
                {
                    type = 4,
                    data = new
                    {
                        content = $"You must supply a valid project id for this to work"
                    }
                };
            }

            var mod = await SharedMethods.SearchModAsync(_redis, _cfApiClient, Convert.ToInt32(projectId.Value));

            if (mod == null)
            {
                return new
                {
                    type = 4,
                    data = new
                    {
                        content = $"Project `{projectId.Value}` could not be found, maybe the API is down or the project does not exist"
                    }
                };
            }

            return new
            {
                type = 4,
                data = new
                {
                    content = $"Project `{projectId.Value}` is: **[{mod.Name}]({mod.Links.WebsiteUrl})**"
                }
            };
        }

        private async Task RegisterDiscordCommandsAsync(string discordAppId)
        {
            var _hc = _httpClientFactory.CreateClient();
        }

        private static byte[] GetBytesFromHexString(string hex)
        {
            var length = hex.Length;
            var bytes = new byte[length / 2];

            for (var i = 0; i < length; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            }

            return bytes;
        }

        public class DiscordPongResult : DiscordInteractionResponse
        {
            public DiscordPongResult() : base(DiscordInteractionCallbackType.Pong)
            {

            }
        }

        public class DiscordInteractionResponse
        {
            public DiscordInteractionCallbackType Type { get; set; }
            public DiscordInteractionResponse(DiscordInteractionCallbackType type)
            {
                Type = type;
            }
        }

        public class DiscordInteractionRequest
        {
            public string Application_Id { get; set; }
            public string Id { get; set; }
            public string Token { get; set; }
            public DiscordInteractionType Type { get; set; }
            public int Version { get; set; }
            public DiscordInteractionUser? User { get; set; }
            public InteractionData? Data { get; set; }
        }

        public class InteractionData
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public int Type { get; set; }

            public object Value { get; set; }

            public List<InteractionData>? Options { get; set; }
        }

        public class DiscordInteractionUser
        {
            public string Avatar { get; set; }
            public string Avatar_Decoration { get; set; }
            public string Discriminator { get; set; }
            public string Id { get; set; }
            public int Public_Flags { get; set; }
            public string Username { get; set; }
        }

        public enum DiscordInteractionType
        {
            Ping = 1,
            ApplicationCommand = 2,
            MessageComponent = 3,
            ApplicationCommandAutocomplete = 4,
            ModalSubmit = 5
        }

        public enum DiscordInteractionCallbackType
        {
            Pong = 1,
            ChannelMessageWithSource = 4,
            DeferredChannelMessageWithSource = 5,
            DeferredUpdateMessage = 6,
            UpdateMessage = 7,
            ApplicationCommandAutocompleteResult = 8,
            Modal = 9
        }
    }
}
