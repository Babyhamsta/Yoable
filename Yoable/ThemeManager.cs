
namespace Yoable
{
    public class ThemeManager
    {
        private Form MainForm;

        public ThemeManager(Form form)
        {
            MainForm = form;
        }

        public void ToggleDarkMode(bool enableDark)
        {
            // Update Form colors
            MainForm.BackColor = enableDark ? Color.FromArgb(30, 30, 30) : SystemColors.Control;
            MainForm.ForeColor = enableDark ? Color.White : SystemColors.ControlText;

            foreach (Control control in MainForm.Controls)
            {
                ApplyThemeToControl(control, enableDark);
            }
        }

        private void ApplyThemeToControl(Control control, bool enableDark)
        {
            Color backgroundColor = enableDark ? Color.FromArgb(40, 40, 40) : SystemColors.Control;
            Color textColor = enableDark ? Color.White : SystemColors.ControlText;

            switch (control)
            {
                case Panel or GroupBox:
                    control.BackColor = backgroundColor;
                    control.ForeColor = textColor;
                    break;

                case Label:
                    control.ForeColor = textColor;
                    break;

                case Button button:
                    button.BackColor = enableDark ? Color.FromArgb(50, 50, 50) : SystemColors.ButtonFace;
                    button.ForeColor = textColor;
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderSize = 0;
                    button.FlatAppearance.MouseOverBackColor = enableDark ? Color.FromArgb(70, 70, 70) : SystemColors.ButtonHighlight;
                    button.FlatAppearance.MouseDownBackColor = enableDark ? Color.FromArgb(90, 90, 90) : SystemColors.ButtonShadow;
                    break;

                case ListBox listBox:
                    listBox.BackColor = backgroundColor;
                    listBox.ForeColor = textColor;
                    listBox.BorderStyle = BorderStyle.FixedSingle;
                    break;

                case TextBox textBox:
                    textBox.BackColor = enableDark ? Color.FromArgb(50, 50, 50) : SystemColors.Window;
                    textBox.ForeColor = textColor;
                    textBox.BorderStyle = BorderStyle.FixedSingle;
                    break;

                case ToolStrip toolStrip:
                    toolStrip.BackColor = enableDark ? Color.FromArgb(50, 50, 50) : SystemColors.Control;
                    toolStrip.ForeColor = textColor;
                    foreach (ToolStripItem item in toolStrip.Items)
                    {
                        item.BackColor = toolStrip.BackColor;
                        item.ForeColor = textColor;
                        if (item is ToolStripDropDownItem menuItem)
                        {
                            ApplyThemeToMenuItems(menuItem, enableDark);
                        }
                    }
                    break;
            }

            // Apply theme recursively to nested controls
            foreach (Control child in control.Controls)
            {
                ApplyThemeToControl(child, enableDark);
            }
        }

        private void ApplyThemeToMenuItems(ToolStripDropDownItem menuItem, bool enableDark)
        {
            menuItem.BackColor = enableDark ? Color.FromArgb(50, 50, 50) : SystemColors.Control;
            menuItem.ForeColor = enableDark ? Color.White : SystemColors.ControlText;
            menuItem.DropDown.BackColor = menuItem.BackColor;
            menuItem.DropDown.ForeColor = menuItem.ForeColor;

            foreach (ToolStripItem subItem in menuItem.DropDownItems)
            {
                if (subItem is ToolStripDropDownItem subMenuItem)
                {
                    ApplyThemeToMenuItems(subMenuItem, enableDark);
                }
                else
                {
                    subItem.BackColor = menuItem.BackColor;
                    subItem.ForeColor = menuItem.ForeColor;
                }
            }
        }
    }
}
