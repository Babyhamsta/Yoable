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

            // Initialize language manager (it will automatically load the saved language)
            // The LanguageManager constructor already loads the language from settings
            _ = LanguageManager.Instance;

            // Show the startup window (combined splash screen and project selector)
            var startupWindow = new StartupWindow();
            startupWindow.Show();
        }
    }
}