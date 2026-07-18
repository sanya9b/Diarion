# 11 — Продуктивність: списки та статистика

- **Priority:** P1 · **Effort:** M · **Status:** Proposed · **Depends on:** — · **Blocks:** —

## Продукт

**Проблема.** Для щоденника, яким користуються місяцями/роками, кілька екранів деградують із ростом історії
(нема віртуалізації), streak рахується марнотратно на кожному save/load, а всі вікна статистики містять
off-by-one (показують `days+1` днів). Перше — про плавність/пам'ять, друге — про **коректність цифр**.

**Конкуренти.** Bearable/Daylio тримають плавність на роках даних (віртуалізовані списки, індексовані запити).
Некоректні періоди статистики підривають довіру до інсайтів.

**Цінність.** Плавність на довгій дистанції (утримання лояльних користувачів), коректні числа (довіра до
статистики й майбутніх кореляцій зі spec 06).

**User story.** *Як довготривалий користувач, я хочу, щоб екрани фінансів/вішліста лишалися швидкими через
роки, а «7 днів» у статистиці означало саме 7 днів.*

## Інженерія

### Проблема / докази
- **Off-by-one:** `Diarion.Core/Services/StatisticsService.cs:24` (`DateTime.Today.AddDays(-days)` з інклюзивним
  `Today`) → `days+1` днів; повторюється на `:85,133,140` (сон/настрій/todo/фінанси).
- **Немає віртуалізації:** Finance та Wishlist рендеряться `BindableLayout` у `ScrollView` (не віртуалізується)
  і живляться запитами, що вантажать усю колекцію без пагінації → всі рядки в візуальному дереві й пам'яті.
- **Streak марнотратний:** `Diarion.Core/Services/DiaryService.cs` (`GetCurrentStreakAsync`, ~69-120) гідратує
  **всі** записи щоденника (з повним текстом) лише щоб прочитати `Date`; викликається на кожному save і load.
- **Подвійний скан:** `TodoService.GetTodoStatsSummaryAsync` (~43-57) робить два окремих повних скани діапазону.
- Дрібне: заповнення прогалин сон-статистики O(days×points) через `FirstOrDefault`; подвійна енумерація
  chart-items; `MainPage` не відписується від VM `PropertyChanged`; повторне читання профілю на кожен день.

### Вимоги (Requirements)
- **R1.** Виправити off-by-one: `AddDays(-(days-1))` (або ексклюзивний кінець) в усіх вікнах статистики; єдиний
  хелпер діапазону дат, щоб не розсинхронізувати.
- **R2.** Замінити `BindableLayout`+`ScrollView` на `CollectionView` (віртуалізація) для Finance та Wishlist;
  додати пагінацію/інкрементне завантаження в `FinanceService`/`WishlistService` (напр. `Skip/Take` з індексом за датою).
- **R3.** Streak без гідратації: проектувати лише `Date` (LiteDB `Select`/`Query.Select` або збережений
  агрегат) і/або кешувати streak; не гідратувати повні документи на кожному save.
- **R4.** Об'єднати подвійний скан у `GetTodoStatsSummaryAsync` в один прохід (обидва лічильники за одну ітерацію).
- **R5.** Дрібні оптимізації: `Dictionary` за датою для gap-fill; один `ToList()` для chart-items; відписка
  `MainPage` від VM; кеш профілю замість `FindAll` на кожен день.

### Критерії приймання (Acceptance)
- **AC1.** «N днів» у всіх вікнах статистики охоплює рівно N календарних днів (unit-тест на межах).
- **AC2.** Finance/Wishlist використовують `CollectionView`; при 1000+ записів прокрутка плавна, пам'ять не
  зростає лінійно з повним матеріалізуванням (перевірка профайлером/спостереженням).
- **AC3.** `GetCurrentStreakAsync` не десеріалізує повні документи (перевірено проєкцією/бенчем); час save не
  зростає помітно з к-стю записів.
- **AC4.** `GetTodoStatsSummaryAsync` робить один прохід; результат ідентичний попередньому.
- **AC5.** Наявні тести статистики зелені; додано тести на межі діапазону та streak-проєкцію.

### Зачеплені файли
- `Diarion.Core/Services/StatisticsService.cs` (`:24,85,133,140`, gap-fill `:60-73`)
- `Diarion.Core/Services/DiaryService.cs` (`GetCurrentStreakAsync`)
- `Diarion.Core/Services/TodoService.cs` (`GetTodoStatsSummaryAsync :43-57`, профіль `:77`)
- `Views/FinancePage.xaml`, `Views/WishlistPage.xaml`,
  `Diarion.Core/Services/FinanceService.cs`, `WishlistService.cs`,
  `Diarion.Core/ViewModels/FinanceViewModel.cs`, `WishlistViewModel.cs`
- `Controls/CategoryDonutChart.cs`, `EmotionDonutChart.cs`, `SleepBarChart.cs`
- `Views/MainPage.xaml.cs` (`:22,58-62`)
- Тести: `Diarion.Tests/StatisticsServiceTests.cs` (+ нові кейси)

### Тест-план
- Unit: межі діапазону дат (N=1,7,30); один прохід todo-stats; streak на проєкції.
- Bench/manual: Finance/Wishlist із 1000+ згенерованих записів (#if DEBUG сідер) — плавність і пам'ять.

### Ризики / застереження
- Виправлення off-by-one змінить наявні числа статистики — очікувано; оновити скріншоти/очікування тестів.
- LiteDB-проєкція: перевірити підтримку `Select` для потрібних полів; за потреби кешувати streak окремо.
