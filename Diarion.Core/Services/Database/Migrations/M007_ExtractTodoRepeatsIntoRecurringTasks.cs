using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Diarion.Models;
using LiteDB;

namespace Diarion.Services.Database.Migrations;

/// <summary>
/// Turns each legacy daily-repeat series into a <see cref="RecurringTask"/> and stamps its instances with
/// the rule's id. The three fields the series used to be made of — <c>IsDailyRepeat</c>,
/// <c>RepeatEndDate</c>, <c>RepeatGroupId</c> — are removed.
///
/// Todos are read as raw <see cref="BsonDocument"/>s because the fields being read no longer exist on the
/// model, and a typed <c>Update</c> would rewrite the whole document and drop them before they could be
/// read. Rules are written through the typed collection: it is new, so there is no legacy shape on that
/// side and no need to guess how the mapper writes an enum or a TimeSpan. Template values come from a
/// typed read of the one row they are copied from, for the same reason.
///
/// Idempotent per document, keyed on the absence of the legacy fields. Per document rather than per
/// collection because the runner has no transaction: a run interrupted halfway must resume and finish.
/// </summary>
public sealed class M007_ExtractTodoRepeatsIntoRecurringTasks : IMigration
{
    public int ToVersion => 7;

    public void Up(LiteDatabase db)
    {
        var raw = db.GetCollection(DatabaseConstants.TodosCollection);
        var typed = db.GetCollection<TodoItem>(DatabaseConstants.TodosCollection);
        var rules = db.GetCollection<RecurringTask>(DatabaseConstants.RecurringTasksCollection);

        // A repeat is a row flagged as one OR a row carrying an end date. The second half is not
        // redundant: turning a series off stamped RepeatEndDate across the whole group but set
        // IsDailyRepeat=false on the one row the user edited. Selecting on the flag alone orphans exactly
        // that row, and an orphan loses the pin that keeps auto-migration from dragging it forward — so
        // every user who ever turned a repeat off would find that instance walking into today.
        var legacy = raw.FindAll().Where(IsUnmigratedRepeat).ToList();
        if (legacy.Count == 0) return;

        // Rows that were never repeats keep a stale IsDailyRepeat=false. Left alone deliberately: the
        // field is gone from the model, so the next typed write of the row drops it anyway, and rewriting
        // every todo in the database to tidy a false is not worth the risk.
        foreach (var group in legacy.GroupBy(GroupKey))
        {
            var ruleId = DeterministicRuleId(group.Key);
            var instances = group.ToList();

            // Existing rule means an earlier run created it and was interrupted before stamping every row.
            // Do not recompute it: this pass can only see the rows that were left, so anchor, end date and
            // template would all be derived from a fragment of the series.
            if (rules.FindById(ruleId) == null)
            {
                rules.Insert(BuildRule(ruleId, instances, typed));
            }

            foreach (var doc in instances)
            {
                doc["RecurringTaskId"] = ruleId;
                doc.Remove("IsDailyRepeat");
                doc.Remove("RepeatEndDate");
                doc.Remove("RepeatGroupId");
                raw.Update(doc);
            }
        }
    }

    private static RecurringTask BuildRule(Guid ruleId, List<BsonDocument> instances, ILiteCollection<TodoItem> typed)
    {
        var active = instances.Where(IsFlaggedRepeat).ToList();

        // The latest instance was the template under the old scheme, so it stays the template here. A
        // series whose newest instance had been demoted to Medium therefore migrates as Medium — faithful
        // to what the user last saw. The demotion stops ratcheting from here on, because the template no
        // longer lives on an instance.
        var templateDoc = (active.Count > 0 ? active : instances)
            .OrderByDescending(d => CalendarDay(d["TargetDate"]))
            .First();
        var template = typed.FindById(templateDoc["_id"]);

        // Open-ended if any still-flagged row has no end date, matching the old generation filter, which
        // kept producing while a single such row survived.
        DateTime? endDate = null;
        if (!active.Any(d => !HasEndDate(d)))
        {
            var ends = instances.Where(HasEndDate).Select(d => CalendarDay(d["RepeatEndDate"])).ToList();
            if (ends.Count > 0) endDate = ends.Max();
        }

        return new RecurringTask
        {
            Id = ruleId,
            TaskDescription = template?.TaskDescription ?? string.Empty,
            Priority = template?.Priority ?? TodoPriority.Medium,
            HasTime = template?.HasTime ?? false,
            TargetTime = template?.TargetTime ?? TimeSpan.Zero,
            HasReminder = template?.HasReminder ?? false,
            Recurrence = new RecurrenceRule
            {
                // Kind is left unwritten: every legacy repeat was daily, and Daily is the default. Writing
                // it would mean choosing between the name and the ordinal, which is the ambiguity M006 had
                // to carry helpers for.
                Anchor = instances.Min(d => CalendarDay(d["TargetDate"])),
                EndDate = endDate
            }
        };
    }

    /// <summary>
    /// The rule's id, derived from the group key rather than generated. The runner has no transaction, so
    /// a run interrupted mid-group resumes seeing only the unstamped remainder: with a fresh Guid that
    /// remainder would become a second rule for the same series, and two rules mean a duplicate task every
    /// day, forever. Two legacy groups sharing a description collapse onto one id, which is correct —
    /// under the old definition they were one group.
    /// </summary>
    private static Guid DeterministicRuleId(string groupKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("todo-repeat:" + groupKey));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static string GroupKey(BsonDocument doc)
    {
        var groupId = doc.ContainsKey("RepeatGroupId") && doc["RepeatGroupId"].IsString
            ? doc["RepeatGroupId"].AsString
            : null;

        // IsNullOrEmpty, not a null check: the old grouping treated an empty string and a null the same,
        // and a series split between the two would migrate as two rules.
        return string.IsNullOrEmpty(groupId)
            ? (doc.ContainsKey("TaskDescription") ? doc["TaskDescription"].AsString ?? string.Empty : string.Empty)
            : groupId;
    }

    private static bool IsUnmigratedRepeat(BsonDocument doc)
        => IsFlaggedRepeat(doc) || HasEndDate(doc);

    private static bool IsFlaggedRepeat(BsonDocument doc)
        => doc.ContainsKey("IsDailyRepeat") && doc["IsDailyRepeat"].IsBoolean && doc["IsDailyRepeat"].AsBoolean;

    private static bool HasEndDate(BsonDocument doc)
        => doc.ContainsKey("RepeatEndDate") && doc["RepeatEndDate"].IsDateTime;

    /// <summary>
    /// LiteDB writes DateTime as UTC and hands it back in whichever kind the read produced. Taking .Date
    /// off a UTC value that is nine in the evening local time names the previous day, which would move a
    /// whole series by one.
    /// </summary>
    private static DateTime CalendarDay(BsonValue value)
    {
        var dt = value.AsDateTime;
        if (dt.Kind == DateTimeKind.Utc) dt = dt.ToLocalTime();
        return dt.Date;
    }
}
