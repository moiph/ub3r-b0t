namespace UB3RB0T.Commands
{
    using System.Threading.Tasks;

    public interface IFluxerCommand
    {
        Task<CommandResponse?> Process(IFluxerBotContext context);
    }
}
