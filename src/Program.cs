namespace UB3RB0T
{
    using Discord;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.FileProviders;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Primitives;
    using System;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using System.Threading.Tasks;
    using Newtonsoft.Json;
    using Serilog;
    using Serilog.Core;
    using Serilog.Events;

    public enum ExitCode : int
    {
        Success = 0,
        UnexpectedError = 1,
        ExpectedShutdown = 2,
        ConnectionRestart = 3,
    }

    class Program
    {
        public void Configure(IApplicationBuilder app, ILoggerFactory loggerFactory)
        {
            app.Run(async context =>
            {
                string response = string.Empty;

                try
                {
                    if (BotInstance is DiscordBot discordBot && BotConfig.Instance.WebListenerInboundAddresses.Contains(context.Connection.RemoteIpAddress.ToString()))
                    {
                        if (context.Request.Query["guilds"] == "1")
                        {
                            response = string.Join($",{Environment.NewLine}",
                                discordBot.Client.Guilds.OrderByDescending(g => g.MemberCount).Select(g => $"{g.Id} | {g.Name} | {g.MemberCount}"));
                        }
                        else if (ulong.TryParse(context.Request.Query["guildId"], out ulong guildId))
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
                                    channelsResponse.Channels.Add(chan.Id, new GuildChannelPermissions
                                    { 
                                        CanRead = channelPermissions.ViewChannel,
                                        CanSend = channelPermissions.SendMessages,
                                        CanEmbed = channelPermissions.EmbedLinks,
                                        CanSpeak = channelPermissions.Speak,
                                    });
                                }

                                foreach (var emoji in guild.Emotes)
                                {
                                    channelsResponse.Emoji.Add(emoji.Id, new EmojiData { Name = emoji.Name, Url = emoji.Url });
                                }

                                channelsResponse.HighestRolePosition = botGuildUser.Roles.OrderByDescending(r => r.Position).FirstOrDefault()?.Position ?? 0;

                                response = JsonConvert.SerializeObject(channelsResponse);
                            }
                            else
                            {
                                response = JsonConvert.SerializeObject(new { error = "Not found" });
                            }
                        }
                        else if (context.Request.Query["shutdown"] == "1")
                        {
                            Log.Information("shutdown request via kestrel; stopping bot");
                            HandleShutdown();
                        }
                        else
                        {
                            response = Assembly.GetEntryAssembly().GetName().Version.ToString();
                        }

                        // if empty, response was not handled
                        if (string.IsNullOrEmpty(response))
                        {
                            response = $"Online{Environment.NewLine}";
                        }
                    }
                    else
                    {
                        Log.Information($"Request skipped for {context.Connection.RemoteIpAddress}");
                        context.Response.StatusCode = 404;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Exception in kestrel handler");
                }

                context.Response.ContentLength = Encoding.UTF8.GetByteCount(response);
                context.Response.ContentType = "text/plain";

                if (!string.IsNullOrEmpty(response))
                {
                    try
                    {
                        await context.Response.WriteAsync(response);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error writing response in kestral handler");
                    }
                }
            });
        }

        static Bot BotInstance;
        static LoggingLevelSwitch levelSwitch;
        static PhysicalFileProvider fileProvider;
        static IChangeToken changeToken;

        static int Main(string[] args)
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
                            return 0;

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

            // Logging
            levelSwitch = new LoggingLevelSwitch
            {
                MinimumLevel = BotConfig.Instance.LogEventLevel
            };

            var logConfiguration = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(levelSwitch)
                .Enrich.WithProperty("Shard", shard.ToString().PadLeft(2))
                .WriteTo.Console(restrictedToMinimumLevel: BotConfig.Instance.IsDevMode ? LogEventLevel.Verbose : LogEventLevel.Fatal,
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3} {Shard}] {Message:lj}{NewLine}{Exception}");

            if (!string.IsNullOrWhiteSpace(BotConfig.Instance.LogsPath))
            {
                logConfiguration.WriteTo.File($"{BotConfig.Instance.LogsPath}{Path.DirectorySeparatorChar}{botType}_shard{shard}_.txt",
                    buffered: true,
                    rollingInterval: RollingInterval.Day,
                    flushToDiskInterval: TimeSpan.FromSeconds(5),
                    retainedFileCountLimit: BotConfig.Instance.LogsRetainedFileCount);
            }

            Log.Logger = logConfiguration.CreateLogger();

            // setup a watcher for configs
            fileProvider = new PhysicalFileProvider(JsonConfig.ConfigRootPath);
            RegisterChangeCallback();

            Console.CancelKeyPress += Console_CancelKeyPress;

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
                        Log.Information($"Starting shard {shard}");

                        exitCode = bot.StartAsync().GetAwaiter().GetResult();
                    }
                }
                catch (Exception ex)
                {
                    Log.Fatal(ex, "Failure in bot loop");
                }

                instanceCount++;
            } while (exitCode == (int)ExitCode.UnexpectedError); // re-create the bot on failures.  Only exit if a clean shutdown occurs.

            Log.CloseAndFlush();

            Log.Fatal("Game over man, {{GameOver}}", "game over!");

            return exitCode;
        }

        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            Log.Information("CancelKeyPress event; stopping bot");
            HandleShutdown();
        }

        private static void HandleShutdown()
        {
            Log.Information("Handling shutdown event");
            try
            {
                BotInstance.StopAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unexcepted error calling StopAsync");
            }
        }

        static async Task ReloadConfigs()
        {
            // immediate access will result in an IOException
            // we could loop and wait for access or just be lazy
            // and assume the human editor of the config files
            // won't make a 2nd edit so rapidly (right? right??)
            await Task.Delay(1000);

            try
            {
                PhrasesConfig.Instance.Reset();
                CommandsConfig.Instance.Reset();
                BotConfig.Instance.Reset();

                levelSwitch.MinimumLevel = BotConfig.Instance.LogEventLevel;

                Log.Information("Configs reloaded.");

                RegisterChangeCallback();
            }
            catch (IOException ex) // of course, never assume...
            {
                Log.Error(ex, "Config reload failed");
            }
        }

        static void RegisterChangeCallback()
        {
            changeToken = fileProvider.Watch("*.json");
            changeToken.RegisterChangeCallback(OnConfigChange, null);
        }

        static void OnConfigChange(object state)
        {
            _ = ReloadConfigs();
        }
    }
}