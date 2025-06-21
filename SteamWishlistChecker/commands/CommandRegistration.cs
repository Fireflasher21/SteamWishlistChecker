using Discord.WebSocket;
using SQLitePCL;

namespace commands
{
    public class CommandRegistration
    {
        private readonly DiscordSocketClient _client;

        // Liste aller Commands
        private readonly List<object> _commands = new();

        public CommandRegistration(DiscordSocketClient client)
        {
            _client = client;
        }

        public void Initialize()
        {
            // Instanzieren und speichern
            var setSteamCommand = new SetSteamCommand(_client);
            _commands.Add(setSteamCommand);

            // Registriere alle Commands beim Bot-Start
            _client.Ready += async () =>
            {
                await setSteamCommand.RegisterAsync(); // Globaler Slash-Command
            };

            // Aktiviere die Handler
            setSteamCommand.HookHandler();
        }
    }
}