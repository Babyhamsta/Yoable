namespace Yoable
{
    public class OverlayManager
    {
        private Panel overlayPanel;
        private Label overlayLabel;
        private ProgressBar overlayProgressBar;
        private Button cancelButton;
        private Form mainForm;
        private CancellationTokenSource cancellationTokenSource;

        public OverlayManager(Form form)
        {
            mainForm = form;
            InitializeOverlayUI();
        }

        private void InitializeOverlayUI()
        {
            overlayPanel = new Panel
            {
                Size = new Size(mainForm.ClientSize.Width, 120),
                BackColor = ColorTranslator.FromHtml("#181C14"),
                Visible = false,
                Location = new Point(0, (mainForm.ClientSize.Height - 120) / 2),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            overlayLabel = new Label
            {
                Text = "Processing...",
                ForeColor = Color.White,
                Font = new Font("Arial", 16, FontStyle.Bold),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 40
            };

            overlayProgressBar = new ProgressBar
            {
                Size = new Size(overlayPanel.Width - 40, 20),
                Location = new Point(20, 50),
                Maximum = 100,
                Visible = false
            };

            cancelButton = new Button
            {
                Text = "Cancel",
                Font = new Font("Arial", 12, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(220, 200, 0, 0),
                Size = new Size(100, 30),
                Location = new Point((overlayPanel.Width - 100) / 2, 80),
                Visible = false
            };

            cancelButton.FlatAppearance.BorderSize = 0;
            cancelButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(250, 255, 0, 0);
            cancelButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(255, 180, 0, 0);
            cancelButton.Click += (s, e) => cancellationTokenSource?.Cancel();

            overlayPanel.Controls.Add(overlayLabel);
            overlayPanel.Controls.Add(overlayProgressBar);
            overlayPanel.Controls.Add(cancelButton);
            mainForm.Controls.Add(overlayPanel);
            overlayPanel.BringToFront();
        }

        public void ShowOverlay(string message = "Processing...")
        {
            mainForm.Invoke((MethodInvoker)delegate
            {
                overlayLabel.Text = message;
                overlayProgressBar.Visible = false;
                cancelButton.Visible = false;
                overlayPanel.Visible = true;
            });
        }

        public void ShowOverlayWithProgress(string message, CancellationTokenSource tokenSource)
        {
            mainForm.Invoke((MethodInvoker)delegate
            {
                cancellationTokenSource = tokenSource;
                overlayLabel.Text = message;
                overlayProgressBar.Value = 0;
                overlayProgressBar.Visible = true;
                cancelButton.Visible = true;
                overlayPanel.Visible = true;
            });
        }

        public void UpdateMessage(string message)
        {
            mainForm.Invoke((MethodInvoker)delegate
            {
                overlayLabel.Text = message;
            });
        }

        public void UpdateProgress(int progress)
        {
            mainForm.Invoke((MethodInvoker)delegate
            {
                overlayProgressBar.Value = progress;
            });
        }

        public void HideOverlay()
        {
            mainForm.Invoke((MethodInvoker)delegate
            {
                overlayPanel.Visible = false;
            });
        }
    }
}