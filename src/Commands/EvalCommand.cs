namespace UB3RB0T.Commands
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Threading.Tasks;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp.Scripting;
    using Microsoft.CodeAnalysis.Scripting;
    using Discord.WebSocket;

    [BotOwnerOnly]
    public class EvalCommand : IDiscordCommand
    {
        private ScriptOptions scriptOptions;

        public EvalCommand()
        {
            this.CreateScriptOptions();
        }

        public async Task<CommandResponse> Process(IDiscordBotContext context)
        {
            var script = context.Message.Content.Split(new[] { ' ' }, 2)[1];
            string result = "no result";
            try
            {
                var evalResult = await CSharpScript.EvaluateAsync<object>(script, scriptOptions, globals: new ScriptHost { Message = context.Message, Client = context.Client });
                result = evalResult.ToString();
            }
            catch (Exception ex)
            {
                result = ex.ToString().SubstringUpTo(800);
            }

            return new CommandResponse { Text = $"``{result}``" };
        }

        private void CreateScriptOptions()
        {
            // mscorlib reference issues when using codeanalysis; 
            // see http://stackoverflow.com/questions/38943899/net-core-cs0012-object-is-defined-in-an-assembly-that-is-not-referenced
            var dd = typeof(object).GetTypeInfo().Assembly.Location;
            var coreDir = Directory.GetParent(dd);

            var references = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile($"{coreDir.FullName}{Path.DirectorySeparatorChar}mscorlib.dll"),
                MetadataReference.CreateFromFile(typeof(object).GetTypeInfo().Assembly.Location),
            };

            var referencedAssemblies = Assembly.GetEntryAssembly().GetReferencedAssemblies();
            foreach (var referencedAssembly in referencedAssemblies)
            {
                var loadedAssembly = Assembly.Load(referencedAssembly);
                references.Add(MetadataReference.CreateFromFile(loadedAssembly.Location));
            }

            this.scriptOptions = ScriptOptions.Default.
                AddImports("System", "System.Linq", "System.Text", "Discord", "Discord.WebSocket").
                AddReferences(references);
        }
    }

    public class ScriptHost
    {
        public SocketMessage Message { get; set; }
        public DiscordSocketClient Client { get; set; }
    }
}
