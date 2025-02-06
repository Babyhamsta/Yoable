using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace YoableWPF
{
    public partial class ChangelogWindow : Window
    {
        private StackPanel changelogPanel;

        public ChangelogWindow(string version, string changelog)
        {
            InitializeComponent();
            Style = (Style)FindResource("ModernWindowStyle");
            Title = $"What's New in {version}";

            // Need to wait for the template to be applied
            Loaded += (s, e) =>
            {
                changelogPanel = (StackPanel)Template.FindName("ChangelogStackPanel", this);
                ParseChangelog(changelog);
            };
        }

        private void ParseChangelog(string changelog)
        {
            if (changelogPanel == null) return;

            changelogPanel.Children.Clear();
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
                    FontSize = 20,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(86, 156, 214)),
                    Margin = new Thickness(0, 10, 0, 15)
                };
                changelogPanel.Children.Add(header);

                // Create bullet points list with more indent
                var bulletList = new RichTextBox
                {
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    IsReadOnly = true,
                    Focusable = false,
                    Margin = new Thickness(25, 0, 0, 10),  // Increased left margin for indentation
                    Padding = new Thickness(0)
                };

                // Configure FlowDocument
                var flowDoc = new FlowDocument()
                {
                    PagePadding = new Thickness(0),
                    LineHeight = 1
                };
                bulletList.Document = flowDoc;

                for (int i = 1; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.StartsWith("-"))
                    {
                        // Create bullet list item
                        var listItem = new List
                        {
                            MarkerStyle = TextMarkerStyle.Disc,
                            Margin = new Thickness(0, 0, 0, 8),  // Added spacing between bullet points
                            Padding = new Thickness(0)
                        };

                        var listParagraph = new Paragraph
                        {
                            Margin = new Thickness(0),
                            Padding = new Thickness(0),
                            LineHeight = 1,
                            TextIndent = 5
                        };
                        listParagraph.Foreground = Brushes.White;
                        listParagraph.Inlines.Add(new Run(line.TrimStart('-', ' ')));

                        listItem.ListItems.Add(new ListItem(listParagraph));
                        flowDoc.Blocks.Add(listItem);
                    }
                }

                changelogPanel.Children.Add(bulletList);
            }
        }

        private void DragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Clear out changelog stuff
            Properties.Settings.Default.ShowChangelog = false;
            Properties.Settings.Default.ChangelogContent = "";
            Properties.Settings.Default.NewVersion = "";
            Properties.Settings.Default.Save();

            Close();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var source = System.Windows.Interop.HwndSource.FromHwnd(handle);
            source?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x84) // WM_NCHITTEST
            {
                var point = new Point();
                int lp = lParam.ToInt32();
                point.X = (short)(lp & 0xFFFF);
                point.Y = (short)(lp >> 16);
                point = PointFromScreen(point);

                if (point.Y < 32)
                {
                    handled = true;
                    return new IntPtr(2);  // HTCAPTION
                }
            }
            return IntPtr.Zero;
        }
    }
}