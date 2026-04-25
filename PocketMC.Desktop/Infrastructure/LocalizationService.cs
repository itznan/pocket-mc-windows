using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using PocketMC.Desktop.Features.Settings;

namespace PocketMC.Desktop.Infrastructure
{
    public sealed class LocalizationService
    {
        private readonly SettingsManager _settingsManager;
        private readonly ResourceDictionary _defaultResourceDictionary = new ResourceDictionary();

        public LocalizationService(SettingsManager settingsManager)
        {
            _settingsManager = settingsManager;
        }

        public IReadOnlyList<LanguageInfo> SupportedLanguages { get; } = new List<LanguageInfo>
        {
            new LanguageInfo("en-US", "English"),
            new LanguageInfo("es-ES", "Español"),
            new LanguageInfo("hi-IN", "हिंदी")
        };

        public string CurrentLanguageCode { get; private set; } = "en-US";

        public void Initialize(string? languageCode)
        {
            var code = !string.IsNullOrWhiteSpace(languageCode) ? languageCode : "en-US";
            if (!SupportedLanguages.Any(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase)))
            {
                code = "en-US";
            }

            CurrentLanguageCode = code;
            LoadResourceDictionary(code);
        }

        public void ChangeLanguage(string languageCode)
        {
            if (string.Equals(CurrentLanguageCode, languageCode, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!SupportedLanguages.Any(l => string.Equals(l.Code, languageCode, StringComparison.OrdinalIgnoreCase)))
            {
                languageCode = "en-US";
            }

            CurrentLanguageCode = languageCode;
            LoadResourceDictionary(languageCode);

            var settings = _settingsManager.Load();
            settings.Language = languageCode;
            _settingsManager.Save(settings);
        }

        public string GetString(string key)
        {
            if (Application.Current?.Resources?.Contains(key) == true)
            {
                return Application.Current.Resources[key]?.ToString() ?? key;
            }

            return key;
        }

        private void LoadResourceDictionary(string languageCode)
        {
            if (Application.Current == null)
            {
                return;
            }

            var appResources = Application.Current.Resources;
            var existingDictionary = appResources.MergedDictionaries
                .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Strings.", StringComparison.OrdinalIgnoreCase));

            if (existingDictionary != null)
            {
                appResources.MergedDictionaries.Remove(existingDictionary);
            }

            var source = new Uri($"pack://application:,,,/PocketMC.Desktop;component/Resources/Strings.{languageCode}.xaml", UriKind.Absolute);
            var languageDictionary = new ResourceDictionary { Source = source };
            appResources.MergedDictionaries.Add(languageDictionary);
        }
    }

    public sealed class LanguageInfo
    {
        public LanguageInfo(string code, string displayName)
        {
            Code = code;
            DisplayName = displayName;
        }

        public string Code { get; }
        public string DisplayName { get; }
    }
}
