namespace UB3RB0T.Commands
{
    using System;
    using System.Threading.Tasks;
    using Discord.WebSocket;

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
                        await context.AudioManager.LeaveAudioAsync(channel);
                    }
                    catch (Exception ex)
                    {
                        // TODO: proper logging
                        Console.WriteLine(ex);
                    }
                }).Forget();
            }

            return Task.FromResult((CommandResponse)null);
        }
    }
}
