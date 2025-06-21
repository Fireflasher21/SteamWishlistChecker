using System.Net;
using api;
using commands;
using db;
using Discord;
using Discord.Rest;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using SQLitePCL;
using Timer = System.Timers.Timer;


namespace main
{

    public class SteamWishlistChecker
    {

        public static SteamWishlistChecker steamWishlistChecker { get; private set; }
        public static IConfigurationRoot Configs { get; private set; }
        public readonly DiscordSocketClient _client;
        private OAuthenticator oAuthenticator;
        private readonly Timer _dailyTimer = new(TimeSpan.FromDays(1).TotalMilliseconds); // täglich
        private static string BOT_TOKEN { get; set; }

        private static async Task Main(string[] args)
        {
            LoadConfig();

            var client = new DiscordSocketClient();
            await client.LoginAsync(TokenType.Bot, BOT_TOKEN);
            await client.StartAsync();

            // Command-Registrierung vorbereiten
            var commandRegistration = new CommandRegistration(client);
            commandRegistration.Initialize();

            //Starting of service
            steamWishlistChecker = new SteamWishlistChecker(client);
            steamWishlistChecker.oAuthenticator = new();
            steamWishlistChecker.oAuthenticator.StartOAuthListener();

            await steamWishlistChecker.Start();
            await Task.Delay(-1);
        }


        public SteamWishlistChecker(DiscordSocketClient client)
        {
            _client = client;
        }

        private static void LoadConfig()
        {
            Configs = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();
            BOT_TOKEN = Configs.GetSection("Discord").Get<api.models.DiscordConfig>()!.BotToken;
        }

        private async Task Start()
        {
            await DatabaseHandling.InitDatabase();
            HashSet<Int64> steamIDs = DatabaseHandling.discord_steam_id_List.Select(k => k.Value.Item1).ToHashSet();
            _dailyTimer.Elapsed += async (_, _) => await SteamAPI.LoadWishlistOfSteamIDs(steamIDs);
            _dailyTimer.Start();
            Console.WriteLine("DailyTimer gestartet");
            await SteamAPI.LoadWishlistOfSteamIDs(steamIDs);
        }

        public async Task CheckGamePrices()
        {
            Console.WriteLine("Starte Check für reduzierte Spiele");
            //Get all games, which are reduced
            Dictionary<Int32, SteamAPI.AppBody> reducedGames = SteamAPI.AppBodyCache
                                                                        .Where(k => k.Value.discount > 0)
                                                                        .ToDictionary();
            var maxReducedGames = await DatabaseHandling.AddGamesToDB(reducedGames);

            await MessageDiscordUser(maxReducedGames);
        }

        private async Task MessageDiscordUser(Dictionary<Int32, SteamAPI.AppBody> reducedGames)
        {
            if (SteamAPI.AppID_List.Count <= 0) return;

            //Foreach user
            foreach (Int16 user_id in DatabaseHandling.discord_steam_id_List.Keys)
            {
                //When user was newly added, send all games even those which where already reduced this steam sale
                bool sendAllReducedGames = DatabaseHandling.newlyAddedUsers.Contains(user_id);
                if(sendAllReducedGames) DatabaseHandling.newlyAddedUsers.Remove(user_id);
                //AppID List from user 
                HashSet<Int32> appids_from_user = SteamAPI.AppID_List
                                                            .Where(k => k.Value.Contains(user_id))
                                                            .Select(k => k.Key)
                                                            .ToHashSet();

                //Check if reduced Game is in Wishlist of user and filter if game was already reduced
                HashSet<SteamAPI.AppBody> reducedGameInfoListOfUser = reducedGames.Where(k => appids_from_user.Contains(k.Key))
                                                                                    .Select(k => k.Value)
                                                                                    .Where(game => sendAllReducedGames || !game.alreadyReduced)
                                                                                    .ToHashSet();

                await DiscordAPI.MessageDiscordUser(_client, DatabaseHandling.discord_steam_id_List[user_id].Item2, reducedGameInfoListOfUser);
            }


            SteamAPI.AppBodyCache.Clear();
            SteamAPI.AppID_List.Clear();
        }
    }
}