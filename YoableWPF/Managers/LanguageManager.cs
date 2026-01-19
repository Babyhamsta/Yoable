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
        private ResourceDictionary currentIniOverrideDictionary; // 用於追蹤 INI 覆蓋資源字典
        private Dictionary<string, string> currentIniStrings; // 用於存儲從 INI 文件讀取的字符串
        private string currentLanguage = "en-US"; // 默認英文（硬編碼）
        private string langFolderPath;

        public event EventHandler LanguageChanged;

        private LanguageManager()
        {
            // 獲取應用程序目錄下的 lang 文件夾
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            langFolderPath = Path.Combine(appDir, "lang");

            // 如果 lang 文件夾不存在，創建它
            if (!Directory.Exists(langFolderPath))
            {
                Directory.CreateDirectory(langFolderPath);
            }

            // 從設置加載語言
            if (!string.IsNullOrEmpty(Properties.Settings.Default.Language))
            {
                currentLanguage = Properties.Settings.Default.Language;
            }
            LoadLanguage(currentLanguage);
        }

        public List<LanguageInfo> GetAvailableLanguages()
        {
            var languages = new List<LanguageInfo>();

            // 英文始終可用（硬編碼）
            languages.Add(new LanguageInfo { Code = "en-US", Name = "English", NativeName = "English" });

            // 從 lang 文件夾掃描所有 .ini 文件
            if (Directory.Exists(langFolderPath))
            {
                var iniFiles = Directory.GetFiles(langFolderPath, "*.ini");
                foreach (var file in iniFiles)
                {
                    var langCode = Path.GetFileNameWithoutExtension(file);
                    // 跳過英文，因為它已經在列表中
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
                var name = langCode; // 默認使用語言代碼
                var nativeName = langCode;

                // 從 [LanguageInfo] 區段讀取語言信息
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
            
            // 保存到設置
            Properties.Settings.Default.Language = languageCode;
            Properties.Settings.Default.Save();

            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        private void LoadLanguage(string languageCode)
        {
            try
            {
                // 移除舊的語言資源（包括英文和 INI 覆蓋的資源）
                if (currentLanguageDictionary != null)
                {
                    Application.Current.Resources.MergedDictionaries.Remove(currentLanguageDictionary);
                    currentLanguageDictionary = null;
                }
                
                // 移除之前添加的 INI 覆蓋資源字典
                if (currentIniOverrideDictionary != null)
                {
                    Application.Current.Resources.MergedDictionaries.Remove(currentIniOverrideDictionary);
                    currentIniOverrideDictionary = null;
                }
                
                currentIniStrings = null;

                // 英文：從 XAML 資源加載（硬編碼）
                if (languageCode == "en-US")
                {
                    LoadEnglishFromXaml();
                }
                else
                {
                    // 其他語言：從 INI 文件加載
                    LoadLanguageFromIni(languageCode);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in LoadLanguage: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                
                // 如果加載失敗，回退到英文
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
            
            // 如果文件不存在，回退到英文
            if (!File.Exists(langFile))
            {
                System.Diagnostics.Debug.WriteLine($"Language file not found: {langFile}, falling back to en-US");
                LoadLanguage("en-US");
                return;
            }

            try
            {
                // 讀取 INI 文件
                currentIniStrings = ParseIniFile(langFile);

                // 先加載英文作為基礎（fallback）
                LoadEnglishFromXaml();

                // 創建新的資源字典，覆蓋英文翻譯
                currentIniOverrideDictionary = new ResourceDictionary();
                foreach (var kvp in currentIniStrings)
                {
                    currentIniOverrideDictionary[kvp.Key] = kvp.Value;
                }

                // 將 INI 覆蓋字典添加到最後，確保它能覆蓋英文資源
                Application.Current.Resources.MergedDictionaries.Add(currentIniOverrideDictionary);
                
                // 強制觸發資源更新通知（用於觸發 DynamicResource 重新評估）
                Application.Current.Resources["LanguageUpdated"] = DateTime.Now.Ticks;
                
                System.Diagnostics.Debug.WriteLine($"Successfully loaded language {languageCode} from INI file with {currentIniStrings.Count} translations");
                
                // 驗證一些關鍵翻譯是否正確加載
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
                    
                    // 跳過空行和註釋
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(";") || trimmed.StartsWith("#"))
                        continue;

                    // 處理區段
                    if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                    {
                        currentSection = trimmed.Substring(1, trimmed.Length - 2);
                        continue;
                    }

                    // 處理鍵值對
                    if (trimmed.Contains("="))
                    {
                        var parts = trimmed.Split(new[] { '=' }, 2);
                        if (parts.Length == 2)
                        {
                            var key = parts[0].Trim();
                            var value = parts[1].Trim();
                            
                            // 支持轉義字符
                            value = value.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t");
                            
                            // 檢查鍵名是否已經包含區段前綴
                            // 如果鍵名已經以 "區段名_" 開頭，就直接使用；否則添加區段前綴
                            string fullKey;
                            if (string.IsNullOrEmpty(currentSection))
                            {
                                fullKey = key;
                            }
                            else if (key.StartsWith($"{currentSection}_", StringComparison.OrdinalIgnoreCase))
                            {
                                // 鍵名已經包含區段前綴，直接使用
                                fullKey = key;
                            }
                            else
                            {
                                // 添加區段前綴
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
            // 如果當前語言是英文，從 XAML 資源字典讀取
            if (currentLanguage == "en-US")
            {
                if (currentLanguageDictionary != null && currentLanguageDictionary.Contains(key))
                {
                    return currentLanguageDictionary[key]?.ToString() ?? key;
                }
            }
            else
            {
                // 其他語言：先從 INI 讀取，如果沒有則從英文 fallback
                if (currentIniStrings != null && currentIniStrings.ContainsKey(key))
                {
                    return currentIniStrings[key];
                }
                
                // Fallback 到英文
                if (currentLanguageDictionary != null && currentLanguageDictionary.Contains(key))
                {
                    return currentLanguageDictionary[key]?.ToString() ?? key;
                }
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
