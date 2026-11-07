# План рефакторингу Diarion

## Пріоритет 1: Розділення обов'язків та усунення God Object (Domain / Core Logic)
- [x] 1.1 Створити `IMenstrualCycleService` та `MenstrualCycleService`. Перенести логіку розрахунку циклу з `MainViewModel`. Написати Unit-тести.
- [x] 1.2 Створити `ICalendarService` та `CalendarService`. Перенести логіку генерації `CalendarDay` з `MainViewModel`. Написати Unit-тести.
- [x] 1.3 Додати метод `GetTodosForDateRangeAsync` у `ITodoService` та оновити `MainViewModel.UpdateCalendarTasksCompletion`, щоб уникнути N+1 запитів.
- [x] 1.4 Розбити `FinanceViewModel` (1314 рядків, єдиний God Object серед view model). Зроблено 2026-10-27 у `feature/v1.2`: батько лишив собі завантаження, стрічку й баланси; рахунки, перекази, планові, бюджети й форма транзакції — окремі view model у `ViewModels/Finance/`, зі спільним знімком даних `FinanceLedger` і колбеком перезавантаження. Конструктор, DI і поведінка без змін; у XAML лише префікси секцій (`Accounts.`, `Transfers.`, `Planned.`, `Budgets.`, `TransactionForm.`). Єдина свідома зміна: правило планової транзакції бере рахунок із фільтра сторінки, а не з пікера форми транзакції, яку користувач міг і не відкривати.

## Пріоритет 2: Очищення інфраструктурного шару (Database)
- [x] 2.1 Створити `IDatabaseSeeder` та `DatabaseSeeder` і винести генерацію mock-даних та ініціалізацію звичок з `DatabaseContext.cs`.
- [x] 2.2 Видалити хардкод зі старішими звичками ("Breakfast", "Сніданок") з `DatabaseContext.cs`.

## Пріоритет 3: Виправлення архітектурних меж у сервісах
- [x] 3.1 У `DiaryService.cs` змінити метод `DeleteEntryAsync`: додати метод `DeleteTodosByDiaryEntryAsync` у `ITodoService` та використовувати його замість прямого виклику `DeleteMany`.
- [x] 3.2 Рефакторинг `TodoService.GetTodosForDateAsync`: виділити логіку міграцій у приватні методи `AutoMigratePastTasks` та `GenerateRepeatingTasks`.

## Пріоритет 4: Очищення UI та Presentation шару
- [x] 4.1 Перенести конфігурацію швидкого меню з `MainViewModel` в `IMenuConfigurationService` або окрему конфігурацію.
- [x] 4.2 Створити Behavior для Drag&Drop в `MainPage.xaml.cs` для очищення code-behind від магічних чисел та логіки анімацій.
- [x] 4.3 Винести логічні блоки UI (Food, Habits, Reflection, Calendar, Menu) в окремі Custom Controls.

## Пріоритет 5: Unit Тестування
- [x] 5.1 Написати Unit-тести для `DiaryDetailViewModel`.
- [x] 5.2 Написати Unit-тести для `TodoDetailViewModel`.
- [x] 5.3 Перевірити покриття всіх нових сервісів та зберегти зелений білд (0 failed tests).