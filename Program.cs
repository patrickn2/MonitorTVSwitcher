namespace GameShortcut;

static class Program
{
    private const string MutexName = @"Local\MonitorTVSwitcher.SingleInstance";

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--switch-to")
        {
            ApplicationConfiguration.Initialize();
            OneShotSwitch.Run(args[1]);
            return;
        }

        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}
