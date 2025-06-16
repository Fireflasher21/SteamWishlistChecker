using Discord.WebSocket;

namespace api
{
    public class DiscordAPI
    {
        public static async Task MessageDiscordUser(DiscordSocketClient client, ulong discordid, HashSet<SteamAPI.AppBody> appBodies)
        {
            var discordUser = await client.Rest.GetUserAsync(discordid);
            var dmChannel = await discordUser.CreateDMChannelAsync();
            foreach (SteamAPI.AppBody body in appBodies)
            {
                await dmChannel.SendMessageAsync($"📉 **{body.name}** hat einen Tiefpreis: **{body.price / 100.0:F2}€** (-{body.discount}%)!\nhttps://store.steampowered.com/app/{body.appid}/");
                Console.WriteLine("User " + discordUser.GlobalName + " wurde benachrichtigt für " + body.name);
            }
        }   
    }
}