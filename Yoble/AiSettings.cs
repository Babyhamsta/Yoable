using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace Yoable
{
    public partial class AiSettings : Form
    {

        public float ConfidenceThreshold { get; private set; }

        public AiSettings(float initialConfidence)
        {
            InitializeComponent();

            ConfidenceThreshold = initialConfidence;

            // Initialize UI
            confidenceTrackBar.Minimum = 0;
            confidenceTrackBar.Maximum = 100;  // 0-100 range
            confidenceTrackBar.Value = (int)(initialConfidence * 100);
            confidenceLabel.Text = $"{confidenceTrackBar.Value}%";

            confidenceTrackBar.Scroll += (s, e) =>
            {
                ConfidenceThreshold = confidenceTrackBar.Value / 100f;
                confidenceLabel.Text = $"{confidenceTrackBar.Value}%";
            };

            confirmButton.Click += (s, e) => this.DialogResult = DialogResult.OK;
        }
    }
}
