using System;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;

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
            var dynamicResource = new DynamicResourceExtension($"i18n_{Key}");
            return dynamicResource.ProvideValue(serviceProvider);
        }
    }
}
