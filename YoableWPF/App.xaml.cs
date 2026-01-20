using System.Configuration;
using System.Data;
using System.Windows;
using YoableWPF.Managers;

namespace YoableWPF
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Upgrade settings on version change to preserve user data (e.g., recent projects)
            if (YoableWPF.Properties.Settings.Default.SettingsUpgradeRequired)
            {
                YoableWPF.Properties.Settings.Default.Upgrade();
                YoableWPF.Properties.Settings.Default.SettingsUpgradeRequired = false;
                YoableWPF.Properties.Settings.Default.Save();
            }

            // Initialize language manager (it will automatically load the saved language)
            // The LanguageManager constructor already loads the language from settings
            _ = LanguageManager.Instance;

            // Show the startup window (combined splash screen and project selector)
            var startupWindow = new StartupWindow();
            startupWindow.Show();
        }
    }
}
