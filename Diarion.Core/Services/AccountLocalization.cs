using System.Globalization;
using Diarion.Models;
using Diarion.Resources.Localization;

namespace Diarion.Services;

/// <summary>
/// Resolves the display name of the migration-created default account so it follows the current UI
/// language instead of whatever language happened to be active when the database was first migrated.
/// </summary>
public static class AccountLocalization
{
    public static string ResolveName(Account? account)
    {
        if (account is null) return string.Empty;

        if (string.IsNullOrEmpty(account.ResourceKey))
            return account.Name;

        var culture = AppResources.Culture ?? CultureInfo.CurrentUICulture;
        return AppResources.ResourceManager.GetString(account.ResourceKey, culture) ?? account.Name;
    }
}
