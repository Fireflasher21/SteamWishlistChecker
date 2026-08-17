using System.Threading.Tasks;
using api;
using Discord.Rest;
using Microsoft.Data.Sqlite;

using UserID = System.Int16;
using AppID = System.Int32;
using SteamID = System.Int64;
using System.ComponentModel;
using System.Collections.Specialized;
using Discord;

namespace db
{
    public static class DatabaseHandling
    {
        //<user_ID,(SteamID,Discord_ID)
        public static readonly Dictionary<UserID, (SteamID, ulong)> discord_steam_id_List = new();
        public static readonly HashSet<UserID> newlyAddedUsers = new();
        private static string _dbPath_Folder = Path.Combine(AppContext.BaseDirectory, "database_do_not_delete");
        private static string _dbPath = $"Data Source={Path.Combine(_dbPath_Folder, "steam_tracker.db")}";


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

            UserID user_id = -1;
            SteamID steamid = -1;
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

        public static async Task AddUser(SteamID steamid, ulong discordid)
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

        public static async Task DeleteUser(ulong discordid)
        {
            using var conn = new SqliteConnection(_dbPath);
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM Users 
                WHERE Discord_ID = $did";
            cmd.Parameters.AddWithValue("$did", discordid);
            var result = await cmd.ExecuteScalarAsync();
            await conn.CloseAsync();
            
            if (result != null)
            {   
                Int16 user_ID = Convert.ToInt16(result);
                discord_steam_id_List.Remove(user_ID);
                if(newlyAddedUsers.Contains(user_ID)) newlyAddedUsers.Remove(user_ID);
            }
        }

        public static async Task<SteamID> GetSteamIDByDiscordID(ulong discordid)
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

        public static async Task<Dictionary<AppID, SteamAPI.AppBody>> AddGamesToDB(Dictionary<AppID, SteamAPI.AppBody> reducedGames)
        {
            if (reducedGames.Count == 0) return reducedGames;

            using var conn = new SqliteConnection(_dbPath);
            await conn.OpenAsync();

            var today = DateOnly.FromDateTime(DateTime.Now);
            var todayInt = int.Parse(today.ToString("yyyyMMdd"));

            // Select games only from database which match the games in reducedGameslist
            var appIds = reducedGames.Keys.ToArray();            
            var trackedGames = new Dictionary<AppID, TrackedGame>();

            // Check SQLite has parameter limit.
            const int batchSize = 500;

            for (int offset = 0; offset < appIds.Length; offset += batchSize)
            {
                var batch = appIds
                    .Skip(offset)
                    .Take(batchSize)
                    .ToArray();

                // using to avoid CloseCon at the end
                using var selectCmd = conn.CreateCommand();


                var parameters = new string[batch.Length];

                // using parameter array to add appIDs as identifier later on
                for (int i = 0; i < batch.Length; i++)
                {
                    var parameterName = $"$id{i}";

                    parameters[i] = parameterName;
                    selectCmd.Parameters.AddWithValue(parameterName, batch[i]);
                }

                // create SQL command for all appIDs to only select games in list
                selectCmd.CommandText = $"""
                    SELECT App_STEAM_ID, App_ID, LowestPrice, Timestamp
                    FROM TrackedApps
                    WHERE App_STEAM_ID IN ({string.Join(", ", parameters)})
                    """;

                using var reader = await selectCmd.ExecuteReaderAsync();

                // read all querys to games in Database
                while (await reader.ReadAsync())
                {
                    var steamId = reader.GetInt32(0);
                    var dbId = reader.GetInt32(1);
                    var storedPrice = reader.GetInt32(2);
                    var storedTimestamp = reader.GetInt32(3);

                    trackedGames[steamId] = new TrackedGame(
                        dbId,
                        storedPrice,
                        storedTimestamp
                    );
                }
            }


            using var transaction = conn.BeginTransaction();

            try
            {
                using var upsertCmd = conn.CreateCommand();

                upsertCmd.Transaction = transaction;

                upsertCmd.CommandText = """
                    INSERT INTO TrackedApps
                        (App_STEAM_ID, LowestPrice, MaxDiscountPercent, Timestamp)
                    VALUES
                        ($steamId, $price, $discount, $timestamp)

                    ON CONFLICT(App_STEAM_ID)
                    DO UPDATE SET
                        LowestPrice = excluded.LowestPrice,
                        MaxDiscountPercent = excluded.MaxDiscountPercent,
                        Timestamp = excluded.Timestamp;
                    """;

                upsertCmd.Parameters.Add("$steamId", SqliteType.Integer);
                upsertCmd.Parameters.Add("$price", SqliteType.Integer);
                upsertCmd.Parameters.Add("$discount", SqliteType.Integer);
                upsertCmd.Parameters.Add("$timestamp", SqliteType.Integer);

                var maxReducedGames = new Dictionary<AppID, SteamAPI.AppBody>();

                foreach (var (appid, game) in reducedGames)
                {
                    bool exists = trackedGames.TryGetValue(appid, out var stored);

                    // skip if price is higher than db price
                    if (exists && game.price > stored.Price)
                        continue;


                    if (exists && game.price == stored.Price)
                    {
                        // add timestamp for date of sale end of last stored timestamp
                        var saleEnd = DateOnly.ParseExact(stored.Timestamp.ToString(),"yyyyMMdd").AddDays(21);

                        var maxReducedGame = new SteamAPI.AppBody(appid,game.name,game.price,game.discount);

                        maxReducedGames.Add(appid, maxReducedGame);

                        // if last sale is still ongoing (saleEnd is after, or today), set bool and skip db entry
                        if (saleEnd >= today)
                        {
                            maxReducedGame.SetAlreadyReduced(true);
                            continue;
                        }
                    }
                    // price is lower or new game
                    else if (!exists || game.price < stored.Price)
                    {
                        maxReducedGames[appid] = new SteamAPI.AppBody(appid,game.name,game.price,game.discount);
                    }

                    // due to upsert, insert or update db entry
                    upsertCmd.Parameters["$steamId"].Value = appid;
                    upsertCmd.Parameters["$price"].Value = game.price;
                    upsertCmd.Parameters["$discount"].Value = game.discount;
                    upsertCmd.Parameters["$timestamp"].Value = todayInt;

                    await upsertCmd.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();

                return maxReducedGames;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public static async Task<List<AppID>> getAllTrackedGameIDs()
        {
            List<AppID> appIDList = [];
            using var conn = new SqliteConnection(_dbPath);
            await conn.OpenAsync();

            var selectAppIdCmd = conn.CreateCommand();
            selectAppIdCmd.CommandText = "SELECT App_ID FROM TrackedApps";

            using var reader = await selectAppIdCmd.ExecuteReaderAsync();
            while(await reader.ReadAsync()) appIDList.Add(reader.GetInt32(0));
            
            await conn.CloseAsync();
            return appIDList;
        }

        private record TrackedGame(
            int DbId,
            int Price,
            int Timestamp
        );

    }
}
