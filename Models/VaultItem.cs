using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace GameVault.Models
{
    public class VaultItem : INotifyPropertyChanged
    {
        private string _id = Guid.NewGuid().ToString();
        private string _title = string.Empty;
        private string _filePath = string.Empty;
        private string _arguments = string.Empty;
        private string _category = "Games"; // Games, Tools, Files
        private DateTime _dateAdded = DateTime.Now;
        private bool _isAutoDetected = false;
        private ImageSource? _iconSource;
        private string _customIconPath = string.Empty;

        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); }
        }

        public string FilePath
        {
            get => _filePath;
            set
            {
                string val = value ?? string.Empty;
                // Heal: bare AUMID paths (no drive letter, no slashes) -> prefix with shell:AppsFolder\
                bool alreadyShell = val.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);
                bool isRooted = val.Length > 1 && (val[1] == ':' || val.StartsWith(@"\\"));
                bool hasSlash = val.Contains('\\') || val.Contains('/');
                if (!alreadyShell && !isRooted && !hasSlash && val.Length > 2)
                {
                    val = "shell:AppsFolder\\" + val;
                }
                _filePath = val;
                OnPropertyChanged();
            }
        }

        public string Arguments
        {
            get => _arguments;
            set { _arguments = value; OnPropertyChanged(); }
        }

        public string Category
        {
            get => _category;
            set { _category = value; OnPropertyChanged(); }
        }

        public DateTime DateAdded
        {
            get => _dateAdded;
            set { _dateAdded = value; OnPropertyChanged(); }
        }

        public bool IsAutoDetected
        {
            get => _isAutoDetected;
            set { _isAutoDetected = value; OnPropertyChanged(); }
        }

        [JsonIgnore]
        public ImageSource? IconSource
        {
            get => _iconSource;
            set { _iconSource = value; OnPropertyChanged(); }
        }

        /// <summary>Full path to a user-chosen icon file (.ico/.png/.exe). Persisted to JSON.</summary>
        public string CustomIconPath
        {
            get => _customIconPath;
            set { _customIconPath = value ?? string.Empty; OnPropertyChanged(); }
        }

        [JsonIgnore]
        public string ExtensionDisplay
        {
            get
            {
                if (string.IsNullOrWhiteSpace(FilePath)) return "";
                string cleanPath = FilePath.Trim();

                // Shell virtual paths and anything containing ! -> APP
                if (cleanPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || cleanPath.Contains("!"))
                    return "APP";

                // Bare AUMID (no drive letter, no slashes) -> APP
                bool isRooted = cleanPath.Length > 1 && (cleanPath[1] == ':' || cleanPath.StartsWith(@"\\"));
                if (!isRooted && !cleanPath.Contains('\\') && !cleanPath.Contains('/'))
                    return "APP";

                int dashDash = cleanPath.IndexOf(" --", System.StringComparison.OrdinalIgnoreCase);
                if (dashDash > 0) cleanPath = cleanPath.Substring(0, dashDash);
                cleanPath = cleanPath.Trim().Trim('"');

                var ext = System.IO.Path.GetExtension(cleanPath);
                if (string.IsNullOrEmpty(ext) || ext.Length > 5)
                    return "APP";

                return ext.TrimStart('.').ToUpperInvariant();
            }
        }

        [JsonIgnore]
        public string CategoryIcon => Category switch
        {
            "Games" => "🎮",
            "Tools" => "🛠",
            "Files" => "📂",
            _ => "📌"
        };

        [JsonIgnore]
        public string CategoryColor => Category switch
        {
            "Games" => "#CC7C3AED",   // violet
            "Tools" => "#CC0EA5E9",   // sky blue
            "Files" => "#CC10B981",   // emerald
            _ => "#CC6366F1"          // indigo default
        };

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
