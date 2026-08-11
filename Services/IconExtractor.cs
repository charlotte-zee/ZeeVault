using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZeeVault.Services
{
    public static class IconExtractor
    {
        private static readonly ConcurrentDictionary<string, ImageSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);

        public static void InvalidateCache() => IconCache.Clear();

        public static void InvalidatePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            IconCache.TryRemove(filePath.Trim().Trim('"'), out _);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHSimpleIDListFromPath([MarshalAs(UnmanagedType.LPWStr)] string pszPath);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(IntPtr pIDL, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_LARGEICON = 0x000000000;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
        private const uint SHGFI_PIDL = 0x000000008;

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int cx;
            public int cy;
            public SIZE(int x, int y) { cx = x; cy = y; }
        }

        [Flags]
        private enum SIIGBF
        {
            SIIGBF_RESISETOSQUARE = 0x00000001,
            SIIGBF_BIGGERSIZEOK = 0x00000002,
            SIIGBF_MEMORYONLY = 0x00000004,
            SIIGBF_ICONONLY = 0x00000008,
            SIIGBF_THUMBNAILONLY = 0x00000010,
            SIIGBF_INCACHEONLY = 0x00000020
        }

        [ComImport]
        [Guid("bcc18b79-556b-4e46-8373-7607a77c3a09")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            void GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        public static ImageSource? GetIconForFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return null;

            string cleanPath = filePath.Trim().Trim('"');

            // 0. Check if this path is mapped in UrlIconCache (for steam:// or uplay:// shortcuts)
            if (WindowsAppSearchService.UrlIconCache.TryGetValue(cleanPath, out var cachedIconPath) && File.Exists(cachedIconPath))
            {
                cleanPath = cachedIconPath;
            }
            else if (cleanPath.Contains("://"))
            {
                string? resolved = TryResolveProtocolIcon(cleanPath);
                if (!string.IsNullOrEmpty(resolved) && File.Exists(resolved))
                {
                    cleanPath = resolved;
                }
            }

            // Normalize bare AUMIDs (no drive letter, no slashes, not steam:// or uplay://) to shell:AppsFolder\ format
            bool alreadyShell = cleanPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);
            bool isProtocol = cleanPath.Contains("://");
            bool isRooted = cleanPath.Length > 1 && (cleanPath[1] == ':' || cleanPath.StartsWith(@"\\"));
            bool hasSlash = cleanPath.Contains('\\') || cleanPath.Contains('/');
            if (!alreadyShell && !isProtocol && !isRooted && !hasSlash && cleanPath.Length > 2)
            {
                cleanPath = "shell:AppsFolder\\" + cleanPath;
            }

            if (IconCache.TryGetValue(cleanPath, out var cached))
                return cached;

            ImageSource? result = null;

            // 0b. Directory — extract folder icon via SHGetFileInfo
            if (Directory.Exists(cleanPath))
            {
                result = ExtractFolderIcon(cleanPath);
                if (result != null)
                {
                    IconCache[cleanPath] = result;
                    return result;
                }
            }

            // 1. If it's a direct .ico file
            if ((cleanPath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) || cleanPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) && File.Exists(cleanPath))
            {
                try
                {
                    using var ico = new Icon(cleanPath, 64, 64);
                    result = ToImageSource(ico);
                }
                catch
                {
                    try
                    {
                        using var sysIcon = Icon.ExtractAssociatedIcon(cleanPath);
                        if (sysIcon != null) result = ToImageSource(sysIcon);
                    }
                    catch { }
                }
            }

            // 2. For regular files — try ExtractAssociatedIcon FIRST (gives proper .txt/.doc/.zip icons)
            if (result == null && File.Exists(cleanPath))
            {
                try
                {
                    using var sysIcon = Icon.ExtractAssociatedIcon(cleanPath);
                    if (sysIcon != null) result = ToImageSource(sysIcon);
                }
                catch { }
            }

            // 3. Try Shell PIDL Extraction (shell:AppsFolder\..., Store Apps, Steam URLs)
            if (result == null)
            {
                result = ExtractShellIconViaPidl(cleanPath);
            }

            // 4. Try IShellItemImageFactory
            if (result == null)
            {
                result = ExtractShellItemImage(cleanPath);
            }

            // 5. Fallback: Standard SHGetFileInfo
            if (result == null)
            {
                result = ExtractShellIcon(cleanPath, !File.Exists(cleanPath));
            }

            IconCache[cleanPath] = result;
            return result;
        }

        private static ImageSource? ExtractShellIconViaPidl(string path)
        {
            try
            {
                IntPtr pidl = SHSimpleIDListFromPath(path);
                if (pidl != IntPtr.Zero)
                {
                    try
                    {
                        var shinfo = new SHFILEINFO();
                        IntPtr hImg = SHGetFileInfo(pidl, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_LARGEICON | SHGFI_PIDL);
                        if (hImg != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
                        {
                            try
                            {
                                using var icon = Icon.FromHandle(shinfo.hIcon);
                                return ToImageSource(icon);
                            }
                            finally
                            {
                                DestroyIcon(shinfo.hIcon);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(pidl);
                    }
                }
            }
            catch { }
            return null;
        }

        private static ImageSource? ExtractShellItemImage(string path)
        {
            try
            {
                Guid guid = typeof(IShellItemImageFactory).GUID;
                SHCreateItemFromParsingName(path, IntPtr.Zero, ref guid, out IShellItemImageFactory factory);
                if (factory != null)
                {
                    factory.GetImage(new SIZE(64, 64), SIIGBF.SIIGBF_ICONONLY | SIIGBF.SIIGBF_BIGGERSIZEOK, out IntPtr hBitmap);
                    if (hBitmap != IntPtr.Zero)
                    {
                        try
                        {
                            BitmapSource bmp = Imaging.CreateBitmapSourceFromHBitmap(
                                hBitmap,
                                IntPtr.Zero,
                                Int32Rect.Empty,
                                BitmapSizeOptions.FromEmptyOptions());
                            bmp.Freeze();
                            return bmp;
                        }
                        finally
                        {
                            DeleteObject(hBitmap);
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static ImageSource? ExtractShellIcon(string path, bool useFileAttributes)
        {
            try
            {
                var shinfo = new SHFILEINFO();
                uint flags = SHGFI_ICON | SHGFI_LARGEICON;
                if (useFileAttributes) flags |= SHGFI_USEFILEATTRIBUTES;

                IntPtr hImg = SHGetFileInfo(path, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);
                if (hImg != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
                {
                    try
                    {
                        using var icon = Icon.FromHandle(shinfo.hIcon);
                        return ToImageSource(icon);
                    }
                    finally
                    {
                        DestroyIcon(shinfo.hIcon);
                    }
                }
            }
            catch { }
            return null;
        }

        private static ImageSource? ExtractFolderIcon(string folderPath)
        {
            try
            {
                var shinfo = new SHFILEINFO();
                // FILE_ATTRIBUTE_DIRECTORY = 0x10
                IntPtr hImg = SHGetFileInfo(folderPath, 0x10, ref shinfo, (uint)Marshal.SizeOf(shinfo),
                    SHGFI_ICON | SHGFI_LARGEICON | SHGFI_USEFILEATTRIBUTES);
                if (hImg != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
                {
                    try
                    {
                        using var icon = Icon.FromHandle(shinfo.hIcon);
                        return ToImageSource(icon);
                    }
                    finally
                    {
                        DestroyIcon(shinfo.hIcon);
                    }
                }
            }
            catch { }
            return null;
        }

        private static string? TryResolveProtocolIcon(string url)
        {
            try
            {
                if (url.StartsWith("steam://rungameid/", StringComparison.OrdinalIgnoreCase))
                {
                    string appId = url.Substring("steam://rungameid/".Length).Trim();
                    if (!string.IsNullOrEmpty(appId))
                    {
                        // Check Steam games icon directory
                        string steamGamesDir = @"C:\Program Files (x86)\Steam\steam\games";
                        if (Directory.Exists(steamGamesDir))
                        {
                            var matches = Directory.GetFiles(steamGamesDir, $"*{appId}*.ico");
                            if (matches.Length > 0 && File.Exists(matches[0]))
                                return matches[0];
                        }

                        // Check Steam librarycache
                        string steamCacheDir = @"C:\Program Files (x86)\Steam\appcache\librarycache";
                        if (Directory.Exists(steamCacheDir))
                        {
                            var iconMatches = Directory.GetFiles(steamCacheDir, $"{appId}_icon.jpg");
                            if (iconMatches.Length > 0 && File.Exists(iconMatches[0]))
                                return iconMatches[0];
                        }
                    }
                }
                else if (url.StartsWith("uplay://", StringComparison.OrdinalIgnoreCase))
                {
                    string uplayDataDir = @"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\data";
                    if (Directory.Exists(uplayDataDir))
                    {
                        var icons = Directory.GetFiles(uplayDataDir, "*.ico");
                        if (icons.Length > 0)
                        {
                            return icons.OrderByDescending(f => File.GetLastWriteTime(f)).FirstOrDefault();
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static ImageSource? ToImageSource(Icon icon)
        {
            try
            {
                BitmapSource bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                bitmapSource.Freeze();
                return bitmapSource;
            }
            catch
            {
                return null;
            }
        }
    }
}

