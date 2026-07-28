using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services.Database;
using LiteDB;

namespace Diarion.Services;

/// <summary>
/// Produces open, portable exports of the local data — full JSON dump, finance CSV, and a diary
/// Markdown document — entirely on-device. The content builders are pure and unit-testable; only
/// <see cref="ExportAndShareAsync"/> touches the platform (temp file + share sheet).
/// </summary>
public class ExportService : IExportService
{
    private const string ExportPrefix = "DiarionExport_";

    private static readonly string[] AllCollections =
    {
        DatabaseConstants.EntriesCollection,
        DatabaseConstants.TodosCollection,
        DatabaseConstants.HabitDefinitionsCollection,
        DatabaseConstants.HarmfulHabitTrackersCollection,
        DatabaseConstants.ReadingTrackerBooksCollection,
        DatabaseConstants.HappyMomentsCollection,
        DatabaseConstants.GoodDeedsCollection,
        DatabaseConstants.ProfileCollection,
        DatabaseConstants.WishlistCollection,
        DatabaseConstants.FinanceCollection,
        DatabaseConstants.NotesCollection,
        DatabaseConstants.BudgetsCollection,
        DatabaseConstants.AccountsCollection,
        DatabaseConstants.TransfersCollection,
    };

    private readonly IDatabaseContext _dbContext;
    private readonly IFileSystemService _fileSystem;
    private readonly IShareService _shareService;

    public ExportService(IDatabaseContext dbContext, IFileSystemService fileSystem, IShareService shareService)
    {
        _dbContext = dbContext;
        _fileSystem = fileSystem;
        _shareService = shareService;
    }

    public Task<string> BuildAsync(ExportFormat format) => format switch
    {
        ExportFormat.Csv => Task.Run(BuildFinanceCsv),
        ExportFormat.Markdown => Task.Run(BuildDiaryMarkdown),
        _ => Task.Run(BuildJson)
    };

    public async Task<bool> ExportAndShareAsync(ExportFormat format)
    {
        try
        {
            CleanupOldExports();

            var content = await BuildAsync(format);
            var ext = format switch
            {
                ExportFormat.Csv => "csv",
                ExportFormat.Markdown => "md",
                _ => "json"
            };

            var path = Path.Combine(_fileSystem.CacheDirectory, $"{ExportPrefix}{DateTime.Now:yyyyMMdd_HHmmss}.{ext}");
            await File.WriteAllTextAsync(path, content, new UTF8Encoding(false));

            await _shareService.ShareFileAsync("Diarion Export", path);

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Export Error: {ex.Message}");
            return false;
        }
    }

    private string BuildJson()
    {
        var sb = new StringBuilder();
        sb.Append('{');
        for (int i = 0; i < AllCollections.Length; i++)
        {
            var name = AllCollections[i];
            var array = new BsonArray(_dbContext.GetCollection<BsonDocument>(name).FindAll());
            if (i > 0)
            {
                sb.Append(',');
            }
            sb.Append('"').Append(name).Append("\":").Append(JsonSerializer.Serialize(array));
        }
        sb.Append('}');
        return sb.ToString();
    }

    private string BuildFinanceCsv()
    {
        var accountNames = _dbContext.GetCollection<Account>(DatabaseConstants.AccountsCollection)
            .FindAll()
            .ToDictionary(a => a.Id, AccountLocalization.ResolveName);

        var sb = new StringBuilder();
        sb.Append("Date,Type,Account,Category,Amount,Note\n");

        var rows = _dbContext.GetCollection<FinanceTransaction>(DatabaseConstants.FinanceCollection)
            .FindAll()
            .OrderBy(t => t.Date);

        foreach (var t in rows)
        {
            sb.Append(t.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
              .Append(t.Type).Append(',')
              .Append(Csv(AccountName(accountNames, t.AccountId))).Append(',')
              .Append(Csv(t.Category)).Append(',')
              .Append(t.Amount.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(Csv(t.Note)).Append('\n');
        }

        var transfers = _dbContext.GetCollection<Transfer>(DatabaseConstants.TransfersCollection)
            .FindAll()
            .OrderBy(t => t.Date)
            .ToList();

        if (transfers.Count > 0)
        {
            // Transfers are neither income nor expense, so they get their own block rather than a
            // third Type value that would break anything summing the first table.
            sb.Append("\nDate,From,To,Amount,Note\n");
            foreach (var t in transfers)
            {
                sb.Append(t.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                  .Append(Csv(AccountName(accountNames, t.FromAccountId))).Append(',')
                  .Append(Csv(AccountName(accountNames, t.ToAccountId))).Append(',')
                  .Append(t.Amount.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(Csv(t.Note)).Append('\n');
            }
        }

        return sb.ToString();
    }

    private static string AccountName(Dictionary<Guid, string> names, Guid? id) =>
        id is Guid key && names.TryGetValue(key, out var name) ? name : string.Empty;

    private string BuildDiaryMarkdown()
    {
        var sb = new StringBuilder();
        sb.Append("# Diary Export\n\n");

        var entries = _dbContext.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection)
            .FindAll()
            .OrderBy(e => e.Date);

        foreach (var e in entries)
        {
            sb.Append("## ").Append(e.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n');
            if (e.Emotion != Emotion.None)
            {
                sb.Append("- Mood: ").Append(e.Emotion).Append('\n');
            }
            if (e.HourlyMood.Count > 0)
            {
                var hours = e.HourlyMood
                    .Where(h => h.Mood != Emotion.None)
                    .OrderBy(h => h.Hour)
                    .Select(h => $"{h.Hour:00}:00 {h.Mood}");
                sb.Append("- Mood by hour: ").Append(string.Join(", ", hours)).Append('\n');
            }
            if (e.SleepQuality > 0)
            {
                sb.Append("- Sleep quality: ").Append(e.SleepQuality).Append('/').Append(DiaryEntry.MaxRating).Append('\n');
            }
            AppendField(sb, "Gratitude", e.Gratitude);
            AppendField(sb, "Triggers", e.Triggers);
            AppendField(sb, "Soul food", e.SoulFood);
            AppendField(sb, "Support for others", e.SupportForOthers);
            if (!string.IsNullOrWhiteSpace(e.Content))
            {
                sb.Append('\n').Append(e.Content.Trim()).Append('\n');
            }
            sb.Append('\n');
        }

        return sb.ToString();
    }

    private static void AppendField(StringBuilder sb, string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            sb.Append("- ").Append(label).Append(": ")
              .Append(value.Replace("\r", " ").Replace("\n", " ").Trim()).Append('\n');
        }
    }

    private static string Csv(string value)
    {
        value ??= string.Empty;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }

    private void CleanupOldExports()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_fileSystem.CacheDirectory, $"{ExportPrefix}*"))
            {
                try { File.Delete(file); } catch { /* best-effort */ }
            }
        }
        catch
        {
            // Best-effort hygiene.
        }
    }
}
