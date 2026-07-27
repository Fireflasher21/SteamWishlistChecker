namespace api;

using UserID = System.Int16;
using AppID = System.Int32;
using SteamID = System.Int64;

public interface ISteamAPI
{
    IReadOnlyDictionary<AppID, HashSet<UserID>> AppIDUserIds { get; }

    IReadOnlyDictionary<AppID, SteamAPI.AppBody> AppBodyCache { get; }

    void ClearCache();

    Task<bool> LoadWishlistOfSteamIDs(HashSet<(UserID, SteamID)> userSteamIds);

    Task CheckPricesOfAppIDs();
}
