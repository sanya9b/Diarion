# App Store — нотатки для рев'ю, віковий рейтинг, залишок по подачі

Складено 2026-08-17 з коду. Маркетингового тексту тут немає свідомо: заголовок, підзаголовок і опис
залежать від пунктів 1.1 і 1.2 роадмапи («продавати одну роботу»), які ще не вирішені, а вигадувати
позиціювання за власника — гірше, ніж лишити порожнє місце.

> **Шукаєте, з чого почати подачу?** Цей файл — для рев'юера і для історії рішень. Порядок дій, у
> якому натискати кнопки, — [`submission-runbook.md`](submission-runbook.md); розділи 6–8 нижче
> лишаються поясненням «чому саме так», на яке той порядок спирається.

---

## 1. Знахідка, яка змінює оцінку блокера 0.9

Блокер 0.9 сформульований так: «жодного "фертильність", "овуляція", "контрацепція", "клінічно" —
призначення продукту регулятор читає з опису, а не з коду». Перша половина правильна, друга —
неповна, і код це показує.

Застосунок **уже рахує і показує фертильне вікно**, причому не як побічний обчислювальний артефакт,
а як названу фічу в інтерфейсі:

| Що | Де |
|---|---|
| Календарний (ритмічний) метод: овуляція за 14 днів до наступного початку, вікно −5/+1 день | `Diarion.Core/Services/CycleForecastCalculator.cs:40-42` |
| Прапорець у моделі прогнозу | `Diarion.Core/Models/CycleForecast.cs:69` |
| Підпис «Фертильне вікно (оцінка)» / «Fertile window (estimate)» на екрані циклу | `AppResources.uk.resx:818`, `AppResources.resx:830`, `Views/CyclePage.xaml:42` |
| **Іконка соски** на днях вікна в календарі | `Controls/CalendarView.xaml:101`, `Resources/Images/pacifier.svg` |

Показується безумовно — тумблера «плануємо вагітність» немає, гілка вмикається щоразу, коли прогноз
її порахував (`CalendarSectionViewModel.cs:226`, `CycleViewModel.cs:173`).

**Чому це важливо саме для подачі.** І 21 CFR 884.5370, і EU MDR Rule 15 виводять призначення
продукту з того, як він себе подає, а подає він себе не лише описом у магазині: інтерфейс — це теж
маркування. Соска-пустушка поруч із передбаченим вікном повідомляє мету «планування зачаття»
недвозначно і без жодного слова. Рев'юер Apple цей екран відкриє.

### Рішення власника, 2026-08-24: соска вимкнена у v1

Обрано перший із трьох варіантів, що були тут перелічені, — **прибрати іконку, лишити підпис**.
Механізм той самий, що й у `GenerationOffered`: `CycleDisplay.FertileWindowMarkerOffered = false`,
нічого не видалено, розрахунок працює, `CalendarDay.IsFertileWindow` і далі каже правду про день.
Малює тепер `ShowsFertileWindow`. Повернути — один прапорець.

Розв'язальний аргумент знайшовся не в переліку вище, а в тому, **де що лежить**. Чесне речення в
застосунку вже було: `CycleDisclaimer` — «не медична порада і не метод контрацепції». Але воно на
екрані циклу, а соску малює `CalendarView`, який стоїть на головній. Тобто найсильніший сигнал
наміру був на екрані без застереження, а застереження — на екрані без сигналу. Вимкнення іконки
зводить твердження і його уточнення на один екран: підпис «Фертильне вікно (оцінка)» лишається
рівно над дисклеймером.

Ціна нульова: іконка не несла інформації, якої немає в підписі.

**Умова повернення**, і вона важливіша за сам прапорець: який екран показує маркер, той показує й
дисклеймер. Розходження цих двох — і є те, що виправлено сьогодні.

**Блокер 0.9 переписаний 2026-08-31** — уже не список заборонених слів, а правило з трьох рядків:
слова лишаються під забороною в тексті, картинка каже те саме, що слово, а твердження і його
застереження живуть на одному екрані. Третій рядок і є умовою повернення маркера, виписаною вище.

---

## 2. Віковий рейтинг — чим відповідати на анкету

Apple питає про зміст, а не про наміри, тож відповіді нижче навмисно незручні. Розбіжність між
анкетою і тим, що рев'юер бачить на екрані, — окрема підстава для відмови, дорожча за вищий рейтинг.

| Питання анкети | Відповідь | Підстава в коді |
|---|---|---|
| Medical / Treatment-Focused Content | **Так, Infrequent/Mild** | Трекер циклу з симптомами, самопочуття, сон, фертильне вікно. Це інформація про здоров'я, а не лікування — але «ні» тут неправда. Відповідь не змінилась від того, що HealthKit прибрали: дані ті самі, просто вводяться руками |
| Alcohol, Tobacco, or Drug Use or References | **Так, Infrequent/Mild** | Трекер кинутих звичок; підказка в полі прямо каже «Наприклад: куріння або безкінечний скролінг» (`AppResources.uk.resx:380`). Контекст — відмова від звички, тобто мінімальний ступінь |
| Sexual Content or Nudity | Ні | Репродуктивне здоров'я — це медична категорія, не сексуальний контент |
| Violence / Horror / Profanity / Gambling / Contests | Ні | Немає |
| User-Generated Content | Ні | Записи бачить лише автор; немає ні обміну, ні стрічки, ні профілів, тож модерація за 1.2 не застосовна |
| Unrestricted Web Access | Ні | Вебв'ю немає; єдине мережеве звернення — завантаження файлів моделей |
| In-App Purchases | Ні (для v1) | StoreKit у проєкті відсутній |

**Вікові ворота всередині застосунку (2026-09-20).** Рядок «Інтимне життя» в блоці здоров'я
показується лише профілю з указаною датою народження і віком 18+, календар циклу — з 14
(`Diarion.Core/Services/AgeGate.cs`, `UserProfile.IsIntimateLifeAvailable` /
`IsCycleFeatureAvailable`). Дата народження необов'язкова, лишається на пристрої й нікуди не
експортується; без неї обидва розділи просто не показуються. **На анкету це не впливає**: Apple
оцінює вміст, а не те, кому він показаний, тож відповіді вище лишаються тими самими. Що це дає на
рев'ю — рядок «Sexual Content: ні» тепер підкріплений не лише тлумаченням («особистий журнал, не
контент»), а й тим, що рев'юер із порожнім профілем цього рядка взагалі не побачить. Якщо він
спитає, чому, відповідь у цих двох реченнях.

Очікуваний підсумок за системою рейтингів 2025 — **13+**. Якщо після рішення по фертильному вікну
воно лишиться, будьте готові до 16+ і не сперечайтесь: рейтинг дешевший за апеляцію.

---

## 3. Текст у поле «Notes» App Store Connect

> Обидва рішення закриті 2026-08-29: HealthKit прибрано з v1, тож його абзац вилучено; ШІ
> лишається, тож абзац про мережу лишається теж. Текст нижче можна копіювати як є.

```
No account is required and there is no sign-in, so no demo credentials are needed.
All data is created by the reviewer on the device and stored locally.

App lock: the app may offer to set a PIN during onboarding. This is optional and can be
skipped. If a PIN is set, it is stored only as a hash on the device and cannot be recovered
— if you set one and lose it, please reinstall rather than contacting us.

Onboarding and in-app guides: after the first-run setup, a short guide highlights the
real controls on the home screen. The Dashboard and Settings screens each show their own
guide the first time you open them. To replay any of them without reinstalling, open
Settings > Screen > "Show the guides again": the Settings guide starts immediately, and
the home screen and the Dashboard show theirs the next time you open them.

Network and AI: the only network request the app makes is downloading model weight
files (~123 MB) from HuggingFace, and only after the user starts that download in
Settings > AI. No diary content, no text and no identifier ever leaves the device. All
model inference runs on the device. If the download is inconvenient to test, every other
feature works without it.

The app has no accounts, no servers, no sync, no analytics, no advertising and no
third-party SDKs that contact a network.

Cycle tracking is a personal log and forecast based on the dates the user enters. It is
not a medical device, gives no medical advice, and is not intended for contraception.
```

Останній абзац — не формальність. Він потрібен незалежно від того, який варіант із розділу 1 буде
обрано, бо це єдине місце, де намір можна заявити словами до того, як його прочитають з екрана.

Абзац про гіди доданий 2026-08-31, разом із етапом 3 онбордингу. Причина проста: кожен гід
показується один раз на екран і сам себе позначає баченим. Рев'юер, який відкрив головну до того, як
дійшов до цих нотаток, більше його не побачить — і без цього абзацу єдиним способом переглянути
онбординг удруге лишалося перевстановлення. Шлях названий дослівно тими рядками, що на екрані
(`SettingsTabScreen`, `TourReplayLabel`), щоб не довелось шукати за перекладом.

---

## 4. Що зроблено в цьому проході

- `Platforms/iOS/Info.plist` — тільки iPhone (`UIDeviceFamily` = 1) і тільки портрет; додано
  `ITSAppUsesNonExemptEncryption`.
- `Platforms/iOS/Entitlements.plist` — додано `com.apple.developer.healthkit`, прибрано мертвий
  `increased-memory-limit`.
- `Platforms/iOS/Resources/PrivacyInfo.xcprivacy` — увімкнено декларацію `UserDefaults` (CA92.1),
  додано явні `NSPrivacyTracking=false` і порожній `NSPrivacyCollectedDataTypes`.
- `Platforms/iOS/Resources/{en,uk}.lproj/InfoPlist.strings` — прибрано мертві рядки камери й
  фотогалереї, додано локалізований `NSHealthShareUsageDescription`.
- Прибрано `Resources/Images/dotnet_bot.png` і його рядок у `Diarion.csproj`.
- `store/privacy-policy.md` — чернетка UA + EN, зібрана з коду.

Після всіх правок: `dotnet build -f net10.0-ios -c Release -t:Compile` — 0 помилок, 0 попереджень;
`dotnet test` — 1401 пройшов, 8 пропущено, 0 впало.

---

## 5. Що лишилось, і чому саме це

**Впирається в рішення власника:**

1. ~~**Домен → `ApplicationId` → URL політики приватності.**~~ Домен `diarion.org` куплений
   2026-08-29, `ApplicationId` став `org.diarion.app` тим самим заходом — розділ 8. Лишився хвіст
   ланцюжка, і він **не в репозиторії**: підняти `/privacy`, `/en/privacy` і `/support`, завести
   пошту на домені, зареєструвати App ID у Developer Portal.
2. ~~**Фертильне вікно**~~ — вирішено 2026-08-24, розділ 1. Іконку вимкнено, підпис і розрахунок
   лишились, анкета вікового рейтингу (§2) від цього не змінюється: фертильне вікно все ще названо
   словами на екрані циклу, тож «Medical / Treatment-Focused Content» так і лишається «так».
3. ~~**AI у v1.**~~ Вирішено 2026-08-29: **лишається**. `OnDeviceAi.EmbeddingsOffered = true`, у
   першій подачі є вкладка AI, завантаження 123 МБ і абзац про мережу в §3, в описі й у політиці.
   Ціна відмови була б у темах статистики й факторі настрою, і власник її платити не став.
4. ~~**HealthKit.**~~ Вирішено 2026-08-29: **прибрано з v1** — розділ 8. Capability на App ID більше
   не потрібна, підпис від неї не залежить, і питання «які дані здоров'я ви читаєте» на рев'ю не
   виникає взагалі.
5. **Монетизація.** StoreKit немає взагалі. Роадмапа (рядок 7) фіксує одноразовий lifetime, а не
   підписку.

**Впирається в залізо, якого тут немає:**

6. **Release-IPA на реальному iPhone.** Блокер 0.3 — `MtouchLink=SdkOnly` + `UseInterpreter=true` —
   це обхід крешу без діагнозу. Збирається, але жодна збірка з поточним кодом не запускалась на
   пристрої. TestFlight internal, пройти всі екрани. Конвеєр для цього тепер є — розділ 7; лишилось
   заповнити секрети й натиснути кнопку. Заліза це не заміняє: результат треба відкрити на телефоні.
7. **Скриншоти.** 6.9" iPhone. iPad більше не потрібен — застосунок оголошений як iPhone-only.

**Дешеве, але не в цьому проході:**

8. ~~**iOS у CI.**~~ Зроблено 2026-08-18 — розділ 6.
9. **Заголовок, підзаголовок, ключові слова, опис** — чернетка обома мовами написана 2026-08-18,
   [`app-store-listing.md`](app-store-listing.md). Лишається вибір власника й публікація.

---

## 6. iOS у CI (закриває пункт 8)

Два джоби в `.github/workflows/ci.yml`, обидва на кожен пуш.

**`ios-typecheck` — `dotnet build Diarion.csproj -f net10.0-ios -c Release -t:Compile`.**
До нього все під `Platforms/iOS/` не компілювалось у CI взагалі: джоб `tests` збирає `Diarion.Core`
і тести (звичайний `net10.0`), джоб `android-permissions` — андроїдну голову. `HealthDataService`,
біометрика й делегат нотифікацій перевірялись рівно одним механізмом — тим, що про них не забули.

Раннер — **`windows-latest`, не macOS**. `-t:Compile` — це Roslyn і генератор XAML проти
референсних збірок iOS; Xcode не викликається, Mac і pairing не потрібні. GitHub рахує хвилини
macOS ×10, Windows ×2, тобто той самий результат на `macos-26` коштував би вп'ятеро дорожче.
Ubuntu не підходить з іншої причини: `Diarion.csproj:5` додає `net10.0-ios` у `TargetFrameworks`
лише коли ОС не Linux, тож `-f net10.0-ios` там падає з «project does not target that framework».

`Release`, не `Debug`: саме там живуть `MtouchLink=SdkOnly` і `UseInterpreter=true`, а символ
умовної компіляції, який існує лише в одній конфігурації, — рівно те, що цей джоб має ловити.
`build-ios.yml` це не заміняє: підписаний IPA й далі вимагає Mac, бо `actool`, лінкер і AOT — Xcode.

**`ios-submission-metadata` — чотири файли мають узгоджуватись, і жоден не перевіряє компілятор.**
`Info.plist`, `Entitlements.plist`, `PrivacyInfo.xcprivacy` і два `InfoPlist.strings`. Кожне
правило — те, що вже одного разу зламалось саме тут (розділ 4), і кожен збій тихий:

| Перевірка | Як воно ламається без неї |
|---|---|
| `ITSAppUsesNonExemptEncryption` присутній | Кожен аплоуд стає на анкеті export compliance |
| `NSHealthShareUsageDescription` ⇄ `com.apple.developer.healthkit` | Authorisation повертається unsuccessful мовчки — той самий баг, що жив тут місяцями |
| Ключі `*UsageDescription` збігаються в Info.plist і в обох `.lproj` | Українцю показують англійський запит дозволу |
| `Preferences` у коді ⇒ `NSPrivacyAccessedAPICategoryUserDefaults` | Лист ITMS-91053 через три години після аплоуду |
| `UIDeviceFamily` = тільки iPhone | iPad-збірка без iPad-скриншотів — подача не приймається |
| `ApplicationId` не `com.companyname.*` | Bundle id незмінний після першої подачі |

Останній — попередження на гілках і **помилка на `main`**: пункт 1 цього розділу відкритий, і
червоний CI на кожен пуш у фічу привчав би на нього не дивитись, а от змерджити його в `main`
тепер не вийде.

Перевірка робиться на `python3` + `plistlib`, без збірки (~15 с). Кожне з семи правил прогнано
на мутованій копії файлів — усі сім ловлять, чистий стан лишається зеленим.

---

## 7. Підписана збірка й аплоуд (частково закриває пункт 6)

`build-ios.yml` до 2026-08-24 вмів рівно одне: зібрати **непідписаний** `.app`, запакувати його
zip'ом у теку `Payload` і віддати артефактом. Для перевірки «чи воно збирається» цього досить — і
саме тому воно так і лишалось, — але в App Store Connect такий файл не потрапить у принципі:
підпису немає, а IPA, склеєний уручну, відкидається навіть підписаним.

Тепер у воркфлоу два режими, які обираються при запуску:

| `mode` | Що робить | Потребує секретів |
|---|---|---|
| `unsigned` (типово) | Те, що було. Ловить `actool`, лінкер і AOT — усе, чого `ios-typecheck` у `ci.yml` не запускає | ні |
| `appstore` | `Apple Distribution` + `ArchiveOnBuild=true`, далі `altool --validate-app` і `--upload-app` | так, вісім |

`ArchiveOnBuild=true` — те, чого бракувало: `.ipa` робить сам таргет публікації, а не `zip`.

**Вісім секретів репозиторію.** Усі вісім готуються один раз, у кроці 2
[`submission-runbook.md`](submission-runbook.md) — там же порядок, у якому цей воркфлоу запускається
відносно решти подачі:

| Секрет | Звідки |
|---|---|
| `IOS_P12_BASE64` | Сертифікат Apple Distribution у `.p12`, `base64 -w0` |
| `IOS_P12_PASSWORD` | Пароль, заданий при експорті `.p12` |
| `IOS_PROVISIONING_PROFILE_BASE64` | App Store provisioning profile, `base64 -w0` |
| `IOS_CODESIGN_IDENTITY` | Рядок цілком: `Apple Distribution: Ім'я Прізвище (TEAMID)` |
| `IOS_PROVISIONING_PROFILE_NAME` | Ім'я профілю так, як воно в Developer Portal |
| `APPSTORE_API_KEY_ID` | App Store Connect API Key, роль App Manager |
| `APPSTORE_API_ISSUER_ID` | Звідти ж |
| `APPSTORE_API_KEY_BASE64` | Файл `.p8` цього ключа, `base64 -w0` |

Сертифікат і ключ можна зробити з Windows: CSR генерується `openssl req`, `.cer` з порталу
конвертується `openssl x509` + `openssl pkcs12 -export`. Mac потрібен тільки для самої збірки, і
його дає раннер.

**Три речі в цьому воркфлоу зроблені навмисно, і кожну легко «спростити» назад:**

- **Секрети перевіряються до збірки, а не на аплоуді.** Хвилина `macos-26` коштує ×10; дізнатись про
  порожній секрет на останньому кроці — це заплатити за прогін двічі.
- **Bundle id перевіряється тут окремо**, хоча `ci.yml` уже валить `com.companyname.*` на `main`.
  Залити збірку можна й з гілки, тобто це остання межа перед незворотним: після першого аплоуду
  ідентифікатор не змінює ні App Store Connect, ні підтримка.
- **`--validate-app` окремим викликом перед `--upload-app`.** Ловить те саме, але за секунди й **не
  витрачає номер збірки**: той самий `ApplicationVersion` вдруге не приймається, тож після невдалого
  аплоуду доводиться піднімати його в коді й комітити.

Окремий keychain у `$RUNNER_TEMP`, а не `login`: типовий залочений, і `codesign` на ньому спиняється
на діалозі, якого нікому натиснути. Видаляється в `if: always()` — раннер і так одноразовий, але
крок, що виконується лише при успіху, лишає матеріал підпису на впалій збірці.

---

## 8. Bundle id і HealthKit (2026-08-29, закриває пункти 1 і 4 §5)

Власник купив `diarion.org`, і це розблокувало ланцюжок, який стояв із 2026-08-01.

### `ApplicationId` = `org.diarion.app`

Реверсний DNS від домену, яким власник володіє. `.app`, а не `.diarion`, з однієї причини на
майбутнє: віджет чи share-extension мусять бути **сусідами** ідентифікатора застосунку, а не його
дітьми, тож `org.diarion.*` лишається вільним під них.

Значення живе рівно в одному місці (`Diarion.csproj`) — правка однорядкова, важким тут було саме
рішення. Воно незворотне: Apple не міняє ідентифікатор після першої відправленої збірки, Play
випалює його назавжди навіть для видаленого застосунку. Правило в `ci.yml`, яке валило `main` на
`com.companyname.*`, тепер зелене й лишається на місці — воно ще знадобиться, якщо шаблон колись
повернеться копіпастом.

### HealthKit прибрано з v1

Рішення далося легше, ніж очікувалось, бо перевірка показала те, чого не побачив прохід 0.10:
**кнопки не було**. `MainViewModel.ImportSleepDataAsync` — єдиний споживач `IHealthDataService`, і
його не прив'язує жоден XAML. Entitlement, рядок призначення й дві локалізовані копії існували для
функції, яку користувач не міг викликати ніяк. Плата за них була цілком реальна: capability на App
ID руками (інакше падає підпис, і CI цього не ловить, бо публікує з `CodesignKey="-"`) плюс
питання рев'ю «які дані здоров'я ви читаєте і навіщо».

**Прибрати самі ключі було б помилкою, і це найважливіше в цій правці.** Apple сканує бінарник на
звернення до API: збірка, яка й далі лінкує `HKHealthStore` без `NSHealthShareUsageDescription`,
відпадає на аплоуді з ITMS-90683 — тобто прибирання метаданих без прибирання коду проміняло б
робочу функцію на невдале завантаження. Тому гілки `#if IOS` у `HealthDataService` стали
`#if IOS && HEALTHKIT`, а символ не визначений ніде: рядок, який його визначає, лежить
закоментований у `Diarion.csproj`. На iOS ці гілки тепер компілюються у ті самі «unsupported», що
й андроїдні, і HealthKit у бінарнику немає взагалі.

Нічого не видалено — той самий уклад, що в `OnDeviceAi.GenerationOffered` і
`CycleDisplay.FertileWindowMarkerOffered`.

**Повернення — один коміт із чотирьох частин, і три з них тримає CI.** Розкоментувати
`DefineConstants`, повернути entitlement і рядок призначення (обидва виписані дослівно там, звідки
прибрані), увімкнути capability на App ID. У `ios-submission-metadata` додано два правила, які
роблять будь-які три з чотирьох без четвертої червоними:

| Стан | Що каже CI |
|---|---|
| `HEALTHKIT` визначено, метаданих немає | ITMS-90683 на аплоуді, а не зауваження на рев'ю |
| Метадані є, `HEALTHKIT` не визначено | Рев'ю питає, що читає дані; чесна відповідь — ніщо |

Четверту частину — capability на App ID — не перевіряє ніщо і не перевірить: вона поза
репозиторієм.

Заодно обидва сканери (`.strings` і `csproj`) тепер зрізають коментарі перед пошуком ключів. Без
цього ключ, виписаний у коментарі для того, хто його повертатиме, читався б як живий — і правило
«переклад є, а в Info.plist ключа немає» падало б саме на записці, залишеній йому на допомогу.

Після правок: iOS Release type-check 0 помилок / 0 попереджень; `dotnet test` — 1430 пройшли, 8
пропущено, 0 впало; сім правил `ios-submission-metadata` зелені, обидва нові прогнані на мутованих
копіях і ловлять.

---

## 9. Перша відповідь рев'ю (2026-09-22): Guideline 2.1 — Information Needed

Не відмова по суті, а стандартний запит до кожного нового акаунта розробника: відео з реального
пристрою плюс шість питань. Відповідь нижче надіслана в тред App Store Connect разом із записом
екрана, а скорочена версія (ліміт поля 4000 знаків) додана в Notes; обидва тексти лежать у
`D:\WORK\apple-signing\` (`app-review-reply.txt`, `asc-notes-full.txt`). Факти звірені з кодом
збірки (2): єдиний HTTP-клієнт — `HttpModelFileTransfer`, маршрут `SnapshotPreview` зареєстрований,
але з інтерфейсу недосяжний, тож хмарних звітів у збірці немає і в п. 4 їх не названо.

**Результат.** Відповідь із відео надіслана 2026-09-22 о 8:16, о 19:24 того ж дня версія перейшла в
In Review і пробула там майже два тижні без нових питань; 2026-09-25 надіслано запит статусу через
форму App Review на developer.apple.com/contact. Схвалено без змін у збірці, у магазині з 2026-10-06:
<https://apps.apple.com/ua/app/diarion/id6814251806>.

**Сценарій запису** (iPhone 13 Pro Max, iOS 26, свіже встановлення з TestFlight, 2–3 хвилини):
запуск → онбординг, три кроки → головна: настрій, сон, здоров'я, харчування, звички → тап по даті,
календар → «Планувальник», додати задачу → швидке меню: Нотатки, Звички, Фінанси, Читання →
Статистика → Налаштування: Профіль (дата народження, після неї з'являється картка циклу), Дані
(замок, резервна копія), ШІ (завантаження показане, не запущене). Без акаунтів, без покупок — це
й сказано в п. 1 відповіді.

```
Thank you for the review. Answers to the six points, in order. The same text is now in the Notes field for future submissions.

1. SCREEN RECORDING
Attached to this message. Recorded on a physical iPhone 13 Pro Max running the current iOS. It starts with launching the app on a fresh install (onboarding, which can be skipped) and walks through the typical flow: the daily entry on the home screen (mood, sleep, health, food, habits), the quick-menu screens (notes, planner, habit tracker, finances, reading), the statistics dashboard, and Settings (profile, data/backup, app lock, the optional AI download). There is no account registration, login or account deletion because the app has no accounts. There is no user-generated content shared with anyone: everything a user writes is visible only to that user on that device, so there is no reporting or blocking mechanism to show. There is no paid content: the app is free with no in-app purchases.

2. PURPOSE AND TARGET AUDIENCE
Diarion is a private personal diary and life tracker: one place for a daily entry (text, mood, sleep, how you feel, food), habits, tasks with reminders, notes, finances, reading, happy moments, good deeds, a wishlist and an optional cycle calendar. The audience is anyone 13+ who wants to keep such records for themselves. The problem it solves: most diary and tracker apps require an account and sync personal notes to a server. Diarion has no account, no server and no analytics; everything stays in an encrypted database on the phone, with a passphrase-encrypted backup and JSON/CSV/Markdown export so the data is the user's to keep or move.

3. SETUP AND ACCESSING THE MAIN FEATURES
No login, no credentials and no sample files are needed. On first launch a three-step onboarding appears (welcome, choose which modules to show, optional daily reminder); every step can be skipped. An app lock (PIN, Face ID) is optional and lives in Settings > Data; if a PIN is set it is stored only as a hash and cannot be recovered, so please do not set one you might forget (reinstall clears it).
- Home screen: today's entry. Tap the date at the top to open the calendar and switch days. Blocks: mood, sleep, health, food, habits, guided prompt, reflection. Which blocks appear is chosen in onboarding and in Settings > Screen.
- Quick menu (icons at the top of the home screen): Notes, Reading, Happy Moments, Good Deeds, Habits, Wishlist, Finance.
- "Planner" tab next to "Notes" on the home screen: the day's tasks with reminders.
- Bottom tabs: Statistics (a month at a glance, correlations) and Settings.
- Two rows are shown by age: the "Intimate life" row of the health block appears only for a profile with a date of birth of 18+, and the cycle calendar (Settings > Profile) only from 14. To see them, set a date of birth in Settings > Profile. Without a date, neither is shown; this is by design.
- Settings > AI offers to download an on-device language model (~123 MB). This is optional; no feature you need to test depends on it, and every other feature works with it left off.
- Guides: after onboarding, a short guide highlights the home-screen controls; Statistics and Settings each show their own guide once. To replay them: Settings > Screen > "Show the guides again".

4. EXTERNAL SERVICES, TOOLS AND PLATFORMS
None are used to deliver the core functionality. There is no authentication service, no payment processor, no data provider, no analytics, no advertising and no third-party SDK that contacts a network. The only network request the app can make is downloading model weight files from huggingface.co, and only after the user starts that download in Settings > AI. The model then runs entirely on the device; no diary content, no text and no identifier ever leaves the phone. Platform features used: local notifications (reminders), Face ID / Touch ID through the system API for the optional app lock, and the system share sheet for backups and exports.

5. REGIONAL DIFFERENCES
None. The app functions identically in all regions. The interface is available in Ukrainian and English and follows the device language; currency for the finance section is chosen by the user.

6. REGULATED INDUSTRY / PROTECTED MATERIAL
Not applicable. The app is not a medical device and makes no medical claims: the cycle calendar is a personal log and estimate based on dates the user enters, and the screen carries a notice that it is not medical advice and not a method of contraception. The app contains no protected third-party material; all content is created by the user, and the fonts and icons are licensed or our own.
```

---

## 10. Версія 1.1 — підписка (спека 15, R6.4)

Чинні Notes (повна відповідь з §9 у `D:\WORK\apple-signing\asc-notes-full.txt`) двічі кажуть
неправду про 1.1: «the app is free with no in-app purchases» (п. 1) і «no payment processor» (п. 4).
Їх треба виправити, а блок про підписку поставити **першим**, бо головне питання рев'ю цієї версії —
саме підписка, і найімовірніше заперечення — «де обіцяне безкоштовно для користувачів 1.0»: у
пісочниці правило від Apple не спрацьовує (R2.2), і рев'юер бачить пейвол.

**Блок на початок Notes** (≈1700 знаків; ліміт поля 4000, тож решту повного тексту доведеться
скоротити — п. 3 найдовший):

```
WHAT IS NEW IN 1.1: AN AUTO-RENEWABLE SUBSCRIPTION
Writing now requires the "Diarion Base" auto-renewable subscription: monthly (diarion.base.monthly) or yearly (diarion.base.yearly), each with a 2-week free introductory offer. Purchases use StoreKit 2 directly; there is no third-party billing SDK and no server.
- The paywall appears once, right after onboarding. It shows the price per period, the free-trial terms, the auto-renewal notice, Restore Purchases, and links to the Terms of Use (https://diarion.org/en/terms) and the Privacy Policy. "Later" closes it.
- Without a subscription the app is read-only, not locked: every entry can be read, and backup, export, import, the app lock, theme and language keep working. A banner at the top of the home screen, and any attempt to write, open the paywall again. Settings > Data > Subscription shows the state and offers Subscribe, Manage Subscription and Restore Purchases.
- Users who installed version 1.0 keep everything 1.0 had, free, for ever. The app recognises them when the device already had a 1.0 diary at the first launch of 1.1, or when AppTransaction.originalAppVersion is 2 or lower (1.0 shipped as build 2) in the Production environment. In the sandbox originalAppVersion is always "1.0", so a reviewer on a fresh install sees the regular new-user path with the paywall. This is expected.
```

**Правки в повному тексті:**

- п. 1, речення `There is no paid content: the app is free with no in-app purchases.` замінити на
  `Paid content: the Diarion Base subscription described at the top of these notes; the purchase flow is shown at the end of the recording.`
  — і перезаписати відео: після онбордингу пейвол, «Пізніше», банер, закритий запис, Налаштування →
  Дані → Підписка. Купівлю показувати не обов'язково, але пейвол — так.
- п. 4, `There is no authentication service, no payment processor, …` замінити на
  `There is no authentication service and no payment processor other than Apple's own In-App Purchase (StoreKit), …`.
- Наприкінці п. 3 додати рядок: `To see the read-only mode without waiting for a subscription to lapse, tap "Later" on the paywall.`

**Відповідь на очікуване питання «where is the free access for existing users?»** — коротко, якщо
спитають після подачі:

```
Existing-user access is decided on the device and cannot be shown in the sandbox: the app grants it when AppTransaction.originalAppVersion is 2 or lower in the Production environment, or when a 1.0 diary was already on the device at the first launch of 1.1. In the sandbox and TestFlight originalAppVersion is always "1.0", so every review install is treated as a new user. The rule is covered by unit tests (LegacyAccessDetectorTests) for production, sandbox and malformed values.
```
