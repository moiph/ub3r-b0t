﻿namespace UB3RB0T.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Discord;
    using Discord.WebSocket;
    using Serilog;

    [BotPermissions(ChannelPermission.ManageMessages, "RequireManageMessages")]
    [UserPermissions(ChannelPermission.ManageMessages, "RequireUserManageMessages")]
    public class ClearCommand : IDiscordCommand
    {
        public async Task<CommandResponse> Process(IDiscordBotContext context)
        {
            if (context.Channel is IDMChannel)
            {
                return null;
            }

            var guildUser = context.Author as SocketGuildUser;

            var guildChannel = context.Channel as SocketGuildChannel;

            string[] parts = context.Message.Content.Split(new[] { ' ' }, 3);
            if (parts.Length != 2 && parts.Length != 3)
            {
                return new CommandResponse { Text = "Usage: .clear #; Usage for user specific messages: .clear # username" };
            }

            IUser deletionUser = null;

            if (parts.Length == 3)
            {
                if (ulong.TryParse(parts[2], out ulong userId))
                {
                    deletionUser = guildChannel.GetUser(userId);
                }
                else
                {
                    deletionUser = guildUser.Guild.Users.Find(parts[2]).FirstOrDefault();
                }

                if (deletionUser == null)
                {
                    return new CommandResponse { Text = "Couldn't find the specified user. Try their ID if nick matching is struggling" };
                }
            }

            var botGuildUser = guildChannel.GetUser(context.Client.CurrentUser.Id);

            if (int.TryParse(parts[1], out int count))
            {
                var textChannel = context.Message.Channel as ITextChannel;
                // +1 for the current .clear message
                if (deletionUser == null)
                {
                    count = Math.Min(99, count) + 1;
                }
                else
                {
                    count = Math.Min(100, count);
                }

                // download messages until we've hit the limit
                var msgsToDelete = new List<IMessage>();
                var msgsToDeleteCount = 0;
                ulong? lastMsgId = null;
                var i = 0;
                while (msgsToDeleteCount < count)
                {
                    i++;
                    IEnumerable<IMessage> downloadedMsgs;
                    try
                    {
                        if (!lastMsgId.HasValue)
                        {
                            downloadedMsgs = await textChannel.GetMessagesAsync(count).FlattenAsync();
                        }
                        else
                        {
                            downloadedMsgs = await textChannel.GetMessagesAsync(lastMsgId.Value, Direction.Before, count).FlattenAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        downloadedMsgs = Array.Empty<IMessage>();
                        Log.Error(ex, "Failure to download messages in clear command");
                    }

                    if (downloadedMsgs.Count() > 0)
                    {
                        lastMsgId = downloadedMsgs.Last().Id;

                        var msgs = downloadedMsgs.Where(m => (deletionUser == null || m.Author?.Id == deletionUser.Id) && !m.IsPinned).Take(count - msgsToDeleteCount);
                        msgsToDeleteCount += msgs.Count();
                        msgsToDelete.AddRange(msgs);
                    }
                    else
                    {
                        break;
                    }

                    if (i >= 5)
                    {
                        break;
                    }
                }

                var settings = SettingsConfig.GetSettings(guildUser.Guild.Id.ToString());
                if (settings.HasFlag(ModOptions.Mod_LogDelete) && context.Client.GetChannel(settings.Mod_LogId) is ITextChannel modLogChannel && modLogChannel.GetCurrentUserPermissions().SendMessages)
                {
                    modLogChannel.SendMessageAsync($"{guildUser.Username}#{guildUser.Discriminator} cleared {msgsToDeleteCount} messages from {textChannel.Mention}").Forget();
                }

                try
                {
                    await (context.Message.Channel as ITextChannel).DeleteMessagesAsync(msgsToDelete);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return new CommandResponse { Text = "Bots cannot delete messages older than 2 weeks." };
                }

                return null;
            }

            return new CommandResponse { Text = "Usage: .clear #" };
        }
    }
}
