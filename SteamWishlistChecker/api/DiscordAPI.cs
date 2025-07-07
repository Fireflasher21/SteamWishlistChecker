using System.Formats.Asn1;
using System.Net;
using System.Runtime.InteropServices;
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
                try
                {
                    listener.Prefixes.Add(_config.LocalServer);
                    listener.Start();
                    Console.WriteLine("OAuth listener started on " + _config.LocalServer);
                }
                catch (HttpListenerException e)
                {
                    Console.WriteLine("Could not start OAuthenticator. Reason:\n" + e.Message);
                }
                while (true)
                {
                    try
                    {
                        HttpListenerContext context = await listener.GetContextAsync();
                        var request = context.Request;
                        var response = context.Response;

                        if (_config.DevMode) Console.WriteLine("Request was: " + request.InputStream);
                        await using var writer = new StreamWriter(response.OutputStream);

                        string? code = request.QueryString["code"];
                        if (string.IsNullOrEmpty(code))
                        {
                            response.StatusCode = 400;
                            await writer.WriteAsync("Missing code\n");
                            continue;
                        }

                        // Exchange the code for token
                        string token = await ExchangeCodeForToken(code);
                        if (token.Equals(string.Empty))
                        {
                            await writer.WriteAsync("Wrong Code\n");
                        }
                        var userId = await GetUserIdFromToken(token);

                        if (ulong.TryParse(userId, out var discordUserId))
                        {
                            await api.MessageDiscordUser(discordUserId, _config.StartingMessage);
                            await writer.WriteAsync("Falls du keine Direktnachricht von dem Bot bekommst, wende dich bitte an die Person welche dir den Link geschickt hat\n");
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
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

            if (_config.DevMode) Console.WriteLine("Response from Discord API: " + json);

            var obj = System.Text.Json.JsonDocument.Parse(json);
            
            if (json.Contains("\"error\":") && obj.RootElement.TryGetProperty("error", out var errorProperty))
            {
                string error = errorProperty.GetString()!;
                string description = obj.RootElement.GetProperty("error_description").GetString()!;
                Console.WriteLine($"Error from Discord API: {error} - {description}");
                return string.Empty;  // Return an empty string or handle the error
            }

            return obj.RootElement.GetProperty("access_token").GetString()!;
        }

        private async Task<string> GetUserIdFromToken(string accessToken)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            var response = await client.GetStringAsync("https://discord.com/api/users/@me");

            if(_config.DevMode)Console.WriteLine("Response from Discord: " + response);

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
            public string LocalServer { get; set; } = "";
            public string StartingMessage { get; set; } = "";
            public bool DevMode { get; set; } = false;
        }
    }

}