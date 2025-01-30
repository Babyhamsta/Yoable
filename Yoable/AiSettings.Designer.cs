namespace Yoable
{
    partial class AiSettings
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            confidenceTrackBar = new TrackBar();
            MainLabel = new Label();
            confirmButton = new Button();
            confidenceLabel = new Label();
            ((System.ComponentModel.ISupportInitialize)confidenceTrackBar).BeginInit();
            SuspendLayout();
            // 
            // confidenceTrackBar
            // 
            confidenceTrackBar.Location = new Point(21, 37);
            confidenceTrackBar.Maximum = 100;
            confidenceTrackBar.Name = "confidenceTrackBar";
            confidenceTrackBar.Size = new Size(260, 45);
            confidenceTrackBar.TabIndex = 0;
            confidenceTrackBar.Value = 50;
            // 
            // MainLabel
            // 
            MainLabel.AutoSize = true;
            MainLabel.Location = new Point(110, 9);
            MainLabel.Name = "MainLabel";
            MainLabel.Size = new Size(82, 15);
            MainLabel.TabIndex = 1;
            MainLabel.Text = "AI Confidence";
            // 
            // confirmButton
            // 
            confirmButton.Location = new Point(57, 103);
            confirmButton.Name = "confirmButton";
            confirmButton.Size = new Size(188, 23);
            confirmButton.TabIndex = 2;
            confirmButton.Text = "Confirm";
            confirmButton.UseVisualStyleBackColor = true;
            // 
            // confidenceLabel
            // 
            confidenceLabel.AutoSize = true;
            confidenceLabel.Location = new Point(137, 75);
            confidenceLabel.Name = "confidenceLabel";
            confidenceLabel.Size = new Size(29, 15);
            confidenceLabel.TabIndex = 3;
            confidenceLabel.Text = "50%";
            // 
            // AiSettings
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(302, 131);
            Controls.Add(confidenceLabel);
            Controls.Add(confirmButton);
            Controls.Add(MainLabel);
            Controls.Add(confidenceTrackBar);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "AiSettings";
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "AI Settings";
            ((System.ComponentModel.ISupportInitialize)confidenceTrackBar).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private TrackBar confidenceTrackBar;
        private Label MainLabel;
        private Button confirmButton;
        private Label confidenceLabel;
    }
}