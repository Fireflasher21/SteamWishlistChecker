using api;
using db;
using Discord;
using Discord.WebSocket;

namespace commands
{

    public class SetSteamCommand
    {
        private readonly DiscordSocketClient _client;

        public SetSteamCommand(DiscordSocketClient client)
        {
            _client = client;
        }

        public async Task RegisterAsync(SocketGuild? guild = null)
        {
            var command = new SlashCommandBuilder()
                .WithName("setsteam")
                .WithDescription("Verknüpft deinen Discord-Account mit deiner SteamID64")
                .AddOption("steamid", ApplicationCommandOptionType.String, "Deine SteamID64", isRequired: true);

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
                    Int64 steamid = Int64.Parse(command.Data.Options.First().Value.ToString()!);
                    var discordUserId = command.User.Id;
                    var steamid_indb = await DatabaseHandling.GetSteamIDByDiscordID(discordUserId);
                    if (steamid_indb == steamid)
                    {
                        await command.RespondAsync("❌ Diese SteamID ist bereits hinterlegt");
                        return;
                    }

                    await command.DeferAsync(ephemeral: true);
                    var isValid = await SteamAPI.IsSteamIDValid(steamid);
                    string message = "";
                    switch (isValid)
                    {
                        case -1:
                            message = "❌ Diese SteamID ist ungültig";
                            break;
                        case -2:
                            message = "❌ Die Wunschliste ist privat";
                            break;
                        case 1:
                            message = "✅ Deine SteamID wurde erfolgreich gespeichert.";
                            await DatabaseHandling.AddUser(steamid, discordUserId);
                            Console.WriteLine("Steam ID " + steamid + " wurde für " + command.User.GlobalName + " gespeichert");
                            break;
                    }
                    await command.FollowupAsync(message);

                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.StackTrace);
                }
            };

        }
    }
}