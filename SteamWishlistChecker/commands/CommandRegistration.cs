using Discord.WebSocket;

namespace commands
{
    public class CommandRegistration
    {
        private readonly DiscordSocketClient _client;
        private readonly Dictionary<string, ISlashCommand> _commands = new();

        private bool _commandsRegistered = false;

        public CommandRegistration(DiscordSocketClient client)
        {
            _client = client;
        }

        public void Initialize()
        {
            // Commands hinzufügen
            Register(new SetSteamCommand(_client));
            Register(new Unsubscribe(_client));

            // Events abonnieren
            _client.Ready += OnReady;
            _client.SlashCommandExecuted += OnSlashCommandExecuted;
        }

        private void Register(ISlashCommand command)
        {
            _commands.Add(command.Name, command);
        }

        private async Task OnReady()
        {
            // Verhindert doppelte Registrierung bei Reconnects
            if (_commandsRegistered)
                return;

            _commandsRegistered = true;

            foreach (var command in _commands.Values)
            {
                await command.RegisterAsync();
            }
        }

        private async Task OnSlashCommandExecuted(SocketSlashCommand command)
        {
            if (_commands.TryGetValue(command.Data.Name, out var slashCommand))
            {
                await slashCommand.ExecuteAsync(command);
            }
        }
    }
}


