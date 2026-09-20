using System.Linq;

namespace HyMotion.Desktop;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        ApplicationConfiguration.Initialize();
        var update = args.Any(a => string.Equals(a, "--update", StringComparison.OrdinalIgnoreCase));
        Application.Run(new SetupForm(autoUpdate: update));
    }
}
