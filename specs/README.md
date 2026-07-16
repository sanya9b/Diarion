# Diarion — Specifications (specs/)

Набір інженерно-продуктових специфікацій, згенерований на основі глибокого аналізу коду
та бенчмарку зі світовими аналогами (2026-07-16). Кожен spec — окрема ініціатива у **гібридному**
форматі: продуктовий контекст + конкуренти зверху, інженерні вимоги / критерії приймання / файли /
тест-план знизу.

> Джерело знахідок: повний аналіз 7 вимірів коду + 7 ринкових категорій, з адверсальною верифікацією
> проти реального коду (34 підтверджено, 1 частково, 1 спростовано). Посилання `file:line` перевірені.

## Як користуватися
- Кожен spec самодостатній і придатний до окремого PR.
- `Status`: `Proposed` → `Approved` → `In progress` → `Done`.
- Спершу закриваємо **P0** (безпека + втрата даних) — до них небезпечно робити решту.
- Кодування усіх файлів: **UTF-8 без BOM** (див. `AGENTS.md`).

## Реєстр специфікацій

| #  | Spec | Пріоритет | Складність | Залежить від | Статус |
|----|------|-----------|-----------|--------------|--------|
| 01 | [Шифрування даних at rest](01-encryption-at-rest.md) | **P0** | M | — | Proposed |
| 02 | [Втрата даних автозбереження](02-autosave-dataloss.md) | **P0** | S | — | Proposed |
| 03 | [Безпечний бекап та імпорт](03-safe-backup-and-import.md) | **P0** | M | 01 | Proposed |
| 04 | [Справжнє блокування + PIN](04-app-lock-and-pin.md) | **P0** | M | 01 | Proposed |
| 05 | [Підключення захоплення настрою](05-mood-capture-wiring.md) | **P0** | M | — | Proposed |
| 06 | [Кореляційний рушій інсайтів](06-correlation-insights-engine.md) | P1 | L | 05 | Proposed |
| 07 | [Onboarding + щоденне нагадування](07-onboarding-and-daily-reminder.md) | P1 | M | — | Proposed |
| 08 | [Доступність та адаптивний UI](08-accessibility-and-adaptive-ui.md) | P1 | M/L | — | Proposed |
| 09 | [Портативний експорт даних](09-data-export.md) | P1 | M | — | Proposed |
| 10 | [Декаплінг Core від MAUI](10-core-framework-decoupling.md) | P1 | M/L | — | Proposed |
| 11 | [Продуктивність: списки та статистика](11-performance-lists-and-stats.md) | P1 | M | — | Proposed |
| 12 | [Версіонування схеми та міграції](12-schema-versioning-and-migrations.md) | P1 | L | 03 | Proposed |

## Рекомендований порядок виконання
1. **02** (S, миттєвий фікс втрати даних) → **01** → **03** → **04** (безпекова гілка).
2. **05** (оживити настрій) → **11** (off-by-one + віртуалізація) → **07** (retention).
3. **06** (розумні інсайти) → **09** (експорт) → **08** (доступність).
4. **10** та **12** — паралельно як технічний борг/фундамент.

## P2 — беклог диференціації фіч (spec розгортається за потреби)

Ці ініціативи мають конкурентну цінність, але не блокують реліз. Кожну можна розгорнути в повноцінний
spec на запит. Формат: **фіча — конкуренти — інженерна нотатка (де реалізувати)**.

| Фіча | Конкуренти | Інженерна нотатка |
|------|-----------|-------------------|
| Гнучкі графіки звичок + habit-strength + heatmap | Loop, HabitKit, Habitify | `Schedule` value-object у `HabitDefinition`, `TargetCount`; новий `Controls/ContributionGrid` |
| Нагадування per-habit | Streaks, Way of Life | `ReminderTimes` у `HabitDefinition` → `INotificationService` |
| Quit-tracker: money-saved, live-годинник, milestones, лог зривів | I Am Sober, Quitzilla, QuitNic | `CostPerUnit/UnitsPerDay`, `List<RelapseEvent>`, milestone-рушій; бонус — авто-переказ у Finance |
| Керовані промпти + CBT + вечірній ритуал вдячності | Stoic, Reflectly, Finch, Intellect | `PromptService` (seed .resx UK/EN), rules-based вибір за настроєм |
| Адаптивний прогноз циклу + лог симптомів | Clue, Flo, Natural Cycles | `CycleLog` колекція, rolling mean/SD, індикатор варіативності |
| HealthKit / Health Connect (read) | Bearable, Apple Journal, Journey | Розширити `HealthDataService`, per-metric consent, лише локальний імпорт |
| Home/lock-screen віджети | Streaks, HabitKit, Loop | WidgetKit (iOS) / Glance-RemoteViews (Android) через нативний embedding |
| Нотатки: backlinks `[[…]]`, теги, full-text пошук, quick-capture inbox | Obsidian, Notion, Amplenote, Todoist | `links`/`inbox` колекції LiteDB, share-target, глобальний екран пошуку |
| Планер: time-blocking таймлайн + NL/RRULE-повтори | Structured, TickTick, Todoist | Day-timeline view; RRULE-рушій (переюзати для звичок) |
| Фінанси: бюджети, планові транзакції, рахунки, звіти | Money Manager, Wallet, Spendee | `Budget`/`Account`/`RecurringTransaction` моделі + сервіси |
| Читання: полиці, прогрес, рейтинги, сесії, цілі, mood-теги | StoryGraph, Bookly, Goodreads | `ReadingStatus`, `ReadingSession`, `ReadingGoal`; переюзати `Emotion` |
| М'яка гейміфікація (прощаючі streaks) + Year in Pixels | Daylio, Finch | `StreakService`, `Controls/YearInPixels` (read-only над датами) |
| Opt-in on-device / cloud AI (резюме, «копни глибше») | Apple Journal, Day One, Stoic | Вимкнено за замовчуванням; on-device (Foundation Models/Gemini Nano) або узгоджений API без retention |

## Стратегічний контекст (для всіх spec)
- **Privacy-by-architecture — маркетинговий стовп №1.** Але спершу треба реально зашифрувати (spec 01/03/04),
  інакше це хибна реклама.
- **Монетизація:** щедре безкоштовне ядро + одноразовий lifetime «Diarion Pro»; ніколи не гейтити експорт і приватність.
- **Клин ринку:** «рідний український розумний щоденник, повністю офлайн».
