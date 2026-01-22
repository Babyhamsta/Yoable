using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace YoableWPF.Managers
{
    public class LanguageManager
    {
        private static LanguageManager _instance;
        public static LanguageManager Instance => _instance ??= new LanguageManager();

        private ResourceDictionary _currentLanguageDictionary;
        private ResourceDictionary _fallbackDictionary; // Always English
        private string _currentLanguage = "en-US";

        private static readonly List<LanguageInfo> SupportedLanguages = new()
        {
            new LanguageInfo { Code = "en-US", Name = "English", NativeName = "English" },
            new LanguageInfo { Code = "ja-JP", Name = "Japanese", NativeName = "日本語" },
            new LanguageInfo { Code = "ru-RU", Name = "Russian", NativeName = "Русский" },
            new LanguageInfo { Code = "zh-CN", Name = "Chinese (Simplified)", NativeName = "简体中文" },
            new LanguageInfo { Code = "zh-TW", Name = "Chinese (Traditional)", NativeName = "繁體中文" }
        };

        public event EventHandler LanguageChanged;

        private LanguageManager()
        {
            // Load English as fallback
            _fallbackDictionary = LoadLanguageResource("en-US");

            // Load saved language preference
            var savedLanguage = Properties.Settings.Default.Language;
            if (!string.IsNullOrEmpty(savedLanguage) && SupportedLanguages.Any(l => l.Code == savedLanguage))
            {
                _currentLanguage = savedLanguage;
            }

            LoadLanguage(_currentLanguage);
        }

        public List<LanguageInfo> GetAvailableLanguages() => SupportedLanguages.ToList();

        public string CurrentLanguage => _currentLanguage;

        public void SetLanguage(string languageCode)
        {
            if (_currentLanguage == languageCode) return;
            if (!SupportedLanguages.Any(l => l.Code == languageCode)) return;

            _currentLanguage = languageCode;
            LoadLanguage(languageCode);

            // Save preference
            Properties.Settings.Default.Language = languageCode;
            Properties.Settings.Default.Save();

            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        private void LoadLanguage(string languageCode)
        {
            try
            {
                // Remove old language dictionary
                if (_currentLanguageDictionary != null)
                {
                    Application.Current.Resources.MergedDictionaries.Remove(_currentLanguageDictionary);
                }

                // Load the new language
                _currentLanguageDictionary = LoadLanguageResource(languageCode);
                Application.Current.Resources.MergedDictionaries.Add(_currentLanguageDictionary);

                System.Diagnostics.Debug.WriteLine($"Loaded language: {languageCode}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading language {languageCode}: {ex.Message}");

                // Fallback to English if loading fails
                if (languageCode != "en-US")
                {
                    LoadLanguage("en-US");
                }
            }
        }

        private ResourceDictionary LoadLanguageResource(string languageCode)
        {
            var assemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
            return new ResourceDictionary
            {
                Source = new Uri($"/{assemblyName};component/Resources/Languages/Strings.{languageCode}.xaml", UriKind.Relative)
            };
        }

        public string GetString(string key)
        {
            // Try current language first
            if (_currentLanguageDictionary?.Contains(key) == true)
            {
                return _currentLanguageDictionary[key]?.ToString() ?? key;
            }

            // Fallback to English
            if (_fallbackDictionary?.Contains(key) == true)
            {
                return _fallbackDictionary[key]?.ToString() ?? key;
            }

            // Return key if not found
            return key;
        }

        /// <summary>
        /// Forces DynamicResource bindings in a window to re-evaluate after language changes
        /// </summary>
        public static void ReloadWindowResources(Window window)
        {
            if (window == null) return;

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

            // Force refresh
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
