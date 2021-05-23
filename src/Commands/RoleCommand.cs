namespace UB3RB0T.Commands
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Net;
    using System.Text.RegularExpressions;
    using Discord;
    using Discord.Net;
    using Discord.WebSocket;
    using Serilog;

    [BotPermissions(GuildPermission.ManageRoles, "RequireManageRoles")]
    public class DeRoleCommand : RoleCommand
    {
        public DeRoleCommand() : base(false) { }
    }

    [BotPermissions(GuildPermission.ManageRoles, "RequireManageRoles")]
    public class RoleCommand : IDiscordCommand
    {
        private static readonly Regex RoleIdRegex = new Regex("roleid:(?<roleid>[0-9]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RoleMentionRegex = new Regex("<@&(?<roleid>[0-9]+)>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly bool isAdd;

        public RoleCommand()
        {
            this.isAdd = true;
        }

        public RoleCommand(bool isAdd)
        {
            this.isAdd = isAdd;
        }

        public async Task<CommandResponse> Process(IDiscordBotContext context)
        {
            if (context.GuildChannel != null)
            {
                var settings = SettingsConfig.GetSettings(context.GuildChannel.Guild.Id.ToString());
                var roleArgs = context.Message.Content.Split(new[] { ' ' }, 2);

                if (roleArgs.Length == 1)
                {
                    return new CommandResponse { Text = $"Usage: {settings.Prefix}role rolename | {settings.Prefix}derole rolename" };
                }

                if (roleArgs[1].StartsWith("generate "))
                {
                    var roleGenArgs = context.Message.Content.Split(new[] { ' ' }, 5);
                    if (roleGenArgs.Length >= 3)
                    {
                        return await this.RoleGenerate(context, roleGenArgs);
                    }
                }

                IRole requestedRole = context.SocketMessage?.MentionedRoles.FirstOrDefault();
                if (requestedRole == null)
                {
                    var guildRoles = context.GuildChannel.Guild.Roles.OrderByDescending(r => r.Position);
                    requestedRole = guildRoles.FirstOrDefault(r => r.Name.IEquals(roleArgs[1])) ?? 
                        guildRoles.FirstOrDefault(r => r.Name.IContains(roleArgs[1]));

                    if (requestedRole == null)
                    {
                        return new CommandResponse { Text = "I couldn't find that role. either the role is sponsored by waldo or carmen sandiego, or you need to improve your spelling" };
                    }
                }

                if (!context.Settings.SelfRoles.ContainsKey(requestedRole.Id))
                {
                    return new CommandResponse { Text = $"woah there buttmunch tryin' to cheat the system? you don't have the AUTHORITY to self-assign the {requestedRole.Name.ToUpperInvariant()} role. now make like a tree and get outta here" };
                }

                var guildAuthor = context.Message.Author as IGuildUser;
                if (isAdd && guildAuthor.RoleIds.Contains(requestedRole.Id))
                {
                    return new CommandResponse { Text = $"seriously? you already have the {requestedRole.Name} role. settle DOWN, freakin' role enthustiast" };
                }

                if (!isAdd && !guildAuthor.RoleIds.Contains(requestedRole.Id))
                {
                    return new CommandResponse { Text = $"seriously? you don't even have the {requestedRole.Name} role. settle DOWN, freakin' role unenthustiast" };
                }

                try
                {
                    if (isAdd)
                    {
                        await guildAuthor.AddRoleAsync(requestedRole);
                        return new CommandResponse { Text = $"access granted to role `{requestedRole.Name}`. congratulation !" };
                    }
                    else
                    {
                        await guildAuthor.RemoveRoleAsync(requestedRole);
                        return new CommandResponse { Text = $"access removed from role `{requestedRole.Name}`. congratulation ... ?" };
                    }
                }
                catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
                {
                    return new CommandResponse { Text = "...it seems I cannot actually modify that role. yell at management (verify the role orders, bot's role needs to be above the ones being managed)" };
                }
            }

            return new CommandResponse { Text = "role command does not work in private channels" };
        }

        // TODO:
        // Share some of this logic with the command itself
        public static async Task<bool> AddRoleViaReaction(IUserMessage message, SocketReaction reaction, IUser user)
        {
            // +/- reaction indicates an add/remove role request.
            if (reaction.Channel is SocketGuildChannel guildChannel)
            {
                // check for a mentioned role or roleid:### 
                string roleIdText = string.Empty;
                var roleMention = RoleMentionRegex.Match(message.Content);
                if (roleMention.Success)
                {
                    roleIdText = roleMention.Groups["roleid"].ToString();
                }
                else
                {
                    var roleIdMatch = RoleIdRegex.Match(message.Content);
                    if (roleIdMatch.Success)
                    {
                        roleIdText = roleIdMatch.Groups["roleid"].ToString();
                    }
                }
                
                if (ulong.TryParse(roleIdText, out var roleId))
                {
                    var settings = SettingsConfig.GetSettings(guildChannel.Guild.Id);
                    if (settings.SelfRoles.ContainsKey(roleId))
                    {
                        var requestedRole = guildChannel.Guild.Roles.FirstOrDefault(r => r.Id == roleId);
                        if (requestedRole != null)
                        {
                            var guildAuthor = user as IGuildUser;
                            var customEmote = reaction.Emote as Emote;
                            var isAdd = reaction.Emote.Name == "➕" || customEmote?.Id == settings.RoleAddEmoteId;
                            var isRemove = reaction.Emote.Name == "➖" || customEmote?.Id == settings.RoleRemoveEmoteId;

                            try
                            {
                                if (isAdd)
                                {
                                    await guildAuthor.AddRoleAsync(requestedRole);
                                }
                                else if (isRemove)
                                {
                                    await guildAuthor.RemoveRoleAsync(requestedRole);
                                }
                                else
                                {
                                    Log.Warning("Unexpected role via reaction case, was not add nor remove");
                                }

                                return true;
                            }
                            catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
                            {
                                Log.Warning("Permissions error adding reaction role");
                                throw;
                            }
                        }
                        else
                        {
                            Log.Warning($"Role {roleId} not found for reaction add/remove");
                        }
                    }
                }
            }

            return false;
        }

        public static async Task<bool> AddRoleViaReaction(ulong roleId, IGuildUser user)
        {
            var settings = SettingsConfig.GetSettings(user.Guild.Id);
            if (settings.SelfRoles.ContainsKey(roleId))
            {
                var requestedRole = user.Guild.Roles.FirstOrDefault(r => r.Id == roleId);
                if (requestedRole != null)
                {
                    try
                    {
                        if (!user.RoleIds.Contains(roleId))
                        {
                            await user.AddRoleAsync(requestedRole);
                        }
                        else
                        {
                            await user.RemoveRoleAsync(requestedRole);
                        }

                        return true;
                    }
                    catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
                    {
                        Log.Warning("Permissions error adding reaction role");
                        throw;
                    }
                }
                else
                {
                    Log.Warning($"Role {roleId} not found for reaction add/remove");
                }
            }

            return false;
        }

        private async Task<CommandResponse> RoleGenerate(IDiscordBotContext context, string[] roleGenArgs)
        {
            var guildUser = context.Message.Author as SocketGuildUser;
            var settings = context.Settings;

            if (!guildUser.GuildPermissions.Has(GuildPermission.ManageRoles))
            {
                return new CommandResponse { Text = "You need manage roles permission do that." };
            }

            var helpText = $"Usage: {settings.Prefix}role generate @role channelId Text to pair with the message here";

            IRole genRole = context.SocketMessage?.MentionedRoles.FirstOrDefault();
            if (genRole == null && ulong.TryParse(roleGenArgs[2], out var roleId))
            {
                genRole = context.GuildChannel.Guild.GetRole(roleId);
            }

            if (genRole == null)
            {
                return new CommandResponse { Text = helpText };
            }

            if (!context.Settings.SelfRoles.ContainsKey(genRole.Id))
            {
                return new CommandResponse { Text = $"That role is not currently self-assignable. Fix the settings first." };
            }

            var channel = context.SocketMessage?.MentionedChannels.FirstOrDefault() as SocketTextChannel;
            if (channel == null && ulong.TryParse(roleGenArgs[3], out var chanId))
            {
                channel = context.GuildChannel.Guild.GetChannel(chanId) as SocketTextChannel;
            }

            if (channel != null)
            {
                try
                {
                    var genMessage = await channel.SendMessageAsync($"{genRole.Mention} - {roleGenArgs[4]}");
                    var addEmote = settings.RoleAddEmoteId != 0 ? 
                        await context.GuildChannel.Guild.GetEmoteAsync(settings.RoleAddEmoteId) :
                        new Emoji("➕") as IEmote;
                    var removeEmote = settings.RoleRemoveEmoteId != 0 ?
                        await context.GuildChannel.Guild.GetEmoteAsync(settings.RoleRemoveEmoteId) :
                        new Emoji("➖") as IEmote;

                    await genMessage.AddReactionAsync(addEmote);
                    await genMessage.AddReactionAsync(removeEmote);

                    return new CommandResponse { Text = $"Role generate message sent to {channel.Mention}" };
                }
                catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
                {
                    return new CommandResponse { Text = "Missing permissions to send messages or add reactions to target channel." };
                }
            }

            return new CommandResponse { Text = $"Couldn't find that channel. {helpText}" };
        }
    }
}
