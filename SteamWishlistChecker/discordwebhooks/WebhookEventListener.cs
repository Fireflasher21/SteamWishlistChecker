using System.Net;
using System.Text;
using System.Text.Json;
using api.models;
using api;

namespace discord.webhook
{

    internal sealed class WebhookEventListener
    {
        private readonly DiscordConfig _config;
        private readonly IDiscordAPI _discord;
        private readonly byte[] _publicKeyBytes;

        public WebhookEventListener(DiscordConfig config, IDiscordAPI discord)
        {
            _config         = config;
            _discord        = discord;
            _publicKeyBytes = Convert.FromHexString(config.PublicKey);
        }

        // ── Lifecycle ────────────────────────────────────────────────────────────

        public void Start() => _ = RunAsync();

        private async Task RunAsync()
        {   
            using var listener = new HttpListener();
            
            try
            {
                listener.Prefixes.Add(_config.WebhookEventUrl); 

                listener.Start();
                Console.WriteLine($"[WebhookEvents] Listening on {_config.WebhookEventUrl}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WebhookEvents] Could not start listener: {ex}");
                return;
            }

            while (true)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await listener.GetContextAsync();

                    _ = HandleRequestAsync(ctx);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WebhookEvents] Accept error: {ex.Message}");
                }


            }
        }


        private async Task HandleRequestAsync(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var res = ctx.Response;

            try
            {
                if (req.HttpMethod != "POST")
                {
                    res.StatusCode = 405;
                    res.Close();
                    return;
                }

                string body;
                using (var reader = new StreamReader(req.InputStream, Encoding.UTF8, leaveOpen: false))
                    body = await reader.ReadToEndAsync();

                if (_config.DevMode) Console.WriteLine($"[WebhookEvents] Body: {body}");

                // ── Signature verification ────────────────────────────────────
                string? signature = req.Headers["X-Signature-Ed25519"];
                string? timestamp  = req.Headers["X-Signature-Timestamp"];

                if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(timestamp) || !VerifySignature(signature, timestamp, body))
                {
                    res.StatusCode = 401;
                    res.Close();
                    return;
                }

                using var doc = JsonDocument.Parse(body);
                int type = doc.RootElement.GetProperty("type").GetInt32();

                switch (type)
                {
                    // Type 0 — PING (Discord verifies the endpoint on setup)
                    case 0:
                        res.StatusCode = 204;
                        res.Close();
                        Console.WriteLine("[WebhookEvents] PING acknowledged.");
                        break;

                    // Type 1 — Webhook Event (APPLICATION_AUTHORIZED, etc.)
                    case 1:
                        // ACK first — Discord retries if no 204 within 3 s.
                        res.StatusCode = 204;
                        res.Close();
                        await HandleEventAsync(doc.RootElement);
                        break;

                    default:
                        res.StatusCode = 400;
                        res.Close();
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WebhookEvents] ❌ {ex.Message}");
                try { res.StatusCode = 500; res.Close(); } catch { /* already closed */ }
            }
        }

        private async Task HandleEventAsync(JsonElement root)
        {
            if (!root.TryGetProperty("event", out var eventEl))
                return;

            string eventType = eventEl.GetProperty("type").GetString() ?? string.Empty;

            switch (eventType)
            {
                case "APPLICATION_AUTHORIZED":
                    await HandleApplicationAuthorizedAsync(eventEl);
                    break;

                default:
                    if (_config.DevMode) Console.WriteLine($"[WebhookEvents] Unhandled event type: {eventType}");
                    break;
            }
        }

        // APPLICATION_AUTHORIZED
        private async Task HandleApplicationAuthorizedAsync(JsonElement eventEl)
        {
            string? userIdRaw = eventEl
                .GetProperty("data")
                .GetProperty("user")
                .GetProperty("id")
                .GetString();

            if (!ulong.TryParse(userIdRaw, out ulong userId))
            {
                Console.WriteLine("[WebhookEvents] ⚠️ Could not parse user ID from APPLICATION_AUTHORIZED");
                return;
            }

            Console.WriteLine($"[WebhookEvents] APPLICATION_AUTHORIZED for user {userId}");
            await _discord.MessageDiscordUser(userId, _config.StartingMessage);
        }

        // Ed25519 signature verification
        private bool VerifySignature(string signature, string timestamp, string body)
        {
            try
            {
                byte[] sigBytes     = Convert.FromHexString(signature);
                byte[] messageBytes = Encoding.UTF8.GetBytes(timestamp + body);

                var algorithm = NSec.Cryptography.SignatureAlgorithm.Ed25519;
                var key = NSec.Cryptography.PublicKey.Import(
                    algorithm,
                    _publicKeyBytes,
                    NSec.Cryptography.KeyBlobFormat.RawPublicKey);

                return algorithm.Verify(key, messageBytes, sigBytes);
            }
            catch
            {
                return false;
            }
        }
    }
}