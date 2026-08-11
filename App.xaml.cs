using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace ZeeVault
{
    public partial class App : Application
    {
        private static Mutex? _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            _mutex = new Mutex(true, "ZeeVault_SingleInstance", out bool isNew);
            if (!isNew)
            {
                MessageBox.Show("ZeeVault is already running.", "ZeeVault", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                try
                {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), args.ExceptionObject.ToString());
                }
                catch { }
            };

            DispatcherUnhandledException += (s, args) =>
            {
                try
                {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), args.Exception.ToString());
                }
                catch { }
            };

            base.OnStartup(e);
        }
    }
}
