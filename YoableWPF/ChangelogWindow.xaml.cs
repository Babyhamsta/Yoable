using System;
using System.Windows;
using System.Windows.Controls;

namespace YoableWPF
{
    public partial class ChangelogWindow : Window
    {
        private bool isUpdatePrompt = false;

        // Constructor for post-update changelog viewing
        public ChangelogWindow(string version, string changelog)
        {
            InitializeComponent();
            VersionTitle.Text = $"What's New in {version}";
            ParseChangelog(changelog);
            SetupForViewing();
        }

        // Constructor for pre-update decision
        public ChangelogWindow(string version, string changelog, bool showUpdateButtons)
        {
            InitializeComponent();
            isUpdatePrompt = showUpdateButtons;
            VersionTitle.Text = $"Update Available: {version}";
            ParseChangelog(changelog);

            if (showUpdateButtons)
            {
                SetupForUpdatePrompt();
            }
            else
            {
                SetupForViewing();
            }
        }

        private void SetupForUpdatePrompt()
        {
            // Show update buttons, hide close button
            UpdateButton.Visibility = Visibility.Visible;
            NotNowButton.Visibility = Visibility.Visible;
            CloseButton.Visibility = Visibility.Collapsed;
        }

        private void SetupForViewing()
        {
            // Show close button, hide update buttons
            UpdateButton.Visibility = Visibility.Collapsed;
            NotNowButton.Visibility = Visibility.Collapsed;
            CloseButton.Visibility = Visibility.Visible;
        }

        private void ParseChangelog(string changelog)
        {
            if (ChangelogStackPanel == null) return;

            ChangelogStackPanel.Children.Clear();
            var sections = changelog.Split(new[] { "##" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var section in sections)
            {
                if (string.IsNullOrWhiteSpace(section)) continue;

                var lines = section.Trim().Split('\n');
                if (lines.Length == 0) continue;

                // Section header
                var header = new TextBlock
                {
                    Text = lines[0].Trim(),
                    Style = (Style)FindResource("SectionHeader")
                };
                ChangelogStackPanel.Children.Add(header);

                // Process bullet points
                for (int i = 1; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    if (line.StartsWith("-"))
                    {
                        var bulletPanel = new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Margin = new Thickness(20, 0, 0, 8)
                        };

                        var bullet = new TextBlock
                        {
                            Text = "•  ",
                            FontSize = 13,
                            VerticalAlignment = VerticalAlignment.Top,
                            Margin = new Thickness(0, 0, 5, 0)
                        };

                        var content = new TextBlock
                        {
                            Text = line.TrimStart('-', ' '),
                            FontSize = 13,
                            TextWrapping = TextWrapping.Wrap,
                            VerticalAlignment = VerticalAlignment.Top
                        };

                        bulletPanel.Children.Add(bullet);
                        bulletPanel.Children.Add(content);
                        ChangelogStackPanel.Children.Add(bulletPanel);
                    }
                }
            }
        }

        private void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            // User chose to update
            DialogResult = true;
            Close();
        }

        private void NotNowButton_Click(object sender, RoutedEventArgs e)
        {
            // User chose not to update
            DialogResult = false;
            Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Clear out changelog stuff only if this was post-update viewing
            if (!isUpdatePrompt)
            {
                Properties.Settings.Default.ShowChangelog = false;
                Properties.Settings.Default.ChangelogContent = "";
                Properties.Settings.Default.NewVersion = "";
                Properties.Settings.Default.Save();
            }

            Close();
        }
    }
}