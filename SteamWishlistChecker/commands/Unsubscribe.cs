using api;
using db;
using Discord;
using Discord.WebSocket;

namespace commands
{

    public class Unsubscribe
    {
        private readonly DiscordSocketClient _client;

        public Unsubscribe(DiscordSocketClient client)
        {
            _client = client;
        }

        public async Task RegisterAsync(SocketGuild? guild = null)
        {
            var command = new SlashCommandBuilder()
                .WithName("unsubscribe")
                .WithDescription("Löscht deine gespeicherte Discord und SteamID");

            if (guild != null)
            {
                await guild.CreateApplicationCommandAsync(command.Build());
            }
            else
            {
                await _client.CreateGlobalApplicationCommandAsync(command.Build());
            }

        }

        public void HookHandler()
        {
            _client.SlashCommandExecuted += async (SocketSlashCommand command) =>
            {
                try
                {
                    var discordUserId = command.User.Id;
                    var steamid_indb = await DatabaseHandling.GetSteamIDByDiscordID(discordUserId);
                    
                    if (steamid_indb == -1)
                    {
                        await command.RespondAsync("❌ Du bist nicht registriert");
                        return;
                    }

                    await command.DeferAsync(ephemeral: true);

                    await DatabaseHandling.DeleteUser(discordUserId);
                    Console.WriteLine("Eintrag wurde von " + command.User.GlobalName + " gelöscht");
                    string message = "Deine Discord und SteamID wurden erfolgreich gelöscht, du bekommst in Zukunft keine Nachrichten mehr";
                    
                    await command.FollowupAsync(message);

                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            };

        }
    }
}