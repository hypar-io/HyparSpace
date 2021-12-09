
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace Hypar.Server
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            Elements.Serialization.glTF.GltfExtensions.GltfCachePath = "/Users/andrewheumann/Desktop/Test-disk-cache";

            await HyparServer.StartAsync(
                args,
                Path.GetFullPath(Path.Combine(@"/Users/andrewheumann/Dev/HyparSpace/LayoutFunctions/PrivateOfficeLayout/server", "..")),
                typeof(PrivateOfficeLayout.Function),
                typeof(PrivateOfficeLayout.PrivateOfficeLayoutInputs));
        }
    }
}