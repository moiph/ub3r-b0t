namespace UB3RB0T.Modules
{
    using System.Threading.Tasks;

    public enum ModuleResult
    {
        Continue,
        Stop,
    }

    public interface IModule
    {
        Task<ModuleResult> Process(IBotContext moduleContext);
    }
}
