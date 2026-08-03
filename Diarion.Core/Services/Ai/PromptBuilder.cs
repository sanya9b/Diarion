using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Diarion.Models.Ai;

namespace Diarion.Services.Ai;

/// <summary>A numbered context block, and the entry it came from.</summary>
/// <param name="Marker">The number the model is told to cite, starting at 1.</param>
public sealed record ChatCitation(int Marker, string SourceKind, string SourceId, DateTime SourceDate, string Text);

/// <param name="IsAnswerable">False when retrieval was too weak to answer from. The model is not called.</param>
public sealed record ChatPrompt(bool IsAnswerable, string Text, IReadOnlyList<ChatCitation> Citations);

/// <summary>
/// Turns a question plus retrieved passages into the exact text the model sees.
/// </summary>
/// <remarks>
/// The refusal gate lives here, before generation, rather than in the prompt. A small model told
/// "say you do not know" will still sometimes answer; a model that is never invoked cannot.
/// </remarks>
public static class PromptBuilder
{
    /// <summary>
    /// Below this a passage is noise. Measured rather than guessed — a relevant single-word query
    /// scored 0.306 against its entry while unrelated text reached 0.259.
    /// See specs/13-on-device-ai.md.
    /// </summary>
    public const float MinRelevance = 0.28f;

    /// <summary>
    /// Two passages, not one. A single weak match is a coincidence often enough that answering from
    /// it produces confident nonsense about the user's own life.
    /// </summary>
    public const int MinPassages = 2;

    /// <summary>How many passages reach the prompt after diversification.</summary>
    public const int MaxPassages = 8;

    /// <summary>
    /// Trade-off between relevance and variety when picking passages. Pure relevance returns eight
    /// paraphrases of the same evening; this keeps most of the relevance and spends the rest on
    /// covering different days.
    /// </summary>
    public const float MmrLambda = 0.7f;

    /// <summary>Rough character budget for the context blocks — the model's window is not ours to fill.</summary>
    public const int MaxContextChars = 6000;

    public static ChatPrompt Build(string question, IReadOnlyList<ScoredChunk> retrieved)
    {
        ArgumentNullException.ThrowIfNull(retrieved);

        if (string.IsNullOrWhiteSpace(question))
        {
            return new ChatPrompt(false, string.Empty, []);
        }

        var relevant = retrieved.Where(c => c.Score >= MinRelevance).ToList();
        if (relevant.Count < MinPassages)
        {
            return new ChatPrompt(false, string.Empty, []);
        }

        var selected = Diversify(relevant, MaxPassages);
        var citations = selected
            .Select((c, i) => new ChatCitation(
                i + 1,
                c.Chunk.SourceKind,
                c.Chunk.SourceId,
                c.Chunk.SourceDate,
                c.Chunk.Text))
            .ToList();

        return new ChatPrompt(true, Compose(question, citations), citations);
    }

    /// <summary>
    /// Maximal marginal relevance: each pick trades similarity to the question against similarity
    /// to what is already picked.
    /// </summary>
    private static List<ScoredChunk> Diversify(List<ScoredChunk> candidates, int limit)
    {
        var pool = candidates.OrderByDescending(c => c.Score).ToList();
        var chosen = new List<ScoredChunk>();
        var chosenVectors = new List<float[]>();

        while (chosen.Count < limit && pool.Count > 0)
        {
            ScoredChunk? best = null;
            var bestValue = float.NegativeInfinity;
            float[]? bestVector = null;

            foreach (var candidate in pool)
            {
                var vector = EmbeddingMath.FromBytes(candidate.Chunk.Vector);

                var redundancy = chosenVectors.Count == 0
                    ? 0f
                    : chosenVectors
                        .Where(v => v.Length == vector.Length)
                        .Select(v => EmbeddingMath.DotNormalized(v, vector))
                        .DefaultIfEmpty(0f)
                        .Max();

                var value = (MmrLambda * candidate.Score) - ((1 - MmrLambda) * redundancy);
                if (value > bestValue)
                {
                    bestValue = value;
                    best = candidate;
                    bestVector = vector;
                }
            }

            if (best is null || bestVector is null)
            {
                break;
            }

            chosen.Add(best);
            chosenVectors.Add(bestVector);
            pool.Remove(best);
        }

        // Chronological, so the model reads a life in the order it was lived.
        return chosen.OrderBy(c => c.Chunk.SourceDate).ToList();
    }

    private static string Compose(string question, IReadOnlyList<ChatCitation> citations)
    {
        var builder = new StringBuilder();

        // Ukrainian, because the diary is. Instructing a multilingual model in the answer's language
        // is what stops it drifting into English mid-sentence.
        builder.AppendLine("Ти — помічник, який відповідає ВИКЛЮЧНО за записами щоденника нижче.");
        builder.AppendLine("Правила:");
        builder.AppendLine("1. Використовуй лише те, що написано в записах. Нічого не додавай від себе.");
        builder.AppendLine("2. Після кожного твердження став номер запису у квадратних дужках, напр. [1].");
        builder.AppendLine("3. Якщо у записах немає відповіді — так і скажи, не вигадуй.");
        builder.AppendLine("4. Відповідай українською, стисло, ОДНИМ реченням.");
        builder.AppendLine("5. Не перелічуй записи. Обери лише ті, що справді відповідають на питання.");
        builder.AppendLine();

        // The example is the single most valuable line in this method. Without it Qwen3-1.7B
        // answered every question by echoing all four passages back — the block format invites
        // enumeration — and scored 1/4 on the Ukrainian evaluation. With it, 4/4. Nothing about the
        // model changed; the instruction alone could not convey the shape of an answer.
        builder.AppendLine("Приклад.");
        builder.AppendLine("ЗАПИСИ:");
        builder.AppendLine("[1] 2 травня 2026: Цілий день дощ, нікуди не виходив.");
        builder.AppendLine("[2] 5 травня 2026: Зустрівся з Олегом, довго гуляли містом.");
        builder.AppendLine("ПИТАННЯ: З ким я бачився?");
        builder.AppendLine("ВІДПОВІДЬ: Ви гуляли містом з Олегом [2].");
        builder.AppendLine();
        builder.AppendLine("ЗАПИСИ:");

        var used = 0;
        foreach (var citation in citations)
        {
            var date = citation.SourceDate.ToString("d MMMM yyyy", CultureInfo.CurrentCulture);
            var text = citation.Text.Trim();

            // The date is what makes a "when did I..." question answerable at all.
            var block = $"[{citation.Marker}] {date}: {text}";
            if (used + block.Length > MaxContextChars)
            {
                break;
            }

            builder.AppendLine(block);
            used += block.Length;
        }

        builder.AppendLine();
        builder.AppendLine($"ПИТАННЯ: {question.Trim()}");
        builder.AppendLine("ВІДПОВІДЬ:");

        return builder.ToString();
    }
}
