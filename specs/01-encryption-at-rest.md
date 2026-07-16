# 01 — Шифрування даних at rest

- **Priority:** P0 · **Effort:** M · **Status:** Proposed · **Depends on:** — · **Blocks:** 03, 04

## Продукт

**Проблема/можливість.** Diarion позиціонується як «privacy-first, local-only» щоденник, але база даних із
найчутливішими записами (настрій, сон, здоров'я, менструальний цикл, інтимне життя, фінанси, нотатки)
зберігається **повністю у відкритому вигляді**. Це не виконана ключова обіцянка продукту й пряма
репутаційна/юридична загроза (особливо cycle-дані після Roe).

**Конкуренти.** Day One шифрує E2E **за замовчуванням для всіх** і робить це маркетинговим гаслом («немає
сервера — нічого зламати»). Clue/Drip/Periodical позиціонуються на локальному зберіганні + GDPR. Жоден із
них не лишає дані відкритими на диску.

**Цінність.** Перетворює головний технічний факт Diarion на перевірювану перевагу: «ваш щоденник ніколи
не покидає телефон і зашифрований ключем, що не залишає пристрій».

**User story.** *Як користувач, я хочу, щоб мої записи були нечитабельними навіть якщо хтось отримає файл
БД (root, adb, форензика, вкрадений телефон), щоб моє приватне життя лишалось приватним.*

## Інженерія

### Проблема / докази
- `Diarion.Core/Services/Database/DatabaseContext.cs:35` — `_db = new LiteDatabase(_dbPath);` відкриває БД
  **без пароля**. LiteDB 5.x підтримує AES через `Password` у connection string, але він не заданий.
- `Diarion.Core/Models/UserProfile.cs:31` — прапорець блокування (`IsBiometricAuthEnabled`) лежить у тій самій
  незашифрованій БД, тож захисне налаштування нічого не захищає.
- `Platforms/Android/AndroidManifest.xml` — `allowBackup="true"` + зайвий дозвіл `INTERNET`: ОС може
  авто-бекапити відкриту БД у Google Cloud / через adb, що прямо суперечить «no cloud».
- iOS: файл БД не має явного `NSFileProtectionComplete`.

### Вимоги (Requirements)
- **R1.** БД відкривається через `ConnectionString { Filename = path, Password = <key>, Connection = Shared }`
  (або `Direct` — обрати за результатом тесту на локи).
- **R2.** Ключ шифрування генерується один раз (криптостійко, ≥256 біт), зберігається у `SecureStorage`
  (iOS Keychain / Android Keystore-backed) і **ніколи не потрапляє в БД, Preferences чи логи**.
- **R3.** Одноразова **міграція існуючої відкритої БД** у зашифровану при апгрейді: детектувати незашифровану
  БД → `LiteEngine.Rebuild`/rebuild-copy у зашифровану → замінити атомарно → зафіксувати `SchemaVersion`
  (координація зі spec 12).
- **R4.** In-memory режим для тестів (`useInMemory=true`) лишається без пароля (не ламати тести).
- **R5.** `Platforms/Android/AndroidManifest.xml`: `android:allowBackup="false"`,
  `android:fullBackupContent="false"`, прибрати `INTERNET` (якщо мережа не потрібна для core).
- **R6.** iOS: встановити `NSFileProtectionComplete` (або `CompleteUnlessOpen`) для файлу БД.
- **R7.** Обробка втрати ключа: якщо `SecureStorage` очищено (напр. після відновлення пристрою), показати
  контрольований стан «дані недоступні» замість краху; задокументувати цей threat-model trade-off.

### Критерії приймання (Acceptance)
- **AC1.** Файл `diarion_local.db` неможливо відкрити `new LiteDatabase(path)` без пароля (кидає виняток),
  і hex-дамп не містить читабельного тексту записів.
- **AC2.** Після апгрейду зі старої (відкритої) збірки всі наявні дані читаються у новій (зашифрованій) без втрат.
- **AC3.** Ключ присутній лише у `SecureStorage`; grep по логах/Preferences/БД його не знаходить.
- **AC4.** На Android `adb backup` не витягує читабельну БД; маніфест не містить `INTERNET` (або він обґрунтований).
- **AC5.** Усі наявні unit-тести (in-memory) лишаються зеленими.

### Зачеплені файли
- `Diarion.Core/Services/Database/DatabaseContext.cs` (відкриття з паролем)
- `Diarion.Core/Services/Database/IDatabaseContext.cs` (за потреби)
- Новий: `Diarion.Core/Services/Security/IEncryptionKeyProvider.cs` (Core-інтерфейс)
- Новий: `Services/SecureStorageKeyProvider.cs` (app-проєкт, реалізація через `SecureStorage`)
- `Extensions/ServiceCollectionExtensions.cs` (реєстрація key-provider)
- `Platforms/Android/AndroidManifest.xml`, `Platforms/iOS/*` (Info.plist/entitlements)
- Координація з `12-schema-versioning-and-migrations.md`

### Тест-план
- Unit: генерація/збереження/зчитування ключа (мок `IEncryptionKeyProvider`).
- Unit: міграція відкрита→зашифрована на тимчасовому файлі (не in-memory) — усі колекції збережені.
- Integration: відкриття зашифрованої БД без ключа кидає виняток (AC1).
- Manual/E2E: чистий апгрейд поверх старої збірки з реальними даними (AC2); `adb backup` перевірка (AC4).

### Ризики / застереження
- **Втрата ключа = втрата даних** — це усвідомлений компроміс privacy-first; обов'язково поєднати з
  зашифрованим експортом (spec 03/09), щоб користувач мав шлях відновлення.
- Пароль LiteDB не можна змінити «на льоту» без rebuild — врахувати в міграції.
- `Connection=Shared` vs `Direct`: перевірити взаємодію з `_lock` та бекап-копіюванням файлу (spec 03).
