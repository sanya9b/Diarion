# 17 — Шлях до першої гіпотези: 14 днів тріалу як видимий об'єкт на головному екрані

**Status:** Approved (рішення D1–D5 підтверджені власником 2026-10-14) · **Пріоритет:** P1 (активація, конверсія тріалу) · **Складність:** M · **Залежить від:** 06, 15, 16 (фаза 0 і знак) · **Гілка:** `feature/insight-path` (worktree `D:\WORK\SOURCE_CODE\Diarion-insight`, від `feature/day-polish`)

Дата: 2026-10-13. Стан коду: `CorrelationService.MinSampleSize = 14`, тріал Apple 14 днів (spec 15, R2),
картка «N з 14 днів записано» живе на третій вкладці застосунку, у вкладці «Загальне» статистики, під
графіками.

## Продукт

**Проблема/можливість.** Підписка робить утримання в перші два тижні грошима: Apple списує оплату в
день 14, якщо людина не скасувала раніше. Флагманська цінність Diarion — перевірена гіпотеза — з'являється
лише після **14 парних днів** (настрій плюс чинник в один день), тобто в найкращому разі в день списання
і лише без жодного пропуску. Хто пропустив два дні, скасовує, так і не побачивши, за що платить.
Конкурентний аналіз (§5) називає «порожню картку два тижні» однією з двох найзадокументованіших причин
відтоку; картка 2.3 чесно каже «2 з 14», але її треба знайти на третій вкладці, і за замовчуванням вона
рахує «з першого числа місяця», тож третього числа показує «3 з 14» людині, що пише вже дванадцятий день.

**Що утримує в Finch і Duolingo** — не персонаж, а видимий об'єкт, що росте від щоденної дії. У Diarion
такий об'єкт уже є: власний щоденник людини. Цей spec робить його видимим на головному екрані як шлях
із чотирма віхами, кожна з яких відкриває щось, що в застосунку вже існує. Жодної істоти, жодної валюти,
жодного почуття провини за пропуск.

**Конкуренти.** Daylio показує «streak» і «entries», але ніколи — відстань до інсайту; Bearable ховає
кореляції за 30 днями історії і підпискою; Exist.io пише «чекайте три тижні». Ніхто не каже «ще 5 днів зі
сном — і буде перша перевірена гіпотеза».

**Цінність.** Новий користувач щодня бачить, скільки лишилось і що саме записати, щоб день зарахувався;
на 1, 3, 7 і 14 парних днях отримує щось відчутне; на 14-му бачить першу гіпотезу прямо в картці. Для
користувачів, у яких гіпотези вже є, картки не існує.

**User story.** *Як новий користувач на третій день тріалу, я бачу на головному екрані смужку з 14
поділок, закладку на третій і підпис «3 з 14 · найближче — сон»; я розумію, що сьогодні треба відмітити
настрій і сон.* *Як людина, що пропустила вихідні, я бачу ту саму закладку на тій самій поділці, а не
обнулення.* *Як користувач, у якого гіпотеза вже тримається, я ніколи не бачу цієї картки.*

**Не входить.** Зміна порогу 14, формул, FDR-корекції чи текстів гіпотез (spec 06); таймер тріалу чи
будь-яка згадка дати списання в картці (годинник Apple живе на екрані підписки, spec 15 R2); нові
сповіщення; телеметрія; персонаж.

## Інженерія

### Проблема / докази

- Поріг: [`Diarion.Core/Services/CorrelationService.cs:26`](../Diarion.Core/Services/CorrelationService.cs)
  `MinSampleSize = 14`; `GetReadinessAsync` (`:140`) повертає `CorrelationReadiness(PairedDays, RequiredDays)`
  — лише число, без ключа найближчого чинника, хоча цикл по чинниках його вже знає.
- Де картка: [`Views/StatisticsPage.xaml:389`](../Views/StatisticsPage.xaml) `InsightProgressAnchor`,
  дані з [`MoodStatsViewModel.cs:170`](../Diarion.Core/ViewModels/Statistics/MoodStatsViewModel.cs);
  діапазон за замовчуванням — [`StatisticsViewModel.cs:295`](../Diarion.Core/ViewModels/StatisticsViewModel.cs)
  `ApplyRange(StatsRange.MonthToDate())`, метод приватний.
- Головний екран: [`Views/MainPage.xaml:30-33`](../Views/MainPage.xaml) — під верхньою панеллю йде
  `AccessBanner`, далі календар. `AccessBanner.xaml.cs` бере свою `AccessBannerViewModel` з DI, щоб не
  роздувати `MainViewModel` (415 рядків) — той самий шаблон потрібен тут.
- Хук оновлення: `DiaryService.Announce` шле `DocumentChangedMessage` на кожне збереження/видалення;
  є також `TodoChangedMessage`, `CycleLogChangedMessage` ([`Models/Messages.cs`](../Diarion.Core/Models/Messages.cs)).
- Збереження «бачив»: `UserProfile.TourVersionSeen` та два сусідні лічильники — поле LiteDB без міграції,
  відсутнє читається як 0 ([`TourService.cs:54-77`](../Diarion.Core/Services/TourService.cs)).
- Доступ: `AccessState.CanWrite` ([`Services/Store/AccessState.cs`](../Diarion.Core/Services/Store/AccessState.cs));
  у режимі читання шлях не може рухатись, отже не має показуватись.
- Вартість підрахунку: `BuildSeriesAsync` будує й теми тексту (`BuildThemeFactorsAsync` →
  `IThemeClusterService.SummariseAsync`), що на головному екрані після кожного збереження зайве.

### Рішення власника, від яких залежить решта

- **D1. Вікно — останні 30 днів**, константа `InsightPath.WindowDays = 30`, незалежно від чипів
  статистики. Чому не «з першого числа»: шлях має бути стабільним щодня; чому не «весь час»: гіпотеза,
  яку покаже віха 14, має збігатися з тим, що покаже статистика за той самий період (R2.6). 30 днів
  вміщують 14 парних із двома пропусками на тиждень.
- **D2. Рахуються парні дні найближчого чинника**, не записи й не календарні дні — рівно те, що потім
  перевірить движок. Підказка називає, що саме треба записати, щоб день зарахувався.
- **D3. Чотири віхи — 1, 3, 7, 14 парних днів**, кожна відкриває вже наявний артефакт: 1 — перший
  піксель у Year in Pixels; 3 — розподіл емоцій (donut); 7 — настрій за годинами й тижнем плюс
  «найближчий чинник: сон, 7 з 14»; 14 — перша гіпотеза в самій картці (чинник, напрямок, n) і перехід до
  статистики. Святкування — лише ці чотири, кожне один раз.
- **D4. Місце — одразу під `AccessBanner`**, над календарем: єдине місце, яке бачить і той, хто не
  гортає. Альтернатива «між вкладками і блоками» програє: там картка їде під клавіатуру.
- **D5. «Сховати до наступної віхи»**, не назавжди: `UserProfile.InsightPathHiddenBelowMilestone` (int).
  Картка повертається, коли є новина. «Показати гіди знову» в налаштуваннях скидає і це поле (одна дія —
  усе спочатку, як у `TourService.ResetSeen`).

### Вимоги (Requirements)

**Межі (R0)**

- **R0.1.** Статистика не змінюється: ті самі `MinSampleSize`, `HasUsableSpread`, Pearson, Fisher-z,
  Benjamini–Hochberg. Шлях лише читає `GetReadinessAsync`/`GetMoodCorrelationsAsync`.
- **R0.2.** Жодних мережевих запитів, дозволів, лічильників, що покидають пристрій. Міряється лише
  конверсією тріалу у звітах App Store Connect / Play Console за версіями (до/після).
- **R0.3.** У картці немає дати списання, слова «тріал» і ціни. Вона про дані, не про гроші.
- **R0.4.** Без нових NuGet.

**Core (R1)**

- **R1.1.** `CorrelationReadiness` отримує `string? BestFactorKey` (ключ чинника з найбільшою кількістю
  парних днів із придатним розкидом; `null`, якщо жоден); `GetReadinessAsync` дістає його з того самого
  циклу. Новий необов'язковий параметр `includeThemes = true`; шлях передає `false` — теми тексту
  потребують енкодера й не мають вирішувати, чи зарахувався день.
- **R1.2.** Чистий тип `InsightPathMilestones` (Core, без залежностей): `Reached(int pairedDays)` →
  найвища досягнута віха з {1, 3, 7, 14} або 0; `Next(int pairedDays)` → наступна або `null`.
- **R1.3.** `IInsightPathService.GetAsync()` → `InsightPathState(bool IsVisible, int PairedDays,
  int RequiredDays, string? BestFactorKey, int ReachedMilestone, int? NextMilestone,
  MoodCorrelation? FirstHypothesis)`. Правила видимості: `IsVisible = CanWrite && !HasHoldingHypothesis
  && ReachedMilestone >= HiddenBelowMilestone`; `HasHoldingHypothesis` = `GetMoodCorrelationsAsync(window)`
  повернув хоч одну. На віхі 14 `FirstHypothesis` = найсильніша з них, картка видима, доки віху не
  закрито (R1.4). Користувач із гіпотезами й без нової віхи картки не бачить ніколи — так само legacy.
- **R1.4.** `HideUntilNextMilestoneAsync()` пише `UserProfile.InsightPathHiddenBelowMilestone =
  NextMilestone ?? int.MaxValue`; `ResetSeen` у `TourService` обнуляє його.
- **R1.5.** `InsightPathViewModel` (власна VM, як `AccessBannerViewModel`): `IsVisible`, `Segments`
  (14 × `bool`), `ProgressText` («9 з 14»), `HintText» (що записати сьогодні: назва чинника з наявних
  `Factor*` ресурсів), `MilestoneTitle`/`MilestoneBody`/`HasMilestone`, `HypothesisText`, команди
  `OpenCommand`, `HideCommand`. Оновлення: при появі сторінки та на `DocumentChangedMessage(Diary)`,
  `TodoChangedMessage`, `CycleLogChangedMessage` через наявний `AsyncDebouncer` (300 мс), на UI-потоці
  через `IDispatcherService`.
- **R1.6.** Нове повідомлення `StatisticsRangeRequestedMessage(StatsRange)`; `StatisticsViewModel`
  підписаний і викликає `ApplyRange` (метод лишається приватним, повідомлення — єдиний публічний вхід).
  `OpenCommand` = `NavigateToAsync("//StatisticsPage")` + це повідомлення з вікном D1, щоб людина побачила
  ту саму картку/гіпотезу, що й на головному.

**UI (R2)**

- **R2.1.** `Controls/InsightPathCard.xaml(.cs)` під `AccessBanner` у `MainPage`. Монохромна картка
  (`Theme_Snow_Black`, обводка `Theme_Dust_Ocean`, радіус 16 — як у банера), без тонування.
- **R2.2.** Смужка з 14 поділок: порожня — `Theme_Dust_Ocean`, зарахована — `Theme_Midnight_Snow`;
  закладка `Theme_Coral` (той самий `Path`, що в `BrandMark`, spec 16) стоїть над останньою зарахованою
  поділкою. Поділки — не календар: пропущений день не малюється взагалі, бо шлях рахує лише зараховані.
- **R2.3.** Рядок під смужкою: `ProgressText` зліва, `HintText` справа (`Theme_Ocean_Dust`, 12). На віхі —
  другий рядок `MilestoneTitle` (напівжирний) + `MilestoneBody`, кнопка «Переглянути» (`OpenCommand`),
  і текстова «Сховати» (`HideCommand`). Поза віхою кнопок немає — картка не просить нічого.
- **R2.4.** Рух за правилами гілки `feature/motion`: заповнення поділки `Motion.FastMs`; переїзд закладки
  `TranslationX` `Motion.StandardMs` `EaseOut`; поява віхи — прозорість і масштаб 0.96→1
  `Motion.StandardMs` (delight-бюджет: подія рідкісна). У `ReduceMotion` — лише прозорість. Жодного
  bounce, жодної анімації, коли стан не змінився.
- **R2.5.** `SemanticProperties.Description` на картці — одне речення: «9 з 14 днів до першої гіпотези,
  найближче — сон».
- **R2.6.** Тексти в resx обома мовами: `InsightPathProgressFormat`, `InsightPathHintFormat`,
  `InsightPathHintNoFactor` («Відмітьте настрій і сон — так день зарахується»), чотири пари
  `InsightPathMilestone{1,3,7,14}Title/Body`, `InsightPathHypothesisFormat` (чинник · напрямок · n),
  `InsightPathOpen`, `InsightPathHide`. Регістр — за spec 16 D1.

### Критерії приймання (Acceptance)

- **AC1.** На порожній базі в режимі запису картка видима з «0 з 14» і підказкою без чинника.
- **AC2.** 9 днів із настроєм і сном (сон із розкидом) → «9 з 14», `HintText` називає сон, закладка на
  дев'ятій поділці; віха 7 позначена досягнутою, віха 14 — ні.
- **AC3.** 9 днів лише з настроєм (без жодного чинника) → «0 з 14» і підказка R2.6 без чинника: день без
  пари не рахується, і картка каже чому.
- **AC4.** 14 парних днів із кореляцією, що тримається → `HasMilestone`, `HypothesisText` з назвою чинника
  й n = 14; після «Сховати» картка невидима; після нового запису — так само невидима (віх більше немає).
- **AC5.** 14 парних днів, але жодна гіпотеза не тримається (`StatsNoHypothesisHolds`) → картка
  невидима; пояснення лишається в статистиці, як і було (2.3).
- **AC6.** `AccessState.ReadOnly` → невидима незалежно від даних; Legacy з гіпотезами → невидима.
- **AC7.** Пропуск трьох календарних днів не зменшує `PairedDays` і не рухає закладку назад.
- **AC8.** `OpenCommand` відкриває вкладку статистики з тим самим вікном: картка `InsightProgressAnchor`
  або гіпотеза показують те саме число, що й головний екран (ручний прогін).
- **AC9.** `GetAsync()` з `includeThemes:false` не викликає `IThemeClusterService` (Moq `Verify`).
- **AC10.** Нуль попереджень збірки; `Diarion.csproj` без нових пакетів; дев'ять Android-дозволів.

### Зачеплені файли

- Нові: `Diarion.Core/Services/InsightPath/InsightPathMilestones.cs`, `IInsightPathService.cs`,
  `InsightPathService.cs`, `InsightPathState.cs`; `Diarion.Core/ViewModels/InsightPathViewModel.cs`;
  `Controls/InsightPathCard.xaml(.cs)`; тести `InsightPathMilestonesTests.cs`,
  `InsightPathServiceTests.cs`, `InsightPathViewModelTests.cs`.
- Зміни: [`Diarion.Core/Services/ICorrelationService.cs`](../Diarion.Core/Services/ICorrelationService.cs) і
  [`CorrelationService.cs`](../Diarion.Core/Services/CorrelationService.cs) (R1.1),
  [`Diarion.Core/Models/UserProfile.cs`](../Diarion.Core/Models/UserProfile.cs) (поле D5),
  [`Diarion.Core/Models/Messages.cs`](../Diarion.Core/Models/Messages.cs) (R1.6),
  [`Diarion.Core/ViewModels/StatisticsViewModel.cs`](../Diarion.Core/ViewModels/StatisticsViewModel.cs) (підписка R1.6),
  [`Diarion.Core/Services/TourService.cs`](../Diarion.Core/Services/TourService.cs) (`ResetSeen`),
  [`Views/MainPage.xaml`](../Views/MainPage.xaml) (один рядок під банером),
  [`Extensions/ServiceCollectionExtensions.cs`](../Extensions/ServiceCollectionExtensions.cs) (сервіс
  singleton, VM transient), `AppResources.resx` / `.uk.resx`, `CorrelationServiceTests.cs` (BestFactorKey).

### Тест-план

- `InsightPathMilestonesTests` — таблиця `[Theory]`: 0→(0, 1), 1→(1, 3), 2→(1, 3), 3→(3, 7), 6→(3, 7),
  7→(7, 14), 13→(7, 14), 14→(14, null), 20→(14, null).
- `InsightPathServiceTests` — на In-Memory LiteDB за зразком `CorrelationServiceTests.Build(...)`:
  AC1–AC7, AC9; `IAccessGuard`/`IEntitlementService` через Moq; профіль із `HiddenBelowMilestone`.
- `InsightPathViewModelTests` — оновлення на кожне з трьох повідомлень (один `GetAsync` на серію завдяки
  debounce), `HideCommand` пише профіль і ховає, `OpenCommand` шле `StatisticsRangeRequestedMessage` з
  вікном 30 днів і навігує на `//StatisticsPage`; тексти з resx підставлені (`InsightPathProgressFormat`).
- `CorrelationServiceTests` — `BestFactorKey` для одного чинника, двох із різною кількістю днів, і `null`,
  коли розкиду немає.
- `StatisticsViewModelTests` (наявні) — новий `[Fact]`: повідомлення міняє обидві дати одним кроком.
- Ручний: `run-diarion` із DEBUG-сідом на 0 / 3 / 9 / 14 днів; Android Release на телефоні — смужка на
  вузькому екрані, обидві теми, «зменшити рух».

### Фази

- **A. Core** (1 день): R1.1–R1.4 з тестами. Злиття можливе окремо — UI ще не споживає.
- **B. VM і повідомлення** (0.5 дня): R1.5–R1.6, тести VM, підписка в статистиці.
- **C. Картка** (1–1.5 дня): R2.1–R2.6, resx, скриншоти. Залежить від spec 16 фази 0 (токени) і R2.1 (закладка).
- **D. Вимір** (без коду): через 30 днів після релізу порівняти конверсію тріалу з попередньою версією в
  App Store Connect; записати в roadmap під пунктом 2.5.

### Ризики / застереження

- **Людина пише лише настрій і бачить «0 з 14» на дев'ятий день.** Це правда, і вона корисна, але
  образлива, якщо сказана без пояснення. Тому підказка без чинника завжди називає найдешевшу пару
  («настрій і сон») і ніколи не пише «0» без неї (AC3).
- **Розбіжність із третьою вкладкою.** Статистика за замовчуванням рахує «з першого числа»; без R1.6 картка
  на головному й картка в статистиці показуватимуть різні числа. R1.6 обов'язковий, не «потім».
- **Теми тексту.** Повний `BuildSeriesAsync` ганяє кластеризацію тем після кожного збереження; `includeThemes:false`
  і debounce тримають головний екран дешевим. Якщо профілювання покаже, що й так дорого — кешувати
  `InsightPathState` до наступного повідомлення.
- **Перебір святкувань.** Чотири віхи, кожна один раз, жодної на щоденному збереженні. Якщо після
  тесту на людях три з чотирьох віх здаються зайвими — лишити 7 і 14, таблиця в `InsightPathMilestones`
  міняється в одному місці.
- **Користувачі 1.0 з довгою історією** на апгрейді мають гіпотези, отже картки не бачать (R1.3). Ті, у
  кого історія є, а гіпотез немає (мало чинників), побачать картку вперше — це бажано: їм вона теж каже,
  чого бракує.
