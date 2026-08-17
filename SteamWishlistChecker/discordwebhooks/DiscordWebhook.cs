using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace main;

public class DiscordWebhook
{
    private static readonly HttpClient httpClient = new HttpClient();

    public static async Task SendMessage(string webhookUrl, string message)
    {
        var payload = new
        {
            content = message
        };

        string json = JsonSerializer.Serialize(payload);

        using var content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json"
        );

        await httpClient.PostAsync(webhookUrl, content);
    }
}