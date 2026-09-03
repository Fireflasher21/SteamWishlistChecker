using db;
using Microsoft.Extensions.Configuration;

using SteamAPI = api.SteamAPI;
using SteamConfig = api.models.SteamConfig;
using BotConfig = api.models.BotConfig;
using DiscordAPI = api.DiscordAPI;
using DiscordConfig = api.models.DiscordConfig;
using UserID = System.Int16;
using AppID = System.Int32;
using SteamID = System.Int64;
using System.Globalization;
using api;
using Microsoft.Extensions.Primitives;
using System.ComponentModel.DataAnnotations;


namespace main
{

    public class SteamWishlistChecker
    {
        public static List<SteamID> errorOnWishlist = new();
        private readonly IDiscordAPI _discordAPI;
        private readonly ISteamAPI _steamAPI;
        private readonly BotConfig _config;

        public SteamWishlistChecker(BotConfig config, ISteamAPI isteamAPI, IDiscordAPI idiscordAPI)
        {
            _config = config;
            _steamAPI = isteamAPI;
            _discordAPI = idiscordAPI;
        }
    

        private static async Task Main(string[] args)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            ChangeToken.OnChange(
                () => config.GetReloadToken(),
                () => Console.WriteLine("[Bot] appsettings reloaded " + DateTime.Now.ToString())
            );

            var _config = config.GetSection("Bot").Get<BotConfig>() ?? new BotConfig();

            var steamAPI = new SteamAPI(config.GetSection("Steam").Get<SteamConfig>()!);

            var discordAPI = new DiscordAPI(config.GetSection("Discord").Get<DiscordConfig>()!, _config.Token);

            var checker = new SteamWishlistChecker(_config, steamAPI, discordAPI);

            await checker.Run();
        }


        private async Task Run()
        {
            await DatabaseHandling.InitDatabase();
            await _discordAPI.Start();

            Console.WriteLine("[Webhook] Config loaded " + _discordAPI.GetWebHookURLs().Capacity + " URLs");
            while (true)
            { 
                int milliseconds_until_time = getTimeDifferenceToNextTime(TimeOnly.Parse(_config.StartingTime,CultureInfo.InvariantCulture));
                await Task.Delay(milliseconds_until_time);

                await DoUpdate();
            }
        }

        private async Task DoUpdate()
        {
            HashSet<(UserID,SteamID)> userID_steamID_s = DatabaseHandling.discord_steam_id_List.Select(k => (k.Key,k.Value.Item1)).ToHashSet();

            if (await _steamAPI.LoadWishlistOfSteamIDs(userID_steamID_s))
            {
                await CheckGamePrices();
            }
        }

        private async Task CheckGamePrices()
        {
            Console.WriteLine("[Bot] Check für reduzierte Spiele beendet um " + DateTime.Now.ToString("dd-MM-yyyy HH:mm"));
            //Get all games, which are reduced
            Dictionary<AppID, SteamAPI.AppBody> reducedGames = _steamAPI.AppBodyCache
                                                                        .Where(k => k.Value.discount > 0)
                                                                        .ToDictionary();
            var maxReducedGames = await DatabaseHandling.AddGamesToDB(reducedGames);
            
            int timeDifferenceInMinutes = (int)  (TimeOnly.Parse(_config.SendTime).ToTimeSpan().TotalMinutes - TimeOnly.Parse(_config.StartingTime).ToTimeSpan().TotalMinutes);
            TimeSpan timeSpanFromMinutes = TimeSpan.FromMinutes(timeDifferenceInMinutes);

            TimeOnly sendMessagesAtTime = TimeOnly.Parse(_config.SendTime,CultureInfo.InvariantCulture);
            int milliseconds_until_time = getTimeDifferenceToNextTime(sendMessagesAtTime);

            if(milliseconds_until_time >= timeSpanFromMinutes.Milliseconds) 
                Console.WriteLine("[Bot] Checking Game Prices took longer than " + timeDifferenceInMinutes/60 + ":" + timeDifferenceInMinutes%60 + "\nIncrease Timespan!" );
            else await Task.Delay(milliseconds_until_time);
            
            // Send Messages to users
            await MessageDiscordUser(maxReducedGames);
        }

        private async Task MessageDiscordUser(Dictionary<AppID, SteamAPI.AppBody> reducedGames)
        {   
            Task runWebhooksAsync = SendWebhooks(_discordAPI.GetWebHookURLs().Values.ToArray(),reducedGames);

            if (_steamAPI.AppIDUserIds.Count <= 0) return;

            //Foreach user
            foreach (UserID user_id in DatabaseHandling.discord_steam_id_List.Keys)
            {
                if(errorOnWishlist.Contains(user_id))
                {
                    await _discordAPI.MessageDiscordUser(DatabaseHandling.discord_steam_id_List[user_id].Item2,
                        "We could not load your Wishlist.\n" +
                        "Is your Wishlist private or did your account get deleted?\n" +
                        "To disable this message, check your wishlist/account or /unsubscribe");
                    continue;
                }
                //When user was newly added, send all games even those which where already reduced this steam sale
                bool sendAllReducedGames = DatabaseHandling.newlyAddedUsers.Contains(user_id);
                if(sendAllReducedGames && _steamAPI.AppIDUserIds.Select(k => k.Value).Any(k => k.Contains(user_id))) DatabaseHandling.newlyAddedUsers.Remove(user_id);
                //AppID List from user 
                HashSet<AppID> appids_from_user = _steamAPI.AppIDUserIds
                                                            .Where(k => k.Value.Contains(user_id))
                                                            .Select(k => k.Key)
                                                            .ToHashSet();

                //Check if reduced Game is in Wishlist of user and filter if game was already reduced
                HashSet<SteamAPI.AppBody> reducedGameInfoListOfUser = reducedGames.Where(k => appids_from_user.Contains(k.Key))
                                                                                    .Select(k => k.Value)
                                                                                    .Where(game => sendAllReducedGames || !game.alreadyReduced)
                                                                                    .ToHashSet();

                await _discordAPI.MessageDiscordUser(DatabaseHandling.discord_steam_id_List[user_id].Item2, reducedGameInfoListOfUser);
            }
            
            
            // if Webhooks need longer, wait before clearing
            await runWebhooksAsync;

            _steamAPI.ClearCache();
            errorOnWishlist.Clear();
        }

        private async Task SendWebhooks(string[] webHooks,Dictionary<AppID, SteamAPI.AppBody> reducedGames )
        {

            
            foreach (var body in reducedGames.Values.Where(game => !game.alreadyReduced))
            {
                string message =
                    $"📉 **{body.name}** hat einen Tiefpreis: " +
                    $"**{body.price / 100.0:F2}€** (-{body.discount}%)!\n" +
                    $"https://store.steampowered.com/app/{body.appID}/";

                try
                {
                    await Task.WhenAll(webHooks.Select(webhook => DiscordWebhook.SendMessage(webhook, message)));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Webhook] Error: {ex}");
                }

                await Task.Delay(500);
            }
        }


        public static int getTimeDifferenceToNextTime(TimeOnly starting_time)
        {
            TimeSpan time_Today = DateTime.Now.TimeOfDay;

            //If time of day is greater than starting_time
            if (time_Today > starting_time.ToTimeSpan())
            {   
                // Get time in Milliseconds until next TimeOfDay from starting_time
                // 24h - time of day + time to start
                return (int) (TimeSpan.FromDays(1).TotalMilliseconds - time_Today.TotalMilliseconds + starting_time.ToTimeSpan().TotalMilliseconds);
            }
            
            return (int) (starting_time.ToTimeSpan().TotalMilliseconds - time_Today.TotalMilliseconds);
        }
    }

}

namespace api.models
{
    public class BotConfig
    {
        public string Token { get; set; } = "";
        public string StartingTime { get; set; } = "14:00";
        public string SendTime { get; set; } = "16:00";
    }
}