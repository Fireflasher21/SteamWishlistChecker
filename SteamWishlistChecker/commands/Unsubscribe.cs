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

                if (steamIdInDb == -1)
                {
                    await command.RespondAsync(
                        "❌ Du bist nicht registriert.",
                        ephemeral: true);

                    return;
                }

                await command.DeferAsync(ephemeral: true);

                await DatabaseHandling.DeleteUser(discordUserId);

                Console.WriteLine(
                    $"Eintrag wurde von {command.User.GlobalName ?? command.User.Username} gelöscht.");

                await command.FollowupAsync(
                    "✅ Deine Discord- und SteamID wurden erfolgreich gelöscht. Du erhältst künftig keine Benachrichtigungen mehr.",
                    ephemeral: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fehler in Unsubscribe: {ex}");

                if (!command.HasResponded)
                {
                    await command.RespondAsync(
                        "❌ Beim Löschen deiner Daten ist ein Fehler aufgetreten.",
                        ephemeral: true);
                }
                else
                {
                    await command.FollowupAsync(
                        "❌ Beim Löschen deiner Daten ist ein Fehler aufgetreten.",
                        ephemeral: true);
                }
            }
        }
    }
}

