using System;
using System.Threading.Tasks;

namespace Diarion.Services;

/// <summary>Opens a web page outside the app — the terms and the privacy policy the paywall must
/// link to (Apple 3.1.2). Nothing is fetched by the app itself.</summary>
public interface IExternalLinkService
{
    Task OpenAsync(Uri uri);
}
