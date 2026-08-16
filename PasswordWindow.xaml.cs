using System;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace ZeeVault
{
    public class PasswordWindow : Window
    {
        private PasswordBox _passBox = null!;
        private TextBlock _errorText = null!;
        private Border _statusBorder = null!;
        private TextBlock _roastText = null!;
        private TextBlock _hintText = null!;
        private Border _hintPanel = null!;
        private string _passwordHash;
        private string _passwordHint;

        private static readonly Color AccentColor = (Color)ColorConverter.ConvertFromString("#6366F1");
        private static readonly Color AccentHover = (Color)ColorConverter.ConvertFromString("#818CF8");
        private static readonly Color TextGray = (Color)ColorConverter.ConvertFromString("#666666");
        private static readonly Color TextLight = (Color)ColorConverter.ConvertFromString("#F0F0F0");
        private static readonly Color BgDark = (Color)ColorConverter.ConvertFromString("#0B0B0C");
        private static readonly Color BgField = (Color)ColorConverter.ConvertFromString("#1A1A1A");

        public bool IsAuthenticated { get; private set; } = false;

        private static readonly string[] _roastComments = new[]
        {
            "Locked out of your own vault. Impressive.",
            "Perhaps a password manager next time?",
            "The irony of forgetting the password to your own security.",
            "Your memory called. It wants a word.",
            "Security so good, even you can't get in.",
            "Maybe the real password was the friends we made along the way.",
            "Schrödinger's password — it exists and doesn't exist simultaneously.",
            "Plot twist: You set this password yourself.",
            "Your vault is more secure than Fort Knox. Congrats.",
            "At least you're consistent. Consistently forgetful."
        };

        public PasswordWindow(string passwordHash, string passwordHint)
        {
            _passwordHash = passwordHash;
            _passwordHint = passwordHint;

            Title = "ZeeVault - Locked";
            Width = 420;
            Height = 420;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = true;
            Topmost = true;

            BuildUI();
        }

        private void BuildUI()
        {
            var outerBorder = new Border
            {
                Background = new SolidColorBrush(BgDark),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1FFFFFFF")),
                BorderThickness = new Thickness(1, 1, 1, 1),
                CornerRadius = new CornerRadius(18),
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Header (draggable)
            var header = new Border
            {
                Padding = new Thickness(28, 28, 28, 0),
                Cursor = Cursors.Hand
            };
            header.MouseLeftButtonDown += (s, e) => { if (e.ChangedButton == MouseButton.Left) DragMove(); };

            var headerGrid = new Grid();
            var headerStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            headerStack.Children.Add(new TextBlock
            {
                Text = "ZeeVault",
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(AccentColor),
                FontFamily = new FontFamily("Segoe UI")
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = "Enter your password to continue",
                Foreground = new SolidColorBrush(TextGray),
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 0)
            });

            var closeBtn = new Button
            {
                Content = "X",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Width = 28,
                Height = 28,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(TextGray),
                BorderThickness = new Thickness(0, 0, 0, 0),
                Cursor = Cursors.Hand,
            };
            closeBtn.Click += (s, e) => { IsAuthenticated = false; Close(); };
            closeBtn.Template = CreateCloseButtonTemplate();

            headerGrid.Children.Add(headerStack);
            headerGrid.Children.Add(closeBtn);
            header.Child = headerGrid;
            Grid.SetRow(header, 0);

            // Body
            var body = new StackPanel { Margin = new Thickness(28, 20, 28, 28) };

            // Error message
            _statusBorder = new Border
            {
                Background = new SolidColorBrush(BgField),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 14, 14, 14),
                Margin = new Thickness(0, 0, 0, 16),
                Visibility = Visibility.Collapsed
            };
            _errorText = new TextBlock
            {
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            _statusBorder.Child = _errorText;
            body.Children.Add(_statusBorder);

            // Password label
            body.Children.Add(new TextBlock
            {
                Text = "Password",
                Foreground = new SolidColorBrush(TextGray),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 6)
            });

            // Password field (clean like Kiwi Key)
            var passFieldBorder = new Border
            {
                Background = new SolidColorBrush(BgField),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22FFFFFF")),
                BorderThickness = new Thickness(1, 1, 1, 1),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 0, 0, 20)
            };

            _passBox = new PasswordBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0, 0, 0, 0),
                Foreground = new SolidColorBrush(TextLight),
                FontSize = 14,
                Padding = new Thickness(12, 10, 12, 10),
                CaretBrush = new SolidColorBrush(TextLight),
                MaxLength = 64,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _passBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) TryUnlock(); };
            passFieldBorder.Child = _passBox;
            body.Children.Add(passFieldBorder);

            // Unlock button
            var unlockBtn = new Button
            {
                Content = "Unlock",
                Padding = new Thickness(0, 12, 0, 12),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(AccentColor),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0, 0, 0, 0),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            unlockBtn.Click += (s, e) => TryUnlock();
            unlockBtn.Template = CreateButtonTemplate(AccentColor, AccentHover);
            body.Children.Add(unlockBtn);

            // Forgot password
            var forgotBtn = new Button
            {
                Content = "Forgot Password?",
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(TextGray),
                FontSize = 12,
                BorderThickness = new Thickness(0, 0, 0, 0),
                Cursor = Cursors.Hand,
                Padding = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            forgotBtn.Click += (s, e) => ShowHint();
            body.Children.Add(forgotBtn);

            // Hint panel
            _hintPanel = new Border
            {
                Background = new SolidColorBrush(BgField),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(0, 10, 0, 0),
                Visibility = Visibility.Collapsed
            };

            var hintStack = new StackPanel();
            _roastText = new TextBlock
            {
                Text = "",
                Foreground = new SolidColorBrush(TextLight),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };
            hintStack.Children.Add(_roastText);

            hintStack.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22FFFFFF")),
                Margin = new Thickness(0, 0, 0, 10)
            });

            hintStack.Children.Add(new TextBlock
            {
                Text = "HINT",
                Foreground = new SolidColorBrush(TextGray),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            });

            _hintText = new TextBlock
            {
                Text = "",
                Foreground = new SolidColorBrush(AccentColor),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            hintStack.Children.Add(_hintText);

            _hintPanel.Child = hintStack;
            body.Children.Add(_hintPanel);

            var bodyBorder = new Border { Child = body };
            Grid.SetRow(bodyBorder, 1);

            mainGrid.Children.Add(header);
            mainGrid.Children.Add(bodyBorder);
            outerBorder.Child = mainGrid;
            Content = outerBorder;
        }

        private ControlTemplate CreateButtonTemplate(Color normalColor, Color hoverColor)
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Bd";
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);

            var trigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            trigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(hoverColor), "Bd"));

            template.VisualTree = border;
            template.Triggers.Add(trigger);
            return template;
        }

        private ControlTemplate CreateCloseButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Bd";
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
            border.SetValue(Border.PaddingProperty, new Thickness(0, 0, 0, 0));

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);

            var trigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            trigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#33FF3B30")), "Bd"));
            trigger.Setters.Add(new Setter(Button.ForegroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF3B30"))));

            template.VisualTree = border;
            template.Triggers.Add(trigger);
            return template;
        }

        private void TryUnlock()
        {
            string entered = _passBox.Password.Trim();
            if (string.IsNullOrEmpty(entered)) return;

            if (HashPassword(entered) == _passwordHash)
            {
                IsAuthenticated = true;
                Close();
            }
            else
            {
                _errorText.Text = "Wrong password. Try again.";
                _statusBorder.Visibility = Visibility.Visible;
                _passBox.Password = string.Empty;
                _passBox.Focus();
            }
        }

        private void ShowHint()
        {
            var random = new Random();
            _roastText.Text = _roastComments[random.Next(_roastComments.Length)];
            _hintText.Text = _passwordHint;
            _hintPanel.Visibility = Visibility.Visible;

            // Auto-extend window height with animation
            var anim = new DoubleAnimation(520, TimeSpan.FromMilliseconds(200));
            anim.EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            this.BeginAnimation(HeightProperty, anim);
        }

        private static string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            byte[] bytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(bytes);
        }
    }
}
