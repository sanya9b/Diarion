# 02 — Втрата даних автозбереження

- **Priority:** P0 · **Effort:** S · **Status:** Done · **Depends on:** — · **Blocks:** —

## Продукт

**Проблема.** Для щоденника безшумна втрата написаного — найгірший можливий баг: він підриває довіру
безповоротно. Наразі правки дня можуть зникати при швидкому перемиканні дат.

**Конкуренти.** Day One, Journey, Daylio зберігають запис негайно/атомарно per-entry; перемикання контексту
ніколи не губить чернетку.

**Цінність.** Базова надійність — передумова будь-якого утримання. Це найдешевший P0 (складність S) з
найвищим співвідношенням ризик/зусилля.

**User story.** *Як користувач, я хочу бути впевненим, що все написане зберігається, навіть якщо я одразу
перемкнувся на інший день або згорнув застосунок.*

## Інженерія

### Проблема / докази
- `Diarion.Core/ViewModels/MainViewModel.cs:151-162` — `ScheduleAutoSave()` дебаунсить замикання, яке читає
  `CurrentEntry` **у момент виконання** (`:155`), а не той запис, для якого його заплановано.
- `Diarion.Core/ViewModels/MainViewModel.cs:195-203` — `LoadEntriesForDateAsync` підміняє
  `CurrentEntry = new DiaryEntryViewModel(entry)` **без попереднього flush**.
- Сценарій втрати: редагуєш день A → протягом 1 с (вікно debounce) тапаєш день B → `LoadEntriesForDateAsync(B)`
  ставить `CurrentEntry=B` → відкладений save спрацьовує проти **B** (`SyncToModel`+save B), правки дня A
  ніколи не синкаються в модель і зникають.
- Супутньо — гонка: `Diarion.Core/Helpers/AsyncDebouncer.cs:43-59` `FlushAsync` скасовує токен, але замикання,
  що вже пройшло перевірку `IsCancellationRequested` (`:34`) і чекає `action()`, **не зупиняється** → flush і
  debounce виконуються одночасно, обидва роблять `Clear+Add` на `Model.HabitsList` та `Upsert` → можливий
  зіпсований/частковий habit-список.

### Вимоги (Requirements)
- **R1.** У `ScheduleAutoSave` **захопити** конкретний запис у локальну змінну на момент планування
  (`var vm = CurrentEntry;`) і синкати/зберігати саме `vm`, а не поточний `CurrentEntry`.
- **R2.** Перед будь-якою зміною активного дня (`LoadDayContentAsync`/`LoadEntriesForDateAsync`) виконати
  `await FlushAutoSaveAsync()` для попереднього запису.
- **R3.** Викликати `FlushAutoSaveAsync()` також у `OnDisappearing`/зміні вкладки/`OnSleep` застосунку
  (перевірити, що вже підключено після WORK_PLAN етапу 3, і закрити прогалини).
- **R4.** Зробити `AsyncDebouncer` flush/fire **взаємовиключними**: одна операція save виконується в даний
  момент (напр. `SemaphoreSlim(1,1)` навколо `action()`), щоб flush чекав завершення in-flight debounce
  (або скасовував його до `await action()`).
- **R5.** `SyncToModel` має бути ідемпотентним і безпечним при повторному виклику.

### Критерії приймання (Acceptance)
- **AC1.** Тест: редагуємо запис дня A, у межах debounce-вікна перемикаємось на день B → після стабілізації в
  БД присутні **обидва** записи з коректними правками (правки A не втрачені).
- **AC2.** Тест: під час in-flight debounce викликаємо `FlushAutoSaveAsync` → `HabitsList` у БД
  консистентний (немає подвоєних/втрачених елементів), `SaveEntryAsync` не викликається конкурентно.
- **AC3.** Швидке перемикання між кількома днями поспіль не лишає «осиротілих» незбережених правок.
- **AC4.** Наявні тести `MainViewModelTests`/`AsyncDebouncerTests` лишаються зеленими; додано нові кейси.

### Зачеплені файли
- `Diarion.Core/ViewModels/MainViewModel.cs` (`ScheduleAutoSave` ~151, `FlushAutoSaveAsync` ~164,
  `LoadDayContentAsync` ~176, `LoadEntriesForDateAsync` ~195)
- `Diarion.Core/Helpers/AsyncDebouncer.cs`
- `Views/MainPage.xaml.cs` (`OnDisappearing`), `App.xaml.cs` (`OnSleep`, за потреби)
- Тести: `Diarion.Tests/MainViewModelTests.cs`, `Diarion.Tests/AsyncDebouncerTests.cs`

### Тест-план
- Unit (мок `IDiaryService`): відтворити сценарій «edit A → switch B у вікні debounce» і перевірити, що
  `SaveEntryAsync` викликано для A перед завантаженням B (AC1).
- Unit: конкурентний flush+debounce на `AsyncDebouncer` з лічильником одночасних виконань `action` = 1 (AC2).
- Regression: серія швидких перемикань дат.

### Ризики / застереження
- `FlushAutoSaveAsync` тепер може додати невелику затримку при зміні дня — прийнятно; за потреби показати
  короткий busy-стан.
- Стежити, щоб `IsBusy`-гейт (`OnEntryPropertyChanged:147`) не глушив легітимні правки після flush.
