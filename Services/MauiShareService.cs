using System.Threading.Tasks;
using Diarion.Services;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace Diarion.Services;

public class MauiShareService : IShareService
{
    public Task ShareFileAsync(string title, string filePath)
    {
        return Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = title,
            File = new ShareFile(filePath)
        });
    }
}
