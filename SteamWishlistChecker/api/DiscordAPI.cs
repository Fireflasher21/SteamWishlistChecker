using commands;
using Discord;
using Discord.WebSocket;
using DiscordConfig = api.models.DiscordConfig;
using discord.webhook;

namespace api
{
    public class DiscordAPI : IDiscordAPI
    {
        private readonly DiscordSocketClient _client;
        private readonly DiscordConfig _config;
        private readonly CommandRegistration _commands;
        private readonly WebhookEventListener _webhookEventListener;

        public DiscordAPI(DiscordConfig config)
        {
            _config = config;

            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                LogLevel = config.DevMode ? LogSeverity.Debug : LogSeverity.Info
            });

            _commands = new CommandRegistration(_client);
            _commands.Initialize();

            // Empfängt APPLICATION_AUTHORIZED vom Discord Developer Portal → Webhooks-Seite.
            // Hat keinerlei Verbindung zu Slash Commands oder CommandRegistration.
            _webhookEventListener = new WebhookEventListener(config, this);

            _client.Ready += OnReadyAsync;

            if (config.DevMode)
                _client.Log += msg => 
                { 
                    if (msg.Source == "Gateway" && msg.Message.Contains("Heartbeat") || msg.Message.Contains("Latency")) return Task.CompletedTask;
                    Console.WriteLine(msg); return Task.CompletedTask; 
                };
        }

        // ── Lifecycle ────────────────────────────────────────────────────────────

        public async Task Start()
        {
            _webhookEventListener.Start();
            await _client.LoginAsync(TokenType.Bot, _config.BotToken);
            await _client.StartAsync();
        }

        // ── Event handlers ───────────────────────────────────────────────────────

        private async Task OnReadyAsync()
        {
            await _client.SetStatusAsync(UserStatus.Online);
            Console.WriteLine("[Discord] Client ready.");
        }

        // ── Outgoing notifications ───────────────────────────────────────────────

        public async Task MessageDiscordUser(ulong discordId, HashSet<SteamAPI.AppBody> appBodies)
        {
            var discordUser = await _client.Rest.GetUserAsync(discordId);
            var dmChannel   = await discordUser.CreateDMChannelAsync();

            foreach (SteamAPI.AppBody body in appBodies)
            {
                await dmChannel.SendMessageAsync(
                    $"📉 **{body.name}** hat einen Tiefpreis: **{body.price / 100.0:F2}€** (-{body.discount}%)!\n" +
                    $"https://store.steampowered.com/app/{body.appID}/");

                Console.WriteLine($"[Discord] User {discordUser.GlobalName} notified for {body.name}");
            }
        }

        public async Task MessageDiscordUser(ulong discordId, string message)
        {
            try
            {
                var user = await _client.Rest.GetUserAsync(discordId);
                if (user == null)
                {
                    Console.WriteLine($"[Discord] ❌ Could not fetch user {discordId}");
                    return;
                }

                await user.SendMessageAsync(message);
                Console.WriteLine($"[Discord] ✅ DM sent to {user.GlobalName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Discord] ❌ Failed to DM user {discordId}: {ex.Message}");
            }
        }

        public string[] GetWebHookURLs()
        {
            return _config.WebhookUrls != null ? _config.WebhookUrls : Array.Empty<string>();
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    // Config
    // ════════════════════════════════════════════════════════════════════════════

    namespace models
    {
        public class DiscordConfig
        {
            public string BotToken        { get; set; } = "";
            public string ApplicationId   { get; set; } = "";

            public string PublicKey       { get; set; } = "";

            public string WebhookEventUrl { get; set; } = "http://+:5555/webhook/";
            public string[]? WebhookUrls { get; set; } = Array.Empty<string>();

            public string StartingMessage { get; set; } = "";
            public bool   DevMode         { get; set; } = false;
        }
    }
}