namespace UB3RB0T.Commands
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Discord;
    using Discord.WebSocket;

    public class UserInfoCommand : IDiscordCommand
    {
        public async Task<CommandResponse> Process(IDiscordBotContext context)
        {
            SocketUser targetUser = context.Message.MentionedUsers?.FirstOrDefault();

            if (targetUser == null)
            {
                var messageParts = context.Message.Content.Split(new[] { ' ' }, 2);

                if (messageParts.Length == 2)
                {
                    if (context.Message.Channel is SocketGuildChannel guildChannel)
                    {
                        await guildChannel.Guild.DownloadUsersAsync();
                        targetUser = guildChannel.Guild.Users.Find(messageParts[1]).FirstOrDefault() as SocketUser;
                    }

                    if (targetUser == null)
                    {
                        return new CommandResponse { Text = "User not found. Try a direct mention." };
                    }
                }
                else
                {
                    targetUser = context.Message.Author;
                }
            }

            if (!(targetUser is SocketGuildUser guildUser))
            {
                return null;
            }

            var userInfo = new
            {
                Title = $"UserInfo for {targetUser.Username}#{targetUser.Discriminator}",
                AvatarUrl = targetUser.GetAvatarUrl(),
                NicknameInfo = !string.IsNullOrEmpty(guildUser.Nickname) ? $" aka {guildUser.Nickname}" : "",
                Footnote = CommandsConfig.Instance.UserInfoSnippets.Random(),
                Created = $"{targetUser.GetCreatedDate():dd MMM yyyy} {targetUser.GetCreatedDate():hh:mm:ss tt} UTC",
                Joined = guildUser.JoinedAt.HasValue ? $"{guildUser.JoinedAt.Value:dd MMM yyyy} {guildUser.JoinedAt.Value:hh:mm:ss tt} UTC" : "[data temporarily missing]",
                Id = targetUser.Id.ToString(),
                JoinPosition = guildUser.Guild.Users.OrderBy(u => u.JoinedAt).Select((Value, Index) => new { Value, Index }).Single(u => u.Value.Id == targetUser.Id).Index
            };

            EmbedBuilder embedBuilder = null;
            string text = string.Empty;

            var settings = SettingsConfig.GetSettings(guildUser.Guild.Id.ToString());

            if ((context.Message.Channel as ITextChannel).GetCurrentUserPermissions().EmbedLinks && settings.PreferEmbeds)
            {
                embedBuilder = new EmbedBuilder
                {
                    Title = userInfo.Title,
                    ThumbnailUrl = userInfo.AvatarUrl,
                };

                if (!string.IsNullOrEmpty(userInfo.NicknameInfo))
                {
                    embedBuilder.Description = userInfo.NicknameInfo;
                }

                embedBuilder.Footer = new EmbedFooterBuilder
                {
                    Text = userInfo.Footnote,
                };

                embedBuilder.AddField((field) =>
                {
                    field.IsInline = true;
                    field.Name = "created";
                    field.Value = userInfo.Created;
                });

                embedBuilder.AddField((field) =>
                {
                    field.IsInline = true;
                    field.Name = "join position";
                    field.Value = userInfo.JoinPosition;
                });

                if (guildUser.JoinedAt.HasValue)
                {
                    embedBuilder.AddField((field) =>
                    {
                        field.IsInline = true;
                        field.Name = "joined";
                        field.Value = userInfo.Joined;
                    });
                }

                embedBuilder.AddField((field) =>
                {
                    field.IsInline = true;
                    field.Name = "id";
                    field.Value = userInfo.Id;
                });

                var roles = new List<string>();
                foreach (var role in guildUser.Roles)
                {
                    if (role.Name != "@everyone")
                    {
                        roles.Add(role.Name.TrimStart('@'));
                    }
                }

                if (roles.Count > 0)
                {
                    embedBuilder.AddField((field) =>
                    {
                        field.IsInline = false;
                        field.Name = "roles";
                        field.Value = string.Join(", ", roles);
                    });
                }
            }
            else
            {
                text = $"{userInfo.Title}{userInfo.NicknameInfo}: ID: {userInfo.Id} | Created: {userInfo.Created} | Joined: {userInfo.Joined} | Join position: {userInfo.JoinPosition} | word on the street: {userInfo.Footnote}";
            }

            return new CommandResponse { Text = text, Embed = embedBuilder?.Build() };
        }
    }
}
