using System.Net;
using commands;
using Discord;
using Discord.WebSocket;
using DiscordConfig = api.models.DiscordConfig;

namespace api
{
    public class DiscordAPI
    {
        private DiscordSocketClient _client;
        
        private OAuthenticator _oAuthenticator;

        private DiscordConfig _config;

        private CommandRegistration _commands;

        public DiscordAPI(DiscordConfig config)
        {
            _config = config;
            _oAuthenticator = new(config);
            _client = new DiscordSocketClient();
            _commands = new CommandRegistration(_client);
            _commands.Initialize();
        }

        public async Task Start()
        {
            await _client.LoginAsync(TokenType.Bot, _config.BotToken);
            await _client.StartAsync();
            _oAuthenticator.StartOAuthListener(this);
        }

        public async Task MessageDiscordUser(ulong discordid, HashSet<SteamAPI.AppBody> appBodies)
        {
            var discordUser = await _client.Rest.GetUserAsync(discordid);
            var dmChannel = await discordUser.CreateDMChannelAsync();
            foreach (SteamAPI.AppBody body in appBodies)
            {
                await dmChannel.SendMessageAsync($"📉 **{body.name}** hat einen Tiefpreis: **{body.price / 100.0:F2}€** (-{body.discount}%)!\nhttps://store.steampowered.com/app/{body.appID}/");
                Console.WriteLine("User " + discordUser.GlobalName + " wurde benachrichtigt für " + body.name);
            }
        }

        public async Task MessageDiscordUser(ulong discordid, string messages)
        {
            try
            {
                var user = await _client.Rest.GetUserAsync(discordid);
                if (user == null)
                {
                    Console.WriteLine($"❌ Could not fetch user {discordid}");
                    return;
                }

                await user.SendMessageAsync(messages);
                Console.WriteLine($"✅ DM sent to user {user.GlobalName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to DM user {discordid}: {ex.Message}");
            }
        }
    }

    public class OAuthenticator
    {
        private DiscordConfig _config;

        public OAuthenticator(DiscordConfig config)
        {
            _config = config;
        }

        public void StartOAuthListener(DiscordAPI api)
        {
            Task.Run(async () =>
            {
                HttpListener listener = new HttpListener();
                listener.Prefixes.Add(_config.RedirectUri);
                listener.Start();
                Console.WriteLine("OAuth listener started on " + _config.RedirectUri);

                while (true)
                {
                    HttpListenerContext context = await listener.GetContextAsync();
                    var request = context.Request;
                    var response = context.Response;

                    string? code = request.QueryString["code"];
                    if (string.IsNullOrEmpty(code))
                    {
                        response.StatusCode = 400;
                        await using var writer = new StreamWriter(response.OutputStream);
                        await writer.WriteAsync("Missing code");
                        continue;
                    }

                    // Exchange the code for token
                    var token = await ExchangeCodeForToken(code);
                    var userId = await GetUserIdFromToken(token);

                    if (ulong.TryParse(userId, out var discordUserId))
                    {
                        await api.MessageDiscordUser(discordUserId, _config.StartingMessage);
                        await using var writer = new StreamWriter(response.OutputStream);
                        await writer.WriteAsync("Falls du keine Direktnachricht von dem Bot bekommst, wende dich bitte an die Person welche dir den Link geschickt hat");
                    }
                }
            });
        }

        private async Task<string> ExchangeCodeForToken(string code)
        {
            using var client = new HttpClient();
            var values = new Dictionary<string, string>
            {
                { "client_id", _config.ClientId },
                { "client_secret", _config.ClientSecret },
                { "grant_type", "authorization_code" },
                { "code", code },
                { "redirect_uri", _config.RedirectUri },
                { "scope", "identify applications.commands" }
            };

            var content = new FormUrlEncodedContent(values);
            var response = await client.PostAsync("https://discord.com/api/oauth2/token", content);
            var json = await response.Content.ReadAsStringAsync();

            var obj = System.Text.Json.JsonDocument.Parse(json);
            return obj.RootElement.GetProperty("access_token").GetString()!;
        }

        private async Task<string> GetUserIdFromToken(string accessToken)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            var response = await client.GetStringAsync("https://discord.com/api/users/@me");

            var obj = System.Text.Json.JsonDocument.Parse(response);
            return obj.RootElement.GetProperty("id").GetString()!;
        }

    }
        
    namespace models
    {
        public class DiscordConfig
        {
            public string BotToken { get; set; } = "";
            public string ClientId { get; set; } = "";
            public string ClientSecret { get; set; } = "";
            public string RedirectUri { get; set; } = "";
            public string StartingMessage { get; set; } = "";
        }
    }

}