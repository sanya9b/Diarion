using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services.Database;
using LiteDB;

namespace Diarion.Services;

public class GuidedPromptService : IGuidedPromptService
{
    private readonly IDatabaseContext _dbContext;

    public GuidedPromptService(IDatabaseContext dbContext)
    {
        _dbContext = dbContext;
    }

    private ILiteCollection<GuidedPrompt> PromptsCollection =>
        _dbContext.GetCollection<GuidedPrompt>(DatabaseConstants.GuidedPromptsCollection);

    public Task<PromptLibrary> GetLibraryAsync()
        => Task.Run(() => new PromptLibrary(PromptsCollection.FindAll()));

    public Task<GuidedPrompt?> GetByIdAsync(Guid id)
        => Task.Run(() => (GuidedPrompt?)PromptsCollection.FindById(id));

    public Task AddAsync(GuidedPrompt prompt)
    {
        return Task.Run(() =>
        {
            // Unlike habits, a new prompt lands at the end of the library rather than at int.MaxValue,
            // so the list reads in creation order before the user ever reorders anything.
            var highest = PromptsCollection.FindAll()
                .Where(p => p.Order != int.MaxValue)
                .Select(p => p.Order)
                .DefaultIfEmpty(-1)
                .Max();

            prompt.Order = highest + 1;
            PromptsCollection.Insert(prompt);
        });
    }

    public Task UpdateAsync(GuidedPrompt prompt)
        => Task.Run(() => PromptsCollection.Update(prompt));

    public Task DeleteAsync(Guid id, DateTime deleteDate)
    {
        return Task.Run(() =>
        {
            var prompt = PromptsCollection.FindById(id);
            if (prompt == null) return;

            prompt.DeletedAt = deleteDate.Date;
            PromptsCollection.Update(prompt);
        });
    }

    public Task UpdateOrderAsync(List<Guid> orderedIds)
    {
        return Task.Run(() =>
        {
            for (int i = 0; i < orderedIds.Count; i++)
            {
                var prompt = PromptsCollection.FindById(orderedIds[i]);
                if (prompt != null)
                {
                    prompt.Order = i;
                    PromptsCollection.Update(prompt);
                }
            }
        });
    }
}
