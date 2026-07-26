using Discord;
using Discord.WebSocket;

namespace commands
{
    public interface ISlashCommand
    {
        string Name { get; }

        Task RegisterAsync(SocketGuild? guild = null);

        Task ExecuteAsync(SocketSlashCommand command);
    }
}

