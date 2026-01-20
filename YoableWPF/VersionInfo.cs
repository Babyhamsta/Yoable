using System.Reflection;

namespace YoableWPF
{
    public static class VersionInfo
    {
        public static string CurrentVersion { get; } = GetCurrentVersion();

        private static string GetCurrentVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (!string.IsNullOrWhiteSpace(info?.InformationalVersion))
            {
                return info.InformationalVersion;
            }

            return assembly.GetName().Version?.ToString() ?? "0.0.0";
        }
    }
}
