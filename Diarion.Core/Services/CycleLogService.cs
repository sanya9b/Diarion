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
                         .Where(l => !l.IsSymptomOnly && l.Date.Date >= episode.Start && l.Date.Date <= episode.End)
                         .ToList())
            {
                if (row.HasSymptoms)
                {
                    // Un-marking a period must not throw away what the user recorded feeling that day.
                    row.IsSymptomOnly = true;
                    CycleLogsCollection.Update(row);
                    continue;
                }

                CycleLogsCollection.Delete(row.Id);
            }
        });
    }

    public Task<List<CycleLog>> GetLogsAsync()
        => Task.Run(() => CycleLogsCollection.FindAll().OrderBy(l => l.Date).ToList());

    public Task SetSymptomsAsync(DateTime date, IEnumerable<string> symptoms)
    {
        return Task.Run(() =>
        {
            var day = date.Date;
            var list = (symptoms ?? Enumerable.Empty<string>()).Distinct().ToList();
            var row = CycleLogsCollection.FindAll().FirstOrDefault(l => l.Date.Date == day);

            if (row == null)
            {
                // No row for this day: symptoms alone are reason enough to have one, but it must not
                // register as a period day.
                if (list.Count == 0) return;
                CycleLogsCollection.Insert(new CycleLog { Date = day, Symptoms = list, IsSymptomOnly = true });
                return;
            }

            row.Symptoms = list;

            // A symptom-only row with nothing left on it has no reason to exist. A period day does.
            if (list.Count == 0 && row.IsSymptomOnly)
            {
                CycleLogsCollection.Delete(row.Id);
                return;
            }

            CycleLogsCollection.Update(row);
        });
    }

    /// <summary>
    /// Period days only. Symptom-only rows share this collection but are not part of a period — counted
    /// as one they would invent episodes and drag every forecast with them.
    /// </summary>
    private List<DateTime> MarkedDates() => CycleLogsCollection.FindAll()
        .Where(l => !l.IsSymptomOnly)
        .Select(l => l.Date.Date)
        .Distinct()
        .OrderBy(d => d)
        .ToList();
}
