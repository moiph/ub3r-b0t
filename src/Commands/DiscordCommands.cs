namespace UB3RB0T
{
    using Discord;
    using Discord.Net;
    using Discord.WebSocket;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp.Scripting;
    using Microsoft.CodeAnalysis.Scripting;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Reflection;
    using System.Text;
    using System.Threading.Tasks;

    public class DiscordCommands
    {
        public Dictionary<string, Func<SocketUserMessage, Task<CommandResponse>>> Commands { get; private set; }

        private DiscordSocketClient client;
        private AudioManager audioManager;
        private BotApi botApi;

        private ScriptOptions scriptOptions;

        public class CommandResponse
        {
            public string Text { get; set; }
            public Embed Embed { get; set; }
            public FileResponse Attachment { get; set; }
        }

        public class FileResponse
        {
            public string Name { get; set; }
            public Stream Stream { get; set; }
        }

        // TODO:
        // this is icky
        // it's getting worse...TODO: read the above todo and fix it
        internal DiscordCommands(DiscordSocketClient client, AudioManager audioManager, BotApi botApi)
        {
            this.client = client;
            this.audioManager = audioManager;
            this.botApi = botApi;
            this.Commands = new Dictionary<string, Func<SocketUserMessage, Task<CommandResponse>>>();

            this.CreateScriptOptions();

            Commands.Add("debug", (message) =>
            {
                var serverId = (message.Channel as IGuildChannel)?.GuildId.ToString() ?? "n/a";
                var botVersion = Assembly.GetEntryAssembly().GetName().Version.ToString();
                var response = new CommandResponse { Text = $"```Server ID: {serverId} | Channel ID: {message.Channel.Id} | Your ID: {message.Author.Id} | Shard ID: {client.ShardId} | Version: {botVersion} | Discord.NET Version: {DiscordSocketConfig.Version}```"};
                return Task.FromResult(response);
            });

            Commands.Add("seen", async (message) =>
            {
                if (message.Channel is IGuildChannel guildChannel)
                {
                    var settings = SettingsConfig.GetSettings(guildChannel.GuildId.ToString());
                    if (!settings.SeenEnabled)
                    {
                        return new CommandResponse { Text = "Seen data is not being tracked for this server.  Enable it in the admin settings panel." };
                    }

                    string[] parts = message.Content.Split(new[] { ' ' }, 2);
                    if (parts.Length != 2)
                    {
                        return new CommandResponse { Text = "Usage: .seen username" };
                    }

                    var targetUser = (await guildChannel.Guild.GetUsersAsync()).Find(parts[1]).FirstOrDefault();
                    if (targetUser != null)
                    {
                        if (targetUser.Id == client.CurrentUser.Id)
                        {
                            return new CommandResponse { Text = $"I was last seen...wait...seriously? Ain't no one got time for your shit, {message.Author.Username}." };
                        }

                        if (targetUser.Id == message.Author.Id)
                        {
                            return new CommandResponse { Text = $"You were last seen now, saying: ... god DAMN it {message.Author.Username}, quit wasting my time" };
                        }

                        string query = $"seen {targetUser.Id} {targetUser.Username}";
                        var messageData = BotMessageData.Create(message, query, settings);

                        var response = (await this.botApi.IssueRequestAsync(messageData, query)).Responses.FirstOrDefault();

                        if (response != null)
                        {
                            return new CommandResponse { Text = response };
                        }
                    }

                    return new CommandResponse { Text = $"I...omg...I have not seen {parts[1]} in this channel :X I AM SOOOOOO SORRY" };
                }

                return null;
            });

            Commands.Add("remove", async (message) =>
            {
                if (message.Channel is IGuildChannel guildChannel)
                {
                    if (message.Author.Id != guildChannel.Guild.OwnerId)
                    {
                        return new CommandResponse { Text = "Restricted to server owner." };
                    }

                    var settings = SettingsConfig.GetSettings(guildChannel.GuildId.ToString());

                    string[] parts = message.Content.Split(new[] { ' ' }, 3);

                    if (parts.Length != 3)
                    {
                        return new CommandResponse { Text = "Usage: .remove type #; valid types are timer and wc" };
                    }

                    var type = parts[1].ToLowerInvariant();
                    var id = parts[2];

                    string query = $"remove {CommandsConfig.Instance.HelperKey} {type} {id}";
                    var messageData = BotMessageData.Create(message, query, settings);
                    var response = (await this.botApi.IssueRequestAsync(messageData, query)).Responses.FirstOrDefault();

                    if (response != null)
                    {
                        return new CommandResponse { Text = response };
                    }
                }

                return null;
            });

            Commands.Add("status", async (message) =>
            {
                var serversStatus = await Utilities.GetApiResponseAsync<HeartbeatData[]>(BotConfig.Instance.HeartbeatEndpoint);

                var dataSb = new StringBuilder();
                dataSb.Append("```cs\n" +
                   "type       shard   server count      users   voice count\n");

                int serverTotal = 0;
                int userTotal = 0;
                int voiceTotal = 0;
                foreach (HeartbeatData heartbeat in serversStatus)
                {
                    serverTotal += heartbeat.ServerCount;
                    userTotal += heartbeat.UserCount;
                    voiceTotal += heartbeat.VoiceChannelCount;

                    var botType = heartbeat.BotType.PadRight(11);
                    var shard = heartbeat.Shard.ToString().PadLeft(4);
                    var servers = heartbeat.ServerCount.ToString().PadLeft(13);
                    var users = heartbeat.UserCount.ToString().PadLeft(10);
                    var voice = heartbeat.VoiceChannelCount.ToString().PadLeft(13);

                    dataSb.Append($"{botType} {shard}  {servers} {users} {voice}\n");
                }

                // add up totals
                dataSb.Append($"-------\n");
                dataSb.Append($"Total:            {serverTotal.ToString().PadLeft(13)} {userTotal.ToString().PadLeft(10)} {voiceTotal.ToString().PadLeft(13)}\n");

                dataSb.Append("```");

                return new CommandResponse { Text = dataSb.ToString() };
            });

            Commands.Add("voice", (message) =>
            {
                var channel = (message.Author as IGuildUser)?.VoiceChannel;
                if (channel == null)
                {
                    return Task.FromResult(new CommandResponse { Text = "Join a voice channel first" });
                }

                Task.Run(async () =>
                {
                    try
                    {
                        await audioManager.JoinAudioAsync(channel);
                    }
                    catch (Exception ex)
                    {
                        // TODO: proper logging
                        Console.WriteLine(ex);
                    }
                }).Forget();

                return Task.FromResult((CommandResponse)null);
            });

            Commands.Add("dvoice", (message) =>
            {
                if (message.Channel is IGuildChannel channel)
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            await audioManager.LeaveAudioAsync(channel);
                        }
                        catch (Exception ex)
                        {
                            // TODO: proper logging
                            Console.WriteLine(ex);
                        }
                    }).Forget();
                }

                return Task.FromResult((CommandResponse)null);
            });

            Commands.Add("devoice", (message) =>
            {
                if (message.Channel is IGuildChannel channel)
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            await audioManager.LeaveAudioAsync(channel);
                        }
                        catch (Exception ex)
                        {
                            // TODO: proper logging
                            Console.WriteLine(ex);
                        }
                    }).Forget();
                }

                return Task.FromResult((CommandResponse)null);
            });

            Commands.Add("clear", async (message) =>
            {
                if (message.Channel is IDMChannel)
                {
                    return null;
                }

                var guildUser = message.Author as IGuildUser;
                if (!guildUser.GuildPermissions.ManageMessages)
                {
                    return new CommandResponse { Text = "you don't have permissions to clear messages, fartface" };
                }

                var guildChannel = message.Channel as IGuildChannel;

                string[] parts = message.Content.Split(new[] { ' ' }, 3);
                if (parts.Length != 2 && parts.Length != 3)
                {
                    return new CommandResponse { Text = "Usage: .clear #; Usage for user specific messages: .clear # username" };
                }

                IUser deletionUser = null;

                if (parts.Length == 3)
                {
                    if (ulong.TryParse(parts[2], out ulong userId))
                    {
                        deletionUser = await message.Channel.GetUserAsync(userId);
                    }
                    else
                    {
                        deletionUser = (await guildUser.Guild.GetUsersAsync().ConfigureAwait(false)).Find(parts[2]).FirstOrDefault();
                    }

                    if (deletionUser == null)
                    {
                        return new CommandResponse { Text = "Couldn't find the specified user. Try their ID if nick matching is struggling" };
                    }
                }

                var botGuildUser = await guildChannel.GetUserAsync(client.CurrentUser.Id);
                bool botOnly = deletionUser == botGuildUser;

                if (!botOnly && !botGuildUser.GetPermissions(guildChannel).ManageMessages)
                {
                    return new CommandResponse { Text = "yeah I don't have the permissions to delete messages, buttwad." };
                }

                if (int.TryParse(parts[1], out int count))
                {
                    var textChannel = message.Channel as ITextChannel;
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
                                downloadedMsgs = await textChannel.GetMessagesAsync(count).Flatten();
                            }
                            else
                            {
                                downloadedMsgs = await textChannel.GetMessagesAsync(lastMsgId.Value, Direction.Before, count).Flatten();
                            }
                        }
                        catch (Exception ex)
                        {
                            downloadedMsgs = new IMessage[0];
                            Console.WriteLine(ex);
                        }

                        if (downloadedMsgs.Count() > 0)
                        {
                            lastMsgId = downloadedMsgs.Last().Id;
                            
                            var msgs = downloadedMsgs.Where(m => (deletionUser == null || m.Author?.Id == deletionUser.Id)).Take(count - msgsToDeleteCount);
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

                    var settings = SettingsConfig.GetSettings(guildUser.GuildId.ToString());
                    if (settings.HasFlag(ModOptions.Mod_LogDelete) && this.client.GetChannel(settings.Mod_LogId) is ITextChannel modLogChannel && modLogChannel.GetCurrentUserPermissions().SendMessages)
                    {
                        modLogChannel.SendMessageAsync($"{guildUser.Username}#{guildUser.Discriminator} cleared {msgsToDeleteCount} messages from {textChannel.Mention}").Forget();
                    }

                    try
                    {
                        await (message.Channel as ITextChannel).DeleteMessagesAsync(msgsToDelete);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        return new CommandResponse { Text = "Bots cannot delete messages older than 2 weeks." };
                    }

                    return null;
                }
                else
                {
                    return new CommandResponse { Text = "Usage: .clear #" };
                }
            });

            Commands.Add("jpeg", async (message) =>
            {
                var messageParts = message.Content.Split(new[] { ' ' }, 2);
                var fileName = "moar.jpeg";
                var url = string.Empty;
                if (messageParts.Length == 2 && Uri.IsWellFormedUriString(messageParts[1], UriKind.Absolute))
                {
                    url = messageParts[1];
                }
                else
                {
                    Attachment img = message.Attachments.FirstOrDefault();
                    if (img != null || DiscordBot.imageUrls.TryGetValue(message.Channel.Id.ToString(), out img))
                    {
                        url = img.Url;
                        fileName = img.Filename;
                    }
                }

                if (!string.IsNullOrEmpty(url))
                { 
                    using (var httpClient = new HttpClient())
                    {
                        var response = await httpClient.GetAsync(CommandsConfig.Instance.JpegEndpoint.AppendQueryParam("url", url));
                        var stream = await response.Content.ReadAsStreamAsync();

                        return new CommandResponse
                        {
                            Attachment = new FileResponse
                            {
                                Name = fileName,
                                Stream = stream,
                            }
                        };
                    }
                }

                return null;
            });

            Commands.Add("userinfo", async (message) =>
            {
                SocketUser targetUser = message.MentionedUsers?.FirstOrDefault();

                if (targetUser == null)
                {
                    var messageParts = message.Content.Split(new[] { ' ' }, 2);

                    if (messageParts.Length == 2)
                    {
                        if (message.Channel is IGuildChannel guildChannel)
                        {
                            targetUser = (await guildChannel.Guild.GetUsersAsync()).Find(messageParts[1]).FirstOrDefault() as SocketUser;
                        }

                        if (targetUser == null)
                        {
                            return new CommandResponse { Text = "User not found. Try a direct mention." };
                        }
                    }
                    else
                    {
                        targetUser = message.Author;
                    }
                }

                var guildUser = targetUser as IGuildUser;

                if (guildUser == null)
                {
                    return null;
                }

                var userInfo = new
                {
                    Title = $"UserInfo for {targetUser.Username}#{targetUser.Discriminator}",
                    AvatarUrl = targetUser.GetAvatarUrl(),
                    NicknameInfo = !string.IsNullOrEmpty(guildUser.Nickname) ? $" aka {guildUser.Nickname}" : "",
                    Footnote = CommandsConfig.Instance.UserInfoSnippets.Random(),
                    Created = $"{targetUser.GetCreatedDate().ToString("dd MMM yyyy")} {targetUser.GetCreatedDate().ToString("hh:mm:ss tt")} UTC",
                    Joined = $"{guildUser.JoinedAt.Value.ToString("dd MMM yyyy")} {guildUser.JoinedAt.Value.ToString("hh:mm:ss tt")} UTC",
                    Id = targetUser.Id.ToString(),
                };

                EmbedBuilder embedBuilder = null;
                string text = string.Empty;

                var settings = SettingsConfig.GetSettings(guildUser.GuildId.ToString());

                if ((message.Channel as ITextChannel).GetCurrentUserPermissions().EmbedLinks && settings.PreferEmbeds)
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

                    if (guildUser.JoinedAt.HasValue)
                    {
                        embedBuilder.AddField((field) =>
                        {
                            field.IsInline = true;
                            field.Name = "joined";
                            field.Value = userInfo.Joined;
                        });
                    }

                    var roles = new List<string>();
                    foreach (ulong roleId in guildUser.RoleIds)
                    {
                        roles.Add(guildUser.Guild.Roles.First(g => g.Id == roleId).Name.TrimStart('@'));
                    }

                    embedBuilder.AddField((field) =>
                    {
                        field.IsInline = false;
                        field.Name = "roles";
                        field.Value = string.Join(", ", roles);
                    });

                    embedBuilder.AddField((field) =>
                    {
                        field.IsInline = true;
                        field.Name = "Id";
                        field.Value = userInfo.Id;
                    });
                }
                else
                {
                    text = $"{userInfo.Title}{userInfo.NicknameInfo}: ID: {userInfo.Id} | Created: {userInfo.Created} | Joined: {userInfo.Joined} | word on the street: {userInfo.Footnote}";
                }

                return new CommandResponse { Text = text, Embed = embedBuilder };
            });

            Commands.Add("serverinfo", async (message) =>
            {
                EmbedBuilder embedBuilder = null;
                string text = string.Empty;

                if (message.Channel is IGuildChannel guildChannel && message.Channel is ITextChannel textChannel)
                {
                    var guild = guildChannel.Guild;
                    var emojiCount = guild.Emotes.Count();
                    var emojiText = "no custom emojis? I am ASHAMED to be here";
                    if (emojiCount > 50)
                    {
                        emojiText = $"...{emojiCount} emojis? hackers";
                    }
                    else if (emojiCount == 50)
                    {
                        emojiText = "wow 50 custom emojis! that's the max";
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

                    await (message.Channel as SocketGuildChannel).Guild.DownloadUsersAsync();
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


                return new CommandResponse { Text = text, Embed = embedBuilder };
            });

            Commands.Add("roles", (message) =>
            {
                if (message.Channel is IGuildChannel guildChannel)
                {
                    var settings = SettingsConfig.GetSettings(guildChannel.GuildId.ToString());
                    var roles = guildChannel.Guild.Roles.Where(r => settings.SelfRoles.Contains(r.Id)).Select(r => $"``{r.Name.Replace("`", @"\`")}``");

                    return Task.FromResult(new CommandResponse { Text = "The following roles are available to self-assign: " + string.Join(", ", roles) });
                }

                return Task.FromResult(new CommandResponse { Text = "role command does not work in private channels" });
            });

            Commands.Add("role", async (message) =>
            {
                return await this.SelfRole(message, true);
            });

            Commands.Add("derole", async (message) =>
            {
                return await this.SelfRole(message, false);
            });

            Commands.Add("admin", async (message) =>
            {
                if (SettingsConfig.Instance.CreateEndpoint != null && message.Channel is IGuildChannel guildChannel)
                {
                    if ((message.Author as IGuildUser).GuildPermissions.ManageGuild)
                    {
                        var req = WebRequest.Create($"{SettingsConfig.Instance.CreateEndpoint}?id={guildChannel.GuildId}&name={guildChannel.Guild.Name}");
                        try
                        {
                            await req.GetResponseAsync();
                            return new CommandResponse { Text = $"Manage from {SettingsConfig.Instance.ManagementEndpoint}" };
                        }
                        catch (Exception ex)
                        {
                            // TODO: proper logging
                            Console.WriteLine(ex);
                        }
                    }
                    else
                    {
                        return new CommandResponse { Text = "You must have manage server permissions to use that command. nice try, dungheap" };
                    }
                }

                return null;
            });

            Commands.Add("eval", async (message) =>
            {
                if (BotConfig.Instance.Discord.OwnerId == message.Author.Id)
                {
                    var script = message.Content.Split(new[] { ' ' }, 2)[1];
                    string result = "no result";
                    try
                    {
                        var evalResult = await CSharpScript.EvaluateAsync<object>(script, scriptOptions, globals: new ScriptHost { Message = message, Client = client });
                        result = evalResult.ToString();
                    }
                    catch (Exception ex)
                    {
                        result = ex.ToString().Substring(0, Math.Min(ex.ToString().Length, 800));
                    }

                    return new CommandResponse { Text = $"``{result}``" };
                }

                return null;
            });
        }

        private async Task<CommandResponse> SelfRole(SocketMessage message, bool isAdd)
        {
            if (message.Channel is IGuildChannel guildChannel)
            {
                var settings = SettingsConfig.GetSettings(guildChannel.GuildId.ToString());
                var roleArgs = message.Content.Split(new[] { ' ' }, 2);

                if (roleArgs.Length == 1)
                {
                    return new CommandResponse { Text = $"Usage: {settings.Prefix}role rolename | {settings.Prefix}derole rolename" };
                }

                IRole requestedRole = message.MentionedRoles.FirstOrDefault();
                if (requestedRole == null)
                {
                    requestedRole = guildChannel.Guild.Roles.Where(r => r.Name.ToLowerInvariant() == roleArgs[1].ToLowerInvariant()).FirstOrDefault() ??
                        guildChannel.Guild.Roles.Where(r => r.Name.ToLowerInvariant().Contains(roleArgs[1].ToLowerInvariant())).FirstOrDefault();

                    if (requestedRole == null)
                    {
                        return new CommandResponse { Text = "wtf? role not found, spel teh name beter or something." };
                    }
                }

                if (!(await guildChannel.Guild.GetCurrentUserAsync()).GuildPermissions.ManageRoles)
                {
                    return new CommandResponse { Text = "gee thanks asswad I can't manage roles in this server. not much I can do for ya here buddy. unless you wanna, y'know, up my permissions" };
                }

                if (!settings.SelfRoles.Contains(requestedRole.Id))
                {
                    return new CommandResponse { Text = $"woah there buttmunch tryin' to cheat the system? you don't have the AUTHORITY to self-assign the {requestedRole.Name.ToUpperInvariant()} role. now make like a tree and get outta here" };
                }

                var guildAuthor = message.Author as IGuildUser;
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

        private void CreateScriptOptions()
        {
            // mscorlib reference issues when using codeanalysis; 
            // see http://stackoverflow.com/questions/38943899/net-core-cs0012-object-is-defined-in-an-assembly-that-is-not-referenced
            var dd = typeof(object).GetTypeInfo().Assembly.Location;
            var coreDir = Directory.GetParent(dd);

            var references = new List<MetadataReference>
            {   
                MetadataReference.CreateFromFile($"{coreDir.FullName}{Path.DirectorySeparatorChar}mscorlib.dll"),
                MetadataReference.CreateFromFile(typeof(object).GetTypeInfo().Assembly.Location),
            };

            var referencedAssemblies = Assembly.GetEntryAssembly().GetReferencedAssemblies();
            foreach (var referencedAssembly in referencedAssemblies)
            {
                var loadedAssembly = Assembly.Load(referencedAssembly);
                references.Add(MetadataReference.CreateFromFile(loadedAssembly.Location));
            }

            this.scriptOptions = ScriptOptions.Default.
                AddImports("System", "System.Linq", "System.Text", "Discord", "Discord.WebSocket").
                AddReferences(references);
        }
    }

    public class ScriptHost
    {
        public SocketMessage Message { get; set; }
        public DiscordSocketClient Client { get; set; }
    }
}
