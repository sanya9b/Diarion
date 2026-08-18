# Політика приватності Diarion / Diarion Privacy Policy

> **Чернетка, зібрана з коду, а не з побажань.** Кожне твердження нижче має відповідник у
> репозиторії, і поруч у дужках названо файл, який його підтверджує — щоб при наступній зміні коду
> було видно, який абзац перестав бути правдою. Дужки прибрати перед публікацією.
>
> **Що лишається зробити власнику:**
> 1. Підставити домен, поштову адресу для звернень і дату набуття чинності замість `TODO`.
> 2. Викласти обидві мови за постійними URL — Apple вимагає URL, не PDF і не текст у застосунку.
> 3. Перечитати розділ «Здоров'я» після рішення по HealthKit і розділ «Мережа» після рішення по AI:
>    обидва описують те, що ввімкнене **сьогодні**, і обидва ті рішення ще відкриті.
> 4. Переписати повністю, коли відвантажиться спека 14 — з нею вперше з'являється передавання тексту
>    записів третій стороні, і жоден абзац нижче тоді не чинний.

---

## Українською

**Чинна з:** TODO
**Розробник:** TODO (фізична особа)
**Контакт:** TODO@TODO

### Коротко

Diarion не має акаунтів, серверів і синхронізації. Ваш щоденник зберігається на вашому пристрої й
не надсилається ані розробнику, ані будь-кому іншому. Ми не збираємо статистику використання, не
показуємо реклами і не використовуємо жодних аналітичних чи рекламних SDK.

### Які дані застосунок зберігає

Все перелічене живе **виключно** у сховищі застосунку на вашому пристрої:

- записи щоденника, настрій, рефлексії, нотатки й теги;
- звички, задачі й події планера, бажання, добрі справи, приємні моменти;
- фінансові операції, рахунки, бюджети;
- дані трекера циклу й пов'язані з ним симптоми;
- час сну, зчитаний з Apple Health (див. нижче);
- налаштування: тема, мова, валюта, PIN, порядок швидкого меню.

База даних зашифрована на диску. Ключ шифрування створюється на вашому пристрої й зберігається в
системному сховищі ключів (Keychain на iOS, Keystore на Android); розробник його не має і отримати
не може (`Diarion.Core/Services/Database/EncryptedLiteDatabaseFactory.cs`,
`Services/SecureStorageKeyProvider.cs`). PIN зберігається як хеш, а не як число
(`Diarion.Core/Services/PinHasher.cs`).

### Куди дані можуть піти — і тільки за вашою дією

- **Резервна копія.** Ви створюєте її самі. Файл шифрується ключем, похідним від **вашої** парольної
  фрази через PBKDF2 — не ключем пристрою, — і відкривається системним вікном «Поділитися», де
  місце призначення обираєте ви (`Diarion.Core/Services/BackupService.cs`). Парольної фрази
  застосунок ніде не зберігає: забули її — резервну копію не відкрити ніким, включно з розробником.
- **Експорт.** JSON, CSV і Markdown формуються локально й так само віддаються вам через «Поділитися»
  (`Diarion.Core/Services/ExportService.cs`).
- **Звіт про збій.** Якщо застосунок аварійно завершився, останній виняток записується у локальний
  файл, і вкладка «Дані» пропонує надіслати або видалити його. Нічого не надсилається автоматично
  (`Diarion.Core/Diagnostics/CrashReporter.cs`).

### Здоров'я (тільки iOS)

Застосунок може запитати доступ до Apple Health, щоб зчитати **аналіз сну** й підставити час сну у
ваш запис. Доступ **лише на читання**: набір типів для запису порожній, і застосунок ніколи нічого
не записує в Health. Зчитані значення лишаються у вашій локальній базі. Дозвіл необов'язковий —
відмова не блокує жодну функцію (`Services/HealthDataService.cs`). На Android інтеграції зі
здоров'ям немає взагалі.

### Мережа

Застосунок звертається до мережі рівно в одному випадку: **завантаження файлів моделей ШІ** з
HuggingFace, і тільки коли ви самі почали таке завантаження в налаштуваннях
(`Diarion.Core/Services/Ai/HttpModelFileTransfer.cs`). Це завантаження **вниз**: жоден ваш запис,
жодне слово з щоденника і жоден ідентифікатор при цьому не передаються. Обробка тексту моделлю
відбувається на пристрої.

Інших мережевих звернень у застосунку немає: ні телеметрії, ні перевірки оновлень, ні реклами.

### Чого немає

- Немає акаунтів і реєстрації.
- Немає серверів Diarion — надіслати дані нікуди, бо немає куди.
- Немає аналітики, крешлітики, A/B-тестів і рекламних ідентифікаторів.
- Немає передавання даних третім особам і продажу даних.
- Немає відстеження між застосунками (App Tracking Transparency не використовується, бо нічого
  відстежувати).

### Ваші права

Дані у вас на руках, тому права реалізуються діями в застосунку, а не запитом до розробника:
переглянути — відкрити застосунок; отримати копію — «Експорт» або «Резервна копія»; видалити —
видалити застосунок (сховище застосунку зникає разом із ним) або конкретний запис у застосунку. На
Android хмарне резервне копіювання вимкнено на рівні маніфесту (`allowBackup="false"`), тож ваш
щоденник не потрапляє в Google-бекап.

### Діти

Застосунок не призначений для дітей і не збирає жодних даних, а отже не збирає їх і від дітей.

### Зміни політики

Нова редакція публікується за цим самим URL із новою датою набуття чинності. Якщо зміна стосується
передавання даних кудись за межі пристрою, застосунок запитає окрему згоду перед першим таким
передаванням.

### Контакт

TODO@TODO

---

## English

**Effective:** TODO
**Developer:** TODO (individual)
**Contact:** TODO@TODO

### In short

Diarion has no accounts, no servers and no sync. Your diary is stored on your device and is not sent
to the developer or to anyone else. We collect no usage statistics, show no ads, and bundle no
analytics or advertising SDKs.

### What the app stores

All of the following lives **only** in the app's storage on your device:

- diary entries, mood, reflections, notes and tags;
- habits, tasks and planner events, wishlist, good deeds, happy moments;
- financial transactions, accounts, budgets;
- cycle tracker data and its symptoms;
- sleep times read from Apple Health (see below);
- settings: theme, language, currency, PIN, quick-menu order.

The database is encrypted on disk. The encryption key is generated on your device and held in the
system keystore (Keychain on iOS, Keystore on Android); the developer does not have it and cannot
obtain it. The PIN is stored as a hash, not as a number.

### Where data can go — and only if you send it

- **Backup.** You create it. The file is encrypted with a key derived from **your** passphrase via
  PBKDF2 — not with the device key — and handed to the system share sheet, where you pick the
  destination. The app never stores the passphrase: lose it and nobody can open the backup, the
  developer included.
- **Export.** JSON, CSV and Markdown are produced locally and handed to you the same way.
- **Crash report.** If the app crashed, the last exception is written to a local file and the Data
  tab offers to send or delete it. Nothing is sent automatically.

### Health (iOS only)

The app may ask for Apple Health access to read **sleep analysis** and prefill the sleep times of
your entry. Access is **read-only**: the set of types requested for writing is empty and the app
never writes to Health. Values read stay in your local database. The permission is optional and
denying it blocks no feature. There is no health integration on Android at all.

### Network

The app reaches the network in exactly one case: **downloading AI model files** from HuggingFace,
and only when you start that download yourself in settings. It is a download: none of your entries,
no diary text and no identifier are transmitted. Text is processed by the model on the device.

There are no other network calls: no telemetry, no update checks, no ads.

### What there isn't

- No accounts, no registration.
- No Diarion servers — data cannot be sent anywhere because there is nowhere to send it.
- No analytics, crash analytics, A/B tests or advertising identifiers.
- No sharing with or selling to third parties.
- No cross-app tracking (App Tracking Transparency is not used, because there is nothing to track).

### Your rights

The data is in your hands, so the rights are exercised in the app rather than by writing to the
developer: to view it, open the app; to obtain a copy, use Export or Backup; to delete it, delete
the app (its storage goes with it) or delete the individual entry. On Android, cloud backup is
disabled in the manifest (`allowBackup="false"`), so your diary does not end up in a Google backup.

### Children

The app is not directed at children and collects no data, therefore it collects none from children.

### Changes

A new version is published at this same URL with a new effective date. If a change involves sending
data off the device, the app will ask for separate consent before the first such transmission.

### Contact

TODO@TODO
