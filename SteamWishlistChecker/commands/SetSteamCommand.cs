using api;
using db;
using Discord;
using Discord.WebSocket;

namespace commands
{
    public class SetSteamCommand : ISlashCommand
    {
        private readonly DiscordSocketClient _client;

        public string Name => "setsteam";

        public SetSteamCommand(DiscordSocketClient client)
        {
            _client = client;
        }

        public async Task RegisterAsync(SocketGuild? guild = null)
        {
            var command = new SlashCommandBuilder()
                .WithName(Name)
                .WithDescription("Verknüpft deinen Discord-Account mit deiner SteamID64")
                .AddOption(
                    "steamid",
                    ApplicationCommandOptionType.String,
                    "Deine SteamID64",
                    isRequired: true);

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
                var steamIdString = command.Data.Options.First().Value.ToString();

                if (!long.TryParse(steamIdString, out long steamId))
                {
                    await command.RespondAsync(
                        "❌ Die angegebene SteamID ist ungültig.",
                        ephemeral: true);

                    return;
                }

                ulong discordUserId = command.User.Id;

                var steamIdInDb = await DatabaseHandling.GetSteamIDByDiscordID(discordUserId);

                // Benutzer hat bereits eine SteamID hinterlegt
                if (steamIdInDb != -1)
                {
                    await command.RespondAsync(
                        "❌ Du hast bereits eine SteamID hinterlegt.\n" +
                        "Bitte führe zuerst den Befehl `/unsubscribe` aus, " +
                        "um deine aktuelle Verknüpfung zu löschen.",
                        ephemeral: true);

                    return;
                }

                await command.DeferAsync(ephemeral: true);

                var isValid = await SteamAPI.IsSteamIDValid(steamId);

                switch (isValid)
                {
                    case -1:
                        await command.FollowupAsync(
                            "❌ Diese SteamID ist ungültig.",
                            ephemeral: true);
                        break;

                    case -2:
                        await command.FollowupAsync(
                            "❌ Die Wunschliste dieses Steam-Profils ist privat.",
                            ephemeral: true);
                        break;

                    case 1:
                        await DatabaseHandling.AddUser(steamId, discordUserId);

                        Console.WriteLine(
                            $"SteamID {steamId} wurde für " +
                            $"{command.User.GlobalName ?? command.User.Username} gespeichert.");

                        await command.FollowupAsync(
                            "✅ Deine SteamID wurde erfolgreich gespeichert.",
                            ephemeral: true);
                        break;

                    default:
                        await command.FollowupAsync(
                            "❌ Es ist ein unbekannter Fehler bei der SteamID-Prüfung aufgetreten.",
                            ephemeral: true);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fehler in SetSteamCommand: {ex}");

                if (!command.HasResponded)
                {
                    await command.RespondAsync(
                        "❌ Beim Speichern deiner SteamID ist ein Fehler aufgetreten.",
                        ephemeral: true);
                }
                else
                {
                    await command.FollowupAsync(
                        "❌ Beim Speichern deiner SteamID ist ein Fehler aufgetreten.",
                        ephemeral: true);
                }
            }
        }
    }
}

