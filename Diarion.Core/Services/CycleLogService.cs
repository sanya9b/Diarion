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
        => Task.Run(() => CycleLogsCollection.FindAll()
            .Select(l => l.Date.Date)
            .Distinct()
            .OrderBy(d => d)
            .ToList());

    public Task<bool> ToggleAsync(DateTime date)
    {
        return Task.Run(() =>
        {
            var day = date.Date;

            // A period cannot be reported before it happens, and a stray future row would anchor the
            // whole forecast to a date that never occurred.
            if (day > DateTime.Today) return false;

            var existing = CycleLogsCollection.FindAll().Where(l => l.Date.Date == day).ToList();
            if (existing.Count > 0)
            {
                foreach (var row in existing)
                {
                    CycleLogsCollection.Delete(row.Id);
                }
                return false;
            }

            CycleLogsCollection.Insert(new CycleLog { Date = day });
            return true;
        });
    }
}
