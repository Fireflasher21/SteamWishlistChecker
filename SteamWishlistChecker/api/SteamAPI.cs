using db;
using Microsoft.Extensions.Configuration;
using main;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace api
{
    public class SteamAPI
    {

        //<AppID, List<userID>>
        public static readonly Dictionary<Int32, HashSet<Int16>> AppID_List = new();

        //<AppID, AppInfoBody>
        public static readonly Dictionary<Int32, AppBody> AppBodyCache = new();


        private static readonly string STEAM_API_KEY = SteamWishlistChecker.Configs.GetSection("Steam").Get<models.SteamConfig>()!.STEAM_API_KEY;
        private static readonly string API_APP_URL = "https://store.steampowered.com/api/appdetails?appids={0}&cc=de&l=de";
        private static readonly string API_WISHL_URL = "https://api.steampowered.com/IWishlistService/GetWishlist/v1?key=" + STEAM_API_KEY + "&steamid={0}";
        private static readonly string API_STEAM_ID = "https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key=" + STEAM_API_KEY + "&steamids={0}";
        private static readonly string steamID64Pattern = @"^7656119[0-9]{10}$";

        public static async Task LoadWishlistOfSteamIDs(HashSet<Int64> steamIDs)
        {
            Console.WriteLine("Starte Wunschlisten update um: " + DateTime.Now.ToString("dd-MM-yyyy HH:MM"));
            try
            {
                var httpClient = new HttpClient();

                foreach (Int64 steamid in steamIDs)
                {
                    string url = string.Format(API_WISHL_URL, steamid);
                    var response = await httpClient.GetStringAsync(url);
                    var data = JObject.Parse(response);
                    var responseBody = data["response"];
                    if (responseBody == null) throw new Exception();
                    var appIds = responseBody["items"]?
                        .Select(item => Int32.Parse(item["appid"]!.ToString()))
                        .Where(id => id != 0)
                        .ToList();


                    if (appIds is not null)
                    {
                        //First add all AppIDs to our Dictionary
                        foreach (Int32 appid in appIds) AppID_List.Add(appid, new());
                        //Get DB user_ID from list
                        Int16 user_ID = DatabaseHandling.discord_steam_id_List
                                                                    .Where(k => k.Value.Item1 == steamid)
                                                                    .Select(k => k.Key)
                                                                    .ToList().First();
                        //Add user_id to AppID
                        appIds.ForEach(i => AppID_List[i].Add(user_ID));

                    }
                }
                httpClient.Dispose();
            }
            catch { }

            await CheckPricesOfAppIDs();
        }

        public static async Task CheckPricesOfAppIDs()
        {
            Console.WriteLine("Starte Spielpreis update");
            var httpClient = new HttpClient();
            foreach (Int32 appID in AppID_List.Keys)
            {
                string url = string.Format(API_APP_URL, appID);
                var response = await httpClient.GetStringAsync(url);
                //Check if Body exists
                var data = JObject.Parse(response)[appID.ToString()];
                if (data == null || data["success"]?.Value<bool>() != true) continue;

                //get name for game
                var appData = data["data"];
                string name = appData["name"]?.ToString() ?? "Unbekannt ";
                //get price for Game
                var priceData = appData["price_overview"];

                //when no price date, we cant get price
                Int16 finalPrice = 0;
                Int16 discount = 0;
                if (priceData != null)
                {
                    finalPrice = priceData["final"]?.Value<Int16>() ?? 0;
                    discount = priceData["discount_percent"]?.Value<Int16>() ?? 0;
                }

                AppBodyCache.Add(appID, new(appID, name, finalPrice, discount));
                await Task.Delay((int)TimeSpan.FromSeconds(5).TotalMilliseconds);
            }
            httpClient.Dispose();

            await SteamWishlistChecker.steamWishlistChecker.CheckGamePrices();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="steamid"></param>
        /// <returns>1 when valid and public Wishlist, -1 when invalid steamid, -2 when privat wishlist</returns>
        public static async Task<int> IsSteamIDValid(Int64 steamid)
        {
            if (!Regex.IsMatch(steamid.ToString(),steamID64Pattern)) return -1;
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
            public Int32 appid { get; private set; }
            public string name { get; private set; }
            public Int16 price { get; private set; }
            public Int16 discount { get; private set; }
            public bool alreadyReduced { get; private set; }

            public AppBody(Int32 appid, string name, Int16 price, Int16 discount)
            {
                this.appid = appid;
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

