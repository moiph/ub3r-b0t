namespace UB3RB0T.Commands
{
    using System.Threading.Tasks;

    public interface IDiscordCommand
    {
        Task<CommandResponse> Process(IDiscordBotContext context);
    }
}
