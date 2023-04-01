namespace UB3RB0T
{
    using System.Collections.Generic;
    using System.Linq;
    using Discord;
    using Discord.WebSocket;
    using Guilded.Base.Content;
    using Newtonsoft.Json;
    using UB3RIRC;

    /// <summary>
    /// Common message data for generalized use.  Massages data between various client libraries (IRC, Discord, etc)
    /// </summary>
    public class BotMessageData
    {
        public BotType BotType;

        [JsonIgnore]
        public IUserMessage DiscordMessageData { get; private set; }
        [JsonIgnore]
        public SocketInteraction DiscordInteraction { get; private set; }
        [JsonIgnore]
        public MessageData IrcMessageData { get; private set; }
        [JsonIgnore]
        public Message GuildedMessageData { get; private set; }

        public string UserName { get; set; }
        public string UserId { get; set; }
        public string UserHost { get; set; }
        public string TargetUserName { get; set; }
        public string TargetUserId { get; set; }
        public string Channel { get; set; }
        public string Server { get; set; }
        public string MessageId { get; set; }
        public string Content { get; set; }
        public string Command => Query.Split(new[] { ' ' }, 2)?[0];
        public string Prefix { get; set; }
        public Dictionary<string, string> RequestOptions { get; set; }
        public string Query
        {
            get
            {
                string query = string.Empty;
                int argPos = 0;
                if (this.Content.HasMentionPrefix(BotConfig.Instance.Discord.BotId, ref argPos))
                {
                    query = this.Content.Substring(argPos);
                }
                else if (this.Content.StartsWith(this.Prefix))
                {
                    query = this.Content.Substring(this.Prefix.Length);
                }

                return query;
            }
        }
        public string Format { get; set; }
        public bool Sasshat { get; set; }
        public bool AfEnabled { get; set; }
        public bool RateLimitChecked { get; set; }

        public BotMessageData(BotType botType)
        {
            this.BotType = botType;
        }

        public bool MentionsBot(string botName, ulong? id)
        {
            if (this.BotType == BotType.Discord)
            {
                if (!string.IsNullOrEmpty(this.Content) && this.Content.IContains(botName))
                {
                    return true;
                }

                if (this.DiscordMessageData is SocketUserMessage socketUserMessage)
                {
                    return socketUserMessage.MentionedUsers.Count == 1 && socketUserMessage.MentionedUsers.First().Id == id;     
                }

                return false;
            }

            if (this.BotType == BotType.Guilded)
            {
                if (string.IsNullOrEmpty(this.Content) && this.Content.IContains(botName))
                {
                    return true;
                }

                return false;
            }

            return this.IrcMessageData.Text.Contains(botName);
        }

        public static BotMessageData Create(MessageData ircMessageData, IrcClient ircClient, Settings serverSettings)
        {
            return new BotMessageData(BotType.Irc)
            {
                IrcMessageData = ircMessageData,
                UserName = ircMessageData.Nick,
                UserHost = ircMessageData.Host,
                Channel = ircMessageData.Target,
                Server = ircClient.Host,
                Content = ircMessageData.Text,
                Prefix = serverSettings.Prefix,
            };
        }

        public static BotMessageData Create(Message message, Settings serverSettings)
        {
            return new BotMessageData(BotType.Guilded)
            {
                GuildedMessageData = message,
                UserName = message.CreatedBy.ToString(),
                UserHost = message.CreatedBy.ToString(),
                UserId = message.CreatedBy.ToString(),
                Channel = message.ChannelId.ToString(),
                Server = message.ServerId.ToString(),
                MessageId = message.Id.ToString(),
                Content = message.Content,
                Format = serverSettings.PreferEmbeds ? "embed" : string.Empty,
                AfEnabled = serverSettings.AprilFoolsEnabled,
                Sasshat = serverSettings.SasshatEnabled,
                Prefix = serverSettings.Prefix,
            };
        }

        public static BotMessageData Create(IUserMessage message, Settings serverSettings)
        {
            var preferEmbeds = ((message.Channel as SocketTextChannel)?.GetCurrentUserPermissions().EmbedLinks ?? false) && serverSettings.PreferEmbeds;

            var messageData = new BotMessageData(BotType.Discord)
            {
                DiscordMessageData = message,
                UserName = message.Author.Username,
                UserId = message.Author.Id.ToString(),
                UserHost = message.Author.Id.ToString(),
                Channel = message.Channel.Id.ToString(),
                Server = (message.Channel as IGuildChannel)?.GuildId.ToString() ?? "private",
                MessageId = message.Id.ToString(),
                Content = message.Content,
                Format = preferEmbeds ? "embed" : string.Empty,
                AfEnabled = serverSettings.AprilFoolsEnabled,
                Sasshat = serverSettings.SasshatEnabled,
                Prefix = serverSettings.Prefix,
            };

            // if the user does not have @everyone permissions, block its use
            if (!(message.Author as SocketGuildUser)?.GetPermissions(message.Channel as SocketGuildChannel).MentionEveryone ?? false)
            {
                messageData.Content = messageData.Content.Replace("@everyone", "@every\x200Bone").Replace("@here", "@he\x200Bre");
            }

            return messageData;
        }

        public static BotMessageData Create(SocketInteraction interaction, IUserMessage message, Settings serverSettings)
        {
            var preferEmbeds = ((interaction.Channel as SocketTextChannel)?.GetCurrentUserPermissions().EmbedLinks ?? false) && serverSettings.PreferEmbeds;

            var messageData = new BotMessageData(BotType.Discord)
            {
                DiscordInteraction = interaction,
                UserName = interaction.User.Username,
                UserId = interaction.User.Id.ToString(),
                UserHost = interaction.User.Id.ToString(),
                Channel = interaction.Channel.Id.ToString(),
                Server = (interaction.Channel as IGuildChannel)?.GuildId.ToString() ?? "private",
                MessageId = message?.Id.ToString(),
                Format = preferEmbeds ? "embed" : string.Empty,
                AfEnabled = serverSettings.AprilFoolsEnabled,
                Sasshat = serverSettings.SasshatEnabled,
                Content = message?.Content ?? serverSettings.Prefix + ((interaction as SocketCommandBase)?.CommandName ?? (interaction as SocketMessageComponent)?.Data.CustomId),
                Prefix = serverSettings.Prefix,
                TargetUserName = (interaction as SocketUserCommand)?.Data.Member.UserOrNickname(),
                TargetUserId = (interaction as SocketUserCommand)?.Data.Member.Id.ToString(),
            };

            if (interaction is SocketMessageCommand messageCommand)
            {
                messageData.Content = messageCommand.Data.Message.Content;
            }
            else if (interaction is SocketSlashCommand slashCommand)
            {
                messageData.RequestOptions = new Dictionary<string, string>();
                foreach (var option in slashCommand.Data.Options)
                {
                    if (option.Type == ApplicationCommandOptionType.SubCommand)
                    {
                        messageData.Content += $" {option.Name}";
                        messageData.RequestOptions.Add(option.Name, option.Name);
                    }
                    else
                    {
                        messageData.Content += $" {option.Value}";
                        messageData.RequestOptions.Add(option.Name, option.Value.ToString());
                    }
                }
            }

            // if the user does not have @everyone permissions, block its use
            if (!(interaction.User as SocketGuildUser)?.GetPermissions(interaction.Channel as SocketGuildChannel).MentionEveryone ?? false)
            {
                messageData.Content = messageData.Content.Replace("@everyone", "@every\x200Bone").Replace("@here", "@he\x200Bre");
            }

            return messageData;
        }

        public static BotMessageData Create(SocketUser user, SocketGuild guild, Settings serverSettings)
        {
            var messageData = new BotMessageData(BotType.Discord)
            {
                UserName = user.Username,
                UserId = user.Id.ToString(),
                UserHost = user.Id.ToString(),
                Server = guild.Id.ToString(),
                Prefix = serverSettings.Prefix,
            };

            return messageData;
        }
    }
}
