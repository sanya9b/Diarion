---
description: "Motion tokens and animation rules for all UI code (XAML, code-behind, controls, behaviors)."
applyTo: ["**/*.xaml", "**/*.xaml.cs", "Controls/**/*.cs", "Behaviors/**/*.cs"]
---

# UI Motion Rules (Рух та анімації)

Правила взято зі скілів `.claude/skills/animate` і `find-animation-opportunities` (Emil Kowalski)
та `motion-design` (LottieFiles). Вони говорять мовою CSS; тут їхній словник переведено в MAUI.
Характер руху Diarion — «Premium» за класифікацією motion-design: спокійний, без overshoot і bounce.

## 1. Токени — єдине джерело тривалостей і кривих

Клас `Diarion.Resources.Styles.Motion`. Жодних «магічних» мілісекунд у коді чи XAML.

| Токен | мс | Для чого |
|---|---|---|
| `Motion.PressMs` | 120 | натискання: вниз і назад |
| `Motion.FastMs` | 150 | тогли, чипи, крос-фейд контенту, який перемикають десятки разів на день |
| `Motion.StandardMs` | 240 | розгортання секцій, поява контенту, усе розміром з дропдаун |
| `Motion.OverlayMs` | 320 | шторки, оверлеї, діалоги. Стеля для UI; довше лише онбординг і святкування |

Криві: `Motion.EaseOut` (вхід, вихід, типова), `Motion.EaseInOut` (рух, що лишається на екрані),
`Motion.Sheet` (шторки знизу). У CSS це cubic-bezier(0.23, 1, 0.32, 1), (0.77, 0, 0.175, 1) і
(0.32, 0.72, 0, 1). Нові криві додавати в `Diarion.Helpers.CubicBezier`, а не вигадувати на місці.

**ЗАБОРОНЕНО:** `Easing.CubicIn`, `Easing.SinIn` і будь-який ease-in на елементах UI (повільний старт
відкладає саме той момент, на який дивиться користувач); `Easing.BounceOut`/`SpringOut` поза онбордингом.

## 2. Ворота: спочатку вирішити, чи анімувати взагалі

| Як часто користувач це бачить | Рішення |
|---|---|
| 100+ разів на день: навігація між вкладками, дії з клавіатури | без анімації |
| десятки разів: натискання, перемикачі, вибір у списку | лише непомітна: до 150 мс, scale не менше 0.97 |
| зрідка: шторки, діалоги, розгортання секцій | стандартна |
| рідко або вперше: онбординг, порожні стани, успіх, стрік | тут живе бюджет на delight |

Призначення треба назвати одним словом: фідбек, просторова послідовність, індикація стану,
уникнення стрибка, пояснення, delight. «Гарно виглядає» призначенням не є. Дані, які користувач
читає (графіки, суми, календар), не рухаються заради стилю.

## 3. Pressed-фідбек для не-кнопок

`behaviors:PressFeedback.IsEnabled="True"` на будь-якому `VisualElement` або як Setter у Style.
Це `TouchBehavior` з токенами: scale 0.97, opacity 0.9, 120 мс. `Button` має нативний фідбек, йому
не потрібно. Не ставити на елементи з Drag/Drop-жестами і на контейнери, чиї діти мають лишатися
інтерактивними: TouchBehavior робить дітей `InputTransparent`.

## 4. Властивості та форма руху

- Анімувати лише `Opacity`, `Scale`, `TranslationX/Y`, `Rotation`. Не `HeightRequest`, `WidthRequest`,
  `Margin`, `Padding`. Єдиний виняток — акордеон через `HeightRequestTo`, бо трансформ-еквівалента немає.
- Поява від `Scale` 0.95–0.97 і `Opacity` 0, ніколи від `Scale` 0.
- Вихід тим самим шляхом, що вхід, і швидше за вхід.
- Список, що з'являється цілком: стагер 30–80 мс між елементами, не блокує взаємодію.
- Переходи між сторінками лишаються платформними, їх не перевизначаємо.

## 5. Reduced motion

`Motion.ReduceMotion` читає системне налаштування через `IMotionSettings`. Для зсувів і масштабу
використовуй `Motion.Offset(dx)` і `Motion.Scale(0.96)`: у цьому режимі рух зникає, прозорість
лишається. Анімації не вимикати повністю: менше й м'якше, а не нуль.

## 6. Шаблон появи в code-behind

```csharp
view.Opacity = 0;
view.Scale = Motion.Scale(0.96);
view.IsVisible = true;
await Task.WhenAll(
    view.FadeToAsync(1, Motion.StandardMs, Motion.EaseOut),
    view.ScaleToAsync(1, Motion.StandardMs, Motion.EaseOut));
```
