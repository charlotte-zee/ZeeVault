using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ZeeVault.Models;
using ZeeVault.Services;

namespace ZeeVault.Dialogs
{
    public partial class AddItemDialog : Window
    {
        public VaultItem? CreatedItem { get; private set; }

        public AddItemDialog(string? initialPath = null)
        {
            InitializeComponent();
            GlassHelper.EnableGlass(this);

            if (!string.IsNullOrWhiteSpace(initialPath))
            {
                TxtFilePath.Text = initialPath;
            }
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select Game, Executable or File",
                Filter = "All Supported (*.exe;*.lnk;*.doc*;*.txt;*.zip;*.*)|*.exe;*.lnk;*.doc*;*.txt;*.zip;*.*|Executables (*.exe)|*.exe|Shortcuts (*.lnk)|*.lnk|All Files (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                TxtFilePath.Text = openFileDialog.FileName;
            }
        }

        private void TxtFilePath_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtTitle.Text) && File.Exists(TxtFilePath.Text))
            {
                TxtTitle.Text = Path.GetFileNameWithoutExtension(TxtFilePath.Text);
                
                var ext = Path.GetExtension(TxtFilePath.Text).ToLowerInvariant();
                if (ext == ".exe" || ext == ".lnk")
                    CatGames.IsChecked = true;
                else
                    CatFiles.IsChecked = true;
            }
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtFilePath.Text) || string.IsNullOrWhiteSpace(TxtTitle.Text))
            {
                MessageBox.Show("Please specify both a file path and a title.", "ZeeVault", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string selectedCategory = CatTools.IsChecked == true ? "Tools"
                                    : CatFiles.IsChecked == true ? "Files"
                                    : "Games";

            CreatedItem = new VaultItem
            {
                Title = TxtTitle.Text.Trim(),
                FilePath = TxtFilePath.Text.Trim(),
                Category = selectedCategory,
                DateAdded = DateTime.Now
            };

            DialogResult = true;
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
