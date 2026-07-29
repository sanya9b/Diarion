# 10 — Декаплінг Core від MAUI

- **Priority:** P1 · **Effort:** M/L · **Status:** Done · **Depends on:** — · **Blocks:** —

## Продукт

**Проблема.** «Clean Architecture» заявлена, але напівреальна: шар домену/логіки (`Diarion.Core`) залежить від
повного MAUI-фреймворку і всередині нього — прямі виклики UI/Shell/платформи. Це не продуктова фіча, а
**фундамент якості**: без нього більшість ViewModel не тестуються, а платформні деталі вільно течуть у логіку.

**Цінність.** Тестованість (передумова надійності всіх інших spec), можливість переносу/повторного
використання Core, чистіші межі, легша підтримка. Прямо підвищує швидкість і безпеку майбутніх змін.

**User story (dev).** *Як розробник, я хочу писати unit-тести на ViewModels/сервіси без живого Shell/MAUI,
щоб надійно ловити регресії (як-от баг втрати даних зі spec 02).*

## Інженерія

### Проблема / докази
- `Diarion.Core/Diarion.Core.csproj:7` — `<UseMaui>true</UseMaui>`: Core тягне весь MAUI.
- Прямі `Shell.Current.DisplayAlertAsync/GoToAsync/FlyoutIsPresented` у Core VM, попри наявні
  `IDialogService`/`INavigationService`: `ProfileViewModel.cs` (9 місць: 91,116,128,151,161,173,183,194,204,219),
  `TodoDetailViewModel.cs:127,156,180`, `FinanceViewModel.cs:242`, `WishlistViewModel.cs:115`,
  `StatisticsViewModel.cs:204`, `MainViewModel.cs:283`.
- Презентаційні концерни в Core: `ChartItems.cs` (`Colors.Gray`), `MoodStatsViewModel.cs:92`
  (`Color.FromArgb`), `QuickMenuItem.cs:18-23` (`Geometry` + `PathGeometryConverter`).
  ~~`CycleStatusViewModel.cs`~~ — виправлено разом із адаптивним прогнозом циклу: колір ніс
  «ймовірність вагітності», яку прибрали, тож зникло і саме поле, а не переїхало.
- Платформа в Core: `BackupService.cs:6,31,34-37` (`FileSystem`, `Share`), `CalendarSectionViewModel.cs:150,168`
  (`MainThread.BeginInvokeOnMainThread`).
- Неузгоджені простори імен: файли під `Diarion.Core` мають `Diarion.*` (той самий `Diarion.Services`, що й
  app-реалізації); виняток — `IHealthDataService.cs` з `Diarion.Core.Services`.

### Вимоги (Requirements)
- **R1.** Прогнати **всі** алерти/навігацію в Core VM через наявні `IDialogService`/`INavigationService`
  (ін'єктувати де бракує, як уже робить `MainViewModel`); додати метод для flyout/menu у `INavigationService`.
- **R2.** Прибрати `<UseMaui>true</UseMaui>` з `Diarion.Core.csproj`; замінити прямі MAUI-залежності
  абстракціями: `IDispatcher` (для `MainThread`), `IShareService`/`IFileService` (для `BackupService`/експорту),
  винести Color/Geometry за конвертери/UI-шар.
- **R3.** Реалізації абстракцій — в app-проєкті (`Services/`), реєстрація в `Extensions/ServiceCollectionExtensions.cs`.
- **R4.** Нормалізувати простори імен до межі шару (напр. `Diarion.Core.*` для Core, `Diarion.*` для app), або
  щонайменше усунути колізію однакових namespace між Core-інтерфейсами й app-реалізаціями.
- **R5.** Робити поетапно, зберігаючи зелений білд і тести на кожному кроці (не «великий вибух»).

### Критерії приймання (Acceptance)
- **AC1.** `Diarion.Core.csproj` не має `<UseMaui>true` і **не** посилається на UI-типи MAUI
  (`Microsoft.Maui.Controls`, `Shell`, `Graphics.Color`, `Shapes.Geometry`) у Models/Services/ViewModels.
- **AC2.** Жоден Core VM не викликає `Shell.Current.*` напряму; усе — через абстракції.
- **AC3.** `ProfileViewModel`/`TodoDetailViewModel`/`FinanceViewModel`/`WishlistViewModel`/`StatisticsViewModel`
  покриваються unit-тестами без живого Shell (мок `IDialogService`/`INavigationService`).
- **AC4.** Білд і всі наявні тести зелені; додано тести на раніше-нетестовані VM.

### Зачеплені файли
- `Diarion.Core/Diarion.Core.csproj`
- `Diarion.Core/ViewModels/*` (перелічені вище), `Diarion.Core/Services/INavigationService.cs`
- `Diarion.Core/Services/BackupService.cs` (→ абстракції; координація зі spec 03/09)
- Нові Core-інтерфейси: `IDispatcher`(або переюзати MAUI-абстракцію в app), `IShareService`, `IFileService`
- Нові app-реалізації в `Services/`, `Extensions/ServiceCollectionExtensions.cs`
- Конвертери для Color/Geometry в app-проєкті

### Тест-план
- Unit: нові тести на 5 VM, що раніше залежали від Shell (мок діалогів/навігації).
- Build-gate: збірка Core без MAUI UI-посилань (AC1).
- Regression: повний тест-ран після кожного етапу.

### Ризики / застереження
- Обсяг великий і торкається багатьох VM — розбити на кілька PR (спершу діалоги/навігація → потім
  color/geometry/dispatch → потім namespace).
- Прибирання `UseMaui` може відкрити приховані залежності — вести список leak-сайтів і закривати ітеративно.
