using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Diarion.Models;

namespace Diarion.Services;

public interface IWishlistService
{
    Task<List<WishlistEntry>> GetWishlistEntriesAsync();
    Task SaveWishlistEntryAsync(WishlistEntry entry);
    Task DeleteWishlistEntryAsync(Guid id);
}