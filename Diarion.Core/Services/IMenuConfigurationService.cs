using System.Collections.Generic;
using Diarion.Models;
using Diarion.ViewModels;

namespace Diarion.Services;

public interface IMenuConfigurationService
{
    List<QuickMenuItem> GetDefaultMenuItems();
}