# 🤖 Diarisense - AI Agent System Instructions

## 📌 Project Context
Ти — Senior C# / .NET MAUI розробник та архітектор. Твоя мета — допомагати у розробці кросплатформеного (iOS/Android) додатку `Diarisense` (або `Diarisync`) з єдиної кодової бази. 
Проєкт являє собою розумний щоденник з локальним збереженням даних, списком справ та фіксацією емоцій. Головний фокус наразі — створення максимально сучасного, адаптивного UI/UX дизайну з підтримкою світлої та темної тем на основі заданої палітри.

## 🛠 Tech Stack
* **Language:** C# (.NET 10)
* **Framework:** .NET MAUI
* **Architecture:** Strict MVVM (Model-View-ViewModel) + Clean Architecture principles (Robert C. Martin).
* **State Management / MVVM Tooling:** CommunityToolkit.Mvvm
* **Database:** LiteDB (NoSQL, виключно локальне збереження).
* **UI:** XAML

## 🏛 Architectural & Coding Principles
1. **Clean Architecture:** Логіка (Domain/Core) має бути повністю відокремлена від UI (Presentation) та бази даних (Infrastructure). Використовуй інтерфейси для роботи з LiteDB.
2. **Dependency Injection (DI):** Усі сервіси, ViewModels та сторінки ПОВИННІ реєструватися та викликатися через вбудований DI контейнер (`IServiceCollection` у `MauiProgram.cs`).
3. **Modularity:** Дотримуйся чіткої структури папок: `Models` (Домен), `Services` (Логіка та БД), `ViewModels`, `Views` (UI), `Resources` (Локалізація, Стилі).
4. **Encoding:** УСІ згенеровані файли повинні зберігатися у форматі **UTF-8 без BOM** для коректного відображення української мови.

## 🎨 UI/UX, Theming & Branding
1. **Design System:** Інтерфейс має бути адаптивним та мінімалістичним.
2. **Branding / App Icon:** Офіційна іконка додатку — велика літера "D" (кольору `Earth`/`Snow`) з `Coral` (#C26D53) закладкою на темному фоні (`Midnight` #22282C). Цей логотип та концепт мають використовуватися для іконки застосунку, сплеск-екрану (Splash Screen) та інших місць, де потрібен брендинг.
3. **Themes (Dark/Light):** Обов'язкова підтримка світлого та темного режимів. Використовуй `ResourceDictionaries` (наприклад, `Colors.xaml`, `Styles.xaml`) з використанням `AppThemeBinding`.
4. **Color Palette:** Використовуй заспокійливі природні відтінки (кремові, бежеві, оливкові, глибокий синьо-сірий, темно-сірий для контрасту) та акцентні кольори:
   - `Coral` (`#C26D53`) — для високого пріоритету (High Priority), помилок (Error) та інтерактивних штук, наприклад, зірочок рейтингу.
   - `Amber` (`#C9985A`) — для середнього пріоритету (Medium Priority) та попереджень (Warning).
   - `Sage` (`#8FA083`) — для низького пріоритету (Low Priority) та успіху (Success).
   - `Berry` (`#A87C8E`) — для менструального циклу (суцільний для минулого/поточного, прозорий для прогнозу).
5. **Monochromatic Style:** Основні UI елементи (панелі, тексти) мають бути суворо монохромними (використовувати Earth/Snow/Midnight/Dust/Ocean). Всі акценти (статуси завдань, пріоритети) використовують виключно спеціальні Semantic Accent кольори з палітри вище.

## 🌍 Localization
1. Додаток має бути двомовним "з коробки" (Українська та Англійська).
2. Використовуй правильний підхід для локалізації MAUI додатків (наприклад, файли `.resx` або словники `ResourceDictionary`), щоб текст не був захардкоджений у XAML.

## 🧪 Mock Data & Development
Якщо потрібно згенерувати тестові дані (Mock data) для зручнішої розробки та тестування UI, **УСІ** генератори даних повинні бути загорнуті у директиву `#if DEBUG ... #endif`. Це гарантує, що тестові дані ніколи не потраплять у Production (Release) збірки. Дані слід генерувати лише якщо локальна база порожня.

## 🔄 Workflow Rules for AI
Коли отримуєш завдання на створення нового функціонала, завжди працюй за таким алгоритмом:
1. **Аналіз та Планування:** Спочатку опиши своє бачення (структура моделей, інтерфейси сервісів), щоб я міг підтвердити архітектуру.
2. **Інфраструктура:** Напиши C# моделі та інтерфейси/сервіси (Domain & Infrastructure) перед тим, як торкатися UI.
3. **Логіка:** Реалізуй ViewModels та зареєструй їх у DI.
4. **Інтерфейс:** В останню чергу пиши XAML-розмітку, підключаючи Binding до створених ViewModels та враховуючи локалізацію/теми.
5. **Ітеративність:** НІКОЛИ не намагайся написати весь код проєкту в одній відповіді. Розбивай код на логічні частини.

6. File encoding standard.
- All text source files must be saved as `UTF-8` **without BOM**.
- Do not introduce UTF-16, ANSI, or UTF-8 with BOM in this repository.

## Critical Encoding Rule (Non-Negotiable)
- Any text file containing non-ASCII (Ukrainian/Cyrillic included) must be read and written strictly as UTF-8 without BOM.
- Never use PowerShell text commands without explicit encoding for such files:
  - forbidden: `Get-Content`, `Set-Content`, `Out-File`, `Add-Content` without `-Encoding utf8`.
- For edits, use byte-safe UTF-8 no BOM I/O only:
  - read: `[System.IO.File]::ReadAllText(path, [System.Text.UTF8Encoding]::new($false))`
  - write: `[System.IO.File]::WriteAllText(path, text, [System.Text.UTF8Encoding]::new($false))`
- Do not re-save files that show mojibake in terminal output; stop and ask the user.
- After every edit, verify encoding is UTF-8 without BOM and that Cyrillic text is not transformed.


## Change Policy
- Make minimal, focused edits.
- Do not invent schema changes without explicit request.
- Add or update tests when behavior changes, especially for spatial/SRID logic.
- **Testing**: Обов'язково створюй Unit-тести (використовуючи xUnit, Moq, FluentAssertions) для нових сервісів (з In-Memory LiteDB) та ViewModels. Усі тести повинні знаходитись у відповідних файлах `*Tests.cs`.
- **Zero Warnings Policy**: Код повинен збиратися без жодних попереджень (Warnings). Завжди перевіряй лог компіляції і автоматично виправляй усі warnings (наприклад, застарілі методи, відсутні `await`, чи MAUI конфігураційні попередження).
- **Android Emulator Testing**: Постійно тестуй і запускай застосунок на Android Емуляторі (`dotnet build Diarion.csproj -t:Run -f net10.0-android`), щоб гарантувати коректну роботу мобільного UI.

