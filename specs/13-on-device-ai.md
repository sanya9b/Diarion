# 13 — Модуль ШІ на пристрої

- **Priority:** P2 · **Effort:** XL · **Status:** Approved · **Depends on:** 06, 09, 12 · **Blocks:** —

## Продукт

**Проблема/можливість.** Diarion продається як «розумний щоденник», але вся розумність зараз —
детерміновані агрегати й кореляції. `DiaryEntry.AiSummary` змодельоване з першого дня і нічим не
заповнюється. Найгостріше: **у щоденнику немає пошуку взагалі** — `IDiaryService` не має жодного
методу запиту, а `NoteService.SearchNotesAsync` шукає лише по нотатках і лише підрядком. Через
рік ведення записів користувач не може знайти власний запис інакше, ніж гортанням календаря.

**Конкуренти.** Day One і Apple Journal мають семантичний пошук і AI-підсумки; Day One за
серверний AI отримав відкат, якого його ж on-device хвиля не отримала. r/Journaling банить
AI-згенеровані записи. Тобто ринок хоче **знайти й осмислити своє**, а не отримати написане за
себе. Ніхто з лідерів не робить це повністю на пристрої українською.

**Цінність.** Пошук за змістом, теми місяця, звіти й розмова з власним щоденником — без акаунта,
без сервера, без передавання записів кудись. Природний кандидат у платний «Diarion Pro».

**User story.** *Як користувач, я хочу спитати «коли я найкраще спав цього місяця» або «що я писав
про роботу» і отримати відповідь із посиланнями на власні записи, щоб не гортати рік вручну.*

## Інженерія

### Проблема / докази

- `Diarion.Core/Services/IDiaryService.cs` — жодного методу пошуку; записи дістаються лише за датою.
- `Diarion.Core/Services/NoteService.cs:32` — `SearchNotesAsync` робить `FindAll()` + `Contains`
  у пам'яті, лише по нотатках. Коментар там сам визнає, що це вибір простоти.
- `Diarion.Core/Models/DiaryEntry.cs:92` — `AiSummary` існує й не пишеться ніким
  (`PRODUCT_ROADMAP.md:537` тримає це в техборгу).
- `Diarion.Core/Services/StatisticsService.cs` рахує числа, але нічого не знає про **зміст** записів.
- Фонової інфраструктури немає: ні `IHostedService`, ні черги, ні `IProgress<T>`.

### Рішення власника, від яких залежить решта

- **R0.1.** Мережа (2026-08-02): застосунок отримує `INTERNET` + `ACCESS_NETWORK_STATE` **виключно
  для завантаження файлів моделей**. Хмарний інференс, синхронізація та BYOK лишаються відкинутими.
  Обіцянка переформульовується з «немає мережі» на «дані нікуди не йдуть».
- **R0.2.** Джерело моделей — HuggingFace, включно з власним репозиторієм для сконвертованої
  генеративної моделі. Власного сервера немає (несумісно з довічною ліцензією).
- **R0.3.** Чат — **строго grounded RAG**. Слабкий витяг → відмова, ніколи не здогадка.
- **R0.4.** ШІ вимкнений за замовчуванням.

### Вимоги (Requirements)

**Рантайм і ембединги (Фаза A)**

- **R1.** `Microsoft.ML.OnnxRuntime` 1.28.0 у головному проєкті, `.Managed` +
  `Microsoft.ML.Tokenizers` 2.0.0 у Core. Виконавець CPU/XNNPACK; NNAPI не використовувати
  (депрекована з Android 15).
- **R2.** `ITextEmbedder` у Core, реалізація `OnnxTextEmbedder` у головному проєкті — за наявною
  конвенцією «інтерфейс у Core, нативна обгортка поруч із `MauiFileSystemService`». Повертає
  L2-нормалізовані вектори, 384 виміри.
- **R3.** `Microsoft.ML.Tokenizers` **не має завантажувача `tokenizer.json`**. Використати
  `SentencePieceTokenizer.Create(Stream, ...)` на `sentencepiece.bpe.model` і реалізувати ремап
  fairseq окремим класом `XlmrIdMap` у Core: `<s>`=0, `<pad>`=1, `</s>`=2, `<unk>`=3, решта
  `sp_id + 1`, `<mask>`=250001.
- **R4.** Колекція `ai_embeddings` (константа в `DatabaseConstants.cs`): `Id` (детермінований
  SHA-256 від `kind|sourceId|ordinal`), `SourceKind`, `SourceId`, `Ordinal`, `SourceDate`, `Text`,
  `ContentHash`, `ModelId`, `Dim`, `Vector` (`byte[]`, float32 LE). Міграція не потрібна —
  `MigrationRunner.CurrentVersion` лишається 7.
- **R5.** Пошук — повний перебір скалярним добутком (`TensorPrimitives.Dot`) по суцільному
  `float[]`. ANN-індекс не вводити до ~100 тис. чанків.
- **R6.** `ai_embeddings` виключається з `ExportService.AllCollections`; `BackupService` дропає
  колекцію в тимчасовій копії перед пакуванням.
- **R7.** Індексація — одна `Task` + `CancellationTokenSource`, **без курсора**: черга виводиться
  як «джерела без рядка з таким `(SourceId, ContentHash, ModelId)`». Пачки по 8, скасування в
  `App.OnSleep()`, перезабір колекції через `IDatabaseContext.GetCollection<T>()` на кожній пачці
  (бо `Reopen()` інвалідує утримувану). Переіндексація після редагування — наявним `AsyncDebouncer`.

**Каталог і завантаження (Фаза B)**

- **R8.** `IModelDownloadService`: резюмоване завантаження через HTTP `Range`, звірка SHA-256,
  видалення файлу при розбіжності. За замовчуванням лише Wi-Fi, із перемикачем.
- **R9.** Файли — у `AppDataDirectory`, **не в `CacheDirectory`** (систему вільно чистить кеш).
- **R10.** URL пінуються на конкретний commit SHA (`resolve/<sha>/файл`). `catalog.json` на HF із
  вбудованою копією як запасною.
- **R11.** `DeviceTier` за `ActivityManager.MemoryInfo.TotalMem`, `Build.SupportedAbis` і вільним
  місцем: Low (<4 ГБ або 32-біт) — лише ембединги; Mid (4–6 ГБ) — Qwen3-0.6B; High (≥6 ГБ, ≥6 ядер)
  — повний список. «Рекомендовано» — найбільша модель, де `MinTier ≤ tier` і
  `SizeBytes ≤ вільне × 0.5`.
- **R12.** UI — четверта вкладка в `Views/ProfilePage.xaml` (патерн `SelectTabCommand`,
  `CommandParameter="3"`): список моделей зі станом, розміром, вимогами, значком «рекомендовано»,
  прогресом і видаленням; перебудова/видалення індексу; заміряння швидкості на пристрої.

**Інтеграції (Фаза C)**

- **R13.** `ISemanticSearchService` + `Views/SearchPage.xaml`: гібрид векторного пошуку з наявним
  `NoteService.SearchNotesAsync` через reciprocal-rank fusion. Щоденник отримує запит на рівні
  сервісу пошуку, `IDiaryService` не чіпається. Дебаунс 250 мс.
- **R14.** Вкладка «Теми» у `StatisticsViewModel`: k-medoids по векторах чанків діапазону, підпис
  теми — провідне речення медоїда (**без генерації**). Присутність теми в дні подається як фактор
  у наявний `CorrelationService`, успадковуючи його поріг 14 пар і корекцію Benjamini-Hochberg.
- **R15.** `DigestService` на наявних `ReportPeriod` і `StreakCalculator`. **Екстрактивний за
  замовчуванням** — тижневі й місячні звіти працюють без генеративної моделі, на всіх пристроях.
  Заповнює `DiaryEntry.AiSummary`, закриваючи рядок техборгу.

**Генерація і чат (Фаза D)**

- **R16.** `Microsoft.ML.OnnxRuntimeGenAI` **0.14.1, пін обов'язковий** — у 0.15.1 AAR оголошує
  `INTERNET`, `ACCESS_NETWORK_STATE` і телеметрійний `ContentProvider`.
- **R17.** Модель — Qwen3 (Apache-2.0, 100+ мов). Готового ORT-GenAI-формату не існує; збирається
  разово через `model_builder.py` і публікується у власний HF-репозиторій.
- **R18.** `PromptBuilder`: топ-24 → MMR до 8 → бюджет ~1800 токенів, блоки `[1]..[8]` із датами.
  **Ворота відмови до виклику моделі:** найкращий скор < 0.35 або менше двох чанків вище порогу →
  локалізована відмова, модель не запускається.
- **R19.** `CitationParser` витягає `\[(\d+)\]`, відкидає маркери поза діапазоном. **Нуль валідних
  цитат → відповідь понижується до відмови.** Це механічне забезпечення R0.3, а не інструкція
  в промпті.
- **R20.** Чипи-джерела ведуть на `"DiaryDetail"` через `INavigationService`.

**Наскрізні**

- **R21.** Усі рядки — в `AppResources.resx` + `AppResources.uk.resx` + рукописний
  `AppResources.Designer.cs` (три правки на ключ). Жодного хардкоду.
- **R22.** CI-задача `android-permissions` перетворюється з заборони на **білий список**: сім
  дозволених, падіння на будь-якому іншому.
- **R23.** Усі чотири фази кросплатформні (Android, iOS, MacCatalyst, Windows). Не заявляти
  прискорення CoreML/Neural Engine: EP реєструється явно й регулярно повертає підграфи на CPU.

### Критерії приймання (Acceptance)

- **AC1.** `XlmrIdMap` дає id, що збігаються з еталонними, на українському й англійському тексті
  (золоті фікстури). Це головна пастка фази: помилка тут дає правдоподібні, але беззмістовні
  вектори, які нічим себе не викажуть.
- **AC2.** Санітарна перевірка на старті: `cos(собака, цуценя) > cos(собака, бюрократія)`
  українською. Провал **вимикає ШІ**, а не пропускає далі.
- **AC3.** Пошук «що я писав про роботу» знаходить записи, де слова «робота» немає, але зміст той.
- **AC4.** Індексація 1000 записів не блокує UI, переживає згортання застосунку й продовжується з
  того самого місця без дублювання рядків.
- **AC5.** Обірване завантаження моделі відновлюється з байта, на якому спинилось; підмінений файл
  не проходить звірку SHA-256, видаляється, і модель не позначається встановленою.
- **AC6.** На пристрої з <4 ГБ RAM генеративні моделі позначені недоступними, а ембединги працюють.
- **AC7.** Місячний звіт формується без жодної генеративної моделі й містить теми, числа й цитати
  з власних записів.
- **AC8.** Питання, на яке в щоденнику немає даних, дає відмову, а не вигадану відповідь.
  Відповідь без валідних цитат теж дає відмову.
- **AC9.** `dotnet build -f net10.0-android -c Release` дає маніфест рівно з семи дозволів;
  вісім — CI падає.
- **AC10.** Вимкнення ШІ в налаштуваннях дропає `ai_embeddings`; експорт і бекап їх не містять.

### Зачеплені файли

- Новий каталог: `Diarion.Core/Services/Ai/` — `ITextEmbedder`, `ITextGenerator`, `IVectorStore`,
  `IEmbeddingIndexService`, `ISemanticSearchService`, `IDiaryChatService`, `IAiModelStore`,
  `IModelDownloadService`, `IDeviceCapabilityProbe`, `XlmrIdMap`, `EmbeddingMath`, `TextChunker`,
  `LiteDbVectorStore`, `EmbeddingIndexService`, `SemanticSearchService`, `ThemeClusterService`,
  `DigestService`, `PromptBuilder`, `CitationParser`, `AiModelCatalog`, `DeviceTier`
- Новий каталог: `Diarion.Core/Models/Ai/` — `EmbeddingChunk`, `AiModelDescriptor`, `SearchHit`,
  `ChatCitation`, `AiIndexProgress`
- Новий каталог: `Services/Ai/` — `OnnxTextEmbedder`, `XlmRobertaTokenizer`,
  `OnnxGenAiTextGenerator`, `MauiDeviceCapabilityProbe`
- Нові: `Views/SearchPage.xaml`, `Views/AiChatPage.xaml`, `Controls/ThemeBubbleChart.cs`,
  `ViewModels/SearchViewModel.cs`, `ViewModels/AiChatViewModel.cs`, `ViewModels/AiSettingsViewModel.cs`,
  `ViewModels/Statistics/ThemesStatsViewModel.cs`
- Існуючі: `Diarion.csproj`, `Diarion.Core.csproj`, `Platforms/Android/AndroidManifest.xml`,
  `Extensions/ServiceCollectionExtensions.cs`, `AppShell.xaml.cs`, `App.xaml.cs`,
  `Services/Database/DatabaseConstants.cs`, `Services/Database/DatabaseContext.cs`,
  `Services/ExportService.cs`, `Services/BackupService.cs`, `Services/DiaryService.cs`,
  `Services/NoteService.cs`, `Models/UserProfile.cs`, `Models/DiaryEntry.cs`,
  `ViewModels/StatisticsViewModel.cs`, `Views/ProfilePage.xaml`, `Resources/Localization/*`,
  `.github/workflows/ci.yml`
- Тести: `XlmrIdMapTests`, `EmbeddingMathTests`, `TextChunkerTests`, `LiteDbVectorStoreTests`,
  `CitationParserTests`, `PromptBuilderTests`, `DeviceTierTests`, `ModelDownloadServiceTests`,
  `SemanticSearchServiceTests`, `DigestServiceTests`

### Тест-план

- Unit (серійно — паралелізацію xUnit вимкнено через гонки глобального мапера LiteDB): золоті
  фікстури токенізатора; пулінг/нормалізація/косинус; чанкер на межах секцій і порожніх записах;
  roundtrip і ранжування сховища; резюме `Range` і провал SHA-256; цитати поза діапазоном і нуль
  цитат; бюджет токенів і ворота відмови; пороги тірів.
- Manual на Windows (Фази A–C): ORT має win-x64 нативи, тож завантаження, індексація, пошук, теми
  й дайджест перевіряються в реальному застосунку без емулятора.
- Manual на телефоні власника: завантаження по мобільній мережі, швидкість інференсу, UI.
  Емулятора немає — потрібен скриншот.
- Manual (Фаза D): ~40 питань українською по синтетичному щоденнику; запис ток/с і якості.
- CI: `dotnet test` + білий список дозволів у release-маніфесті Android.

### Ризики / застереження

- **Ремап токенізатора — головний ризик.** Помилка не падає, не логується і не помітна в UI:
  пошук просто тихо стає гіршим. Тільки золоті фікстури й AC2 це ловлять.
- **Якість української в Qwen3-0.6B не перевірена.** Опублікованих українських бенчмарків для цих
  моделей немає. Це вимірюється у Фазі D, і негативний результат — прийнятний вихід: звіти, пошук
  і теми від нього не залежать.
- **Пін 0.14.1 — постійне зобов'язання.** Ловиться лише CI-білсписком; без нього оновлення пакета
  тихо додало б телеметрію.
- **Ембединги для української можуть виявитись слабкими.** `paraphrase-multilingual-MiniLM-L12-v2`
  не публікує per-language MTEB. Альтернатива — `multilingual-e5-small` (MIT); перевіряється на
  власному наборі, а не за карткою моделі.
- **Обіцянка приватності звузилась.** Гарантія рівня ОС замінена на твердження. Текст у магазині
  має згадувати мережу явно, інакше це буде хибна реклама — та сама пастка, про яку попереджав
  spec 01.
- **Розмір і час.** Гігабайт по мобільній мережі — це хвилини з відкритим застосунком. Фонове
  завантаження (`DownloadManager` / `NSURLSession`) вводиться лише якщо практика Фази B покаже потребу.
