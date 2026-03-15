using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using Avalonia.Platform;

namespace OrbitalSIP.Services
{
    public class I18nService : INotifyPropertyChanged
    {
        private static I18nService? _instance;
        public static I18nService Instance => _instance ??= new I18nService();

        private Dictionary<string, string> _translations = new();

        public string CurrentLanguage { get; private set; } = "ru";

        public event Action? LanguageChanged;
        public event PropertyChangedEventHandler? PropertyChanged;

        private I18nService()
        {
            LoadLanguage(CurrentLanguage);
        }

        public string this[string key] => Get(key);

        public void LoadLanguage(string langCode)
        {
            CurrentLanguage = langCode;
            _translations.Clear();

            try
            {
                var uri = new Uri($"avares://OrbitalSIP/Assets/i18n/{langCode}.json");
                if (AssetLoader.Exists(uri))
                {
                    using var stream = AssetLoader.Open(uri);
                    using var reader = new StreamReader(stream);
                    var json = reader.ReadToEnd();
                    var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (dict != null)
                    {
                        _translations = dict;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load language {langCode}: {ex.Message}");
            }

            LanguageChanged?.Invoke();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        }

        public string Get(string key, string defaultValue = "")
        {
            if (_translations.TryGetValue(key, out var value))
                return value;
            return string.IsNullOrEmpty(defaultValue) ? key : defaultValue;
        }
    }
}
