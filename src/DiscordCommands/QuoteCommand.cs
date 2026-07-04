namespace UB3RB0T.Commands
{
    using System.Threading.Tasks;
    using Discord;
    using Discord.WebSocket;

    public class QuoteCommand : IDiscordCommand
    {
        public Task<CommandResponse> Process(IDiscordBotContext context)
        {
            CommandResponse response = null;
            string[] parts = context.MessageData.Content.Split(new[] { ' ' }, 3);
            if (parts.Length >= 3 && (parts[1].IEquals("del") || parts[1].IEquals("delete") || parts[1].IEquals("remove")))
            {
                var guildUser = context.Message.Author as SocketGuildUser;
                if (guildUser == null || !guildUser.GuildPermissions.Has(GuildPermission.ManageMessages))
                {
                    response = new CommandResponse { Text = "sorry boss but removing quotes requires manage message permissions. ask management to remove it. and be a jerk about it. trust me it works every time" };
                }
            }

            return Task.FromResult(response);
        }
    }
}
