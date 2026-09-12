using OwaWidget.App.Services;
using Velopack;

namespace OwaWidget.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build()
            .SetArgs(args)
            .SetAppUserModelId(ShortcutInstaller.AppUserModelId)
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}