namespace UB3RB0T.Commands
{
    using System.Threading.Tasks;
    using Discord.WebSocket;
    
    [BotOwnerOnly]
    class CaptainCommand : IDiscordCommand
    {
        public async Task<CommandResponse> Process(IDiscordBotContext context)
        {
            var guildChannel = context.Message.Channel as SocketGuildChannel;
            var textChannel = context.Message.Channel as SocketTextChannel;
            var botGuildUser = guildChannel.GetUser(context.Client.CurrentUser.Id);
            var existingNickname = botGuildUser.Nickname;

            await botGuildUser.ModifyAsync(x => x.Nickname = "Kwame");
            await textChannel.SendMessageAsync("EARTH!");
            await Task.Delay(1100);

            await botGuildUser.ModifyAsync(x => x.Nickname = "Wheeler");
            await textChannel.SendMessageAsync("FIRE!");
            await Task.Delay(1100);

            await botGuildUser.ModifyAsync(x => x.Nickname = "Linka");
            await textChannel.SendMessageAsync("WIND!");
            await Task.Delay(1100);

            await botGuildUser.ModifyAsync(x => x.Nickname = "Gi");
            await textChannel.SendMessageAsync("WATER!");
            await Task.Delay(1100);

            await botGuildUser.ModifyAsync(x => x.Nickname = "Ma-ti");
            await textChannel.SendMessageAsync("HEART!");
            await Task.Delay(1100);

            await botGuildUser.ModifyAsync(x => x.Nickname = "anonymous");
            await textChannel.SendMessageAsync("by your powers combined...");
            await Task.Delay(1100);

            await botGuildUser.ModifyAsync(x => x.Nickname = "Captain Planet");
            await textChannel.SendMessageAsync("I AM CAPTAIN PLANET!");
            await Task.Delay(1100);

            await botGuildUser.ModifyAsync(x => x.Nickname = "Everyone");
            await textChannel.SendMessageAsync("GOOOO PLANET!");
            await Task.Delay(5000);

            await botGuildUser.ModifyAsync(x => x.Nickname = existingNickname);

            return null;
        }
    }
}
