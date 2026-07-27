namespace api;

public interface IDiscordAPI
{
    Task Start();

    Task MessageDiscordUser(ulong discordId, HashSet<SteamAPI.AppBody> appBodies);

    Task MessageDiscordUser(ulong discordId, string message);
}
