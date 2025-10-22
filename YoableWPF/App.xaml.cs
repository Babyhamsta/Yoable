using System.Configuration;
using System.Data;
using System.Windows;

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

            // Show the startup window (combined splash screen and project selector)
            var startupWindow = new StartupWindow();
            startupWindow.Show();
        }
    }
}