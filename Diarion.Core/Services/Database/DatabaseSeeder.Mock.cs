#if DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Diarion.Models;
using Diarion.Resources.Localization;
using LiteDB;

namespace Diarion.Services.Database;

/// <summary>
/// Debug-only showcase diary. Seeded once into an empty database so every screen has something
/// worth looking at (and photographing): a few months of moods, habits and tasks, three months of
/// spending, a shelf of books, a wishlist. Text follows the app language at first launch; the
/// generator is deterministic so two runs produce the same diary.
/// </summary>
public partial class DatabaseSeeder
{
    private static bool IsUkrainian =>
        string.Equals((AppResources.Culture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName, "uk", StringComparison.OrdinalIgnoreCase);

    private static void SeedMockDataIfEmpty(LiteDatabase database)
    {
        var entries = database.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection);
        if (entries.Count() > 0) return;

        var uk = IsUkrainian;
        var random = new Random(2026);
        var today = DateTime.Today;
        var start = today.AddDays(-120);
        var habits = database.GetCollection<HabitDefinition>(DatabaseConstants.HabitDefinitionsCollection).FindAll().ToList();

        SeedProfile(database.GetCollection<UserProfile>(DatabaseConstants.ProfileCollection), uk);
        SeedEntries(entries, habits, uk, random, start, today);
        SeedTodos(database.GetCollection<TodoItem>(DatabaseConstants.TodosCollection), uk, random, start, today);
        SeedFinance(database, uk, random, today);
        SeedBooks(database.GetCollection<ReadingTrackerBook>(DatabaseConstants.ReadingTrackerBooksCollection), uk, today);
        SeedWishlist(database.GetCollection<WishlistEntry>(DatabaseConstants.WishlistCollection), uk, today);
        SeedMoments(database, uk, today);
        SeedHabitTracker(database.GetCollection<HarmfulHabitTracker>(DatabaseConstants.HarmfulHabitTrackersCollection), uk, random, today);
        SeedNotes(database.GetCollection<Note>(DatabaseConstants.NotesCollection), uk, today);
    }

    private static void SeedProfile(ILiteCollection<UserProfile> profiles, bool uk)
    {
        if (profiles.Count() > 0) return;
        // Hryvnia for the Ukrainian diary, dollars for the English one: the device region would
        // otherwise decide, and on a neutral "en" culture there is no region at all.
        profiles.Insert(new UserProfile { CurrencyCode = uk ? "UAH" : "USD" });
    }

    private static readonly Emotion[] GoodDayMoods = { Emotion.Happy, Emotion.Calm, Emotion.Content, Emotion.Grateful, Emotion.Energetic, Emotion.Inspired, Emotion.Relaxed, Emotion.Peaceful };
    private static readonly Emotion[] HardDayMoods = { Emotion.Tired, Emotion.Anxious, Emotion.Stressed, Emotion.Bored, Emotion.Irritated, Emotion.Sad };

    private static void SeedEntries(ILiteCollection<DiaryEntry> entries, List<HabitDefinition> habits, bool uk, Random random, DateTime start, DateTime today)
    {
        string[] breakfasts = uk ? new[] { "Вівсянка з ягодами", "Сирники", "Яєчня з тостом", "Йогурт і гранола", "Омлет зі шпинатом" }
                                 : new[] { "Oats with berries", "Cottage cheese pancakes", "Eggs on toast", "Yoghurt and granola", "Spinach omelette" };
        string[] lunches = uk ? new[] { "Борщ", "Салат із тунцем", "Рис із овочами", "Суп-пюре", "Паста з песто" }
                              : new[] { "Borscht", "Tuna salad", "Rice with vegetables", "Cream soup", "Pesto pasta" };
        string[] dinners = uk ? new[] { "Риба + овочеве рагу", "Запечена картопля", "Курка з броколі", "Сирна тарілка", "Гречка з грибами" }
                              : new[] { "Fish + veg stew", "Baked potatoes", "Chicken with broccoli", "Cheese plate", "Buckwheat with mushrooms" };
        string[] gratitude = uk ? new[] { "Ранкова кава на балконі", "Дзвінок від мами", "Сонце після тижня дощів", "Друзі, які приїхали без приводу", "Тиша ввечері" }
                                : new[] { "Morning coffee on the balcony", "A call from mum", "Sun after a week of rain", "Friends who came over for no reason", "Quiet in the evening" };
        string[] titles = uk ? new[] { "Довга прогулянка", "День без телефону", "Нарешті дочитала", "Зустріч із Олею", "Переставила кімнату" }
                             : new[] { "A long walk", "A day without the phone", "Finished the book at last", "Coffee with Olha", "Rearranged the room" };
        string[] bodies = uk ? new[] {
                "Вийшла раніше, ніж планувала, і пройшла майже десять кілометрів уздовж річки. Голова порожня в хорошому сенсі.",
                "Лишила телефон удома. Перші дві години свербіли руки, потім стало дивно спокійно.",
                "Остання глава виявилась зовсім не такою, як я чекала. Хочеться з кимось обговорити.",
                "Говорили три години й не помітили. Домовились про щось подібне раз на місяць.",
                "Стіл тепер біля вікна. Світло зовсім інше, і працювати чомусь легше." }
            : new[] {
                "Left earlier than planned and walked almost ten kilometres along the river. Head empty, in the good way.",
                "Left the phone at home. Hands itched for two hours, then it got strangely calm.",
                "The last chapter was nothing like I expected. I want to talk it through with someone.",
                "We talked for three hours and did not notice. Agreed to do this once a month.",
                "The desk is by the window now. The light is different, and work is somehow easier." };

        var list = new List<DiaryEntry>();
        for (var date = start; date <= today; date = date.AddDays(1))
        {
            // Roughly one day in nine is skipped, so the calendar looks lived-in rather than generated.
            if (random.Next(9) == 0 && date != today) continue;

            var hardDay = random.NextDouble() < 0.22;
            var mood = hardDay ? HardDayMoods[random.Next(HardDayMoods.Length)] : GoodDayMoods[random.Next(GoodDayMoods.Length)];
            var entry = new DiaryEntry
            {
                Id = Guid.NewGuid(),
                Date = date,
                CreatedAt = date.AddHours(21).AddMinutes(random.Next(0, 50)),
                Emotion = mood,
                SleepStart = new TimeSpan(22 + random.Next(0, 2), random.Next(0, 6) * 10, 0),
                SleepEnd = new TimeSpan(6 + random.Next(0, 2), random.Next(0, 6) * 10, 0),
                SleepQuality = hardDay ? random.Next(2, 4) : random.Next(3, DiaryEntry.MaxRating + 1),
                HealthStatus = hardDay ? random.Next(2, 4) : random.Next(4, DiaryEntry.MaxRating + 1),
                IsBreakfastDone = true,
                BreakfastFood = breakfasts[random.Next(breakfasts.Length)],
                IsSecondBreakfastDone = random.NextDouble() > 0.5,
                SecondBreakfastFood = uk ? "Кава + печиво" : "Coffee + biscuit",
                IsLunchDone = true,
                LunchFood = lunches[random.Next(lunches.Length)],
                IsSnackDone = random.NextDouble() > 0.6,
                SnackFood = uk ? "Яблуко" : "An apple",
                IsDinnerDone = random.NextDouble() > 0.15,
                DinnerFood = dinners[random.Next(dinners.Length)],
                Triggers = string.Empty,
                Gratitude = random.NextDouble() > 0.55 ? gratitude[random.Next(gratitude.Length)] : string.Empty,
                SoulFood = string.Empty,
            };

            // A day's mood by the hour: a morning, a middle and an evening reading.
            foreach (var hour in new[] { 8 + random.Next(0, 2), 13 + random.Next(0, 2), 19 + random.Next(0, 3) })
                entry.HourlyMood.Add(new HourMood { Hour = hour, Mood = random.NextDouble() > 0.3 ? mood : GoodDayMoods[random.Next(GoodDayMoods.Length)] });

            foreach (var habit in habits)
                entry.HabitsList.Add(new HabitItem { HabitId = habit.Id, Name = habit.Name, IsCompleted = random.NextDouble() > (hardDay ? 0.55 : 0.25) });

            if ((date - start).Days % 6 == 2)
            {
                var k = random.Next(titles.Length);
                entry.Title = titles[k];
                entry.Content = bodies[k];
            }

            list.Add(entry);
        }

        entries.InsertBulk(list);
    }

    private static void SeedTodos(ILiteCollection<TodoItem> todos, bool uk, Random random, DateTime start, DateTime today)
    {
        var pool = uk
            ? new (string Text, TodoPriority Priority, int Hour)[] {
                ("Ранкова пробіжка", TodoPriority.Medium, 7), ("Купити продукти на тиждень", TodoPriority.Medium, 18),
                ("Подзвонити бабусі", TodoPriority.High, 19), ("Оплатити комуналку", TodoPriority.High, 12),
                ("Полити квіти", TodoPriority.Low, 9), ("Прочитати 30 сторінок", TodoPriority.Low, 21),
                ("Записатись до стоматолога", TodoPriority.High, 10), ("Йога 20 хвилин", TodoPriority.Medium, 8),
                ("Відповісти на листи", TodoPriority.Medium, 11), ("Запланувати відпустку", TodoPriority.Low, 20),
                ("Зустріч із Марком", TodoPriority.High, 15), ("Медитація", TodoPriority.Low, 22),
                ("Прибрати на кухні", TodoPriority.Medium, 17), ("Здати аналізи", TodoPriority.High, 8) }
            : new (string Text, TodoPriority Priority, int Hour)[] {
                ("Morning run", TodoPriority.Medium, 7), ("Groceries for the week", TodoPriority.Medium, 18),
                ("Call grandma", TodoPriority.High, 19), ("Pay the utility bills", TodoPriority.High, 12),
                ("Water the plants", TodoPriority.Low, 9), ("Read 30 pages", TodoPriority.Low, 21),
                ("Book the dentist", TodoPriority.High, 10), ("Yoga, 20 minutes", TodoPriority.Medium, 8),
                ("Answer emails", TodoPriority.Medium, 11), ("Plan the holiday", TodoPriority.Low, 20),
                ("Meet Mark", TodoPriority.High, 15), ("Meditation", TodoPriority.Low, 22),
                ("Tidy the kitchen", TodoPriority.Medium, 17), ("Blood tests", TodoPriority.High, 8) };

        var list = new List<TodoItem>();
        for (var date = start; date <= today.AddDays(3); date = date.AddDays(1))
        {
            var isPast = date < today;
            var picks = Enumerable.Range(0, pool.Length).OrderBy(_ => random.Next()).Take(random.Next(3, 6));
            foreach (var p in picks)
            {
                var (text, priority, hour) = pool[p];
                var hasTime = random.NextDouble() > 0.3;
                list.Add(new TodoItem
                {
                    Id = Guid.NewGuid(),
                    TargetDate = date,
                    TaskDescription = text,
                    Priority = priority,
                    HasTime = hasTime,
                    TargetTime = new TimeSpan(hour, random.Next(0, 4) * 15, 0),
                    // Past tasks are all done: an open task rolls over to today, and a hundred days of
                    // leftovers would bury the day's own list.
                    IsCompleted = isPast || (date == today && random.NextDouble() > 0.6),
                    HasReminder = hasTime && !isPast && random.NextDouble() > 0.5,
                    CreatedAt = date.AddDays(-1).AddHours(21),
                });
            }
        }
        todos.InsertBulk(list);
    }

    private static void SeedFinance(LiteDatabase database, bool uk, Random random, DateTime today)
    {
        var accounts = database.GetCollection<Account>(DatabaseConstants.AccountsCollection);
        var account = accounts.FindAll().OrderBy(a => a.CreatedAt).FirstOrDefault();
        if (account == null)
        {
            account = new Account { Name = "Main", ResourceKey = "DefaultAccountName" };
            accounts.Insert(account);
        }

        var expenses = uk
            ? new (string Category, int Min, int Max, double PerDay)[] {
                ("Продукти", 180, 1400, 0.45), ("Кава", 60, 140, 0.5), ("Транспорт", 20, 300, 0.4), ("Розваги", 250, 1200, 0.12),
                ("Аптека", 120, 900, 0.07), ("Подарунки", 300, 1500, 0.04), ("Одяг", 600, 2800, 0.04), ("Книги", 250, 600, 0.05) }
            : new (string Category, int Min, int Max, double PerDay)[] {
                ("Groceries", 8, 60, 0.45), ("Coffee", 3, 7, 0.5), ("Transport", 2, 15, 0.4), ("Going out", 15, 70, 0.12),
                ("Pharmacy", 6, 40, 0.07), ("Gifts", 15, 80, 0.04), ("Clothes", 30, 140, 0.04), ("Books", 10, 30, 0.05) };

        var list = new List<FinanceTransaction>();
        var start = today.AddDays(-95);
        for (var date = start; date <= today; date = date.AddDays(1))
        {
            if (date.Day == 5)
                list.Add(new FinanceTransaction { Type = TransactionType.Income, AccountId = account.Id, Amount = uk ? 42000 : 2600, Category = uk ? "Зарплата" : "Salary", Date = date, CreatedAt = date.AddHours(9) });
            if (date.Day == 20)
                list.Add(new FinanceTransaction { Type = TransactionType.Income, AccountId = account.Id, Amount = uk ? 6500 : 400, Category = uk ? "Фриланс" : "Freelance", Date = date, CreatedAt = date.AddHours(16) });
            if (date.Day == 1)
                list.Add(new FinanceTransaction { Type = TransactionType.Expense, AccountId = account.Id, Amount = uk ? 2850 : 180, Category = uk ? "Комуналка" : "Utilities", Date = date, CreatedAt = date.AddHours(10) });
            foreach (var (category, min, max, perDay) in expenses)
            {
                if (random.NextDouble() > perDay) continue;
                list.Add(new FinanceTransaction
                {
                    Type = TransactionType.Expense, AccountId = account.Id,
                    Amount = Math.Round((decimal)(min + random.NextDouble() * (max - min)), uk ? 0 : 2),
                    Category = category, Date = date, CreatedAt = date.AddHours(8 + random.Next(0, 13)),
                });
            }
        }
        database.GetCollection<FinanceTransaction>(DatabaseConstants.FinanceCollection).InsertBulk(list);
    }

    private static void SeedBooks(ILiteCollection<ReadingTrackerBook> books, bool uk, DateTime today)
    {
        var titles = uk
            ? new[] { "«Тигролови» Іван Багряний", "«Атомні звички» Джеймс Клір", "«Місто» Валер'ян Підмогильний", "«Сто років самотності» Маркес", "«Залізна вода» Мирослав Лаюк", "«Дім, в якому...» Маріам Петросян", "«Тихе місце» Ольга Кобилянська" }
            : new[] { "Atomic Habits, James Clear", "The Night Circus, Erin Morgenstern", "Piranesi, Susanna Clarke", "Educated, Tara Westover", "Project Hail Mary, Andy Weir", "Klara and the Sun, Kazuo Ishiguro", "Circe, Madeline Miller" };
        var list = titles.Select((t, i) => new ReadingTrackerBook
        {
            SlotNumber = i + 1, BookTitle = t,
            CompletedOn = today.AddDays(-(titles.Length - i) * 13 - 4),
            CreatedAt = today.AddDays(-(titles.Length - i) * 13 - 30),
        }).ToList();
        books.InsertBulk(list);
    }

    private static void SeedWishlist(ILiteCollection<WishlistEntry> wishlist, bool uk, DateTime today)
    {
        var items = uk
            ? new (string Want, string Wish, bool Done)[] {
                ("Побачити північне сяйво", "Тиждень у Тромсе взимку", false), ("Навчитись плавати кролем", "Абонемент у басейн із тренером", true),
                ("Власна майстерня з гончарства", "Курс на 8 занять, потім коло додому", false), ("Пробігти півмарафон", "Київський півмарафон у квітні", false),
                ("Вивчити 1000 слів іспанською", "По 5 слів щодня", true), ("Поїздка до Карпат без інтернету", "Три дні, намет, книжка", false) }
            : new (string Want, string Wish, bool Done)[] {
                ("See the northern lights", "A week in Tromsø in winter", false), ("Learn to swim front crawl", "Pool pass with a coach", true),
                ("My own pottery corner", "An 8-lesson course, then a wheel at home", false), ("Run a half marathon", "The spring half marathon", false),
                ("Learn 1000 Spanish words", "Five words a day", true), ("A trip to the mountains with no internet", "Three days, a tent, a book", false) };
        var list = items.Select((w, i) => new WishlistEntry
        {
            WantText = w.Want, WishText = w.Wish, GetText = w.Done ? (uk ? "Зроблено" : "Done") : string.Empty,
            IsCompleted = w.Done, Date = today.AddDays(-i * 11 - 3), CreatedAt = today.AddDays(-i * 11 - 3),
        }).ToList();
        wishlist.InsertBulk(list);
    }

    private static void SeedMoments(LiteDatabase database, bool uk, DateTime today)
    {
        var moments = uk
            ? new[] { "Перший сніг і гарячий какао", "Лист від подруги з Лісабона", "Концерт під відкритим небом", "Племінниця сказала моє ім'я", "Захід сонця з даху" }
            : new[] { "First snow and hot cocoa", "A letter from a friend in Lisbon", "An open-air concert", "My niece said my name", "Sunset from the roof" };
        database.GetCollection<HappyMoment>(DatabaseConstants.HappyMomentsCollection).InsertBulk(
            moments.Select((m, i) => new HappyMoment { SlotNumber = i + 1, Title = m, Date = today.AddDays(-i * 9 - 2), CreatedAt = today.AddDays(-i * 9 - 2) }));

        var deeds = uk
            ? new[] { "Допомогла сусідці з продуктами", "Здала кров", "Віддала книжки в бібліотеку", "Переказ на притулок для тварин" }
            : new[] { "Carried the neighbour's groceries", "Donated blood", "Gave books to the library", "Donation to the animal shelter" };
        database.GetCollection<GoodDeed>(DatabaseConstants.GoodDeedsCollection).InsertBulk(
            deeds.Select((d, i) => new GoodDeed { SlotNumber = i + 1, Title = d, Date = today.AddDays(-i * 12 - 5), CreatedAt = today.AddDays(-i * 12 - 5) }));
    }

    private static void SeedHabitTracker(ILiteCollection<HarmfulHabitTracker> trackers, bool uk, Random random, DateTime today)
    {
        var start = today.AddDays(-41);
        var tracker = new HarmfulHabitTracker { HarmfulHabitName = uk ? "Англійська, 5 слів" : "Five Spanish words", StartDate = start, CreatedAt = start };
        for (var d = start; d < today; d = d.AddDays(1))
            if (random.NextDouble() > 0.12) tracker.MarkedDays.Add(d);
        trackers.Insert(tracker);
    }

    private static void SeedNotes(ILiteCollection<Note> notes, bool uk, DateTime today)
    {
        var items = uk
            ? new (string Title, string Body, string Color)[] {
                ("Ідеї на вихідні", "- [ ] Ботанічний сад\n- [x] Ринок на Житньому\n- [ ] Виставка в Мистецькому арсеналі\n#вихідні", "Theme_Sage"),
                ("Рецепт хумусу", "Нут 400 г, тахіні 3 ст. л., лимон, часник, зіра. Збивати довго, додавати крижану воду.\n#рецепти", "Theme_Amber"),
                ("Цитата", "«Ми не піднімаємось до рівня наших цілей, ми падаємо до рівня наших систем». Клір\n#книги", "Theme_Berry") }
            : new (string Title, string Body, string Color)[] {
                ("Weekend ideas", "- [ ] Botanical garden\n- [x] Saturday market\n- [ ] The new exhibition\n#weekend", "Theme_Sage"),
                ("Hummus", "Chickpeas 400 g, tahini 3 tbsp, lemon, garlic, cumin. Blend for a long time, add iced water.\n#recipes", "Theme_Amber"),
                ("Quote", "\"We do not rise to the level of our goals, we fall to the level of our systems.\" Clear\n#books", "Theme_Berry") };
        notes.InsertBulk(items.Select((n, i) => new Note
        {
            Title = n.Title, Content = n.Body, ColorTheme = n.Color,
            Tags = n.Body.Split('#').Skip(1).Select(t => t.Trim().ToLowerInvariant()).ToList(),
            CreatedAt = today.AddDays(-i * 4 - 1), UpdatedAt = today.AddDays(-i * 4 - 1),
        }));
    }
}
#endif
