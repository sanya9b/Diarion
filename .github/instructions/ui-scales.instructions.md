---
description: "Rules for interactive 10-unit rating scales in the application."
applyTo: ["**/*.xaml", "**/*.cs"]
---

# UI Scales & Ratings Guidelines

1. **Standardization:** Всі поля, що представляють оцінку стану, настрою або якості (наприклад, "Якість сну", "Стан здоров'я", "Енергія"), повинні бути стандартизовані до єдиного стилю.
2. **Visual Style:** Використовуємо кастомний компонент `RatingView` (10 одиниць / зірочок / позначок) замість текстових полів (`Entry`).
3. **Data Type:** Відповідні властивості в моделях (напр. `DiaryEntry`) мають зберігатись як `int` (від 1 до 10), а не як `string`.
4. **Hitbox Reliability:** Кожна одиниця шкали (зірочка) має бути клікабельною. Внутрішні елементи (як-от текст чи іконка зірочки) повинні мати `InputTransparent="True"`, щоб не перехоплювати жести на себе.

Використання:
```xml
<controls:RatingView Value="{Binding SleepQuality}" MaxValue="10" />
```
