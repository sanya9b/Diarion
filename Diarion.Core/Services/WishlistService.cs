using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services.Database;
using Diarion.Services.Store;
using LiteDB;

namespace Diarion.Services;

public class WishlistService : IWishlistService
{
    private readonly IDatabaseContext _dbContext;
    private readonly IAccessGuard _accessGuard;

    public WishlistService(IDatabaseContext dbContext, IAccessGuard? accessGuard = null)
    {
        _accessGuard = accessGuard ?? AccessGuard.Unrestricted;
        _dbContext = dbContext;
    }

    private ILiteCollection<WishlistEntry> WishlistCollection => _dbContext.GetCollection<WishlistEntry>(DatabaseConstants.WishlistCollection);

    public Task<List<WishlistEntry>> GetWishlistEntriesAsync()
    {
        return Task.Run(() => WishlistCollection.Query().OrderByDescending(x => x.Date).ToList());
    }

    public Task SaveWishlistEntryAsync(WishlistEntry entry)
    {
        _accessGuard.EnsureWritable();
        return Task.Run(() =>
        {
            if (entry.CreatedAt == default)
            {
                entry.CreatedAt = DateTime.UtcNow;
            }
            WishlistCollection.Upsert(entry);
        });
    }

    public Task DeleteWishlistEntryAsync(Guid id)
    {
        _accessGuard.EnsureWritable();
        return Task.Run(() =>
        {
            WishlistCollection.Delete(id);
        });
    }
}