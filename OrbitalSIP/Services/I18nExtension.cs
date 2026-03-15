using System;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace OrbitalSIP.Services
{
    public class I18nExtension : MarkupExtension
    {
        public string Key { get; set; }

        public I18nExtension(string key)
        {
            Key = key;
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var binding = new Avalonia.Data.Binding($"[{Key}]")
            {
                Source = I18nService.Instance,
                Mode = Avalonia.Data.BindingMode.OneWay
            };
            return binding;
        }
    }
}
