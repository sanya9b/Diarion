# App Store — нотатки для рев'ю, віковий рейтинг, залишок по подачі

Складено 2026-08-17 з коду. Маркетингового тексту тут немає свідомо: заголовок, підзаголовок і опис
залежать від пунктів 1.1 і 1.2 роадмапи («продавати одну роботу»), які ще не вирішені, а вигадувати
позиціювання за власника — гірше, ніж лишити порожнє місце.

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

Це **не** зроблено в цьому проході — прибирати відвантажену фічу може тільки власник. Варіанти, від
найдешевшого:

1. **Прибрати соску, лишити підпис.** Іконка — найсильніший сигнал наміру й найслабша функціональна
   частина; текст «(оцінка)» вже чесний. Один рядок XAML.
2. **Прибрати і соску, і підпис, лишити розрахунок мертвим** — так, як уже зроблено з
   `GenerationOffered`. Фіча не видаляється, просто не пропонується.
3. **Лишити як є і подаватись.** Тоді 0.9 треба переписати: обмеження на слова в описі не рятує,
   коли те саме сказано картинкою, і подача фізособою (0.8) стає ще ризикованішою.

Хай яким буде вибір — його треба зафіксувати в роадмапі поруч із 0.9, бо зараз 0.9 стверджує те, що
код спростовує.

---

## 2. Віковий рейтинг — чим відповідати на анкету

Apple питає про зміст, а не про наміри, тож відповіді нижче навмисно незручні. Розбіжність між
анкетою і тим, що рев'юер бачить на екрані, — окрема підстава для відмови, дорожча за вищий рейтинг.

| Питання анкети | Відповідь | Підстава в коді |
|---|---|---|
| Medical / Treatment-Focused Content | **Так, Infrequent/Mild** | Трекер циклу з симптомами, сон із Health, фертильне вікно. Це інформація про здоров'я, а не лікування — але «ні» тут неправда |
| Alcohol, Tobacco, or Drug Use or References | **Так, Infrequent/Mild** | Трекер кинутих звичок; підказка в полі прямо каже «Наприклад: куріння або безкінечний скролінг» (`AppResources.uk.resx:380`). Контекст — відмова від звички, тобто мінімальний ступінь |
| Sexual Content or Nudity | Ні | Репродуктивне здоров'я — це медична категорія, не сексуальний контент |
| Violence / Horror / Profanity / Gambling / Contests | Ні | Немає |
| User-Generated Content | Ні | Записи бачить лише автор; немає ні обміну, ні стрічки, ні профілів, тож модерація за 1.2 не застосовна |
| Unrestricted Web Access | Ні | Вебв'ю немає; єдине мережеве звернення — завантаження файлів моделей |
| In-App Purchases | Ні (для v1) | StoreKit у проєкті відсутній |

Очікуваний підсумок за системою рейтингів 2025 — **13+**. Якщо після рішення по фертильному вікну
воно лишиться, будьте готові до 16+ і не сперечайтесь: рейтинг дешевший за апеляцію.

---

## 3. Текст у поле «Notes» App Store Connect

> Скопіювати як є після того, як будуть закриті рішення по HealthKit і по AI — обидва абзаци
> позначені `[?]` описують те, що ввімкнене сьогодні, і мають зникнути, якщо фічу вимкнуть.

```
No account is required and there is no sign-in, so no demo credentials are needed.
All data is created by the reviewer on the device and stored locally.

App lock: the app may offer to set a PIN during onboarding. This is optional and can be
skipped. If a PIN is set, it is stored only as a hash on the device and cannot be recovered
— if you set one and lose it, please reinstall rather than contacting us.

[?] Health: on iPhone the app asks for read-only access to Sleep Analysis in order to
prefill the sleep fields of a diary entry. It never writes to HealthKit — the share set
passed to the authorisation request is empty. Denying the permission blocks no feature.

[?] Network and AI: the only network request the app makes is downloading model weight
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

1. **Домен → `ApplicationId` → URL політики приватності.** `Diarion.csproj:37` досі
   `com.companyname.diarion`. Після першої відправки збірки не змінюється ніколи. Один ланцюжок, бо
   домен потрібен і для ідентифікатора, і для хостингу політики.
2. **Фертильне вікно** — розділ 1 цього файлу.
3. **AI у v1.** `OnDeviceAi.EmbeddingsOffered = true`, тобто у першій подачі є вкладка AI,
   завантаження 123 МБ і мережеві дозволи. Якщо AI справді після релізу — один рядок прибирає з
   подачі цілий пласт питань рев'ю; ціна — зникають теми у статистиці й фактор настрою.
4. **HealthKit.** Entitlement додано, але capability на App ID вмикається вручну в Developer
   Portal, інакше підпис падає. Альтернатива — прибрати Health з v1 (див. коментар у
   `Entitlements.plist`).
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

**Вісім секретів репозиторію.** Усі вісім готуються один раз, у кроці 2 інструкції з подачі:

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
