using db;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

using SteamConfig = api.models.SteamConfig;
using UserID = System.Int16;
using AppID = System.Int32;
using SteamID = System.Int64;

namespace api
{


    public class SteamAPI
    {

        //<AppID, List<userID>>
        public readonly Dictionary<AppID, HashSet<UserID>> AppID_UserID_List = new();

        //<AppID, AppInfoBody>
        public readonly Dictionary<AppID, AppBody> AppBodyCache = new();

        private SteamConfig _config;
        private static readonly string API_APP_URL = "https://store.steampowered.com/api/appdetails?appids={0}&cc=de&l=de";
        private static string API_WISHL_URL = "https://api.steampowered.com/IWishlistService/GetWishlist/v1?key={0}&steamid=";
        private static string API_STEAM_ID = "https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key={0}&steamids=";
        private static readonly string steamID64Pattern = @"^7656119[0-9]{10}$";

        
        public SteamAPI(SteamConfig config)
        {
            _config = config;
            API_WISHL_URL = string.Format(API_WISHL_URL,_config.STEAM_API_KEY) + "{0}";
            API_STEAM_ID  = string.Format(API_STEAM_ID,_config.STEAM_API_KEY) + "{0}";
        }
        
        public async Task<bool> LoadWishlistOfSteamIDs(HashSet<SteamID> steamIDs)
        {

            Console.WriteLine("Starte Wunschlisten update um: " + DateTime.Now.ToString("dd-MM-yyyy HH:MM"));
            try
            {
                var httpClient = new HttpClient();

                foreach (SteamID steamid in steamIDs)
                {
                    string url = string.Format(API_WISHL_URL, steamid);
                    var response = await httpClient.GetStringAsync(url);
                    var data = JObject.Parse(response);
                    var responseBody = data["response"];
                    if (responseBody == null) throw new Exception();
                    var AppIDs = responseBody["items"]!
                        .Select(item => AppID.Parse(item["appid"]!.ToString()))
                        .Where(id => id != 0)
                        .ToList();


                    if (AppIDs is not null)
                    {
                        //First add all AppIDs to our Dictionary
                        //foreach (AppID appID in AppIDs) AppID_User_List.Add(appID, new());
                        AppIDs.ForEach(id =>
                        {
                            //Catch Breakpoint when id already exists
                            if(AppID_UserID_List.ContainsKey(id))return; 
                            AppID_UserID_List.Add(id, new());
                        });

                        //Get DB user_ID from list
                        UserID user_ID = DatabaseHandling.discord_steam_id_List
                                                                    .Where(k => k.Value.Item1 == steamid)
                                                                    .Select(k => k.Key)
                                                                    .ToList().First();
                        //Add user_id to AppID
                        AppIDs.ForEach(i => AppID_UserID_List[i].Add(user_ID));

                    }
                }
                httpClient.Dispose();
            }
            catch
            {
                Console.WriteLine("Something went wrong Loading Wishlists of SteamIDs");
                return false;
            }

            await CheckPricesOfAppIDs();
            return true;
        }

        public async Task CheckPricesOfAppIDs()
        {
            Console.WriteLine("Starte Spielpreis update");
            var httpClient = new HttpClient();
            foreach (AppID AppID in AppID_UserID_List.Keys)
            {
                string url = string.Format(API_APP_URL, AppID);
                var response = await httpClient.GetStringAsync(url);
                //Check if Body exists
                var data = JObject.Parse(response)[AppID.ToString()];
                if (data == null || data["success"]?.Value<bool>() != true) continue;

                //get name for game
                var appData = data["data"];
                string name = appData?["name"]?.ToString() ?? "Unbekannt ";
                //get price for Game
                var priceData = appData?["price_overview"];

                //when no price date, we cant get price
                Int16 finalPrice = 0;
                Int16 discount = 0;
                if (priceData != null)
                {
                    finalPrice = priceData["final"]?.Value<Int16>() ?? 0;
                    discount = priceData["discount_percent"]?.Value<Int16>() ?? 0;
                }

                AppBodyCache.Add(AppID, new(AppID, name, finalPrice, discount));
                await Task.Delay((int)TimeSpan.FromSeconds(2).TotalMilliseconds);
            }
            httpClient.Dispose();

            //    await SteamWishlistChecker.steamWishlistChecker.CheckGamePrices();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="steamid"></param>
        /// <returns>1 when valid and public Wishlist, -1 when invalid steamid, -2 when privat wishlist</returns>
        public static async Task<int> IsSteamIDValid(SteamID steamid)
        {
            if (!Regex.IsMatch(steamid.ToString(), steamID64Pattern)) return -1;
            var httpClient = new HttpClient();
            string url = string.Format(API_STEAM_ID, steamid);

            var response = await httpClient.GetStringAsync(url);
            if (!response.Contains(steamid.ToString())) return -1;

            url = string.Format(API_WISHL_URL, steamid);
            response = await httpClient.GetStringAsync(url);

            var data = JObject.Parse(response);
            var responseBody = data["response"];
            return responseBody == null ? -2 : 1;
        }



        public class AppBody
        {
            public AppID appID { get; private set; }
            public string name { get; private set; }
            public Int16 price { get; private set; }
            public Int16 discount { get; private set; }
            public bool alreadyReduced { get; private set; }

            public AppBody(AppID appID, string name, Int16 price, Int16 discount)
            {
                this.appID = appID;
                this.name = name;
                this.price = price;
                this.discount = discount;
                alreadyReduced = false;
            }

            public void SetAlreadyReduced(bool isAlreadyReduced)
            {
                alreadyReduced = isAlreadyReduced;
            }
        }
    }

    namespace models
    {
        public class SteamConfig
        {
            public string STEAM_API_KEY { get; set; } = "";
        }
    }

}

