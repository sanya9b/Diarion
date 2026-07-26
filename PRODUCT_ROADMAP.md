# Diarion — Product Roadmap

Єдине джерело правди про те, що зроблено і що далі. Оновлюй цей файл щоразу, коли пункт закривається.

- **Клин ринку:** «рідний український розумний щоденник, повністю офлайн».
- **Privacy-by-architecture** — маркетинговий стовп №1.
- **Монетизація:** щедре безкоштовне ядро + одноразовий lifetime «Diarion Pro»; ніколи не гейтити експорт і приватність.

Позначки: `[x]` готово · `[~]` в роботі · `[ ]` не починалось.

---

## P0 + P1 — закриті

Дванадцять інженерних специфікацій (безпека, втрата даних, фундамент) відвантажені. Повний опис вимог,
критеріїв приймання і тест-планів — у [`specs/`](specs/README.md).

- [x] 01 Шифрування даних at rest
- [x] 02 Втрата даних автозбереження
- [x] 03 Безпечний бекап та імпорт
- [x] 04 Справжнє блокування + PIN
- [x] 05 Підключення захоплення настрою
- [x] 06 Кореляційний рушій інсайтів
- [x] 07 Onboarding + щоденне нагадування
- [x] 08 Доступність та адаптивний UI
- [x] 09 Портативний експорт даних
- [x] 10 Декаплінг Core від MAUI
- [x] 11 Продуктивність: списки та статистика
- [x] 12 Версіонування схеми та міграції

---

## P2 — диференціація фіч

Конкурентна цінність, реліз не блокують. Кожен пункт можна розгорнути в повноцінний spec на запит.

| Стан | Фіча | Конкуренти | Інженерна нотатка |
|------|------|-----------|-------------------|
| `[x]` | Гнучкі графіки звичок + habit-strength + heatmap | Loop, HabitKit, Habitify | `Schedule` value-object у `HabitDefinition`, `TargetCount`; `Controls/ContributionGrid` |
| `[x]` | Нагадування per-habit | Streaks, Way of Life | `ReminderTimes` у `HabitDefinition` → `INotificationService` |
| `[x]` | Quit-tracker: money-saved, live-годинник, milestones, лог зривів | I Am Sober, Quitzilla, QuitNic | `CostPerUnit/UnitsPerDay`, `List<RelapseEvent>`, milestone-рушій |
| `[x]` | Нотатки: backlinks `[[…]]`, теги, full-text пошук, quick-capture inbox | Obsidian, Notion, Amplenote, Todoist | `links`/`inbox` колекції LiteDB, share-target, глобальний екран пошуку |
| `[~]` | **Фінанси: бюджети, рахунки, планові транзакції, звіти** | Money Manager, Wallet, Spendee | див. розбивку нижче |
| `[x]` | Керовані промпти + CBT + вечірній ритуал вдячності | Stoic, Reflectly, Finch, Intellect | `PromptCatalog` (40 промптів у .resx UK/EN) + чистий `PromptSelector` за валентністю настрою; `Controls/GuidedPromptSection` |
| `[~]` | М'яка гейміфікація (прощаючі streaks) + Year in Pixels | Daylio, Finch | Year in Pixels готовий (`Controls/YearInPixelsView`); лічильник серії полагоджено (`StreakCalculator`), але дні-поблажки й спільний `StreakService` не починались |
| `[ ]` | Адаптивний прогноз циклу + лог симптомів | Clue, Flo, Natural Cycles | `CycleLog` колекція, rolling mean/SD, індикатор варіативності |
| `[ ]` | HealthKit / Health Connect (read) | Bearable, Apple Journal, Journey | Розширити `HealthDataService`, per-metric consent, лише локальний імпорт |
| `[ ]` | Home/lock-screen віджети | Streaks, HabitKit, Loop | WidgetKit (iOS) / Glance-RemoteViews (Android) через нативний embedding |
| `[ ]` | Планер: time-blocking таймлайн + NL/RRULE-повтори | Structured, TickTick, Todoist | Day-timeline view; RRULE-рушій (переюзати для звичок) |
| `[ ]` | Читання: полиці, прогрес, рейтинги, сесії, цілі, mood-теги | StoryGraph, Bookly, Goodreads | Наявний `ReadingTrackerBook` — це лише слот + назва + дата; потрібні `ReadingStatus`, `ReadingSession`, `ReadingGoal` |
| `[ ]` | Opt-in on-device / cloud AI (резюме, «копни глибше») | Apple Journal, Day One, Stoic | Вимкнено за замовчуванням; on-device (Foundation Models/Gemini Nano) або узгоджений API без retention |

### Фінанси — розбивка по фазах

- [x] **Phase A — бюджети.** Місячні ліміти по категоріях, віджети прогресу, деталізація категорії, тумблер у налаштуваннях. `Budget`, `BudgetCalculator`.
- [x] **Phase B — рахунки.** `Account` + міграція `M003`, смуга-фільтр рахунків, форма створення/редагування з вибором іконки й кольору, архівація, видалення з вибором цільового рахунку, перекази між рахунками (`Transfer` — окрема колекція, не `TransactionType`), облік рахунків у CSV/JSON-експорті.
- [ ] **Phase C — планові транзакції.** `RecurringTransaction` + RRULE-рушій (спільний із планером).
- [ ] **Phase D — звіти.** Динаміка по місяцях, порівняння періодів, розріз по рахунках.

---

### Промпти — що далі

- [ ] **Власні промпти користувача.** Розширення протоптане: вбудовані тримають `ResourceKey`, створені користувачем — літеральний текст, точно як `HabitDefinition`. Знадобиться колекція LiteDB і міграція.
- [ ] Історія відповідей окремим екраном (зараз відповідь живе лише в записі дня).
- [ ] Погодинна шкала настрою (`DiaryEntry.MoodScale`) досі не підключена в UI — `PromptSelector` її вже читає й віддає їй пріоритет, тож щойно з'явиться введення, вибір промпта стане точнішим без змін у коді.

---

## Технічний борг і відомі обмеження

- Статистика (`StatisticsService.GetFinanceStatisticsAsync`) агрегує по всіх рахунках. Фільтр по рахунку не додано свідомо — на сторінці статистики немає селектора рахунку, тож параметр був би мертвим кодом. Додати разом із Phase D.
- Перекази рендеряться окремою секцією над стрічкою транзакцій: `CollectionView.ItemTemplate` у `FinancePage.xaml` жорстко типізований під `FinanceTransaction`. Об'єднати стрічку — через `DataTemplateSelector`, коли з'являться планові транзакції (Phase C).
- Тести виконуються послідовно: паралелізацію xUnit вимкнено через гонки глобального маппера LiteDB.
- `DiaryEntry.SupportForOthers` є в моделі, але не має UI від самого початку. Перетинається з GoodDeeds — вирішити, чи це окрема просоціальна фіча, чи поле під видалення.
- `DailyReminderTitle`/`DailyReminderMessage` існують лише в українському resx; англійська тримається на фолбеку `?? "..."` у `AppResources.Designer.cs`. Працює, але єдине таке місце в проєкті.
- Порожні записи, створені старим багом автозбереження, лишились у базах користувачів. Серія їх ігнорує (`HasContent`), але самі рядки ніхто не прибирає — за потреби це разова міграція.
