namespace UB3RB0T
{
    using Discord;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;
    using Newtonsoft.Json;

    public enum ExitCode : int
    {
        Success = 0,
        UnexpectedError = 1,
        ExpectedShutdown = 2,
    }

    class Program
    {
        public enum CtrlType
        {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT = 1,
            CTRL_CLOSE_EVENT = 2,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT = 6,
        }

        [DllImport("Kernel32")]
        private static extern bool SetConsoleCtrlHandler(HandlerRoutine Handler, bool Add);
        private delegate bool HandlerRoutine(CtrlType CtrlType);

        public void Configure(IApplicationBuilder app, ILoggerFactory loggerFactory)
        {
            app.Run(async context =>
            {
                string response = string.Empty;

                try
                {
                    if (BotInstance is DiscordBot discordBot)
                    {
                        if (context.Request.Query["guilds"] == "1" && BotInstance != null)
                        {
                            response = string.Join($",{Environment.NewLine}",
                                discordBot.Client.Guilds.OrderByDescending(g => g.Users.Count).Select(g => $"{g.Id} | {g.Name} | {g.Users.Count}"));
                        }
                        else if (!string.IsNullOrEmpty(context.Request.Query["guildId"]) && BotInstance != null)
                        {
                            if (ulong.TryParse(context.Request.Query["guildId"], out ulong guildId))
                            {
                                var guild = discordBot.Client.GetGuild(guildId);
                                if (guild != null)
                                {
                                    var channelsResponse = new GuildPermisssionsData();

                                    var botGuildUser = guild.CurrentUser;
                                    var channels = guild.Channels.Where(c => c is ITextChannel || c is IVoiceChannel);

                                    foreach (var chan in channels)
                                    {
                                        var channelPermissions = botGuildUser.GetPermissions(chan);
                                        channelsResponse.Channels.Add(chan.Id, new GuildChannelPermissions { CanRead = channelPermissions.ViewChannel, CanSend = channelPermissions.SendMessages });
                                    }

                                    response = JsonConvert.SerializeObject(channelsResponse);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }

                // if empty, response was not handled
                if (string.IsNullOrEmpty(response))
                {
                    response = $"Online{Environment.NewLine}";
                }

                context.Response.ContentLength = Encoding.UTF8.GetByteCount(response);
                context.Response.ContentType = "text/plain";

                try
                {
                    await context.Response.WriteAsync(response);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            });
        }

        static Bot BotInstance;

        static void Main(string[] args)
        {
            var botType = BotType.Discord;
            var shard = 0;
            var totalShards = 1;

            if (args != null)
            {
                foreach (string arg in args)
                {
                    var argParts = arg.Split(new[] { ':' }, 2);

                    switch (argParts[0])
                    {
                        case "/?":
                            Console.WriteLine("dotnet UB3RB0T.dll [/t:type] [/s:shard]");
                            Console.WriteLine("/t:type \t The type of bot to create. [Irc, Discord]");
                            Console.WriteLine("/s:shard \t The shard for this instance (Discord only)");
                            Console.WriteLine("/c:path \t The path to botconfig.json");
                            return;

                        case "/t":
                            if (!Enum.TryParse(argParts[1], /* ignoreCase */true, out botType))
                            {
                                throw new ArgumentException("Invalid bot type specified.");
                            }

                            break;

                        case "/c":
                            Environment.SetEnvironmentVariable(JsonConfig.PathEnvironmentVariableName, argParts[1]);
                            break;

                        case "/s":
                            shard = int.Parse(argParts[1]);
                            break;

                        case "/st":
                            totalShards = int.Parse(argParts[1]);
                            break;
                    }
                }
            }

            int exitCode = (int)ExitCode.Success;
            // Convert to async main method
            var instanceCount = 0;
            do
            {
                try
                {
                    using (var bot = Bot.Create(botType, shard, totalShards, instanceCount))
                    {
                        BotInstance = bot;
                        // Handle clean shutdown when possible
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        {
                            SetConsoleCtrlHandler((ctrlType) =>
                            {
                                bot.StopAsync().GetAwaiter().GetResult();
                                return true;
                            }, true);
                        }

                        exitCode = bot.StartAsync().GetAwaiter().GetResult();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }

                instanceCount++;
            } while (exitCode == (int)ExitCode.UnexpectedError); // re-create the bot on failures.  Only exit if a clean shutdown occurs.

            Console.WriteLine("Game over man, game over!");
        }
    }
}