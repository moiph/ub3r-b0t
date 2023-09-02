namespace UB3RB0T.Commands
{
    using System.Linq;
    using System.Threading.Tasks;
    using Discord;
    using Discord.WebSocket;

    public class ReputationCommand : IDiscordCommand
    {
        public async Task<CommandResponse> Process(IDiscordBotContext context)
        {
            CommandResponse response = null;
            string[] parts = context.MessageData.Content.Split(new[] { ' ' }, 3);
            if (parts.Length >= 3)
            {
                if (parts[1].IEquals("set"))
                {
                    var guildUser = context.Message.Author as SocketGuildUser;
                    if (guildUser == null || !guildUser.GuildPermissions.Has(GuildPermission.ManageGuild))
                    {
                        response = new CommandResponse { Text = "sorry boss but overriding reputation requires manage server permissions. ask management to do it. and be a jerk about it. trust me it works every time" };
                    }
                }
                else if (parts[1].IEquals("add") || parts[1].IEquals("remove"))
                {
                    // verify target user
                    IUser targetUser = context.SocketMessage?.MentionedUsers?.FirstOrDefault();
                    if (targetUser == null)
                    {
                        if (ulong.TryParse(parts[2], out var userId))
                        {
                            await context.GuildChannel.Guild.DownloadUsersAsync();
                            targetUser = context.GuildChannel.GetUser(userId);
                        }
                    }
                    
                    if (targetUser == null)
                    {
                        response = new CommandResponse { Text = "Invalid user supplied" };
                    }
                }
            }

            return response;
        }
    }
}
