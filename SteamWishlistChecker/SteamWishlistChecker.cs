using db;
using Microsoft.Extensions.Configuration;

using SteamAPI = api.SteamAPI;
using SteamConfig = api.models.SteamConfig;
using DiscordAPI = api.DiscordAPI;
using DiscordConfig = api.models.DiscordConfig;
using UserID = System.Int16;
using AppID = System.Int32;
using SteamID = System.Int64;
using System.Globalization;


namespace main
{

    public static class SteamWishlistChecker
    {
        public static IConfigurationRoot Configs { get; private set; }
        public static List<SteamID> errorOnWishlist = new();
        private static DiscordAPI _discordAPI;
        private static SteamAPI _steamAPI;

        //Runs before Main (static constructor)
        static SteamWishlistChecker()
        {
            Configs = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

            _steamAPI = new SteamAPI(GetConfigEntry<SteamConfig>("Steam"));
            _discordAPI = new DiscordAPI(GetConfigEntry<DiscordConfig>("Discord"));
        }

        private static async Task Main(string[] args)
        {
            await DatabaseHandling.InitDatabase();
            await _discordAPI.Start();

            while (true)
            { 
                DateTime today = DateTime.Now;
                TimeOnly starting_time = TimeOnly.Parse("14:00",CultureInfo.InvariantCulture);

                double milliseconds_until_time;
                //If time of day is greater than 16 in Minutes
                if (today.TimeOfDay.TotalMinutes > starting_time.ToTimeSpan().TotalMinutes)
                {   
                    // Get time in Milliseconds until next update at 14:00
                    // 24h - time of day + time to start
                    milliseconds_until_time = TimeSpan.FromDays(1).TotalMilliseconds - today.TimeOfDay.TotalMilliseconds + starting_time.ToTimeSpan().TotalMilliseconds;
                }
                else milliseconds_until_time = starting_time.ToTimeSpan().TotalMilliseconds - today.TimeOfDay.TotalMilliseconds;
                await Task.Delay((int) milliseconds_until_time);

                await DoUpdate();
            }
        }

        private static async Task DoUpdate()
        {
            HashSet<(UserID,SteamID)> userID_steamID_s = DatabaseHandling.discord_steam_id_List.Select(k => (k.Key,k.Value.Item1)).ToHashSet();

            if (await _steamAPI.LoadWishlistOfSteamIDs(userID_steamID_s))
            {
                await CheckGamePrices();
            }
        }

        private static T GetConfigEntry<T>(string section)
        {
            T? config = Configs.GetSection(section).Get<T>();
            if (config == null) {   
                throw new Exception(typeof(T).Name + " is not found in config");
            }

            return config;
        }

        private static async Task CheckGamePrices()
        {
            Console.WriteLine("Starte Check für reduzierte Spiele um " + DateTime.Now.TimeOfDay);
            //Get all games, which are reduced
            Dictionary<AppID, SteamAPI.AppBody> reducedGames = _steamAPI.AppBodyCache
                                                                        .Where(k => k.Value.discount > 0)
                                                                        .ToDictionary();
            var maxReducedGames = await DatabaseHandling.AddGamesToDB(reducedGames);
            
            // Send Messages in at 16:00
            TimeOnly sendMessagesAtTime = TimeOnly.Parse("16:00",CultureInfo.InvariantCulture);
            // Wait time difference between now an 16:00
            double difference = sendMessagesAtTime.ToTimeSpan().TotalMilliseconds - DateTime.Now.TimeOfDay.TotalMilliseconds;
            if(difference < 0) Console.WriteLine("Checking Game Prices took longer than 2h, pls reduce time for checks or increase dedicated Checks");
            else await Task.Delay((int) difference);
            
            // Send Messages to users
            await MessageDiscordUser(maxReducedGames);
        }

        private static async Task MessageDiscordUser(Dictionary<AppID, SteamAPI.AppBody> reducedGames)
        {
            if (_steamAPI.AppID_UserID_List.Count <= 0) return;

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
                if(sendAllReducedGames) DatabaseHandling.newlyAddedUsers.Remove(user_id);
                //AppID List from user 
                HashSet<AppID> appids_from_user = _steamAPI.AppID_UserID_List
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


            _steamAPI.AppBodyCache.Clear();
            _steamAPI.AppID_UserID_List.Clear();
            errorOnWishlist.Clear();
        }
    }
}