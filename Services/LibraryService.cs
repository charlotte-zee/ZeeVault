using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using ZeeVault.Models;

namespace ZeeVault.Services
{
    public class LibraryService
    {
        private static readonly string AppDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZeeVault");
        private static readonly string LibraryFilePath = Path.Combine(AppDataFolder, "library.json");

        public ObservableCollection<VaultItem> Items { get; private set; } = new();

        public LibraryService()
        {
            LoadLibrary();
        }

        public static string GetProductVersionInfo(string filePath)
        {
            try
            {
                string clean = filePath.Split(new[] { " --" }, StringSplitOptions.None)[0].Trim().Trim('"');
                if (File.Exists(clean))
                {
                    var vi = FileVersionInfo.GetVersionInfo(clean);
                    string name = vi.ProductName?.Trim();
                    if (!string.IsNullOrWhiteSpace(name) && name.Length > 1)
                        return CleanName(name);
                    name = vi.FileDescription?.Trim();
                    if (!string.IsNullOrWhiteSpace(name) && name.Length > 1)
                        return CleanName(name);
                }
            }
            catch { }
            return string.Empty;
        }

        private static string CleanName(string name)
        {
            name = name.Replace("\u00BD", "2").Replace("\u00BE", "3").Replace("\u00BC", "1/4");
            name = name.Replace("\u00AE", "").Replace("\u2122", "").Replace("\u2120", "");
            name = Regex.Replace(name, @"[\x00-\x1F]", "");
            name = Regex.Replace(name, @"\s+version\s+\d+[\d.]*", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\s+v\d+[\d.]*$", "");
            name = Regex.Replace(name, @"\s+", " ").Trim();
            return name;
        }

        public void LoadLibrary()
        {
            try
            {
                if (!Directory.Exists(AppDataFolder))
                    Directory.CreateDirectory(AppDataFolder);

                if (File.Exists(LibraryFilePath))
                {
                    string json = File.ReadAllText(LibraryFilePath);
                    var items = JsonSerializer.Deserialize<List<VaultItem>>(json);
                    if (items != null && items.Count > 0)
                    {
                        Items = new ObservableCollection<VaultItem>(items);
                        // Icons are NOT loaded here — LoadIconsAsync is called after the window is shown
                        SaveLibrary();
                        return;
                    }
                }
            }
            catch { }

            Items.Clear();
            SaveLibrary();
        }

        public void SaveLibrary()
        {
            try
            {
                if (!Directory.Exists(AppDataFolder))
                    Directory.CreateDirectory(AppDataFolder);

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(Items, options);
                File.WriteAllText(LibraryFilePath, json);
            }
            catch { }
        }

        /// <summary>
        /// Loads icons asynchronously on a background thread, dispatching each result
        /// to the UI thread individually so WPF shows icons as they arrive.
        /// Call this AFTER the window is loaded (i.e. STA message pump is running).
        /// </summary>
        public async System.Threading.Tasks.Task LoadIconsAsync(System.Windows.Threading.Dispatcher dispatcher)
        {
            var snapshot = Items.ToList();
            await System.Threading.Tasks.Task.Run(() =>
            {
                foreach (var item in snapshot)
                {
                    try
                    {
                        // Determine source path — prefer custom icon if set
                        string iconPath = (!string.IsNullOrWhiteSpace(item.CustomIconPath) && File.Exists(item.CustomIconPath))
                            ? item.CustomIconPath
                            : item.FilePath;

                        var icon = IconExtractor.GetIconForFile(iconPath);

                        // Freeze so it can cross thread boundaries
                        if (icon != null && icon.CanFreeze)
                            icon.Freeze();

                        dispatcher.BeginInvoke(() =>
                        {
                            if (icon != null)
                                item.IconSource = icon;
                        }, System.Windows.Threading.DispatcherPriority.Background);
                    }
                    catch { }
                }
            });
        }

        public bool AddItem(VaultItem item)
        {
            // Block only if the exact same FilePath is already in ZeeVault
            if (Items.Any(i => string.Equals(i.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            // If another item has the exact same Title, differentiate by vendor source
            if (Items.Any(i => string.Equals(i.Title, item.Title, StringComparison.OrdinalIgnoreCase)))
            {
                if (item.FilePath.StartsWith("steam://", StringComparison.OrdinalIgnoreCase))
                    item.Title = $"{item.Title} (Steam)";
                else if (item.FilePath.StartsWith("uplay://", StringComparison.OrdinalIgnoreCase))
                    item.Title = $"{item.Title} (Ubisoft)";
                else if (item.FilePath.StartsWith("com.epicgames", StringComparison.OrdinalIgnoreCase))
                    item.Title = $"{item.Title} (Epic)";
            }

            if (string.IsNullOrWhiteSpace(item.Title))
            {
                string realName = GetProductVersionInfo(item.FilePath);
                if (!string.IsNullOrWhiteSpace(realName) && !item.FilePath.Contains("://"))
                    item.Title = realName;
                else
                    item.Title = Path.GetFileNameWithoutExtension(item.FilePath);
            }
            item.IconSource = IconExtractor.GetIconForFile(item.FilePath);
            Items.Insert(0, item);
            SaveLibrary();
            return true;
        }

        public void RemoveItem(VaultItem item)
        {
            Items.Remove(item);
            SaveLibrary();
        }
    }
}
