# 07 — Onboarding + щоденне нагадування

- **Priority:** P1 · **Effort:** M · **Status:** Proposed · **Depends on:** — · **Blocks:** —

## Продукт

**Проблема.** Немає жодного onboarding: новий користувач потрапляє на порожній щоденник без пояснення секцій,
quick-menu, планера, налаштування циклу чи приватного замка. І для **журнального** застосунку немає щоденного
нагадування вести щоденник — нотифікації є лише для задач. Це найбільша діра **утримання**.

**Конкуренти.** Reflectly/Finch/Daylio починають із теплого onboarding + запитують час щоденного нагадування;
AI-prompted journaling показує ~2.5× вище утримання. Нагадування — головний двигун streak-механік.

**Цінність.** Пряме підвищення D1/D7-retention та частки активних логувальників; збирає стартові дані
(зокрема `LastPeriodStartDate` для циклу, без якого фертильні прогнози не працюють).

**User story.** *Як новий користувач, я хочу за хвилину зрозуміти, що вміє застосунок і як ним користуватися,
і отримувати м'яке нагадування щодня у зручний час, щоб не забувати вести щоденник.*

## Інженерія

### Проблема / докази
- Немає onboarding-потоку; `App.xaml.cs` веде одразу в `AppShell`/`MainPage`.
- `Diarion.Core/Services/INotificationService.cs` має лише `ScheduleTodoReminder`/`CancelTodoReminder` —
  немає денного journaling-нагадування.
- Функції циклу потребують `LastPeriodStartDate`, який ніде не збирається при першому запуску.

### Вимоги (Requirements)
**Onboarding:**
- **R1.** Кілька екранів першого запуску: цінність + приватність (гасло «дані не покидають пристрій»),
  короткий тур по секціях/quick-menu, опційне налаштування замка (spec 04) і часу нагадування, опційний
  ввід дати останньої менструації (для тих, кому релевантно; легко пропустити).
- **R2.** Показувати лише один раз; прапорець «onboardingCompleted» у `Preferences`/`SecureStorage`; маршрут
  вибирається в `App`.
- **R3.** Повністю локалізований (UK/EN, .resx) і в дизайн-системі; доступний (spec 08).

**Щоденне нагадування:**
- **R4.** Розширити `INotificationService`: `ScheduleDailyJournalReminder(TimeSpan time)` /
  `CancelDailyJournalReminder()`.
- **R5.** Налаштування в `ProfilePage`: увімк/вимк + вибір часу; зберігати в `UserProfile`.
- **R6.** Копірайт нагадувань — **запрошення, а не провина** («Хвилинка для себе?»), без guilt-based тону.
- **R7.** (Опційно) не нагадувати, якщо запис за сьогодні вже створено.

### Критерії приймання (Acceptance)
- **AC1.** Перший запуск показує onboarding; повторні — ні (AC: прапорець працює).
- **AC2.** Дозволи на нотифікації запитуються коректно (Android 13+ POST_NOTIFICATIONS, iOS).
- **AC3.** Увімкнене нагадування на час T → локальна нотифікація приходить щодня о T; вимкнення скасовує її.
- **AC4.** Дата циклу, введена в onboarding, активує прогнози циклу.
- **AC5.** Усі рядки локалізовані; екрани доступні для screen-reader (spec 08).

### Зачеплені файли
- Новий: `Views/OnboardingPage.xaml(.cs)` (+ `OnboardingViewModel` за потреби)
- `App.xaml.cs` (вибір стартового маршруту), `AppShell.xaml.cs`
- `Diarion.Core/Services/INotificationService.cs`, `Services/LocalNotificationService.cs`
- `Diarion.Core/Models/UserProfile.cs`, `Diarion.Core/Services/ProfileService.cs`
- `Views/ProfilePage.xaml(.cs)`, `Diarion.Core/ViewModels/ProfileViewModel.cs`
- `Resources/Localization/AppResources.resx` (+ `.uk.resx`)
- Тести: `ProfileViewModelTests` (налаштування нагадування), новий onboarding-тест за потреби

### Тест-план
- Unit: планування/скасування денного нагадування (мок `INotificationService`); збереження часу в профілі.
- Manual/E2E: перший запуск → onboarding → дозвіл → нотифікація наступного дня; повторний запуск без onboarding.

### Ризики / застереження
- Дозволи на нотифікації відрізняються по платформах/версіях — обробити відмову м'яко.
- Не перевантажувати onboarding; дозволити пропуск кожного кроку.
