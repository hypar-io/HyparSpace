
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Linq;

namespace Hypar.Server
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            await HyparServer.StartAsync(
                args,
                Path.GetFullPath(Path.Combine(Assembly.GetExecutingAssembly().Location, "../../../../..")),
                async (executionRequest) =>
                {
                    var input = executionRequest.Args.ToObject<DefineProgramRequirements.DefineProgramRequirementsInputs>();
                    var function = new DefineProgramRequirements.Function();
                    Directory.SetCurrentDirectory(Path.GetDirectoryName(typeof(DefineProgramRequirements.Function).Assembly.Location)!);
                    return await function.Handler(input, null);
                });
        }
    }
}