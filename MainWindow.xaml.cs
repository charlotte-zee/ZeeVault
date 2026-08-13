using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Net.Http;
using ZeeVault.Dialogs;
using ZeeVault.Models;
using ZeeVault.Services;
using Wpf.Ui.Controls;

namespace ZeeVault
{
    public partial class MainWindow : FluentWindow
    {
        private readonly LibraryService _libraryService;
        private string _activeCategory = "All";
        private string _currentLayout = "cards"; // "clean" or "cards"

        public MainWindow()
        {
            InitializeComponent();
            _libraryService = new LibraryService();
            ApplyFilter();
            UpdateGridColumns();
            Loaded += MainWindow_Loaded;
            LocationChanged += MainWindow_LocationChanged;
            PreviewMouseDown += MainWindow_PreviewMouseDown;
            StateChanged += MainWindow_StateChanged;
            Deactivated += MainWindow_Deactivated;
        }

        private void MainWindow_LocationChanged(object? sender, EventArgs e)
        {
            if (SearchDropdown != null && SearchDropdown.IsOpen)
            {
                var offset = SearchDropdown.HorizontalOffset;
                SearchDropdown.HorizontalOffset = offset + 0.0001;
                SearchDropdown.HorizontalOffset = offset;
            }

            // Close settings popup on move so it doesn't get left behind
            if (SettingsPopup != null && SettingsPopup.IsOpen)
            {
                SettingsPopup.IsOpen = false;
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Invalidate stale caches so AUMIDs get re-indexed with correct shell:AppsFolder\ prefix
            IconExtractor.InvalidateCache();
            WindowsAppSearchService.InvalidateCache();

            // Run search index FIRST so UrlIconCache has all shortcut icon mappings ready
            await System.Threading.Tasks.Task.Run(() => WindowsAppSearchService.EnsureIndexed());

            // Now load library icons — all protocol icon mappings are ready in UrlIconCache
            await _libraryService.LoadIconsAsync(Dispatcher);

            // Load and apply saved layout
            _currentLayout = LoadLayoutSetting();
            ApplyLayout();

            // Auto-check for updates on startup
            _ = CheckForUpdateOnStartup();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);
            }
            catch
            {
            }

            // Block resize cursors and drag via WndProc
            var source = HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            source?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_NCHITTEST = 0x0084;
            const int HTCLIENT = 1;

            if (msg == WM_NCHITTEST && WindowState != System.Windows.WindowState.Maximized)
            {
                // Force all border hits to HTCLIENT — no resize cursors, no drag
                handled = true;
                return new IntPtr(HTCLIENT);
            }

            return IntPtr.Zero;
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Enforce fixed size when not maximized
            if (WindowState != System.Windows.WindowState.Maximized)
            {
                if (Width != 960) Width = 960;
                if (Height != 660) Height = 660;
            }

            UpdateGridColumns();
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            UpdateGridColumns();
        }

        private void MainWindow_Deactivated(object? sender, EventArgs e)
        {
            // Close settings popup when app loses focus
            if (SettingsPopup != null && SettingsPopup.IsOpen)
            {
                SettingsPopup.IsOpen = false;
            }
        }

        private void UpdateGridColumns()
        {
            if (ItemsGrid == null) return;

            // Normal mode: 5 columns for a tighter, cleaner look
            double normalCardWidth = (960.0 - 50.0) / 5.0;
            double availableWidth = ActualWidth - 50;

            // How many columns fit at the same card width as normal mode
            int columns = Math.Max(1, (int)Math.Round(availableWidth / normalCardWidth));

            var uniformGrid = FindUniformGrid(ItemsGrid);
            if (uniformGrid != null)
            {
                uniformGrid.Columns = columns;
            }
        }

        private System.Windows.Controls.Primitives.UniformGrid FindUniformGrid(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is System.Windows.Controls.Primitives.UniformGrid grid)
                    return grid;
                var result = FindUniformGrid(child);
                if (result != null) return result;
            }
            return null;
        }

        private void ApplyFilter()
        {
            var search = TxtSearch.Text?.Trim() ?? string.Empty;

            var filtered = _libraryService.Items.Where(item =>
            {
                bool categoryMatch = _activeCategory == "All" || string.Equals(item.Category, _activeCategory, StringComparison.OrdinalIgnoreCase);
                bool searchMatch = string.IsNullOrEmpty(search) ||
                                   item.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                   item.FilePath.Contains(search, StringComparison.OrdinalIgnoreCase);

                return categoryMatch && searchMatch;
            }).ToList();

            ItemsGrid.ItemsSource = filtered;
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string category)
            {
                _activeCategory = category;
                ApplyFilter();
            }
        }

        private WindowsAppSearchResult? _selectedSearchResult;
        private string _searchCategory = "All";

        private void SearchCategory_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string cat)
            {
                _searchCategory = cat;
                ShowSearchDropdownIfNeeded();
            }
        }

        private void TxtSearch_GotFocus(object sender, RoutedEventArgs e)
        {
            Dispatcher.InvokeAsync(ShowSearchDropdownIfNeeded, System.Windows.Threading.DispatcherPriority.Input);
        }

        private void TxtSearch_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!TxtSearch.IsKeyboardFocusWithin)
            {
                TxtSearch.Focus();
            }
            Dispatcher.InvokeAsync(ShowSearchDropdownIfNeeded, System.Windows.Threading.DispatcherPriority.Input);
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
            ShowSearchDropdownIfNeeded();
        }

        private void ShowSearchDropdownIfNeeded()
        {
            string query = TxtSearch.Text.Trim();
            if (query.Length >= 2)
            {
                var results = WindowsAppSearchService.Search(query, _searchCategory);
                SearchResultsList.ItemsSource = results;
                if (results.Count > 0)
                {
                    TxtResultCount.Text = $"{results.Count} apps found";
                    SearchDropdown.IsOpen = true;

                    // Load icons asynchronously on the UI (STA) thread so shell COM calls work
                    var resultsCopy = results.ToList();
                    _ = LoadIconsAsync(resultsCopy);
                }
                else
                {
                    SearchDropdown.IsOpen = false;
                }
            }
            else
            {
                SearchDropdown.IsOpen = false;
                SearchResultsList.ItemsSource = null;
            }
        }

        private async System.Threading.Tasks.Task LoadIconsAsync(List<WindowsAppSearchResult> items)
        {
            foreach (var r in items)
            {
                if (r.Icon != null || string.IsNullOrWhiteSpace(r.ExecutablePath)) continue;
                // Await a background priority dispatch so UI stays responsive between icon loads
                await Dispatcher.InvokeAsync(() =>
                {
                    r.Icon = IconExtractor.GetIconForFile(r.ExecutablePath);
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void SearchResult_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is WindowsAppSearchResult result)
            {
                _selectedSearchResult = result;
                SearchDropdown.IsOpen = false;

                // If icon not loaded yet, load it now on the UI thread
                if (result.Icon == null)
                    result.Icon = IconExtractor.GetIconForFile(result.ExecutablePath);

                PopupAppName.Text = result.Name;
                PopupAppPath.Text = result.ExecutablePath;
                PopupAppIcon.Source = result.Icon;
                PopupTitleInput.Text = result.Name;

                switch (result.SuggestedCategory)
                {
                    case "Games": PopupCatGames.IsChecked = true; break;
                    case "Tools": PopupCatTools.IsChecked = true; break;
                    case "Files": PopupCatFiles.IsChecked = true; break;
                    default: PopupCatGames.IsChecked = true; break;
                }

                TxtSearch.Text = string.Empty;
                AddFromSearchPopup.Visibility = Visibility.Visible;
            }
        }

        private void AddFromSearchPopup_Background_Click(object sender, MouseButtonEventArgs e)
        {
            AddFromSearchPopup.Visibility = Visibility.Collapsed;
        }

        private void AddFromSearchPopup_Close(object sender, RoutedEventArgs e)
        {
            AddFromSearchPopup.Visibility = Visibility.Collapsed;
        }

        private void AddFromSearchPopup_Confirm(object sender, RoutedEventArgs e)
        {
            if (_selectedSearchResult == null) return;

            string category = "Tools";
            if (PopupCatGames.IsChecked == true) category = "Games";
            else if (PopupCatFiles.IsChecked == true) category = "Files";

            string title = PopupTitleInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(title)) title = _selectedSearchResult.Name;

            string customIcon = string.Empty;
            if (WindowsAppSearchService.UrlIconCache.TryGetValue(_selectedSearchResult.ExecutablePath, out var iconP1) && File.Exists(iconP1))
            {
                customIcon = iconP1;
            }
            else if (WindowsAppSearchService.UrlIconCache.TryGetValue(_selectedSearchResult.Name, out var iconP2) && File.Exists(iconP2))
            {
                customIcon = iconP2;
            }

            var item = new VaultItem
            {
                Title = title,
                FilePath = _selectedSearchResult.ExecutablePath,
                CustomIconPath = customIcon,
                Category = category,
                DateAdded = DateTime.Now
            };

            bool added = _libraryService.AddItem(item);
            if (!added)
            {
                System.Windows.MessageBox.Show($"\"{title}\" is already in your ZeeVault!", "Already Added", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }

            ApplyFilter();
            AddFromSearchPopup.Visibility = Visibility.Collapsed;
            _selectedSearchResult = null;
            _lastDropTime = DateTime.Now;
        }

        private Point _dragStartPoint;
        private bool _isDraggingCard = false;
        private DateTime _lastDropTime = DateTime.MinValue;

        private void ItemCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            _isDraggingCard = false;
        }

        private void ItemCard_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && !_isDraggingCard)
            {
                Point position = e.GetPosition(null);
                if (Math.Abs(position.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(position.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    if (sender is FrameworkElement element && element.DataContext is VaultItem item)
                    {
                        _isDraggingCard = true;
                        DragDrop.DoDragDrop(element, new DataObject("VaultItem", item), DragDropEffects.Move);
                        _isDraggingCard = false;
                    }
                }
            }
        }

        private void ItemCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDraggingCard && sender is FrameworkElement element && element.DataContext is VaultItem item)
            {
                // Show loading cursor and launch immediately
                Mouse.OverrideCursor = Cursors.Wait;
                LaunchItem(item);

                // Reset cursor after a short delay
                Dispatcher.InvokeAsync(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(500);
                    Mouse.OverrideCursor = null;
                }, System.Windows.Threading.DispatcherPriority.Normal);
            }
        }

        private void ItemCard_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("VaultItem"))
            {
                e.Effects = DragDropEffects.Move;
                if (sender is Border border)
                {
                    border.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#806366F1"));
                    border.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A6366F1"));
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void ItemCard_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is Border border)
            {
                border.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0EFFFFFF"));
                border.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0AFFFFFF"));
            }
        }

        private void ItemCard_Drop(object sender, DragEventArgs e)
        {
            if (sender is Border border)
            {
                border.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0EFFFFFF"));
                border.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0AFFFFFF"));
            }

            if (e.Data.GetDataPresent("VaultItem"))
            {
                var sourceItem = e.Data.GetData("VaultItem") as VaultItem;
                var targetItem = (sender as FrameworkElement)?.DataContext as VaultItem;

                if (sourceItem != null && targetItem != null && sourceItem != targetItem)
                {
                    int oldIndex = _libraryService.Items.IndexOf(sourceItem);
                    int newIndex = _libraryService.Items.IndexOf(targetItem);

                    if (oldIndex >= 0 && newIndex >= 0)
                    {
                        _libraryService.Items.Move(oldIndex, newIndex);
                        _libraryService.SaveLibrary();
                        ApplyFilter();
                    }
                }
            }
        }

        private void MenuChangePath_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.Tag is VaultItem item)
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = $"Select Executable Target for {item.Title}",
                    Filter = "Executable & Launcher Files (*.exe;*.bat;*.cmd;*.url;*.lnk)|*.exe;*.bat;*.cmd;*.url;*.lnk|All Files (*.*)|*.*"
                };

                if (dialog.ShowDialog() == true)
                {
                    item.FilePath = dialog.FileName;
                    // Only replace icon if user hasn't set a custom one
                    if (string.IsNullOrWhiteSpace(item.CustomIconPath))
                        item.IconSource = IconExtractor.GetIconForFile(dialog.FileName);
                    _libraryService.SaveLibrary();
                    ApplyFilter();
                }
            }
        }

        private void MenuChangeIcon_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.Tag is VaultItem item)
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = $"Choose Icon for \"{item.Title}\"",
                    Filter = "Image & Icon Files (*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.exe)|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.exe|All Files (*.*)|*.*"
                };

                if (dialog.ShowDialog() == true)
                {
                    // Clear old cache entry so the new icon loads fresh
                    IconExtractor.InvalidatePath(dialog.FileName);

                    var newIcon = IconExtractor.GetIconForFile(dialog.FileName);
                    if (newIcon != null)
                    {
                        item.CustomIconPath = dialog.FileName;
                        item.IconSource = newIcon;
                        _libraryService.SaveLibrary();
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("Couldn't extract an icon from that file. Try a .ico or .png image instead.", "ZeeVault", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    }
                }
            }
        }

        private void MenuResetIcon_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.Tag is VaultItem item)
            {
                item.CustomIconPath = string.Empty;
                IconExtractor.InvalidatePath(item.FilePath);
                item.IconSource = IconExtractor.GetIconForFile(item.FilePath);
                _libraryService.SaveLibrary();
            }
        }

        private void LaunchItem(VaultItem item)
        {
            if (string.IsNullOrWhiteSpace(item.FilePath)) return;

            try
            {
                string fp = item.FilePath.Trim();
                ProcessStartInfo psi;

                bool isProtocolUrl = fp.Contains("://");
                bool isShellPath = fp.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);
                bool isBareAumid = !isProtocolUrl && !isShellPath && !Path.IsPathRooted(fp) && !File.Exists(fp);

                if (isProtocolUrl)
                {
                    psi = new ProcessStartInfo
                    {
                        FileName = fp,
                        UseShellExecute = true
                    };
                }
                else if (isShellPath || isBareAumid)
                {
                    string argument = isShellPath ? fp : $"shell:AppsFolder\\{fp}";
                    psi = new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = argument,
                        UseShellExecute = true
                    };
                }
                else
                {
                    psi = new ProcessStartInfo
                    {
                        FileName = fp,
                        Arguments = item.Arguments ?? string.Empty,
                        UseShellExecute = true
                    };

                    try
                    {
                        string? dir = Path.GetDirectoryName(fp);
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                            psi.WorkingDirectory = dir;
                    }
                    catch { }
                }

                Process.Start(psi);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Could not launch item:\n{ex.Message}", "ZeeVault Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void AddItem_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AddItemDialog { Owner = this };
            if (dialog.ShowDialog() == true && dialog.CreatedItem != null)
            {
                _libraryService.AddItem(dialog.CreatedItem);
                ApplyFilter();
            }
        }

        private void MenuLaunch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem mi && mi.Tag is VaultItem item)
            {
                LaunchItem(item);
            }
        }

        private void MenuOpenLocation_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem mi && mi.Tag is VaultItem item && !string.IsNullOrWhiteSpace(item.FilePath))
            {
                try
                {
                    if (File.Exists(item.FilePath) || Directory.Exists(item.FilePath))
                    {
                        Process.Start("explorer.exe", $"/select,\"{item.FilePath}\"");
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Could not open file location:\n{ex.Message}", "ZeeVault", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                }
            }
        }

        private void MenuRemove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem mi && mi.Tag is VaultItem item)
            {
                var res = System.Windows.MessageBox.Show($"Remove '{item.Title}' from ZeeVault?", "Confirm Remove", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
                if (res == System.Windows.MessageBoxResult.Yes)
                {
                    _libraryService.RemoveItem(item);
                    ApplyFilter();
                }
            }
        }

        private void MenuRename_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem mi && mi.Tag is VaultItem item)
            {
                var renameDialog = new System.Windows.Window
                {
                    Title = "Rename",
                    Width = 360,
                    Height = 150,
                    WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                    Owner = this,
                    ResizeMode = System.Windows.ResizeMode.NoResize,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(10, 9, 15)),
                };

                var stack = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
                var label = new System.Windows.Controls.TextBlock
                {
                    Text = "Enter new name:",
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 8),
                };
                var textBox = new System.Windows.Controls.TextBox
                {
                    Text = item.Title,
                    FontSize = 14,
                    Padding = new Thickness(8),
                    Margin = new Thickness(0, 0, 0, 12),
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 41, 59)),
                    Foreground = System.Windows.Media.Brushes.White,
                    BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 116, 139)),
                    BorderThickness = new Thickness(1),
                };
                textBox.GotFocus += (s3, e3) => textBox.SelectAll();
                var buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
                var okBtn = new System.Windows.Controls.Button
                {
                    Content = "OK",
                    Width = 70,
                    Height = 28,
                    Margin = new Thickness(0, 0, 8, 0),
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(99, 102, 241)),
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 12,
                    FontWeight = System.Windows.FontWeights.Bold,
                    Cursor = System.Windows.Input.Cursors.Hand,
                };
                var cancelBtn = new System.Windows.Controls.Button
                {
                    Content = "Cancel",
                    Width = 70,
                    Height = 28,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 65, 85)),
                    Foreground = System.Windows.Media.Brushes.LightGray,
                    FontSize = 12,
                    Cursor = System.Windows.Input.Cursors.Hand,
                };

                okBtn.Click += (s2, e2) =>
                {
                    string newName = textBox.Text.Trim();
                    if (!string.IsNullOrWhiteSpace(newName) && newName != item.Title)
                    {
                        item.Title = newName;
                        _libraryService.SaveLibrary();
                        ApplyFilter();
                    }
                    renameDialog.DialogResult = true;
                    renameDialog.Close();
                };
                cancelBtn.Click += (s2, e2) => { renameDialog.DialogResult = false; renameDialog.Close(); };

                textBox.KeyDown += (s2, e2) =>
                {
                    if (e2.Key == System.Windows.Input.Key.Enter)
                        okBtn.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    else if (e2.Key == System.Windows.Input.Key.Escape)
                        cancelBtn.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                };

                buttons.Children.Add(okBtn);
                buttons.Children.Add(cancelBtn);
                stack.Children.Add(label);
                stack.Children.Add(textBox);
                stack.Children.Add(buttons);
                renameDialog.Content = stack;
                renameDialog.ShowDialog();
            }
        }

        #region Start Menu Drag-and-Drop Support

        // Known folder GUIDs → real paths
        private static readonly Dictionary<string, string> KnownFolderGuids = new(StringComparer.OrdinalIgnoreCase)
        {
            ["{6D809377-6AF0-444B-8957-A3773F02200E}"] = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            ["{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}"] = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            ["{F38BF220-C3BE-11D1-BE5A-00C04FB92596}"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32"),
            ["{D65231B0-B2F1-48AB-BA8A-520DF719829A}"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Common Files"),
            ["{9274F77C-3B25-49DC-8285-496BC46B74E9}"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Common Files"),
        };

        private class StartMenuDropItem
        {
            public string Name { get; set; } = string.Empty;
            public string TargetPath { get; set; } = string.Empty;
            public string Category { get; set; } = "Games";
        }

        /// <summary>
        /// Resolves partial paths that use known folder GUIDs (e.g. {6D809377-...}\LGHUB\...) to real paths.
        /// </summary>
        private static string ResolvePartialPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;

            // Check if path starts with a known folder GUID
            foreach (var kv in KnownFolderGuids)
            {
                if (path.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
                {
                    string remainder = path.Substring(kv.Key.Length).TrimStart('\\');
                    string resolved = Path.Combine(kv.Value, remainder);
                    if (File.Exists(resolved) || Directory.Exists(resolved))
                        return resolved;
                }
            }

            return path;
        }

        private List<StartMenuDropItem> ExtractDropItems(DragEventArgs e)
        {
            var items = new List<StartMenuDropItem>();

            // 1. Try FileDrop (Win32 apps, shortcuts, .url files, and Store app AUMIDs)
            try
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                    if (files != null && files.Length > 0)
                    {
                        foreach (var f in files)
                        {
                            string resolved = ResolvePartialPath(f);

                            if (File.Exists(resolved) || Directory.Exists(resolved))
                            {
                                string ext = Path.GetExtension(resolved).ToLowerInvariant();
                                items.Add(new StartMenuDropItem
                                {
                                    Name = ResolveAppName(resolved),
                                    TargetPath = resolved,
                                    Category = (ext == ".exe" || ext == ".lnk") ? "Games" : "Files"
                                });
                            }
                            else if (f.StartsWith("steam://", StringComparison.OrdinalIgnoreCase) ||
                                     f.StartsWith("uplay://", StringComparison.OrdinalIgnoreCase) ||
                                     f.StartsWith("com.epicgames", StringComparison.OrdinalIgnoreCase))
                            {
                                items.Add(new StartMenuDropItem
                                {
                                    Name = ResolveAppName(f),
                                    TargetPath = f,
                                    Category = "Games"
                                });
                            }
                        }
                        if (items.Count > 0) return items;
                    }
                }
            }
            catch { }

            // 2. Text fallback
            try
            {
                if (e.Data.GetDataPresent(DataFormats.Text))
                {
                    string? text = e.Data.GetData(DataFormats.Text) as string;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        var results = WindowsAppSearchService.Search(text.Trim());
                        if (results.Count > 0)
                        {
                            items.Add(new StartMenuDropItem
                            {
                                Name = results[0].Name,
                                TargetPath = results[0].ExecutablePath,
                                Category = results[0].SuggestedCategory
                            });
                        }
                    }
                }
            }
            catch { }

            return items;
        }

        private string ResolveAppName(string path)
        {
            // 1. Try search index — gives "WhatsApp", "Antigravity", "Settings", etc.
            try
            {
                var searchResults = WindowsAppSearchService.Search(Path.GetFileNameWithoutExtension(path));
                var match = searchResults.FirstOrDefault(r =>
                    string.Equals(r.ExecutablePath, path, StringComparison.OrdinalIgnoreCase));
                if (match != null && !string.IsNullOrWhiteSpace(match.Name))
                    return match.Name;

                if (searchResults.Count > 0 && !string.IsNullOrWhiteSpace(searchResults[0].Name))
                    return searchResults[0].Name;
            }
            catch { }

            // 2. Try product version info from exe
            try
            {
                string productInfo = LibraryService.GetProductVersionInfo(path);
                if (!string.IsNullOrWhiteSpace(productInfo))
                    return productInfo;
            }
            catch { }

            // 3. Fallback to filename
            return Path.GetFileNameWithoutExtension(path);
        }

        #endregion

        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) ||
                e.Data.GetDataPresent("Shell ID List") ||
                e.Data.GetDataPresent(DataFormats.Text))
            {
                e.Effects = DragDropEffects.Copy;
                DropOverlay.Visibility = Visibility.Visible;
            }
        }

        private void Window_DragLeave(object sender, DragEventArgs e)
        {
            DropOverlay.Visibility = Visibility.Collapsed;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            DropOverlay.Visibility = Visibility.Collapsed;

            if ((DateTime.Now - _lastDropTime).TotalMilliseconds < 500) return;

            // Ignore internal card reorder drops
            if (e.Data.GetDataPresent("VaultItem")) return;

            // 1. FileDrop — handles both regular files AND Store app AUMIDs
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null)
                {
                    foreach (string filePath in files)
                    {
                        // Resolve partial paths with known folder GUIDs
                        string resolved = ResolvePartialPath(filePath);

                        if (File.Exists(resolved) || Directory.Exists(resolved))
                        {
                            // Regular file — show Add confirmation popup
                            string ext = Path.GetExtension(resolved).ToLowerInvariant();
                            string category = (ext == ".exe" || ext == ".lnk") ? "Games" : "Files";
                            string title = ResolveAppName(resolved);

                            _selectedSearchResult = new WindowsAppSearchResult
                            {
                                Name = title,
                                ExecutablePath = resolved,
                                Source = "Start Menu",
                                Icon = null
                            };
                            _selectedSearchResult.Icon = IconExtractor.GetIconForFile(resolved);

                            PopupAppName.Text = title;
                            PopupAppPath.Text = resolved;
                            PopupAppIcon.Source = _selectedSearchResult.Icon;
                            PopupTitleInput.Text = title;

                            if (ext == ".exe" || ext == ".lnk") PopupCatGames.IsChecked = true;
                            else PopupCatFiles.IsChecked = true;

                            AddFromSearchPopup.Visibility = Visibility.Visible;
                            return;
                        }
                        else if (resolved.StartsWith("steam://", StringComparison.OrdinalIgnoreCase) ||
                                 resolved.StartsWith("uplay://", StringComparison.OrdinalIgnoreCase) ||
                                 resolved.StartsWith("com.epicgames", StringComparison.OrdinalIgnoreCase))
                        {
                            // Protocol URL — show Add confirmation popup
                            string title = ResolveAppName(resolved);

                            _selectedSearchResult = new WindowsAppSearchResult
                            {
                                Name = title,
                                ExecutablePath = resolved,
                                Source = "Start Menu",
                                Icon = null
                            };
                            _selectedSearchResult.Icon = IconExtractor.GetIconForFile(resolved);

                            PopupAppName.Text = title;
                            PopupAppPath.Text = resolved;
                            PopupAppIcon.Source = _selectedSearchResult.Icon;
                            PopupTitleInput.Text = title;
                            PopupCatGames.IsChecked = true;

                            AddFromSearchPopup.Visibility = Visibility.Visible;
                            return;
                        }
                        else
                        {
                            // Likely a Store app AUMID — show Add confirmation popup
                            string name = ResolveAppNameFromAumid(filePath);
                            _selectedSearchResult = new WindowsAppSearchResult
                            {
                                Name = name,
                                ExecutablePath = filePath,
                                Source = "Start Menu",
                                Icon = null
                            };
                            _selectedSearchResult.Icon = IconExtractor.GetIconForFile(filePath);

                            PopupAppName.Text = name;
                            PopupAppPath.Text = filePath;
                            PopupAppIcon.Source = _selectedSearchResult.Icon;
                            PopupTitleInput.Text = name;
                            PopupCatTools.IsChecked = true;

                            AddFromSearchPopup.Visibility = Visibility.Visible;
                            return;
                        }
                    }
                    ApplyFilter();
                }
                return;
            }

            // 2. Shell IDList Array — fallback
            var dropItems = ExtractDropItems(e);
            if (dropItems.Count > 0)
            {
                foreach (var dropItem in dropItems)
                {
                    if (_libraryService.Items.Any(i =>
                        string.Equals(i.FilePath, dropItem.TargetPath, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    _libraryService.AddItem(new VaultItem
                    {
                        Title = dropItem.Name,
                        FilePath = dropItem.TargetPath,
                        Category = dropItem.Category,
                        DateAdded = DateTime.Now
                    });
                }
                ApplyFilter();
                _lastDropTime = DateTime.Now;
                return;
            }

            // 3. Unsupported
            System.Windows.MessageBox.Show(
                "Could not add this item. Please drag the file directly or use the + Add button.",
                "ZeeVault", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        /// <summary>
        /// Resolves a friendly name for a Store app AUMID by searching the app index.
        /// </summary>
        private string ResolveAppNameFromAumid(string aumidOrPath)
        {
            // Extract a searchable name from the AUMID
            // e.g. "5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App" -> search "WhatsAppDesktop" or "WhatsApp"
            try
            {
                // Try the full search index — match by path
                var results = WindowsAppSearchService.Search(Path.GetFileNameWithoutExtension(aumidOrPath));
                if (results.Count > 0)
                    return results[0].Name;

                // Try searching with parts of the AUMID
                string searchName = aumidOrPath;
                int dotIdx = searchName.IndexOf('.');
                if (dotIdx > 0) searchName = searchName.Substring(dotIdx + 1);
                int underscoreIdx = searchName.IndexOf('_');
                if (underscoreIdx > 0) searchName = searchName.Substring(0, underscoreIdx);

                if (searchName.Length > 2)
                {
                    results = WindowsAppSearchService.Search(searchName);
                    if (results.Count > 0)
                        return results[0].Name;
                }
            }
            catch { }

            // Fallback: clean up the AUMID for display
            string clean = aumidOrPath;
            int bangIdx = clean.IndexOf('!');
            if (bangIdx > 0) clean = clean.Substring(0, bangIdx);
            int udx = clean.IndexOf('_');
            if (udx > 0) clean = clean.Substring(0, udx);
            int dotIdx2 = clean.IndexOf('.');
            if (dotIdx2 > 0) clean = clean.Substring(dotIdx2 + 1);

            return clean;
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
        {
            WindowState = System.Windows.WindowState.Minimized;
        }

        private void MaximizeBtn_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == System.Windows.WindowState.Maximized
                ? System.Windows.WindowState.Normal
                : System.Windows.WindowState.Maximized;
        }

        private void SettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            SettingsPopup.IsOpen = !SettingsPopup.IsOpen;
        }

        private void MainWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (SettingsPopup != null && SettingsPopup.IsOpen)
            {
                var pos = e.GetPosition(this);
                var popupBounds = SettingsPopupBorder?.TranslatePoint(new Point(0, 0), this);
                var btnBounds = SettingsBtn?.TranslatePoint(new Point(0, 0), this);

                bool clickedInsidePopup = popupBounds.HasValue &&
                    pos.X >= popupBounds.Value.X && pos.X <= popupBounds.Value.X + (SettingsPopupBorder?.ActualWidth ?? 0) &&
                    pos.Y >= popupBounds.Value.Y && pos.Y <= popupBounds.Value.Y + (SettingsPopupBorder?.ActualHeight ?? 0);

                bool clickedOnButton = btnBounds.HasValue &&
                    pos.X >= btnBounds.Value.X && pos.X <= btnBounds.Value.X + (SettingsBtn?.ActualWidth ?? 0) &&
                    pos.Y >= btnBounds.Value.Y && pos.Y <= btnBounds.Value.Y + (SettingsBtn?.ActualHeight ?? 0);

                if (!clickedInsidePopup && !clickedOnButton)
                {
                    SettingsPopup.IsOpen = false;
                }
            }
        }

        private void SettingsPopup_Open(object sender, EventArgs e)
        {
            var scale = SettingsPopupBorder.RenderTransform as ScaleTransform;
            if (scale == null) return;
            scale.ScaleY = 0;
            var anim = new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }

        private void SettingsPopup_Closed(object sender, EventArgs e)
        {
            // Reset back to menu items when popup closes
            if (SettingsMenuItems != null && AboutInlinePanel != null)
            {
                SettingsMenuItems.Visibility = Visibility.Visible;
                AboutInlinePanel.Visibility = Visibility.Collapsed;
            }

            // Hide layout submenu popup
            if (LayoutSubmenuPopup != null)
                LayoutSubmenuPopup.IsOpen = false;
        }

        private string GetCurrentVersion()
        {
            try
            {
                string versionPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "version.txt");
                if (File.Exists(versionPath))
                    return File.ReadAllText(versionPath).Trim();
            }
            catch { }
            return "1.0.0";
        }

        private async void CheckUpdate_Click(object sender, MouseButtonEventArgs e)
        {
            UpdateStatusText.Text = "Checking for updates...";
            SettingsPopup.IsOpen = false;

            try
            {
                string currentVersion = GetCurrentVersion();
                using var http = new System.Net.Http.HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("ZeeVault-Updater");
                var response = await http.GetStringAsync("https://api.github.com/repos/charlotte-zee/ZeeVault/releases/latest");

                // Simple JSON parse for tag_name
                int tagIdx = response.IndexOf("\"tag_name\"");
                if (tagIdx < 0) { UpdateStatusText.Text = "Could not check for updates."; return; }
                int colonIdx = response.IndexOf(':', tagIdx);
                int quoteStart = response.IndexOf('"', colonIdx + 1);
                int quoteEnd = response.IndexOf('"', quoteStart + 1);
                string latestTag = response.Substring(quoteStart + 1, quoteEnd - quoteStart - 1).TrimStart('v');

                if (latestTag == currentVersion)
                {
                    UpdateStatusText.Text = "You're running the latest version.";
                    System.Windows.MessageBox.Show(
                        $"You're already running the latest version (v{currentVersion}).",
                        "ZeeVault", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                else
                {
                    var result = System.Windows.MessageBox.Show(
                        $"A new version (v{latestTag}) is available!\n\nYou're running v{currentVersion}.\n\nDownload now?",
                        "Update Available", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Information);

                    if (result == System.Windows.MessageBoxResult.Yes)
                    {
                        // Find the setup exe download URL
                        int assetsIdx = response.IndexOf("\"browser_download_url\"");
                        if (assetsIdx >= 0)
                        {
                            int aColon = response.IndexOf(':', assetsIdx);
                            int aStart = response.IndexOf('"', aColon + 1);
                            int aEnd = response.IndexOf('"', aStart + 1);
                            string downloadUrl = response.Substring(aStart + 1, aEnd - aStart - 1);
                            Process.Start(new ProcessStartInfo(downloadUrl) { UseShellExecute = true });
                        }
                        else
                        {
                            Process.Start(new ProcessStartInfo($"https://github.com/charlotte-zee/ZeeVault/releases/tag/v{latestTag}") { UseShellExecute = true });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatusText.Text = "Could not check for updates.";
                System.Windows.MessageBox.Show(
                    $"Could not check for updates:\n{ex.Message}",
                    "ZeeVault", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        private void About_Click(object sender, MouseButtonEventArgs e)
        {
            SettingsMenuItems.Visibility = Visibility.Collapsed;
            AboutInlinePanel.Visibility = Visibility.Visible;
            AboutInlineVersionText.Text = $"Version {GetCurrentVersion()}";
        }

        private void AboutBack_Click(object sender, RoutedEventArgs e)
        {
            SettingsMenuItems.Visibility = Visibility.Visible;
            AboutInlinePanel.Visibility = Visibility.Collapsed;
        }

        private void AboutGitHub_Click(object sender, RoutedEventArgs e)
        {
            SettingsPopup.IsOpen = false;
            Process.Start(new ProcessStartInfo("https://github.com/charlotte-zee/ZeeVault") { UseShellExecute = true });
        }

        #region Layout Switching

        private void LayoutMenu_Enter(object sender, MouseEventArgs e)
        {
            if (LayoutSubmenuPopup != null)
                LayoutSubmenuPopup.IsOpen = true;
        }

        private void LayoutMenu_Leave(object sender, MouseEventArgs e)
        {
            // Hide submenu after a small delay so clicking submenu items works
            Dispatcher.InvokeAsync(async () =>
            {
                await System.Threading.Tasks.Task.Delay(200);
                if (!LayoutMenuItem.IsMouseOver && !LayoutSubmenuPopup.IsMouseOver)
                {
                    if (LayoutSubmenuPopup != null)
                        LayoutSubmenuPopup.IsOpen = false;
                }
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void LayoutSubmenu_Enter(object sender, MouseEventArgs e)
        {
            // Keep submenu visible when hovering over it
        }

        private void LayoutSubmenu_Leave(object sender, MouseEventArgs e)
        {
            // Hide submenu when leaving it
            Dispatcher.InvokeAsync(async () =>
            {
                await System.Threading.Tasks.Task.Delay(200);
                if (!LayoutMenuItem.IsMouseOver && !LayoutSubmenuPopup.IsMouseOver)
                {
                    if (LayoutSubmenuPopup != null)
                        LayoutSubmenuPopup.IsOpen = false;
                }
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void LayoutClean_Click(object sender, MouseButtonEventArgs e)
        {
            _currentLayout = "clean";
            ApplyLayout();
            SaveLayoutSetting();
            LayoutSubmenuPopup.IsOpen = false;
            SettingsPopup.IsOpen = false;
        }

        private void LayoutCards_Click(object sender, MouseButtonEventArgs e)
        {
            _currentLayout = "cards";
            ApplyLayout();
            SaveLayoutSetting();
            LayoutSubmenuPopup.IsOpen = false;
            SettingsPopup.IsOpen = false;
        }

        private void ApplyLayout()
        {
            if (_currentLayout == "clean")
            {
                ItemsGrid.ItemTemplate = (DataTemplate)FindResource("CleanItemTemplate");
                LayoutCleanCheck.Visibility = Visibility.Visible;
                LayoutCardsCheck.Visibility = Visibility.Collapsed;
            }
            else
            {
                ItemsGrid.ItemTemplate = (DataTemplate)FindResource("CardsItemTemplate");
                LayoutCleanCheck.Visibility = Visibility.Collapsed;
                LayoutCardsCheck.Visibility = Visibility.Visible;
            }
        }

        private string _settingsPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ZeeVault", "settings.json");

        private void SaveLayoutSetting()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(_settingsPath)!;
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                System.IO.File.WriteAllText(_settingsPath, $"{{\"layout\":\"{_currentLayout}\"}}");
            }
            catch { }
        }

        private string LoadLayoutSetting()
        {
            try
            {
                if (System.IO.File.Exists(_settingsPath))
                {
                    string json = System.IO.File.ReadAllText(_settingsPath);
                    int idx = json.IndexOf("\"layout\"");
                    if (idx >= 0)
                    {
                        int colon = json.IndexOf(':', idx);
                        int start = json.IndexOf('"', colon + 1);
                        int end = json.IndexOf('"', start + 1);
                        return json.Substring(start + 1, end - start - 1);
                    }
                }
            }
            catch { }
            return "clean";
        }

        #endregion

        #region Auto-Update System

        private string _latestVersion = string.Empty;
        private string _latestDownloadUrl = string.Empty;
        private string _downloadedInstallerPath = string.Empty;
        private System.Net.Http.HttpClient? _updateHttpClient;
        private CancellationTokenSource? _downloadCts;

        private async Task CheckForUpdateOnStartup()
        {
            try
            {
                await Task.Delay(2000); // Small delay so UI loads first

                _updateHttpClient = new System.Net.Http.HttpClient();
                _updateHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ZeeVault-Updater");
                var response = await _updateHttpClient.GetStringAsync("https://api.github.com/repos/charlotte-zee/ZeeVault/releases/latest");

                int tagIdx = response.IndexOf("\"tag_name\"");
                if (tagIdx < 0) return;
                int colonIdx = response.IndexOf(':', tagIdx);
                int quoteStart = response.IndexOf('"', colonIdx + 1);
                int quoteEnd = response.IndexOf('"', quoteStart + 1);
                string latestTag = response.Substring(quoteStart + 1, quoteEnd - quoteStart - 1).TrimStart('v');

                string currentVersion = GetCurrentVersion();
                if (latestTag == currentVersion) return;

                // New version found — extract download URL
                _latestVersion = latestTag;
                int assetsIdx = response.IndexOf("\"browser_download_url\"");
                if (assetsIdx >= 0)
                {
                    int aColon = response.IndexOf(':', assetsIdx);
                    int aStart = response.IndexOf('"', aColon + 1);
                    int aEnd = response.IndexOf('"', aStart + 1);
                    _latestDownloadUrl = response.Substring(aStart + 1, aEnd - aStart - 1);
                }

                Dispatcher.Invoke(() =>
                {
                    UpdateBannerText.Text = $"Update available: v{latestTag} (you have v{currentVersion})";
                    UpdateBanner.Visibility = Visibility.Visible;
                });
            }
            catch
            {
                // Silently fail — user can still check manually from settings
            }
        }

        private async void UpdateDownload_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_latestDownloadUrl)) return;

            UpdateBanner.Visibility = Visibility.Collapsed;
            UpdateProgressBorder.Visibility = Visibility.Visible;
            UpdateProgressText.Text = "Downloading update...";

            try
            {
                _downloadCts = new CancellationTokenSource();
                string tempDir = Path.Combine(Path.GetTempPath(), "ZeeVault_Update");
                Directory.CreateDirectory(tempDir);
                _downloadedInstallerPath = Path.Combine(tempDir, "ZeeVault-Setup.exe");

                using var response = await _updateHttpClient!.GetAsync(_latestDownloadUrl, HttpCompletionOption.ResponseHeadersRead, _downloadCts.Token);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var totalRead = 0L;
                var buffer = new byte[8192];

                using var contentStream = await response.Content.ReadAsStreamAsync(_downloadCts.Token);
                using var fileStream = new FileStream(_downloadedInstallerPath, FileMode.Create, FileAccess.Write, FileShare.None);

                int bytesRead;
                while ((bytesRead = await contentStream.ReadAsync(buffer, _downloadCts.Token)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), _downloadCts.Token);
                    totalRead += bytesRead;

                    Dispatcher.Invoke(() =>
                    {
                        if (totalBytes > 0)
                        {
                            int percent = (int)(totalRead * 100 / totalBytes);
                            UpdateProgressText.Text = $"Downloading update... {percent}%";
                        }
                        else
                        {
                            UpdateProgressText.Text = $"Downloading update... {totalRead / 1024:N0} KB";
                        }
                    });
                }

                Dispatcher.Invoke(() =>
                {
                    UpdateProgressBorder.Visibility = Visibility.Collapsed;
                    InstallBanner.Visibility = Visibility.Visible;
                });
            }
            catch (OperationCanceledException)
            {
                Dispatcher.Invoke(() =>
                {
                    UpdateProgressBorder.Visibility = Visibility.Collapsed;
                });
            }
            catch (Exception)
            {
                Dispatcher.Invoke(() =>
                {
                    UpdateProgressBorder.Visibility = Visibility.Collapsed;
                    UpdateBannerText.Text = "Download failed. Try again.";
                    UpdateBanner.Visibility = Visibility.Visible;
                });
            }
        }

        private void UpdateCancel_Click(object sender, RoutedEventArgs e)
        {
            _downloadCts?.Cancel();
        }

        private void UpdateInstall_Click(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(_downloadedInstallerPath)) return;

            try
            {
                // Run installer with UAC elevation
                Process.Start(new ProcessStartInfo
                {
                    FileName = _downloadedInstallerPath,
                    Verb = "runas",
                    UseShellExecute = true
                });

                // Close ZeeVault so installer can overwrite files
                Dispatcher.Invoke(() =>
                {
                    Application.Current.Shutdown();
                });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // User clicked No on UAC prompt — do nothing
            }
        }

        private void UpdateDismiss_Click(object sender, RoutedEventArgs e)
        {
            UpdateBanner.Visibility = Visibility.Collapsed;
            InstallBanner.Visibility = Visibility.Collapsed;
        }

        #endregion
    }
}
