using System.ComponentModel;
using System.Globalization;

namespace Diarion.Resources.Localization
{
    public class LocalizationResourceManager : INotifyPropertyChanged
    {
        public static LocalizationResourceManager Instance { get; } = new LocalizationResourceManager();

        private LocalizationResourceManager()
        {
        }

        public string this[string resourceKey]
        {
            get
            {
                return AppResources.ResourceManager.GetString(resourceKey, AppResources.Culture) ?? resourceKey;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void SetCulture(CultureInfo culture)
        {
            AppResources.Culture = culture;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }
    }
}