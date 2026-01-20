using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Markup;

namespace YoableWPF.Managers
{
    public class LanguageManager
    {
        private static LanguageManager instance;
        public static LanguageManager Instance => instance ??= new LanguageManager();

        private ResourceDictionary currentLanguageDictionary;
        private ResourceDictionary currentIniOverrideDictionary; // Used to track INI override resource dictionary
        private Dictionary<string, string> currentIniStrings; // Used to store strings read from INI file
        private string currentLanguage = "en-US"; // Default to English (hardcoded)
        private string langFolderPath;

        public event EventHandler LanguageChanged;

        private LanguageManager()
        {
            // Get the lang folder in the application directory
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            langFolderPath = Path.Combine(appDir, "lang");

            // Create the lang folder if it doesn't exist
            if (!Directory.Exists(langFolderPath))
            {
                Directory.CreateDirectory(langFolderPath);
            }

            // Load language from settings
            if (!string.IsNullOrEmpty(Properties.Settings.Default.Language))
            {
                currentLanguage = Properties.Settings.Default.Language;
            }
            LoadLanguage(currentLanguage);
        }

        public List<LanguageInfo> GetAvailableLanguages()
        {
            var languages = new List<LanguageInfo>();

            // English is always available (hardcoded)
            languages.Add(new LanguageInfo { Code = "en-US", Name = "English", NativeName = "English" });

            // Scan all .ini files in the lang folder
            if (Directory.Exists(langFolderPath))
            {
                var iniFiles = Directory.GetFiles(langFolderPath, "*.ini");
                foreach (var file in iniFiles)
                {
                    var langCode = Path.GetFileNameWithoutExtension(file);
                    // Skip English since it's already in the list
                    if (langCode == "en-US") continue;

                    var langInfo = GetLanguageInfoFromFile(file, langCode);
                    if (langInfo != null)
                    {
                        languages.Add(langInfo);
                    }
                }
            }

            return languages;
        }

        private LanguageInfo GetLanguageInfoFromFile(string filePath, string langCode)
        {
            try
            {
                var lines = File.ReadAllLines(filePath, Encoding.UTF8);
                var name = langCode; // Default to language code
                var nativeName = langCode;

                // Read language information from the [LanguageInfo] section
                bool inLanguageInfo = false;
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed == "[LanguageInfo]")
                    {
                        inLanguageInfo = true;
                        continue;
                    }
                    if (trimmed.StartsWith("[") && trimmed != "[LanguageInfo]")
                    {
                        inLanguageInfo = false;
                        continue;
                    }
                    if (inLanguageInfo && trimmed.Contains("="))
                    {
                        var parts = trimmed.Split(new[] { '=' }, 2);
                        if (parts.Length == 2)
                        {
                            var key = parts[0].Trim();
                            var value = parts[1].Trim();
                            if (key == "Name") name = value;
                            if (key == "NativeName") nativeName = value;
                        }
                    }
                }

                return new LanguageInfo { Code = langCode, Name = name, NativeName = nativeName };
            }
            catch
            {
                return null;
            }
        }

        public string CurrentLanguage => currentLanguage;

        public void SetLanguage(string languageCode)
        {
            if (currentLanguage == languageCode) return;

            currentLanguage = languageCode;
            LoadLanguage(languageCode);

            // Save to settings
            Properties.Settings.Default.Language = languageCode;
            Properties.Settings.Default.Save();

            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        private void LoadLanguage(string languageCode)
        {
            try
            {
                // Remove old language resources (including English and INI override resources)
                if (currentLanguageDictionary != null)
                {
                    Application.Current.Resources.MergedDictionaries.Remove(currentLanguageDictionary);
                    currentLanguageDictionary = null;
                }

                // Remove previously added INI override resource dictionary
                if (currentIniOverrideDictionary != null)
                {
                    Application.Current.Resources.MergedDictionaries.Remove(currentIniOverrideDictionary);
                    currentIniOverrideDictionary = null;
                }

                currentIniStrings = null;

                // English: Load from XAML resource (hardcoded)
                if (languageCode == "en-US")
                {
                    LoadEnglishFromXaml();
                }
                else
                {
                    // Other languages: Load from INI file
                    LoadLanguageFromIni(languageCode);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in LoadLanguage: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");

                // If loading fails, fallback to English
                if (languageCode != "en-US")
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load language {languageCode}, falling back to en-US");
                    LoadLanguage("en-US");
                }
            }
        }

        private void LoadEnglishFromXaml()
        {
            try
            {
                var assemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
                var resourcePath = $"Resources/Languages/Strings.en-US.xaml";
                
                currentLanguageDictionary = new ResourceDictionary();
                var uri = new Uri($"/{assemblyName};component/{resourcePath}", UriKind.Relative);
                currentLanguageDictionary.Source = uri;
                Application.Current.Resources.MergedDictionaries.Add(currentLanguageDictionary);
                System.Diagnostics.Debug.WriteLine($"Successfully loaded English from XAML");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load English from XAML: {ex.Message}");
                throw;
            }
        }

        private void LoadLanguageFromIni(string languageCode)
        {
            var langFile = Path.Combine(langFolderPath, $"{languageCode}.ini");

            // If file doesn't exist, fallback to English
            if (!File.Exists(langFile))
            {
                System.Diagnostics.Debug.WriteLine($"Language file not found: {langFile}, falling back to en-US");
                LoadLanguage("en-US");
                return;
            }

            try
            {
                // Read INI file
                currentIniStrings = ParseIniFile(langFile);

                // Load English first as a base (fallback)
                LoadEnglishFromXaml();

                // Create new resource dictionary to override English translations
                currentIniOverrideDictionary = new ResourceDictionary();
                foreach (var kvp in currentIniStrings)
                {
                    currentIniOverrideDictionary[kvp.Key] = kvp.Value;
                }

                // Add INI override dictionary last to ensure it overrides English resources
                Application.Current.Resources.MergedDictionaries.Add(currentIniOverrideDictionary);

                // Force trigger resource update notification (to trigger DynamicResource re-evaluation)
                Application.Current.Resources["LanguageUpdated"] = DateTime.Now.Ticks;

                System.Diagnostics.Debug.WriteLine($"Successfully loaded language {languageCode} from INI file with {currentIniStrings.Count} translations");

                // Verify some key translations are loaded correctly
                var testKeys = new[] { "Settings_Title", "Menu_Project", "Common_OK" };
                foreach (var testKey in testKeys)
                {
                    var testValue = GetString(testKey);
                    System.Diagnostics.Debug.WriteLine($"Test translation for '{testKey}': '{testValue}' (expected non-English if loaded correctly)");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading language from INI: {ex.Message}");
                throw;
            }
        }

        private Dictionary<string, string> ParseIniFile(string filePath)
        {
            var result = new Dictionary<string, string>();
            string currentSection = "";

            try
            {
                var lines = File.ReadAllLines(filePath, Encoding.UTF8);
                
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();

                    // Skip empty lines and comments
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(";") || trimmed.StartsWith("#"))
                        continue;

                    // Process sections
                    if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                    {
                        currentSection = trimmed.Substring(1, trimmed.Length - 2);
                        continue;
                    }

                    // Process key-value pairs
                    if (trimmed.Contains("="))
                    {
                        var parts = trimmed.Split(new[] { '=' }, 2);
                        if (parts.Length == 2)
                        {
                            var key = parts[0].Trim();
                            var value = parts[1].Trim();

                            // Support escape characters
                            value = value.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t");

                            // Check if key name already contains section prefix
                            // If key name already starts with "SectionName_", use it directly; otherwise add section prefix
                            string fullKey;
                            if (string.IsNullOrEmpty(currentSection))
                            {
                                fullKey = key;
                            }
                            else if (key.StartsWith($"{currentSection}_", StringComparison.OrdinalIgnoreCase))
                            {
                                // Key name already contains section prefix, use directly
                                fullKey = key;
                            }
                            else
                            {
                                // Add section prefix
                                fullKey = $"{currentSection}_{key}";
                            }

                            result[fullKey] = value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing INI file: {ex.Message}");
            }

            return result;
        }

        public string GetString(string key)
        {
            // If current language is English, read from XAML resource dictionary
            if (currentLanguage == "en-US")
            {
                if (currentLanguageDictionary != null && currentLanguageDictionary.Contains(key))
                {
                    return currentLanguageDictionary[key]?.ToString() ?? key;
                }
            }
            else
            {
                // Other languages: First read from INI, if not found fallback to English
                if (currentIniStrings != null && currentIniStrings.ContainsKey(key))
                {
                    return currentIniStrings[key];
                }

                // Fallback to English
                if (currentLanguageDictionary != null && currentLanguageDictionary.Contains(key))
                {
                    return currentLanguageDictionary[key]?.ToString() ?? key;
                }
            }

            return key; // If not found, return the key itself
        }

        /// <summary>
        /// Forces DynamicResource bindings in a window to re-evaluate after language changes
        /// </summary>
        public static void ReloadWindowResources(Window window)
        {
            if (window == null)
                return;

            // Remove local language resource dictionary from window (if exists)
            var languageDictToRemove = window.Resources.MergedDictionaries
                .FirstOrDefault(d => d.Source != null && d.Source.ToString().Contains("Languages/Strings."));

            if (languageDictToRemove != null)
            {
                window.Resources.MergedDictionaries.Remove(languageDictToRemove);
            }

            // Force all DynamicResource bindings to re-lookup resources
            var tempDict = new ResourceDictionary();
            window.Resources.MergedDictionaries.Add(tempDict);
            window.Resources.MergedDictionaries.Remove(tempDict);

            // Force refresh all controls using DynamicResource
            window.InvalidateVisual();
            window.UpdateLayout();
        }
    }

    public class LanguageInfo
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public string NativeName { get; set; }
    }
}
