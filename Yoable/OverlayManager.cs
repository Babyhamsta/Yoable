using System.Windows.Forms;

namespace Yoble
{
    public class OverlayManager
    {
        private Panel overlayPanel;
        private Label overlayLabel;
        private Button cancelButton;
        public CancellationTokenSource aiProcessingToken;
        private Form MainForm;

        public OverlayManager(Form form)
        {
            MainForm = form;

            overlayPanel = new Panel
            {
                Size = new Size(form.Width, 100),
                BackColor = ColorTranslator.FromHtml("#181C14"),
                Visible = false
            };

            overlayLabel = new Label
            {
                Text = "Running AI Detections...",
                ForeColor = Color.White,
                Font = new Font("Arial", 16, FontStyle.Bold),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 50
            };

            cancelButton = new Button
            {
                Text = "Cancel",
                Font = new Font("Arial", 12, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(220, 200, 0, 0),
                Size = new Size(100, 30),
                Location = new Point((overlayPanel.Width - 100) / 2, 60), // Centered inside banner
                Visible = true
            };

            // Modern button styling (flat, rounded corners)
            cancelButton.FlatAppearance.BorderSize = 0;
            cancelButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(250, 255, 0, 0); // Brighter red hover effect
            cancelButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(255, 180, 0, 0); // Slightly darker red click effect
            cancelButton.Location = new Point((overlayPanel.Width - cancelButton.Width) / 2, 55);

            cancelButton.Click += (s, e) =>
            {
                if (aiProcessingToken != null)
                {
                    aiProcessingToken.Cancel();
                    overlayLabel.Text = "Cancelling AI Detections...";
                }
            };

            overlayPanel.Controls.Add(overlayLabel);
            overlayPanel.Controls.Add(cancelButton);
            form.Controls.Add(overlayPanel);
            overlayPanel.BringToFront();
        }

        public void CenterOverlay()
        {
            overlayPanel.Location = new Point(
                (MainForm.ClientSize.Width - overlayPanel.Width) / 2,
                (MainForm.ClientSize.Height - overlayPanel.Height) / 2
            );
        }

        public void ShowOverlay() => overlayPanel.Visible = true;

        public void HideOverlay() => overlayPanel.Visible = false;
    }
}
