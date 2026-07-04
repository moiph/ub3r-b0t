namespace UB3RB0T.Commands
{
    using System.Reflection;
    using System.Threading.Tasks;
    using Discord.WebSocket;

    public class DebugCommand : IDiscordCommand
    {
        public Task<CommandResponse> Process(IDiscordBotContext context)
        {
            var serverId = context.GuildChannel?.Guild.Id.ToString() ?? "n/a";
            var botVersion = Assembly.GetEntryAssembly().GetName().Version.ToString();
            var response = new CommandResponse
            {
                Text = $"```Server ID: {serverId} | Channel ID: {context.Channel.Id} | Your ID: {context.Author.Id} | Shard ID: {context.Client.ShardId} | Version: {botVersion} | Discord.NET Version: {DiscordSocketConfig.Version}```"
            };

            return Task.FromResult(response);
        }
    }
}
