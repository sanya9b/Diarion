using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services.Database;
using LiteDB;

namespace Diarion.Services;

public class CycleLogService : ICycleLogService
{
    private readonly IDatabaseContext _dbContext;

    public CycleLogService(IDatabaseContext dbContext)
    {
        _dbContext = dbContext;
    }

    private ILiteCollection<CycleLog> CycleLogsCollection =>
        _dbContext.GetCollection<CycleLog>(DatabaseConstants.CycleLogsCollection);

    public Task<List<DateTime>> GetMarkedDatesAsync()
        => Task.Run(() => MarkedDates());

    public Task AddEpisodeAsync(DateTime start, int length)
    {
        return Task.Run(() =>
        {
            var existing = MarkedDates().ToHashSet();
            var today = DateTime.Today;

            var toInsert = Enumerable.Range(0, Math.Max(1, length))
                .Select(offset => start.Date.AddDays(offset))
                .Where(day => day <= today && !existing.Contains(day))
                .Select(day => new CycleLog { Date = day })
                .ToList();

            if (toInsert.Count > 0) CycleLogsCollection.InsertBulk(toInsert);
        });
    }

    public Task RemoveEpisodeAsync(DateTime anyDayOfIt)
    {
        return Task.Run(() =>
        {
            // Deleting from the episode's own span rather than from a fixed length: the user may have
            // logged a longer or shorter period than the setting says.
            var episode = CycleForecastCalculator.BuildHistory(MarkedDates())
                .Episodes
                .FirstOrDefault(e => anyDayOfIt.Date >= e.Start && anyDayOfIt.Date <= e.End);

            if (episode == null) return;

            foreach (var row in CycleLogsCollection.FindAll()
                         .Where(l => l.Date.Date >= episode.Start && l.Date.Date <= episode.End)
                         .ToList())
            {
                CycleLogsCollection.Delete(row.Id);
            }
        });
    }

    private List<DateTime> MarkedDates() => CycleLogsCollection.FindAll()
        .Select(l => l.Date.Date)
        .Distinct()
        .OrderBy(d => d)
        .ToList();
}
