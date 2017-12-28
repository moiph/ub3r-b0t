namespace UB3RB0T.Commands
{
    using System.Linq;
    using System.Threading.Tasks;
    using Discord;
    using Discord.WebSocket;

    public class ServerInfoCommand : IDiscordCommand
    {
        public async Task<CommandResponse> Process(IDiscordBotContext context)
        {
            EmbedBuilder embedBuilder = null;
            string text = string.Empty;

            if (context.Message.Channel is IGuildChannel guildChannel && context.Message.Channel is ITextChannel textChannel)
            {
                var guild = guildChannel.Guild;
                var emojiCount = guild.Emotes.Count();
                var emojiText = "no custom emojis? I am ASHAMED to be here";
                if (emojiCount > 100)
                {
                    emojiText = $"...{emojiCount} emojis? IMPOSSIBLE";
                }
                else if (emojiCount == 100)
                {
                    emojiText = $"wow {emojiCount} custom emojis! that's the max";
                }
                else if (emojiCount > 90)
                {
                    emojiText = $"...{emojiCount} emojis? hackers";
                }
                else if (emojiCount > 75)
                {
                    emojiText = $"what?! {emojiCount} emojis? that's...that's incredible.";
                }
                else if (emojiCount > 50)
                {
                    emojiText = $"jeeez {emojiCount} custom emojis! excellent work.";
                }
                else if (emojiCount == 50)
                {
                    emojiText = $"wow {emojiCount} custom emojis! that's the old max pre-animated emojis";
                }
                else if (emojiCount >= 40)
                {
                    emojiText = $"{emojiCount} custom emojis in here. impressive...most impressive...";
                }
                else if (emojiCount > 25)
                {
                    emojiText = $"the custom emoji force is strong with this guild. {emojiCount} is over halfway to the max.";
                }
                else if (emojiCount > 10)
                {
                    emojiText = $"{emojiCount} custom emoji is...passable";
                }
                else if (emojiCount > 0)
                {
                    emojiText = $"really, only {emojiCount} custom emoji? tsk tsk.";
                }

                await (context.Message.Channel as SocketGuildChannel).Guild.DownloadUsersAsync();
                var serverInfo = new
                {
                    Title = $"Server Info for {guild.Name}",
                    UserCount = (await guild.GetUsersAsync()).Count(),
                    Owner = (await guild.GetOwnerAsync()).Username,
                    Created = $"{guild.CreatedAt.ToString("dd MMM yyyy")} {guild.CreatedAt.ToString("hh:mm:ss tt")} UTC",
                    EmojiText = emojiText,
                };

                var settings = SettingsConfig.GetSettings(guildChannel.GuildId.ToString());

                if (textChannel.GetCurrentUserPermissions().EmbedLinks && settings.PreferEmbeds)
                {
                    embedBuilder = new EmbedBuilder
                    {
                        Title = serverInfo.Title,
                        ThumbnailUrl = guildChannel.Guild.IconUrl,
                    };

                    embedBuilder.AddField((field) =>
                    {
                        field.IsInline = true;
                        field.Name = "Users";
                        field.Value = serverInfo.UserCount;
                    });

                    embedBuilder.AddField((field) =>
                    {
                        field.IsInline = true;
                        field.Name = "Owner";
                        field.Value = serverInfo.Owner;
                    });

                    embedBuilder.AddField((field) =>
                    {
                        field.IsInline = true;
                        field.Name = "Id";
                        field.Value = guild.Id;
                    });

                    embedBuilder.AddField((field) =>
                    {
                        field.IsInline = true;
                        field.Name = "created";
                        field.Value = serverInfo.Created;
                    });

                    embedBuilder.Footer = new EmbedFooterBuilder
                    {
                        Text = serverInfo.EmojiText,
                    };

                    if (!string.IsNullOrEmpty(guild.SplashUrl))
                    {
                        embedBuilder.Footer.IconUrl = guild.SplashUrl;
                    }
                }
                else
                {
                    text = $"{serverInfo.Title}: Users: {serverInfo.UserCount} | Owner: {serverInfo.Owner} | Id: {guild.Id} | Created: {serverInfo.Created} | {serverInfo.EmojiText}";
                }
            }
            else
            {
                text = "This command only works in servers, you scoundrel";
            }

            return new CommandResponse { Text = text, Embed = embedBuilder.Build() };
        }
    }
}
