using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Diarion.Services;
using Microsoft.Maui.Storage;

namespace Diarion.Services;

public class MauiFilePickerService : IFilePickerService
{
    public async Task<Stream?> PickBackupFileAsync(string title)
    {
        var fileType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            { DevicePlatform.iOS, new[] { "public.data", "public.database" } },
            { DevicePlatform.Android, new[] { "application/octet-stream" } },
            { DevicePlatform.WinUI, new[] { ".db" } },
            { DevicePlatform.macOS, new[] { "public.data", "public.database" } }
        });

        var result = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = title,
            FileTypes = fileType
        });

        if (result == null)
        {
            return null;
        }

        return await result.OpenReadAsync();
    }
}
