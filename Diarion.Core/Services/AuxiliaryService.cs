using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services.Database;
using LiteDB;

namespace Diarion.Services;

public class AuxiliaryService : IAuxiliaryService
{
    private readonly IDatabaseContext _dbContext;

    public AuxiliaryService(IDatabaseContext dbContext)
    {
        _dbContext = dbContext;
    }

    private ILiteCollection<ReadingTrackerBook> ReadingTrackerBooksCollection => _dbContext.GetCollection<ReadingTrackerBook>(DatabaseConstants.ReadingTrackerBooksCollection);
    private ILiteCollection<HappyMoment> HappyMomentsCollection => _dbContext.GetCollection<HappyMoment>(DatabaseConstants.HappyMomentsCollection);
    private ILiteCollection<GoodDeed> GoodDeedsCollection => _dbContext.GetCollection<GoodDeed>(DatabaseConstants.GoodDeedsCollection);

    public Task<List<ReadingTrackerBook>> GetReadingTrackerBooksAsync()
    {
        return Task.Run(() => ReadingTrackerBooksCollection.Query().OrderBy(x => x.SlotNumber).ToList());
    }

    public Task SaveReadingTrackerBookAsync(ReadingTrackerBook book)
    {
        return Task.Run(() =>
        {
            var normalizedTitle = (book.BookTitle ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedTitle))
            {
                throw new ArgumentException("Book title is required.", nameof(book));
            }

            if (book.SlotNumber <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(book), "Slot number must be greater than zero.");
            }

            var completedOn = book.CompletedOn.Date > DateTime.Today ? DateTime.Today : book.CompletedOn.Date;
            var hasDuplicateSlot = ReadingTrackerBooksCollection.FindAll()
                .Any(x => x.Id != book.Id && x.SlotNumber == book.SlotNumber);

            if (hasDuplicateSlot)
            {
                throw new InvalidOperationException("Book slot is already filled.");
            }

            book.BookTitle = normalizedTitle;
            book.CompletedOn = completedOn;
            book.CreatedAt = book.CreatedAt == default ? DateTime.UtcNow : book.CreatedAt;

            ReadingTrackerBooksCollection.Upsert(book);
        });
    }

    public Task DeleteReadingTrackerBookAsync(int slotNumber)
    {
        return Task.Run(() =>
        {
            var existingSlot = ReadingTrackerBooksCollection.FindOne(x => x.SlotNumber == slotNumber);
            if (existingSlot != null)
            {
                ReadingTrackerBooksCollection.Delete(existingSlot.Id);
            }
        });
    }

    public Task<List<HappyMoment>> GetHappyMomentsAsync()
    {
        return Task.Run(() => HappyMomentsCollection.Query().OrderBy(x => x.SlotNumber).ToList());
    }

    public Task DeleteHappyMomentAsync(int slotNumber)
    {
        return Task.Run(() =>
        {
            HappyMomentsCollection.DeleteMany(x => x.SlotNumber == slotNumber);
        });
    }

    public Task SaveHappyMomentAsync(HappyMoment moment)
    {
        return Task.Run(() =>
        {
            var normalizedTitle = (moment.Title ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedTitle))
            {
                throw new ArgumentException("Moment title is required.", nameof(moment));
            }

            if (moment.SlotNumber <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(moment), "Slot number must be positive.");
            }

            var momentDate = moment.Date.Date > DateTime.Today ? DateTime.Today : moment.Date.Date;
            var existingSlot = HappyMomentsCollection.FindOne(x => x.SlotNumber == moment.SlotNumber);

            if (existingSlot != null)
            {
                moment.Id = existingSlot.Id;
                moment.CreatedAt = existingSlot.CreatedAt;
            }
            else if (moment.CreatedAt == default)
            {
                moment.CreatedAt = DateTime.UtcNow;
            }

            moment.Title = normalizedTitle;
            moment.Date = momentDate;

            HappyMomentsCollection.Upsert(moment);
        });
    }

    public Task<List<GoodDeed>> GetGoodDeedsAsync()
    {
        return Task.Run(() => GoodDeedsCollection.Query().OrderBy(x => x.SlotNumber).ToList());
    }

    public Task SaveGoodDeedAsync(GoodDeed deed)
    {
        return Task.Run(() =>
        {
            var normalizedTitle = (deed.Title ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedTitle))
            {
                throw new ArgumentException("Deed title is required.", nameof(deed));
            }

            if (deed.SlotNumber <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(deed), "Slot number must be positive.");
            }

            var deedDate = deed.Date.Date > DateTime.Today ? DateTime.Today : deed.Date.Date;
            var existingSlot = GoodDeedsCollection.FindOne(x => x.SlotNumber == deed.SlotNumber);

            if (existingSlot != null)
            {
                deed.Id = existingSlot.Id;
                deed.CreatedAt = existingSlot.CreatedAt;
            }
            else if (deed.CreatedAt == default)
            {
                deed.CreatedAt = DateTime.UtcNow;
            }

            deed.Title = normalizedTitle;
            deed.Date = deedDate;

            GoodDeedsCollection.Upsert(deed);
        });
    }

    public Task DeleteGoodDeedAsync(int slotNumber)
    {
        return Task.Run(() =>
        {
            var existingSlot = GoodDeedsCollection.FindOne(x => x.SlotNumber == slotNumber);
            if (existingSlot != null)
            {
                GoodDeedsCollection.Delete(existingSlot.Id);
            }
        });
    }
}