using System;
using System.Threading.Tasks;
using Diarion.Models;

namespace Diarion.Services;

public interface IGuidedPromptService
{
    /// <summary>The whole collection as a snapshot, deleted rows included so historic entries resolve.</summary>
    Task<PromptLibrary> GetLibraryAsync();

    Task<GuidedPrompt?> GetByIdAsync(Guid id);
    Task AddAsync(GuidedPrompt prompt);
    Task UpdateAsync(GuidedPrompt prompt);

    /// <summary>Soft delete: the prompt stops being offered but stays resolvable for entries that used it.</summary>
    Task DeleteAsync(Guid id, DateTime deleteDate);

    Task UpdateOrderAsync(System.Collections.Generic.List<Guid> orderedIds);
}
