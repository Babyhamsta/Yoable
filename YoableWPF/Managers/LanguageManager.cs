using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Markup;

namespace YoableWPF.Managers
{
    public class LanguageManager
    {
        private static LanguageManager instance;
        public static LanguageManager Instance => instance ??= new LanguageManager();

        private ResourceDictionary currentLanguageDictionary;
        private string currentLanguage = "zh-TW"; // 默認繁體中文

        public event EventHandler LanguageChanged;

        private LanguageManager()
        {
            // 從設置加載語言
            if (!string.IsNullOrEmpty(Properties.Settings.Default.Language))
            {
                currentLanguage = Properties.Settings.Default.Language;
            }
            LoadLanguage(currentLanguage);
        }

        public List<LanguageInfo> GetAvailableLanguages()
        {
            return new List<LanguageInfo>
            {
                new LanguageInfo { Code = "zh-TW", Name = "繁體中文", NativeName = "繁體中文" },
                new LanguageInfo { Code = "zh-CN", Name = "简体中文", NativeName = "简体中文" },
                new LanguageInfo { Code = "en-US", Name = "English", NativeName = "English" }
            };
        }

        public string CurrentLanguage => currentLanguage;

        public void SetLanguage(string languageCode)
        {
            if (currentLanguage == languageCode) return;

            currentLanguage = languageCode;
            LoadLanguage(languageCode);
            
            // 保存到設置
            Properties.Settings.Default.Language = languageCode;
            Properties.Settings.Default.Save();

            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        private void LoadLanguage(string languageCode)
        {
            try
            {
                // 移除舊的語言資源
                if (currentLanguageDictionary != null)
                {
                    Application.Current.Resources.MergedDictionaries.Remove(currentLanguageDictionary);
                    currentLanguageDictionary = null;
                }

                // 加載新的語言資源
                // 使用 ResourceDictionary.Source 屬性，這是最可靠的方式
                var assemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
                var resourcePath = $"Resources/Languages/Strings.{languageCode}.xaml";
                
                try
                {
                    // 方法 1: 使用 Source 屬性（最簡單可靠）
                    currentLanguageDictionary = new ResourceDictionary();
                    var uri = new Uri($"/{assemblyName};component/{resourcePath}", UriKind.Relative);
                    currentLanguageDictionary.Source = uri;
                    Application.Current.Resources.MergedDictionaries.Add(currentLanguageDictionary);
                    System.Diagnostics.Debug.WriteLine($"Successfully loaded language: {languageCode} using Source property");
                }
                catch (Exception ex1)
                {
                    System.Diagnostics.Debug.WriteLine($"Method 1 failed: {ex1.Message}");
                    
                    // 方法 2: 嘗試使用 Application.LoadComponent
                    try
                    {
                        var uri = new Uri($"/{assemblyName};component/{resourcePath}", UriKind.Relative);
                        currentLanguageDictionary = (ResourceDictionary)Application.LoadComponent(uri);
                        Application.Current.Resources.MergedDictionaries.Add(currentLanguageDictionary);
                        System.Diagnostics.Debug.WriteLine($"Successfully loaded language: {languageCode} using LoadComponent");
                    }
                    catch (Exception ex2)
                    {
                        System.Diagnostics.Debug.WriteLine($"Method 2 failed: {ex2.Message}");
                        
                        // 方法 3: 嘗試使用 pack URI
                        try
                        {
                            var packUri = new Uri($"pack://application:,,,/{assemblyName};component/{resourcePath}", UriKind.Absolute);
                            currentLanguageDictionary = (ResourceDictionary)Application.LoadComponent(packUri);
                            Application.Current.Resources.MergedDictionaries.Add(currentLanguageDictionary);
                            System.Diagnostics.Debug.WriteLine($"Successfully loaded language: {languageCode} using pack URI");
                        }
                        catch (Exception ex3)
                        {
                            System.Diagnostics.Debug.WriteLine($"Method 3 failed: {ex3.Message}");
                            
                            // 如果所有方法都失敗，嘗試使用默認語言
                            if (languageCode != "zh-TW")
                            {
                                System.Diagnostics.Debug.WriteLine($"Failed to load language {languageCode}, falling back to zh-TW");
                                LoadLanguage("zh-TW");
                                return;
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"Failed to load default language zh-TW");
                                System.Diagnostics.Debug.WriteLine($"All methods failed. Resource path: {resourcePath}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in LoadLanguage: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                // 不顯示錯誤對話框，避免在啟動時出現問題
            }
        }

        public string GetString(string key)
        {
            if (currentLanguageDictionary != null && currentLanguageDictionary.Contains(key))
            {
                return currentLanguageDictionary[key]?.ToString() ?? key;
            }
            return key; // 如果找不到，返回 key 本身
        }
    }

    public class LanguageInfo
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public string NativeName { get; set; }
    }
}

