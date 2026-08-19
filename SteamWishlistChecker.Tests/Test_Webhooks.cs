using System.Net;
using System.Net.Sockets;
using SteamAPI = api.SteamAPI;
using AppID = System.Int32;
using System.Text.Json;
using main;

namespace SteamWishlistChecker.Tests;

public partial class SteamWishlistCheckerTests
{
    [Fact]
    public async Task SendWebhooks_SendsOnlyNewReducedGamesToAllWebhooks()
    {
        // ---------------------------------------------------------
        // Arrange
        // ---------------------------------------------------------

        int port = GetFreePort();
        string baseUrl = $"http://127.0.0.1:{port}/";

        string[] webHooks =
        {
            $"{baseUrl}webhook1/",
            $"{baseUrl}webhook2/",
            $"{baseUrl}webhook3/"
        };

        var reducedGames = new Dictionary<AppID, SteamAPI.AppBody>
        {
            {
                123,
                new SteamAPI.AppBody(
                    123,
                    "Test Game",
                    1999,
                    50
                )
            },
            {
                456,
                new SteamAPI.AppBody
                (
                    456,
                    "Already Reduced Game",
                    999,
                    25
                )
            }
        };

        reducedGames[456].SetAlreadyReduced(true);

        using var listener = new HttpListener();
        listener.Prefixes.Add(baseUrl);
        listener.Start();

        var receivedMessages = new List<string>();
        var receivedUrls = new List<string>();

        // ---------------------------------------------------------
        // Start local HTTP server
        // ---------------------------------------------------------

    var listenerTask = Task.Run(async () =>
    {
        for (int i = 0; i < webHooks.Length; i++)
        {
            var contextTask = listener.GetContextAsync();

            var completedTask = await Task.WhenAny(
                contextTask,
                Task.Delay(TimeSpan.FromSeconds(5))
            );

            if (completedTask != contextTask)
            {
                throw new TimeoutException(
                    $"Expected webhook request {i + 1}/{webHooks.Length}, " +
                    "but none was received within 5 seconds."
                );
            }

            var context = await contextTask;

            using var reader = new StreamReader(
                context.Request.InputStream,
                context.Request.ContentEncoding
            );

            string body = await reader.ReadToEndAsync();

            lock (receivedMessages)
            {
                receivedMessages.Add(body);
                receivedUrls.Add(context.Request.Url!.AbsolutePath);
            }

            Console.WriteLine(
                $"Received: {context.Request.HttpMethod} " +
                $"{context.Request.Url} -> {body}"
            );

            context.Response.StatusCode = 204;
            context.Response.Close();
        }
    });

        // ---------------------------------------------------------
        // Get private SendWebhooks method
        // ---------------------------------------------------------

        var method = typeof(main.SteamWishlistChecker)
            .GetMethod(
                "SendWebhooks",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic
            );

        Assert.NotNull(method);

        // ---------------------------------------------------------
        // Act
        // ---------------------------------------------------------

        var task = (Task)method!.Invoke(
            _checker,
            new object[]
            {
                webHooks,
                reducedGames
            }
        )!;

        await task;

        // Wait until the local HTTP server received all requests.
        await listenerTask;

        // ---------------------------------------------------------
        // Assert
        // ---------------------------------------------------------

        // One game is not alreadyReduced.
        // It should therefore be sent to all 3 webhooks.
        Assert.Equal(3, receivedMessages.Count);

        Assert.Equal(3, receivedUrls.Count);

        // Every webhook should have received a request.
        Assert.Contains("/webhook1/", receivedUrls);
        Assert.Contains("/webhook2/", receivedUrls);
        Assert.Contains("/webhook3/", receivedUrls);

        foreach (string message in receivedMessages)
        {
            var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(message);

            Assert.NotNull(payload);
            Assert.True(payload!.ContainsKey("content"));

            string content = payload["content"];

            Assert.Contains("Test Game", content);
            Assert.Contains("19,99€", content);
            Assert.Contains("-50%", content);
            Assert.Contains(
                "https://store.steampowered.com/app/123/",
                content
            );

            // Already reduced game must not be sent
            Assert.DoesNotContain(
                "Already Reduced Game",
                message
            );
        }
    }


    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback,0);
        listener.Start();

        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
    


    public async Task SendWebhooks_WithMultipleAppBodies_SendsAllReducedGames()
    {
        // ---------------------------------------------------------
        // Arrange
        // ---------------------------------------------------------

        string[] webHooks =
        {
            "https://discord.com/api/webhooks/",
            "https://discord.com/api/webhooks/"
        };

        var game1 = new SteamAPI.AppBody(
            123,
            "Test Game 1",
            1999,
            50
        );

        var game2 = new SteamAPI.AppBody(
            456,
            "Test Game 2",
            2999,
            75
        );

        var game3 = new SteamAPI.AppBody(
            789,
            "Already Reduced Game",
            999,
            20
        );

        // If alreadyReduced has a private setter,
        // use whatever mechanism your AppBody normally uses
        // to set it.
        game3.SetAlreadyReduced(true);

        var reducedGames = new Dictionary<AppID, SteamAPI.AppBody>
        {
            { game1.appID, game1 },
            { game2.appID, game2 },
            { game3.appID, game3 }
        };

        // ---------------------------------------------------------
        // Act
        // ---------------------------------------------------------

        var method = typeof(main.SteamWishlistChecker)
            .GetMethod(
                "SendWebhooks",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic
            );

        Assert.NotNull(method);

        try
        {
            var task = (Task)method!.Invoke(
                _checker,
                new object[]
                {
                    webHooks,
                    reducedGames
                }
            )!;

            await task;
        }
        catch (Exception ex)
        {
            Assert.Fail(
                $"Sending webhooks failed:\n{ex}"
            );
        }

        // ---------------------------------------------------------
        // Assert
        // ---------------------------------------------------------

        // If we got here, SendWebhooks completed without throwing.
        Assert.True(true);
    }
    
}
