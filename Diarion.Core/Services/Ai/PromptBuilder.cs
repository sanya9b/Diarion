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
/// How much of the model's context, and of the device's memory, one answer may spend.
/// </summary>
/// <remarks>
/// Everything here is memory, not quality-for-its-own-sake. ORT-GenAI sizes the KV cache from
/// <c>max_length</c>, which is the prompt plus the answer, and Qwen3-1.7B spends roughly 112 KiB
/// per token on it — 28 layers by 8 KV heads by 128, in fp16. A full-budget prompt reaches about
/// 2800 tokens, so the cache alone is around 340 MB on top of 1.1 GB of weights. On a phone with a
/// hard per-app ceiling that is the difference between an answer and the system killing the app
/// without a diagnostic.
///
/// The refusal thresholds are deliberately not in here. What counts as an answerable question is a
/// property of the diary, not of the phone, and a cheap device must not get confident answers a
/// better one would have refused.
/// </remarks>
/// <param name="MaxPassages">How many passages reach the prompt after diversification.</param>
/// <param name="MaxContextChars">Rough character budget for the context blocks.</param>
/// <param name="MaxAnswerTokens">Ceiling on the generated answer.</param>
/// <param name="UnloadEncoderBeforeGenerating">
/// Drop the encoder once the question has become a vector. Costs a reload on the next question and
/// hands back its memory for the length of the decode, which is when the generator wants it most.
/// </param>
public sealed record PromptBudget(
    int MaxPassages,
    int MaxContextChars,
    int MaxAnswerTokens,
    bool UnloadEncoderBeforeGenerating)
{
    /// <summary>
    /// What the 2026-08-05 evaluation measured: 22/30 answerable and 10/10 refusals. Any change to
    /// these numbers invalidates that run, so they are the baseline rather than a default anyone
    /// should tune casually.
    /// </summary>
    public static readonly PromptBudget Full = new(8, 6000, 320, UnloadEncoderBeforeGenerating: false);

    /// <summary>
    /// For devices at the bottom of what can run the generator at all. Halving the context takes
    /// the KV cache from roughly 340 MB to 170, and dropping the encoder returns another ~150 MB
    /// for the decode. Four passages instead of eight is a real cost to breadth — the trade is
    /// against not offering chat on these devices, not against offering it in full.
    /// </summary>
    public static readonly PromptBudget Tight = new(4, 2600, 224, UnloadEncoderBeforeGenerating: true);
}

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

    /// <summary>
    /// The exception to <see cref="MinPassages"/>: one passage this close answers on its own.
    ///
    /// <remarks>
    /// A diary keeps most specific facts in exactly one entry, so demanding a second passage
    /// refuses the questions retrieval handles best. In the 2026-08-05 evaluation four answerable
    /// questions died at the gate in 0.2 s with the right entry ranked first — «Скільки коштував
    /// велосипед?» scored 0.646 and was still refused. The ten unanswerable questions never got
    /// past 0.445, so the bar sits in the gap between them: it admits three of those four and none
    /// of the unanswerable ones. The gap is 0.044 wide on forty questions, which is thin — this is
    /// the constant to re-measure once there is real usage.
    /// </remarks>
    /// </summary>
    public const float MinStandaloneRelevance = 0.47f;

    /// <summary>
    /// What a passage must score to count as support for opening the gate, as opposed to merely
    /// being worth showing.
    ///
    /// <remarks>
    /// <see cref="MinRelevance"/> answers "is this worth putting in the prompt"; this answers "does
    /// this corroborate anything". Conflating them let a question the diary cannot answer through
    /// on one mediocre match plus one near-noise one: «Яку машину я купив?» (0.445 + 0.311) was
    /// answered with the bicycle, «Куди я їздив у відпустку за кордон?» (0.397 + 0.281) with Lviv.
    /// Of the nine evaluation questions that retrieved exactly two passages, the five answerable
    /// ones all scored 0.49 or better and the four unanswerable ones 0.445 or worse — the weak
    /// second passage was never what made the difference, so it should not be what opens the gate.
    /// </remarks>
    /// </summary>
    public const float SubstantialRelevance = 0.35f;

    /// <summary>
    /// Trade-off between relevance and variety when picking passages. Pure relevance returns eight
    /// paraphrases of the same evening; this keeps most of the relevance and spends the rest on
    /// covering different days.
    /// </summary>
    public const float MmrLambda = 0.7f;

    /// <summary>
    /// Qwen3's soft switch out of its reasoning mode. Its chat template only inserts the empty
    /// think block when <c>enable_thinking</c> is defined and false, and ORT-GenAI 0.14.1 offers no
    /// way to define it, so the switch travels in the message. Without it the whole 320-token budget
    /// went to an English monologue that ran out mid-sentence before reaching an answer.
    /// A model that has never heard of it reads nine stray characters; <see cref="ReasoningFilter"/>
    /// is what makes the outcome certain either way.
    /// </summary>
    private const string NoThinkSwitch = " /no_think";

    /// <param name="budget">
    /// How much context and memory this device may spend. Optional, and defaulting to
    /// <see cref="PromptBudget.Full"/>, because that is what the evaluation measured — a caller
    /// that says nothing gets the measured behaviour.
    /// </param>
    public static ChatPrompt Build(string question, IReadOnlyList<ScoredChunk> retrieved, PromptBudget? budget = null)
    {
        ArgumentNullException.ThrowIfNull(retrieved);

        budget ??= PromptBudget.Full;

        if (string.IsNullOrWhiteSpace(question))
        {
            return new ChatPrompt(false, string.Empty, []);
        }

        var relevant = retrieved.Where(c => c.Score >= MinRelevance).ToList();

        // Two corroborating passages, or one strong enough to stand alone. Passages between
        // MinRelevance and SubstantialRelevance still reach the prompt as context; they just do
        // not get a vote on whether the question is answerable at all.
        var supporting = relevant.Count(c => c.Score >= SubstantialRelevance);
        var answerable = supporting >= MinPassages
            || relevant.Any(c => c.Score >= MinStandaloneRelevance);

        if (!answerable)
        {
            return new ChatPrompt(false, string.Empty, []);
        }

        var selected = Diversify(relevant, budget.MaxPassages);
        var citations = selected
            .Select((c, i) => new ChatCitation(
                i + 1,
                c.Chunk.SourceKind,
                c.Chunk.SourceId,
                c.Chunk.SourceDate,
                c.Chunk.Text))
            .ToList();

        return new ChatPrompt(true, Compose(question, citations, budget.MaxContextChars), citations);
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

    private static string Compose(string question, IReadOnlyList<ChatCitation> citations, int maxContextChars)
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
            if (used + block.Length > maxContextChars)
            {
                break;
            }

            builder.AppendLine(block);
            used += block.Length;
        }

        builder.AppendLine();
        // On the question line, where the evaluation run measured it, not after "ВІДПОВІДЬ:" —
        // there it would sit exactly where the answer is supposed to begin.
        builder.AppendLine($"ПИТАННЯ: {question.Trim()}{NoThinkSwitch}");
        builder.AppendLine("ВІДПОВІДЬ:");

        return builder.ToString();
    }
}
