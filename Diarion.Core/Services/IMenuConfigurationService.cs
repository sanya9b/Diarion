using System.Collections.Generic;
using Diarion.Models;

namespace Diarion.Services;

public interface IMenuConfigurationService
{
    List<QuickMenuItem> GetDefaultMenuItems();
}