using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Diarion.Models.Ai;
using Diarion.Services.Ai;
using Diarion.Services.Database;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The Ukrainian evaluation the spec asks for, run through the shipping code rather than beside it.
/// </summary>
/// <remarks>
/// Every earlier measurement was a Python harness that rebuilt the prompt itself, and that is how a
/// real defect stayed hidden: the harness had quietly worked around what the app actually sent. So
/// this drives <see cref="DiaryChatService"/> — the production retrieval, the production prompt,
/// the production citation gate — and only the diary underneath it is synthetic.
///
/// Two numbers come out, and they measure different promises. Of the questions the diary answers,
/// how many come back with the right source; of the questions it does not, how many are refused.
/// The second is the one the feature is sold on, and it is the weaker of the two: the gate catches
/// an answer that cites nothing, and cannot catch an answer that cites the wrong thing.
/// </remarks>
public class UkrainianChatEvaluationTests
{
    private const string EnableVariable = "DIARION_AI_EVAL";

    private static string CacheRoot =>
        Environment.GetEnvironmentVariable("DIARION_AI_MODEL_CACHE")
        ?? Path.Combine(Path.GetTempPath(), "diarion-ai-models");

    private static string ReportPath =>
        Environment.GetEnvironmentVariable("DIARION_AI_EVAL_REPORT")
        ?? Path.Combine(Path.GetTempPath(), "diarion-ua-eval.txt");

    /// <summary>
    /// Floors set just under what was measured on 2026-08-05 — 20/30 and 8/10 — so a regression
    /// trips and the ordinary wobble of a quantised model does not.
    /// </summary>
    private const double MinAnswerAccuracy = 0.60;

    private const double MinRefusalRate = 0.70;

    [EnvironmentGatedFact(EnableVariable, "Runs the real 1.7B model over 40 questions; takes minutes.")]
    public async Task TheModelAnswersUkrainianFromTheDiaryOrRefuses()
    {
        using var database = new DatabaseContext(useInMemory: true);
        var store = new LiteDbVectorStore(database);
        var embedder = await LoadEmbedderAsync();

        await IndexAsync(store, embedder);

        using var generator = new OnnxGenAiTextGenerator(new EvalGenerativeLocator());
        generator.IsAvailable.Should().BeTrue(
            "the generative model has to be installed before its Ukrainian can be measured");

        var chat = new DiaryChatService(store, embedder, generator, new AlwaysAvailable());

        var report = new StringBuilder();
        var answerable = Questions.Where(q => q.Sources.Length > 0).ToList();
        var unanswerable = Questions.Where(q => q.Sources.Length == 0).ToList();
        var answeredWell = 0;
        var refusedWell = 0;

        foreach (var question in Questions)
        {
            var started = DateTime.UtcNow;
            var result = await AskAsync(chat, question.Text);
            var elapsed = DateTime.UtcNow - started;

            var cited = result.Citations.Select(c => c.SourceId).ToArray();
            var expected = question.Sources;

            // Strict on purpose: a citation the question did not ask for is as wrong as a missing
            // one, because the user opens it and finds an unrelated day.
            var wellSourced = cited.Length > 0 && cited.All(expected.Contains);
            var verdict = expected.Length == 0
                ? result.IsRefusal
                : !result.IsRefusal && wellSourced;

            if (verdict && expected.Length == 0)
            {
                refusedWell++;
            }
            else if (verdict)
            {
                answeredWell++;
            }

            report.AppendLine(
                $"[{(verdict ? "OK" : "NO")}] {question.Text}");
            report.AppendLine(
                $"     expected={Describe(expected)} cited={Describe(cited)} " +
                $"refusal={result.Refusal} {elapsed.TotalSeconds:F1}s");
            report.AppendLine($"     {Collapse(result.Text)}");
            report.AppendLine();
        }

        var answerAccuracy = (double)answeredWell / answerable.Count;
        var refusalRate = (double)refusedWell / unanswerable.Count;

        report.AppendLine(
            $">>> answerable: {answeredWell}/{answerable.Count} ({answerAccuracy:P0}) correctly sourced");
        report.AppendLine(
            $">>> unanswerable: {refusedWell}/{unanswerable.Count} ({refusalRate:P0}) refused");

        File.WriteAllText(ReportPath, report.ToString(), Encoding.UTF8);

        using (new FluentAssertions.Execution.AssertionScope())
        {
            answerAccuracy.Should().BeGreaterThanOrEqualTo(MinAnswerAccuracy,
                $"the diary answers these questions; see {ReportPath}");
            refusalRate.Should().BeGreaterThanOrEqualTo(MinRefusalRate,
                $"the diary answers none of these; see {ReportPath}");
        }
    }

    /// <summary>
    /// Where the retrieval gate sits relative to the questions, measured without the generator.
    /// </summary>
    /// <remarks>
    /// Four answerable questions above were refused in a fifth of a second — the model was never
    /// called, so no amount of prompt work would have helped them. Lowering the threshold is the
    /// obvious response and the wrong one to make by feel: five of the eight honest refusals came
    /// from this same gate, not from the citation check, so whatever it admits it also admits for
    /// questions the diary cannot answer. This prints both sides of that trade at once.
    /// </remarks>
    [EnvironmentGatedFact(EnableVariable, "Encoder only; seconds.")]
    public async Task TheRetrievalGateAdmitsAndRefusesAtEachThreshold()
    {
        var embedder = await LoadEmbedderAsync();
        var vectors = new Dictionary<string, float[]>();

        foreach (var entry in Diary)
        {
            vectors[entry.Id] = await embedder.EmbedAsync(entry.Text);
        }

        float[] thresholds = [0.34f, 0.31f, PromptBuilder.MinRelevance, 0.25f, 0.22f, 0.19f];
        var report = new StringBuilder();
        report.AppendLine("question | best | rank of expected | passes at " + string.Join("/", thresholds));
        report.AppendLine();

        var admitted = thresholds.ToDictionary(t => t, _ => (Answerable: 0, Unanswerable: 0));

        foreach (var question in Questions)
        {
            var query = await embedder.EmbedAsync(question.Text);
            var scored = Diary
                .Select(e => (e.Id, Score: EmbeddingMath.DotNormalized(query, vectors[e.Id])))
                .OrderByDescending(s => s.Score)
                .ToList();

            var expectedRank = question.Sources.Length == 0
                ? "—"
                : (scored.FindIndex(s => question.Sources.Contains(s.Id)) + 1).ToString();

            var passes = new List<string>();
            foreach (var threshold in thresholds)
            {
                // The gate as PromptBuilder applies it: enough passages, not just a good one.
                var above = scored.Count(s => s.Score >= threshold);
                var open = above >= PromptBuilder.MinPassages;
                passes.Add(open ? "y" : "·");

                if (!open)
                {
                    continue;
                }

                var counts = admitted[threshold];
                admitted[threshold] = question.Sources.Length == 0
                    ? (counts.Answerable, counts.Unanswerable + 1)
                    : (counts.Answerable + 1, counts.Unanswerable);
            }

            report.AppendLine(
                $"{string.Join(string.Empty, passes)}  best={scored[0].Score:F3} " +
                $"expected@{expectedRank,-2} {question.Text}");
        }

        report.AppendLine();
        foreach (var threshold in thresholds)
        {
            var (answerable, unanswerable) = admitted[threshold];
            report.AppendLine(
                $">>> {threshold:F2}: reaches the model for {answerable}/30 answerable, " +
                $"{unanswerable}/10 unanswerable");
        }

        File.WriteAllText(
            Path.ChangeExtension(ReportPath, ".gate.txt"), report.ToString(), Encoding.UTF8);

        // Whatever the tuning says, the shipped threshold has to keep letting real questions through.
        admitted[PromptBuilder.MinRelevance].Answerable.Should().BeGreaterThan(20);
    }

    private static string Describe(IReadOnlyCollection<string> sources) =>
        sources.Count == 0 ? "—" : string.Join(",", sources.OrderBy(s => s));

    private static string Collapse(string text) =>
        text.Length == 0 ? "(відмова)" : text.ReplaceLineEndings(" ");

    private static async Task<ChatResult> AskAsync(IDiaryChatService chat, string question)
    {
        await foreach (var delta in chat.AskAsync(question))
        {
            if (delta is { IsComplete: true, Answer: not null })
            {
                return delta.Answer;
            }
        }

        throw new InvalidOperationException("The chat stream ended without a final message.");
    }

    private static async Task IndexAsync(IVectorStore store, ITextEmbedder embedder)
    {
        var chunks = new List<EmbeddingChunk>();

        foreach (var entry in Diary)
        {
            var texts = TextChunker.ChunkText(entry.Text);
            var vectors = await embedder.EmbedBatchAsync(texts);

            chunks.AddRange(texts.Select((text, ordinal) => new EmbeddingChunk
            {
                Id = EmbeddingChunk.BuildId(EmbeddingSourceKind.Diary, entry.Id, ordinal),
                SourceKind = EmbeddingSourceKind.Diary,
                SourceId = entry.Id,
                Ordinal = ordinal,
                SourceDate = entry.Date,
                Text = text,
                ContentHash = entry.Id,
                ModelId = embedder.ModelId,
                Dim = embedder.Dimensions,
                Vector = EmbeddingMath.ToBytes(vectors[ordinal]),
            }));
        }

        store.UpsertBatch(chunks);
        store.CountForModel(embedder.ModelId).Should().Be(chunks.Count);
    }

    private static async Task<OnnxTextEmbedder> LoadEmbedderAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
        var downloads = new EvalPathProvider();
        var service = new ModelDownloadService(http, downloads);
        var encoder = AiModelCatalog.MiniLmEncoder;

        if (service.GetState(encoder) != ModelInstallState.Installed)
        {
            (await service.DownloadAsync(encoder)).Should().BeTrue();
        }

        return new OnnxTextEmbedder(new EvalEmbeddingLocator());
    }

    private sealed record Entry(string Id, DateTime Date, string Text);

    private sealed record Question(string Text, params string[] Sources);

    /// <summary>
    /// Twenty entries, each holding one fact nothing else holds, so a wrong citation is unambiguous
    /// rather than arguable. Written at the length people actually write at — a one-line diary
    /// retrieves differently from a real one, and the earlier harnesses were measuring one-liners.
    /// </summary>
    private static readonly Entry[] Diary =
    [
        new("e01", new DateTime(2026, 3, 2),
            "Перший день на новій роботі. Тімліда звати Марта, вона зустріла мене на рецепції і "
            + "півдня водила по офісу, який виявився на Подолі, у старому будинку з високими "
            + "вікнами. Дали ноутбук, доступи і купу документів на підпис. Голова гуде від імен."),
        new("e02", new DateTime(2026, 3, 9),
            "Злягла застуда. Температура під тридцять вісім, горло дере, у голові вата. Взяв "
            + "лікарняний на три дні, лежав під ковдрою і пив чай з лимоном літрами. Дивитися "
            + "нічого не міг, навіть серіал не пішов."),
        new("e03", new DateTime(2026, 3, 17),
            "Нарешті купив велосипед. Синій, з тонкими шинами, за чотирнадцять тисяч — довго "
            + "вибирав між ним і дорожчим, але цей сів як влитий. Проїхався додому через півміста "
            + "і зрозумів, що фізична форма в мене нікудишня."),
        new("e04", new DateTime(2026, 3, 24),
            "Склав іспит з водіння з другої спроби. Першого разу завалив паралельне паркування, "
            + "сьогодні воно вийшло з першого разу. Інструктор сказав, що я забагато думаю і мало "
            + "дивлюся в дзеркала. Права будуть за тиждень."),
        new("e05", new DateTime(2026, 4, 1),
            "Посварився з Тарасом через гроші. Він позичив ще взимку і щоразу переводить розмову, "
            + "а сьогодні я не витримав і сказав усе прямо. Він образився. Тиждень не розмовляємо, "
            + "і я не знаю, хто має написати перший."),
        new("e06", new DateTime(2026, 4, 8),
            "Ходив на концерт у Жовтневий палац. Грали «Один в каное», зал підспівував майже "
            + "кожну пісню. Стояв у проході, бо квиток був поганий, але звук усе одно накрив. "
            + "Вийшов на вулицю о пів на одинадцяту з дзвоном у вухах."),
        new("e07", new DateTime(2026, 4, 15),
            "Почав вивчати іспанську. По двадцять хвилин щодня, зранку до кави, поки голова "
            + "свіжа. Поки що тільки вимова і найпростіші фрази, але приємно, що літери читаються "
            + "як пишуться. Мета — за рік дивитися серіал без субтитрів."),
        new("e08", new DateTime(2026, 4, 22),
            "Пересадив фікус. Купив землю і горщик на два розміри більший, бо коріння вже лізло "
            + "з дренажних отворів. Руки по лікті в землі, підвіконня в бруді, але рослина явно "
            + "видихнула. Полив і поставив далі від батареї."),
        new("e09", new DateTime(2026, 5, 3),
            "Три дні у Львові. Їхав потягом уночі, спав погано, зате приїхав на світанку в "
            + "порожнє місто. Дощ ішов усі три дні без перерви, тож половину часу я просидів по "
            + "кав'ярнях, і це виявилось найкращою частиною поїздки."),
        new("e10", new DateTime(2026, 5, 11),
            "Розболівся зуб, і то так, що о третій ночі я сидів на кухні. Зранку записався до "
            + "стоматолога, він розсвердлив, почистив і поставив пломбу. Сорок хвилин страху й "
            + "півгодини оніміла щелепа, але біль зник як не було."),
        new("e11", new DateTime(2026, 5, 19),
            "Мене підвищили. Марта викликала на розмову, і я весь коридор думав, що щось не так, "
            + "а вона сказала про нову посаду і плюс двадцять відсотків до зарплати. Здається, я "
            + "неадекватно тихо відреагував. Увечері нарешті дійшло."),
        new("e12", new DateTime(2026, 5, 27),
            "Пробіг перший напівмарафон. Дві години чотирнадцять хвилин — не швидко, але я жодного "
            + "разу не перейшов на крок. Останні п'ять кілометрів тримався на самій упертості й "
            + "людях уздовж траси. На фініші плакав, і мені не соромно."),
        new("e13", new DateTime(2026, 6, 4),
            "Мама приїхала на тиждень. Одразу зайняла кухню і зварила борщ на трилітрову каструлю, "
            + "хоча я просив не готувати. Ходили разом у парк, вона розповідала про сусідів, яких "
            + "я не пам'ятаю. Квартира вперше за рік пахне домом."),
        new("e14", new DateTime(2026, 6, 12),
            "Зламався ноутбук — просто не увімкнувся зранку. Відніс у ремонт, сказали, що "
            + "материнська плата, і тримали чотири дні. Чотири дні без роботи, і виявилось, що я "
            + "не знаю, чим себе зайняти без екрана."),
        new("e15", new DateTime(2026, 6, 20),
            "Перше побачення з Іриною. Пили каву на Рейтарській, сиділи три години, і я жодного "
            + "разу не глянув на телефон. Вона сміється так, що хочеться сказати щось ще смішніше. "
            + "Домовилися побачитись у суботу."),
        new("e16", new DateTime(2026, 6, 28),
            "Третю ніч поспіль безсоння. Лягаю об одинадцятій, а засинаю близько четвертої, і всі "
            + "ці години голова перебирає дурниці по колу. Зранку тіло як чуже. Спробував не пити "
            + "каву після обіду — поки не допомогло."),
        new("e17", new DateTime(2026, 7, 5),
            "Складав шафу з Ікеї чотири години. Інструкція без жодного слова, тільки чоловічок, "
            + "який усе робить правильно з першого разу. Наприкінці лишилася одна зайва деталь, і "
            + "я вирішив, що так і було задумано. Шафа стоїть, дверцята зачиняються."),
        new("e18", new DateTime(2026, 7, 13),
            "Загубив гаманець у метро. Зрозумів уже на ескалаторі, повернувся — нема. Написав "
            + "заяву, подумки попрощався з картками й документами. А наступного дня подзвонили зі "
            + "станції: хтось здав, усе на місці, навіть готівка."),
        new("e19", new DateTime(2026, 7, 21),
            "Почав ходити в басейн двічі на тиждень. Плавати я толком не вмію, тож перше заняття "
            + "було переважно про те, як дихати і не панікувати. Тренер спокійний, вода тепла, і "
            + "після сорока хвилин у голові тиша, якої давно не було."),
        new("e20", new DateTime(2026, 7, 29),
            "Кіт Мурчик заліз на дах сусіднього гаража і не міг злізти. Дві години його звідти "
            + "знімали: спершу я з драбиною, потім сусід, потім ми обидва з ковбасою. Зліз сам, "
            + "коли всім набридло. Дивився на нас, ніби це ми поводились нерозумно."),
    ];

    /// <summary>
    /// Thirty questions the diary answers and ten it does not. The ten are the point: a model that
    /// scores well on the thirty and invents on the ten is worse than useless in a diary.
    /// </summary>
    private static readonly Question[] Questions =
    [
        new("Як звати мого тімліда?", "e01"),
        new("Де розташований офіс, у якому я працюю?", "e01"),
        new("Коли я хворів на застуду?", "e02"),
        new("Скільки коштував велосипед?", "e03"),
        new("Якого кольору мій велосипед?", "e03"),
        new("З якої спроби я склав іспит з водіння?", "e04"),
        new("Через що ми посварилися з Тарасом?", "e05"),
        new("На чий концерт я ходив?", "e06"),
        new("Яку мову я почав вивчати?", "e07"),
        new("Скільки часу на день я приділяв мові?", "e07"),
        new("Яку рослину я пересадив?", "e08"),
        new("Куди я їздив у травні?", "e09"),
        new("Яка була погода у Львові?", "e09"),
        new("Що в мене було з зубом?", "e10"),
        new("На скільки зросла моя зарплата?", "e11"),
        new("За який час я пробіг напівмарафон?", "e12"),
        new("Хто приїжджав до мене в червні?", "e13"),
        new("Що готувала мама?", "e13"),
        new("Що трапилося з ноутбуком?", "e14"),
        new("З ким у мене було перше побачення?", "e15"),
        new("Де ми пили каву на першому побаченні?", "e15"),
        new("Що в мене було зі сном наприкінці червня?", "e16"),
        new("Скільки я збирав шафу?", "e17"),
        new("Що я загубив у метро?", "e18"),
        new("Як часто я ходжу в басейн?", "e19"),
        new("Що сталося з котом?", "e20"),
        new("Як звати мого кота?", "e20"),
        new("Коли я вийшов на нову роботу?", "e01"),
        new("Чи мене підвищили на роботі?", "e11"),
        new("Чому я не спав ночами?", "e16"),

        new("Скільки я плачу за оренду квартири?"),
        new("Куди я їздив у відпустку за кордон?"),
        new("Як звали мою першу вчительку?"),
        new("Чи я курю?"),
        new("Яку машину я купив?"),
        new("Що мені подарували на день народження?"),
        new("Скільки в мене братів і сестер?"),
        new("Чи був я коли-небудь у Японії?"),
        new("Про що була моя дипломна робота?"),
        new("Скільки я важу?"),
    ];

    private sealed class EvalPathProvider : IAiModelPathProvider
    {
        public string GetModelDirectory(string modelId) => Path.Combine(CacheRoot, modelId);
    }

    private sealed class EvalEmbeddingLocator : IEmbeddingModelLocator
    {
        public EmbeddingModelFiles? TryLocate()
        {
            var model = AiModelCatalog.MiniLmEncoder;
            var directory = Path.Combine(CacheRoot, model.Id);

            return new EmbeddingModelFiles(
                model.Id,
                Path.Combine(directory, "model.onnx"),
                Path.Combine(directory, "sentencepiece.bpe.model"),
                model.Dimensions,
                model.MaxTokens);
        }
    }

    private sealed class EvalGenerativeLocator : IGenerativeModelLocator
    {
        public GenerativeModelFiles? TryLocate()
        {
            var model = AiModelCatalog.Qwen3Generator;
            var directory = Path.Combine(CacheRoot, model.Id);

            return Directory.Exists(directory) ? new GenerativeModelFiles(model.Id, directory) : null;
        }
    }

    /// <summary>Consent is not what this test measures; it is pinned elsewhere and assumed here.</summary>
    private sealed class AlwaysAvailable : IAiAvailability
    {
        public Task<bool> CanEmbedAsync() => Task.FromResult(true);

        public Task<bool> CanGenerateAsync() => Task.FromResult(true);
    }
}
