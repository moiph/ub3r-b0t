namespace UB3RB0T
{
    using Discord;
    using Discord.WebSocket;
    using System;
    using System.Linq;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    public class DiscordCommands
    {
        public Dictionary<string, Func<SocketMessage, Task>> Commands { get; private set; }

        public DiscordCommands()
        {
            this.Commands = new Dictionary<string, Func<SocketMessage, Task>>();

            Commands.Add("debug", async (message) =>
            {
                var serverId = (message.Channel as IGuildChannel)?.GuildId.ToString() ?? "n/a";
                await message.Channel.SendMessageAsync($"```Server ID: {serverId} | Channel ID: {message.Channel.Id} | Your ID: {message.Author.Id} | Shard ID: {0}```");
                return;
            });

            Commands.Add("voice", async (message) =>
            {
                var channel = (message.Author as IGuildUser)?.VoiceChannel;
                if (channel == null) { await message.Channel.SendMessageAsync("User must be in a voice channel, or a voice channel must be passed as an argument."); return; }

                // TODO: audio
                // Get the IAudioClient by calling the JoinAsync method
                // this._audio = await channel.ConnectAsync();

                return;
            });

            Commands.Add("userinfo", async (message) =>
            {
                SocketUser targetUser = message.MentionedUsers?.FirstOrDefault() ?? message.Author;
                var guildUser = targetUser as IGuildUser;

                if (guildUser == null)
                {
                    return;
                }

                var embedBuilder = new EmbedBuilder
                {
                    Title = $"UserInfo for {targetUser.Username}#{targetUser.Discriminator}",
                    ThumbnailUrl = targetUser.AvatarUrl
                };

                if (!string.IsNullOrEmpty(guildUser.Nickname))
                {
                    embedBuilder.Description = $"aka {guildUser.Nickname}";
                }

                embedBuilder.Footer = new EmbedFooterBuilder
                {
                    Text = CommandsConfig.Instance.UserInfoSnippets.Random()
                };

                embedBuilder.AddField((field) =>
                {
                    field.IsInline = true;
                    field.Name = "created";
                    field.Value = $"{targetUser.GetCreatedDate().ToString("dd MMM yyyy")} {targetUser.GetCreatedDate().ToString("hh:mm:ss tt")} UTC";
                });

                embedBuilder.AddField((field) =>
                {
                    field.IsInline = true;
                    field.Name = "joined";
                    field.Value = $"{guildUser.JoinedAt.Value.ToString("dd MMM yyyy")} {guildUser.JoinedAt.Value.ToString("hh:mm:ss tt")} UTC";
                });

                var roles = new List<string>();
                foreach (ulong roleId in guildUser.RoleIds)
                {
                    roles.Add(guildUser.Guild.Roles.First(g => g.Id == roleId).Name.TrimStart('@'));
                }

                embedBuilder.AddField((field) =>
                {
                    field.IsInline = true;
                    field.Name = "roles";
                    field.Value = string.Join(", ", roles);
                });

                embedBuilder.AddField((field) =>
                {
                    field.IsInline = true;
                    field.Name = "Id";
                    field.Value = targetUser.Id.ToString();
                });

                await message.Channel.SendMessageAsync(((char)1).ToString(), false, embedBuilder);
            });

            // TODO: I guess csharp.scripting isn't supported yet :(
            /*
            Commands.Add("eval", async (message) =>
            {
                if (BotConfig.Instance.Discord.OwnerId == message.Author.Id)
                {
                    var script = message.Content.Split(new[] { ' ' }, 2)[1];
                    var scriptOptions = ScriptOptions.Default.
                        AddImports("System", "System.Linq", "System.Text").
                        AddReferences(typeof(Enumerable).GetTypeInfo().Assembly);

                    string result = "no result";
                    try
                    {
                        var evalResult = await CSharpScript.EvaluateAsync<object>(script, scriptOptions, globals: new ScriptHost { message = message });
                        result = evalResult.ToString();
                    }
                    catch (Exception ex)
                    {
                        result = ex.ToString().Substring(0, Math.Min(ex.ToString().Length, 800));
                    }
                    e.Channel.SendMessage(string.Format("``{0}``", result));
                }
            });
            */
        }
    }
}
