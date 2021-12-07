
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace Hypar.Server
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            await HyparServer.StartAsync(
                args,
                Path.GetFullPath(Path.Combine(@"/Users/andrewheumann/Dev/HyparSpace/ZonePlanningFunctions/SpacePlanningZones/server", "..")),
                typeof(SpacePlanningZones.Function),
                typeof(SpacePlanningZones.SpacePlanningZonesInputs));
        }
    }
}