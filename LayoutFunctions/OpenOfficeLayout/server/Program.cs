
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
                Path.GetFullPath(Path.Combine(@"C:\Users\Leland\Documents\GitHub\HyparSpace\LayoutFunctions\OpenOfficeLayout\server", "..")),
                typeof(OpenOfficeLayout.Function),
                typeof(OpenOfficeLayout.OpenOfficeLayoutInputs));
        }
    }
}