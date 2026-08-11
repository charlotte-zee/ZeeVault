using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace GameVault.Services
{
    public class WindowsAppSearchResult : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        public string Name { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;

        private ImageSource? _icon;
        public ImageSource? Icon
        {
            get => _icon;
            set { _icon = value; OnPropertyChanged(nameof(Icon)); }
        }

        public string DisplayPath
        {
            get
            {
                if (ExecutablePath.StartsWith("steam://", StringComparison.OrdinalIgnoreCase))
                    return "Steam Game";
                if (ExecutablePath.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
                {
                    string id = ExecutablePath.Replace("shell:AppsFolder\\", "");
                    int excl = id.IndexOf('!');
                    if (excl > 0) id = id.Substring(0, excl);
                    return id;
                }
                return ExecutablePath;
            }
        }

        public string SuggestedCategory
        {
            get
            {
                string lower = (Name + " " + ExecutablePath).ToLowerInvariant();
                if (lower.Contains("game") || lower.Contains("steam") || lower.Contains("play") ||
                    lower.Contains("xbox") || lower.Contains("minecraft") || lower.Contains("fortnite") ||
                    lower.Contains("roblox") || lower.Contains("epic") || lower.Contains("gog") ||
                    lower.Contains("riot") || lower.Contains("league") || lower.Contains("valorant"))
                    return "Games";

                if (lower.Contains("code") || lower.Contains("studio") || lower.Contains("tool") ||
                    lower.Contains("cmd") || lower.Contains("terminal") || lower.Contains("git") ||
                    lower.Contains("paint") || lower.Contains("notepad") || lower.Contains("calc") ||
                    lower.Contains("word") || lower.Contains("excel") || lower.Contains("powerpoint") ||
                    lower.Contains("photoshop") || lower.Contains("obs") || lower.Contains("blender") ||
                    lower.Contains("unity") || lower.Contains("unreal") || lower.Contains("docker") ||
                    lower.Contains("postman") || lower.Contains("rider") || lower.Contains("pycharm"))
                    return "Tools";

                return "Games";
            }
        }
    }

    public static class WindowsAppSearchService
    {
        private static List<WindowsAppSearchResult>? _cache;
        private static readonly object SyncLock = new();

        public static List<WindowsAppSearchResult> Search(string query, string category = "All")
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
                return new List<WindowsAppSearchResult>();

            EnsureIndexed();

            lock (SyncLock)
            {
                if (_cache == null) return new List<WindowsAppSearchResult>();

                string q = query.Trim();

                var matches = _cache
                    .Where(a => a.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                Path.GetFileNameWithoutExtension(a.ExecutablePath).Contains(q, StringComparison.OrdinalIgnoreCase));

                if (!string.Equals(category, "All", StringComparison.OrdinalIgnoreCase))
                {
                    matches = matches.Where(a => string.Equals(a.SuggestedCategory, category, StringComparison.OrdinalIgnoreCase));
                }

                return matches
                    .OrderBy(a => a.Name.Equals(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(a => a.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(a => a.Name)
                    .Take(25)
                    .ToList();
            }
        }

        public static void EnsureIndexed()
        {
            if (_cache != null) return;

            lock (SyncLock)
            {
                if (_cache != null) return;
                _cache = BuildIndex();
            }
        }

        public static void InvalidateCache()
        {
            lock (SyncLock) { _cache = null; }
        }

        public static ConcurrentDictionary<string, string> UrlIconCache { get; } = new(StringComparer.OrdinalIgnoreCase);

        public class UrlShortcutInfo
        {
            public string TargetUrl { get; set; } = string.Empty;
            public string IconPath { get; set; } = string.Empty;
        }

        public static UrlShortcutInfo? ParseUrlShortcut(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return null;

                var info = new UrlShortcutInfo();
                foreach (var line in File.ReadAllLines(filePath))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                    {
                        info.TargetUrl = trimmed.Substring(4).Trim();
                    }
                    else if (trimmed.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase))
                    {
                        info.IconPath = trimmed.Substring(9).Trim().Trim('"');
                    }
                }

                if (!string.IsNullOrWhiteSpace(info.TargetUrl))
                    return info;
            }
            catch { }
            return null;
        }

        private static string NormalizeForComparison(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            string n = name.ToLowerInvariant();
            // Strip version numbers like 6.1.0.0, v1.2, 2024, etc.
            n = Regex.Replace(n, @"\s+v?\d+([\.\d]+)*$", "");
            n = Regex.Replace(n, @"\s+", "");
            return n;
        }

        private static void AddResultIfUnique(Dictionary<string, WindowsAppSearchResult> results, string name, string path, string source)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) return;

            bool isProtocol = path.Contains("://");
            string normNew = NormalizeForComparison(name);

            foreach (var existing in results.Values)
            {
                // 1. Same exact path -> duplicate!
                if (string.Equals(existing.ExecutablePath, path, StringComparison.OrdinalIgnoreCase))
                    return;

                string normExisting = NormalizeForComparison(existing.Name);

                // 2. If this new entry is an "Installed Program" (raw Registry), and we ALREADY have a Start Menu or Store entry for this app -> SKIP Registry duplicate!
                if (source == "Installed Program" && (normExisting.Equals(normNew, StringComparison.OrdinalIgnoreCase) || normNew.StartsWith(normExisting, StringComparison.OrdinalIgnoreCase) || normExisting.StartsWith(normNew, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }

                // 3. Same App Name / Normalized Name
                if (string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase) || (normNew.Length > 3 && normExisting.Equals(normNew, StringComparison.OrdinalIgnoreCase)))
                {
                    bool existingIsProtocol = existing.ExecutablePath.Contains("://");

                    // Both standard Win32 / Store apps (e.g. Sublime Text, VS Code, WinDirStat) -> skip duplicate!
                    if (!isProtocol && !existingIsProtocol)
                        return;

                    // Both protocol URLs from same vendor -> skip duplicate!
                    if (isProtocol && existingIsProtocol)
                    {
                        string p1 = path.Split(':')[0];
                        string p2 = existing.ExecutablePath.Split(':')[0];
                        if (string.Equals(p1, p2, StringComparison.OrdinalIgnoreCase))
                            return;
                    }
                }
            }

            string key = name + "|" + path;
            results[key] = new WindowsAppSearchResult
            {
                Name = name,
                ExecutablePath = path,
                Source = source,
                Icon = null
            };
        }

        private static List<WindowsAppSearchResult> BuildIndex()
        {
            var results = new Dictionary<string, WindowsAppSearchResult>(StringComparer.OrdinalIgnoreCase);

            // 1. Shell Applications Folder (shell:AppsFolder) - Finds ALL Store & Windows Apps (Netflix, Spotify, etc.)
            try
            {
                Type? shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType != null)
                {
                    dynamic? shell = Activator.CreateInstance(shellType);
                    if (shell != null)
                    {
                        dynamic? appsFolder = shell.NameSpace("shell:AppsFolder");
                        if (appsFolder != null)
                        {
                            foreach (dynamic item in appsFolder.Items())
                            {
                                try
                                {
                                    string name = (string)item.Name;
                                    string path = (string)item.Path;

                                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) continue;

                                    name = CleanAppName(name);
                                    if (IsIgnoredApp(name)) continue;

                                    string source = "App";
                                    if (path.StartsWith("steam://", StringComparison.OrdinalIgnoreCase))
                                    {
                                        source = "Steam Game";
                                    }
                                    else if (path.StartsWith("uplay://", StringComparison.OrdinalIgnoreCase))
                                    {
                                        source = "Ubisoft";
                                    }
                                    else if (!Path.IsPathRooted(path) && !File.Exists(path) && !path.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
                                    {
                                        string rawId = path.Replace("shell:AppsFolder\\", "").Replace("shell:AppsFolder", "").TrimStart('\\');
                                        path = "shell:AppsFolder\\" + rawId;
                                        source = path.Contains("!") ? "Store App" : "App";
                                    }

                                    AddResultIfUnique(results, name, path, source);
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
            catch { }

            // 2. Start Menu Shortcuts (.lnk and .url files for Steam, Ubisoft, Epic, Win32 apps)
            string[] startMenuPaths = {
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\Start Menu\Programs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs")
            };

            foreach (var smDir in startMenuPaths)
            {
                if (!Directory.Exists(smDir)) continue;
                try
                {
                    // A. Process .lnk files
                    foreach (var lnk in Directory.GetFiles(smDir, "*.lnk", SearchOption.AllDirectories))
                    {
                        try
                        {
                            string rawName = Path.GetFileNameWithoutExtension(lnk);
                            string name = CleanAppName(rawName);
                            if (IsIgnoredApp(name)) continue;

                            var target = ResolveShortcutTarget(lnk);
                            string exePath = target ?? lnk;

                            if (target != null && !File.Exists(target) && !target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) && !target.Contains("://"))
                                continue;

                            string source = "App";
                            if (exePath.StartsWith("steam://", StringComparison.OrdinalIgnoreCase)) source = "Steam Game";
                            else if (exePath.StartsWith("uplay://", StringComparison.OrdinalIgnoreCase)) source = "Ubisoft";
                            else if (exePath.Contains("Ubisoft", StringComparison.OrdinalIgnoreCase)) source = "Ubisoft";

                            AddResultIfUnique(results, name, exePath, source);
                        }
                        catch { }
                    }

                    // B. Process .url internet shortcuts (Steam, Ubisoft, Epic Games Store, GOG)
                    foreach (var urlFile in Directory.GetFiles(smDir, "*.url", SearchOption.AllDirectories))
                    {
                        try
                        {
                            string rawName = Path.GetFileNameWithoutExtension(urlFile);
                            string name = CleanAppName(rawName);
                            if (IsIgnoredApp(name)) continue;

                            var urlInfo = ParseUrlShortcut(urlFile);
                            if (urlInfo == null || string.IsNullOrWhiteSpace(urlInfo.TargetUrl)) continue;

                            string targetUrl = urlInfo.TargetUrl;
                            string source = "App";
                            if (targetUrl.StartsWith("steam://", StringComparison.OrdinalIgnoreCase)) source = "Steam Game";
                            else if (targetUrl.StartsWith("uplay://", StringComparison.OrdinalIgnoreCase)) source = "Ubisoft";
                            else if (targetUrl.StartsWith("com.epicgames", StringComparison.OrdinalIgnoreCase)) source = "Epic";
                            else if (targetUrl.StartsWith("goggalaxy://", StringComparison.OrdinalIgnoreCase)) source = "GOG";

                            // Store icon mapping for icon extraction
                            if (!string.IsNullOrWhiteSpace(urlInfo.IconPath))
                            {
                                UrlIconCache[targetUrl] = urlInfo.IconPath;
                                UrlIconCache[name] = urlInfo.IconPath;
                            }

                            AddResultIfUnique(results, name, targetUrl, source);
                        }
                        catch { }
                    }
                }
                catch { }
            }

            // 3. Registry Uninstall Entries (HKLM & HKCU, 64-bit & 32-bit)
            var regKeys = new (Microsoft.Win32.RegistryKey root, string subPath)[]
            {
                (Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                (Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
                (Microsoft.Win32.Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                (Microsoft.Win32.Registry.CurrentUser, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall")
            };

            foreach (var (root, subPath) in regKeys)
            {
                try
                {
                    using var key = root.OpenSubKey(subPath);
                    if (key == null) continue;

                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        try
                        {
                            using var subKey = key.OpenSubKey(subKeyName);
                            if (subKey == null) continue;

                            string? displayName = subKey.GetValue("DisplayName") as string;
                            string? installLocation = subKey.GetValue("InstallLocation") as string;
                            string? uninstallString = subKey.GetValue("UninstallString") as string;
                            string? displayIcon = subKey.GetValue("DisplayIcon") as string;

                            if (string.IsNullOrWhiteSpace(displayName)) continue;
                            string name = CleanAppName(displayName);
                            if (IsIgnoredApp(name)) continue;

                            string? exePath = null;

                            if (!string.IsNullOrWhiteSpace(displayIcon))
                            {
                                string cleanIconPath = displayIcon.Split(',')[0].Trim('"');
                                if (File.Exists(cleanIconPath)) exePath = cleanIconPath;
                            }

                            if (exePath == null && !string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation))
                            {
                                var exes = Directory.GetFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly);
                                if (exes.Length > 0)
                                    exePath = exes.OrderByDescending(f => new FileInfo(f).Length).First();
                            }

                            if (exePath == null && !string.IsNullOrWhiteSpace(uninstallString))
                            {
                                var match = Regex.Match(uninstallString, @"""([^""]+\.exe)""", RegexOptions.IgnoreCase);
                                if (match.Success && File.Exists(match.Groups[1].Value))
                                    exePath = match.Groups[1].Value;
                            }

                            if (exePath == null || !File.Exists(exePath)) continue;

                            AddResultIfUnique(results, name, exePath, "Installed Program");
                        }
                        catch { }
                    }
                }
                catch { }
            }

            // 4. LocalAppData Programs (%LocalAppData%\Programs - Spotify, Discord, VSCode user installs!)
            string localAppDataPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
            if (Directory.Exists(localAppDataPrograms))
            {
                try
                {
                    foreach (var dir in Directory.GetDirectories(localAppDataPrograms))
                    {
                        try
                        {
                            string folderName = Path.GetFileName(dir);
                            var exes = Directory.GetFiles(dir, "*.exe", SearchOption.AllDirectories);
                            if (exes.Length == 0) continue;

                            string exeCandidate = exes.FirstOrDefault(e => Path.GetFileNameWithoutExtension(e).Equals(folderName, StringComparison.OrdinalIgnoreCase))
                                                  ?? exes.OrderByDescending(f => new FileInfo(f).Length).First();

                            string name = CleanAppName(folderName);
                            if (IsIgnoredApp(name)) continue;

                            if (!results.ContainsKey(name))
                            {
                                results[name] = new WindowsAppSearchResult
                                {
                                    Name = name,
                                    ExecutablePath = exeCandidate,
                                    Source = "User App",
                                    Icon = null  // Loaded lazily on UI thread
                                };
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }

            return results.Values.OrderBy(a => a.Name).ToList();
        }

        private static bool IsIgnoredApp(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            if (name.Length < 2) return true;
            string lower = name.Trim().ToLowerInvariant();

            // Ignore action shortcuts like "Uninstall XYZ", "Uninstall_XYZ", or "unins000"
            // DO NOT ignore legitimate software named "BCUninstaller", "Revo Uninstaller", etc.
            bool isUninstallAction = lower.Equals("uninstall") ||
                                     lower.StartsWith("uninstall ") ||
                                     lower.StartsWith("uninstall_") ||
                                     lower.StartsWith("uninstall-") ||
                                     lower.EndsWith(" uninstall") ||
                                     lower.Contains("unins000");

            if (isUninstallAction) return true;

            return (lower.Contains("setup") && !lower.Contains("studio")) ||
                   lower.Equals("update") || lower.StartsWith("update ") ||
                   lower.Contains("help") || lower.Contains("documentation") || lower.Contains("readme") ||
                   lower.Contains("web site") || lower.Contains("license") || lower.Contains("report a problem") ||
                   lower.StartsWith("microsoft visual c++") || lower.StartsWith("windows driver package");
        }

        private static string CleanAppName(string name)
        {
            name = Regex.Replace(name, @"\s*[\(\[].*?[\)\]]", "");
            name = name.Replace("\u00AE", "").Replace("\u2122", "").Replace("\u2120", "");
            name = Regex.Replace(name, @"[\x00-\x1F]", "");
            name = Regex.Replace(name, @"\s+", " ").Trim();
            return name;
        }

        private static string? ResolveShortcutTarget(string lnkPath)
        {
            try
            {
                using var fs = new FileStream(lnkPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new BinaryReader(fs);

                fs.Seek(76, SeekOrigin.Begin);
                uint flags = reader.ReadUInt32();
                if ((flags & 0x00000001) == 0) return null;

                ushort idListSize = reader.ReadUInt16();
                fs.Seek(idListSize, SeekOrigin.Current);

                uint linkInfoSize = reader.ReadUInt32();
                if (linkInfoSize < 8) return null;

                uint linkInfoFlags = reader.ReadUInt32();
                if ((linkInfoFlags & 0x00000001) == 0) return null;

                uint volumeIDOffset = reader.ReadUInt32();
                uint localBasePathOffset = reader.ReadUInt32();
                uint commonNetworkRelativeLinkOffset = reader.ReadUInt32();
                uint commonPathSuffixOffset = reader.ReadUInt32();

                fs.Seek(linkInfoSize + volumeIDOffset - 4, SeekOrigin.Begin);
                uint volumeIDSize = reader.ReadUInt32();
                fs.Seek(volumeIDSize - 4, SeekOrigin.Current);

                fs.Seek(linkInfoSize + localBasePathOffset - 4, SeekOrigin.Begin);
                var bytes = new List<byte>();
                byte b;
                while ((b = reader.ReadByte()) != 0)
                    bytes.Add(b);

                string localBasePath = System.Text.Encoding.UTF8.GetString(bytes.ToArray());

                if (commonPathSuffixOffset > 0)
                {
                    fs.Seek(linkInfoSize + commonPathSuffixOffset - 4, SeekOrigin.Begin);
                    var suffixBytes = new List<byte>();
                    while ((b = reader.ReadByte()) != 0)
                        suffixBytes.Add(b);
                    string suffix = System.Text.Encoding.UTF8.GetString(suffixBytes.ToArray());
                    localBasePath += suffix;
                }

                return localBasePath;
            }
            catch
            {
                return null;
            }
        }
    }
}
