namespace UB3RB0T
{
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Linq;
    using System.Runtime.InteropServices;

    class Program
    {
        public enum ExitCode : int
        {
            Success = 0,
            UnexpectedError = 1,
            ExpectedShutdown = 2,
        }

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
                if (context.Request.Query["guilds"] == "1" && BotInstance != null)
                {
                    response = string.Join($",{Environment.NewLine}", BotInstance.client.Guilds.Select(g => g.Id));
                }
                else
                {
                    response = $"Online{Environment.NewLine}";
                }
                context.Response.ContentLength = response.Length;
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync(response);
            });
        }

        static Bot BotInstance;

        static void Main(string[] args)
        {
            var botType = BotType.Discord;
            var shard = 0;

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
                    using (var bot = new Bot(botType, shard, instanceCount))
                    {
                        BotInstance = bot;
                        // Handle clean shutdown when possible
                        SetConsoleCtrlHandler((ctrlType) =>
                        {
                            bot.ShutdownAsync().GetAwaiter().GetResult();
                            return true;
                        }, true);

                        exitCode = bot.RunAsync().GetAwaiter().GetResult();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }

                instanceCount++;
            } while (exitCode == (int)ExitCode.UnexpectedError); // re-create the bot on failures.  Only exit if a clean shutdown occurs.

            Console.WriteLine("Game over man, game over!");
            Console.ReadLine();
        }
    }
}