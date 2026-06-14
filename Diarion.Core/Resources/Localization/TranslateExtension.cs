using System;
using Microsoft.Maui.Controls.Xaml;
using Microsoft.Maui.Controls;

namespace Diarion.Resources.Localization
{
    [ContentProperty(nameof(Key))]
    public class TranslateExtension : IMarkupExtension<BindingBase>
    {
        public string Key { get; set; } = string.Empty;

        public BindingBase ProvideValue(IServiceProvider serviceProvider)
        {
            return new Binding
            {
                Mode = BindingMode.OneWay,
                Path = $"[{Key}]",
                Source = LocalizationResourceManager.Instance
            };
        }

        object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider)
        {
            return ProvideValue(serviceProvider);
        }
    }
}