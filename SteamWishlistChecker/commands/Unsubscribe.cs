using db;
using Discord;
using Discord.WebSocket;

namespace commands
{
    public class Unsubscribe : ISlashCommand
    {
        private readonly DiscordSocketClient _client;

        public string Name => "unsubscribe";

        public Unsubscribe(DiscordSocketClient client)
        {
            _client = client;
        }

        public async Task RegisterAsync(SocketGuild? guild = null)
        {
            var command = new SlashCommandBuilder()
                .WithName(Name)
                .WithDescription("Löscht deine gespeicherte Discord- und SteamID");

            if (guild != null)
            {
                await guild.CreateApplicationCommandAsync(command.Build());
            }
            else
            {
                await _client.CreateGlobalApplicationCommandAsync(command.Build());
            }
        }

        public async Task ExecuteAsync(SocketSlashCommand command)
        {
            try
            {
                ulong discordUserId = command.User.Id;

                var steamIdInDb = await DatabaseHandling.GetSteamIDByDiscordID(discordUserId);
                
                String message = "❌ Du bist nicht registriert.";
                
                if (steamIdInDb == -1)
                {
                    await command.RespondAsync(message,ephemeral: true);

                    return;
                }

                await command.DeferAsync(ephemeral: true);

                await DatabaseHandling.DeleteUser(discordUserId);
                
                message = "✅ Deine Discord- und SteamID wurden erfolgreich gelöscht. Du erhältst künftig keine Benachrichtigungen mehr.";
                
                Console.WriteLine($"Eintrag wurde von {command.User.GlobalName ?? command.User.Username} gelöscht.");

                await command.FollowupAsync(message,ephemeral: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fehler in Unsubscribe: {ex}");
                message = "❌ Beim Löschen deiner Daten ist ein Fehler aufgetreten.";
                
                if (!command.HasResponded)
                {
                    await command.RespondAsync(message,ephemeral: true);
                }
                else
                {
                    await command.FollowupAsync(message,ephemeral: true);
                }
            }
        }
    }
}

