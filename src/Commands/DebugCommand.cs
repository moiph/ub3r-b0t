namespace UB3RB0T.Commands
{
    using System.Reflection;
    using System.Threading.Tasks;
    using Discord.WebSocket;

    public class DebugCommand : IDiscordCommand
    {
        public Task<CommandResponse> Process(IDiscordBotContext context)
        {
            var message = context.Message;
            var serverId = (message.Channel as SocketGuildChannel)?.Guild.Id.ToString() ?? "n/a";
            var botVersion = Assembly.GetEntryAssembly().GetName().Version.ToString();
            var response = new CommandResponse
            {
                Text = $"```Server ID: {serverId} | Channel ID: {message.Channel.Id} | Your ID: {message.Author.Id} | Shard ID: {context.Client.ShardId} | Version: {botVersion} | Discord.NET Version: {DiscordSocketConfig.Version}```"
            };
            return Task.FromResult(response);
        }
    }
}
