using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GameVault.Dialogs;
using GameVault.Models;
using GameVault.Services;
using Wpf.Ui.Controls;

namespace GameVault
{
    public partial class MainWindow : FluentWindow
    {
        private readonly LibraryService _libraryService;
        private string _activeCategory = "All";

        public MainWindow()
        {
            InitializeComponent();
            _libraryService = new LibraryService();
            ApplyFilter();
            UpdateGridColumns();
            Loaded += MainWindow_Loaded;
            LocationChanged += MainWindow_LocationChanged;
        }

        private void MainWindow_LocationChanged(object? sender, EventArgs e)
        {
            if (SearchDropdown != null && SearchDropdown.IsOpen)
            {
                var offset = SearchDropdown.HorizontalOffset;
                SearchDropdown.HorizontalOffset = offset + 0.0001;
                SearchDropdown.HorizontalOffset = offset;
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
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateGridColumns();
        }

        private void UpdateGridColumns()
        {
            if (ItemsGrid == null) return;

            double availableWidth = ActualWidth - 50;
            int cardMinWidth = 165;
            int columns = Math.Max(1, Math.Min(6, (int)(availableWidth / cardMinWidth)));

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
        }

        private Point _dragStartPoint;
        private bool _isDraggingCard = false;

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
                LaunchItem(item);
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

        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
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

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (string filePath in files)
                {
                    if (File.Exists(filePath) || Directory.Exists(filePath))
                    {
                        string ext = Path.GetExtension(filePath).ToLowerInvariant();
                        string category = (ext == ".exe" || ext == ".lnk") ? "Games" : "Files";

                        var item = new VaultItem
                        {
                            Title = Path.GetFileNameWithoutExtension(filePath),
                            FilePath = filePath,
                            Category = category,
                            DateAdded = DateTime.Now
                        };
                        _libraryService.AddItem(item);
                    }
                }
                ApplyFilter();
            }
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
        }
    }
}
