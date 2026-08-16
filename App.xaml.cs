using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace ZeeVault
{
    public partial class App : Application
    {
        private static Mutex? _mutex;

        protected override async void OnStartup(StartupEventArgs e)
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

            // Don't shut down when dialogs close
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Check if password is set
            var settings = LoadPasswordSettings();
            if (settings.IsPasswordSet)
            {
                // Preload main window in background while password window is shown
                var mainWindow = new MainWindow();
                MainWindow = mainWindow;
                var preloadTask = mainWindow.PreloadAsync();

                var passWindow = new PasswordWindow(settings.PasswordHash, settings.PasswordHint);
                passWindow.ShowDialog();

                if (!passWindow.IsAuthenticated)
                {
                    Shutdown();
                    return;
                }

                // Wait for preload to finish, then show
                await preloadTask;
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                mainWindow.Show();
            }
            else
            {
                // No password — open vault directly
                var mainWindow = new MainWindow();
                MainWindow = mainWindow;
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                mainWindow.Show();
            }
        }

        private (bool IsPasswordSet, string PasswordHash, string PasswordHint) LoadPasswordSettings()
        {
            try
            {
                string settingsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ZeeVault", "settings.json");

                if (File.Exists(settingsPath))
                {
                    string json = File.ReadAllText(settingsPath);

                    string passwordSet = ExtractJsonBool(json, "passwordSet");
                    if (passwordSet == "true")
                    {
                        string passwordHash = ExtractJsonString(json, "passwordHash");
                        string passwordHint = ExtractJsonString(json, "passwordHint");
                        return (true, passwordHash, passwordHint);
                    }
                }
            }
            catch { }

            return (false, string.Empty, string.Empty);
        }

        private string ExtractJsonString(string json, string key)
        {
            int idx = json.IndexOf($"\"{key}\"");
            if (idx < 0) return string.Empty;
            int colon = json.IndexOf(':', idx);
            int start = json.IndexOf('"', colon + 1);
            int end = json.IndexOf('"', start + 1);
            return json.Substring(start + 1, end - start - 1);
        }

        private string ExtractJsonBool(string json, string key)
        {
            int idx = json.IndexOf($"\"{key}\"");
            if (idx < 0) return string.Empty;
            int colon = json.IndexOf(':', idx);
            int start = colon + 1;
            while (start < json.Length && json[start] == ' ') start++;
            int end = start;
            while (end < json.Length && json[end] != ',' && json[end] != '}') end++;
            return json.Substring(start, end - start).Trim();
        }
    }
}
