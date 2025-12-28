namespace UB3RB0T.Commands
{
    using System;
    using System.Threading.Tasks;
    using Discord.WebSocket;
    using Serilog;

    public class VoiceLeaveCommand : IDiscordCommand
    {
        public Task<CommandResponse> Process(IDiscordBotContext context)
        {
            if (context.Message.Channel is SocketGuildChannel channel)
            {
                Task.Run(async () =>
                {
                    try
                    {
                        Log.Information($"{{Indicator}} Leaving audio on guild {channel.Guild.Id}", "[audio]");
                        await context.AudioManager.LeaveAudioAsync(channel);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failure in voice leave command");
                    }
                }).Forget();
            }

            return Task.FromResult((CommandResponse)null);
        }
    }
}
