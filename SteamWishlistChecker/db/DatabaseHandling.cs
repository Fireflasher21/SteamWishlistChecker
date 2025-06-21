using System.Threading.Tasks;
using api;
using Discord.Rest;
using Microsoft.Data.Sqlite;

namespace db
{
    public static class DatabaseHandling
    {
        //<user_ID,(SteamID,Discord_ID)
        public static readonly Dictionary<Int16, (Int64, ulong)> discord_steam_id_List = new();
        public static readonly HashSet<Int16> newlyAddedUsers = new();
        private static string _dbPath_Folder = Path.Combine(AppContext.BaseDirectory, "database_do_not_delete");
        private static string _dbPath = $"Data Source={Path.Combine(_dbPath_Folder,"steam_tracker.db")}";


        public static async Task InitDatabase()
        {
            Directory.CreateDirectory(_dbPath_Folder);
            using var conn = new SqliteConnection(_dbPath);
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                                CREATE TABLE IF NOT EXISTS Users (
                                User_ID INTEGER PRIMARY KEY,
                                Steam_ID INTEGER NOT NULL,
                                Discord_Id INTEGER UNIQUE NOT NULL
                            );

                            CREATE TABLE IF NOT EXISTS TrackedApps (
                                App_ID INTEGER PRIMARY KEY,
                                App_STEAM_ID INTEGER NOT NULL,
                                LowestPrice INTEGER,
                                MaxDiscountPercent INTEGER,
                                Timestamp INTEGER NOT NULL
                            );

                            CREATE TABLE IF NOT EXISTS SubscribedApps (
                                Tracking_ID INTEGER PRIMARY KEY,
                                UserID INTEGER NOT NULL,
                                AppID INTEGER NOT NULL,
                                FOREIGN KEY (UserID) REFERENCES Users (User_ID),
                                FOREIGN KEY (AppID) REFERENCES TrackedApps (App_ID)
                            );
            ";
            await cmd.ExecuteNonQueryAsync();
            await conn.CloseAsync();

            await RefreshDiscordSteamIDList();
        }
        private static async Task RefreshDiscordSteamIDList()
        {
            using var conn = new SqliteConnection(_dbPath);
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM Users";
            var reader = await cmd.ExecuteReaderAsync();

            Int16 user_id = -1;
            Int64 steamid = -1;
            ulong discordid = 0;

            while (await reader.ReadAsync() == true)
            {
                user_id = reader.GetInt16(0);
                steamid = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
                discordid = reader.IsDBNull(2) ? 0 : ulong.Parse(reader.GetString(2));
                if (user_id != -1) discord_steam_id_List.Add(user_id, (steamid, discordid));
            }
            await reader.CloseAsync();
            await conn.CloseAsync();
        }

        public static async Task AddUser(Int64 steamid, ulong discordid)
        {
            using var conn = new SqliteConnection(_dbPath);
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Users (Steam_ID, Discord_ID)
                VALUES ($sid, $did)
                ON CONFLICT(Discord_ID)
                DO UPDATE SET Steam_ID = $sid;";
            cmd.Parameters.AddWithValue("$sid", steamid);
            cmd.Parameters.AddWithValue("$did", discordid);
            Int16 user_ID = Convert.ToInt16(await cmd.ExecuteScalarAsync());
            await conn.CloseAsync();

            discord_steam_id_List.Add(user_ID, (steamid, discordid));
            newlyAddedUsers.Add(user_ID);
        }

        public static async Task<Int64> GetSteamIDByDiscordID(ulong discordid)
        {
            using var conn = new SqliteConnection(_dbPath);
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Steam_ID FROM Users WHERE User_ID = (SELECT User_ID FROM Users WHERE Discord_Id = $did)";
            cmd.Parameters.AddWithValue("$did", discordid);
            object? result = await cmd.ExecuteScalarAsync();
            await conn.CloseAsync();

            if (result == null || result == DBNull.Value) return -1;

            return Int64.Parse(result.ToString()!);
        }

        public static async Task<Dictionary<Int32, SteamAPI.AppBody>> AddGamesToDB(Dictionary<Int32, SteamAPI.AppBody> reducedGames)
        {
            if (reducedGames.Count <= 0) return reducedGames;

            using var conn = new SqliteConnection(_dbPath);
            await conn.OpenAsync();

            Dictionary<Int32, SteamAPI.AppBody> maxReducedGames = new();
            foreach (Int32 appid in reducedGames.Keys)
            {
                // Get App_ID if exists
                var selectAppIdCmd = conn.CreateCommand();
                selectAppIdCmd.CommandText = "SELECT App_ID, LowestPrice, Timestamp FROM TrackedApps WHERE App_STEAM_ID = $aid";
                selectAppIdCmd.Parameters.AddWithValue("$aid", appid);
                using var reader = await selectAppIdCmd.ExecuteReaderAsync();

                int? appDbId = null;
                int storedPrice = 0;
                int storedTimestamp = 0;

                if (await reader.ReadAsync())
                {
                    appDbId = reader.GetInt32(0);
                    storedPrice = reader.GetInt16(1);
                    storedTimestamp = reader.GetInt32(2);
                }
                await reader.CloseAsync();

                var timestamp = DateOnly.FromDateTime(DateTime.Now);

                var name = reducedGames[appid].name;
                var price = reducedGames[appid].price;
                var discount = reducedGames[appid].discount;

                if (appDbId == null)
                {
                    // Insert app and subscribe
                    var insertAppCmd = conn.CreateCommand();
                    insertAppCmd.CommandText = @"INSERT INTO TrackedApps (App_STEAM_ID, LowestPrice, MaxDiscountPercent, Timestamp)
                                            VALUES ($aid, $price, $discount,$timestamp);
                                            SELECT last_insert_rowid();";
                    insertAppCmd.Parameters.AddWithValue("$aid", appid);
                    insertAppCmd.Parameters.AddWithValue("$price", price);
                    insertAppCmd.Parameters.AddWithValue("$discount", discount);
                    insertAppCmd.Parameters.AddWithValue("$timestamp", timestamp.ToString("yyyyMMdd"));
                    appDbId = Convert.ToInt32(await insertAppCmd.ExecuteScalarAsync());

                    //insert into return Dictionary
                    maxReducedGames.Add(appid, new(appid, name, price, discount));
                }
                else if (price <= storedPrice)
                {
                    var updateCmd = conn.CreateCommand();
                    updateCmd.CommandText = @"UPDATE TrackedApps SET LowestPrice = $price, MaxDiscountPercent = $discount, Timestamp = $timestamp WHERE App_ID = $aid";
                    updateCmd.Parameters.AddWithValue("$price", price);
                    updateCmd.Parameters.AddWithValue("$discount", discount);
                    updateCmd.Parameters.AddWithValue("$timestamp", timestamp.ToString("yyyyMMdd"));
                    updateCmd.Parameters.AddWithValue("$aid", appDbId);
                    await updateCmd.ExecuteNonQueryAsync();

                    //insert into return Dictionary
                    maxReducedGames.Add(appid, new(appid, name, price, discount));


                    if (price == storedPrice)
                    {
                        //to avoid spamming when price is the same as already stored, we check if the sale has ended
                        // == 0 (same day) set bool
                        // > 0 (in future) sale is ongoing, set bool
                        // < 0 (passed) new sale, skip
                        var sale_end = DateOnly.ParseExact(storedTimestamp.ToString(), "yyyyMMdd").AddDays(21);
                        if (sale_end.CompareTo(timestamp) >= 0)
                        {
                            //insert into return Dictionary
                            maxReducedGames[appid].SetAlreadyReduced(true);
                        }
                    }
                }
            }

            await conn.CloseAsync();
            return maxReducedGames;
        }

    }
}