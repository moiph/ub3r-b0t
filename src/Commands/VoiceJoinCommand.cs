namespace UB3RB0T.Commands
{
    using System;
    using System.Threading.Tasks;
    using Discord.WebSocket;
    using Serilog;

    public class VoiceJoinCommand : IDiscordCommand
    {
        public Task<CommandResponse> Process(IDiscordBotContext context)
        {
            var channel = (context.Message.Author as SocketGuildUser)?.VoiceChannel;
            if (channel == null)
            {
                return Task.FromResult(new CommandResponse { Text = "Join a voice channel first" });
            }

            Task.Run(async () =>
            {
                try
                {
                    Log.Information($"{{Indicator}} Joining audio on guild {channel.Guild.Id}", "[audio]");
                    if (await context.AudioManager.JoinAudioAsync(channel, allowReconnect: false))
                    {
                        await context.Message.Channel.SendMessageAsync($"[Joined <#{channel.Id}>]");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failure in voice join command");
                }
            }).Forget();

            return Task.FromResult((CommandResponse)null);
        }
    }
}
