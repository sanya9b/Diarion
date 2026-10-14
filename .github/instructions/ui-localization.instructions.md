---
description: "Enforces UI localization for all text. Must be applied when editing XAML or ViewModels."
applyTo: ["**/*.xaml", "**/*ViewModel.cs", "**/Views/**/*.cs"]
---

# UI Localization Rules (Двомовність)

Цей проєкт підтримує дві мови (Укр/Англ). **ЗАБОРОНЕНО** використовувати хардкод тексту (hardcoded strings) у будь-яких файлах, що відповідають за UI.

## Основні правила

1. **NO HARDCODED TEXT**: Усі видимі користувачеві рядки у `*.xaml` та `*ViewModel.cs` ПОВИННІ братися з ресурсів.
2. **Resource Files**: 
   - Англійська (за замовчуванням): `Resources/Localization/AppResources.resx`
   - Українська: `Resources/Localization/AppResources.uk.resx`
3. **Encoding (КРИТИЧНО)**: При додаванні нових рядків до `.resx` файлів через термінал або PowerShell, зберігай їх **ВИНЯТКОВО у форматі UTF-8 без BOM**. Інакше кирилиця перетвориться на кракозябри.
   
## Використання в XAML
Завжди використовуй розширення `x:Static` для прив'язки тексту:
```xml
<!-- Неправильно: -->
<Label Text="Мої записи" />

<!-- Правильно: -->
<Label Text="{x:Static localization:AppResources.MyEntriesTitle}" />
```
*Не забудь підключити namespace у XAML: `xmlns:localization="clr-namespace:Diarion.Resources.Localization"`*

## Використання в C# (ViewModels)
Звертайся до згенерованого класу `AppResources` (його namespace: `Diarion.Resources.Localization.AppResources`):
```csharp
// Неправильно:
await Shell.Current.DisplayAlert("Помилка", "Немає інтернету", "ОК");

// Правильно:
await Shell.Current.DisplayAlert(AppResources.AlertError, AppResources.NoInternetError, "OK");
```

## Якщо потрібен новий текст:
1. Додай запис у `AppResources.resx` (англійський варіант).
2. Додай запис у `AppResources.uk.resx` (український варіант).
3. Використай згенеровану властивість у коді.

## Голос (spec 16, D1 — затверджено 2026-10-14)

1. **Регістр.** Застосунок говорить із людиною на **«ви»**: інтерфейс, підказки, помилки, онбординг, гіди,
   пейвол, юридичні тексти. **«Ти»** лишається лише в питаннях щоденника — керовані промпти
   (`PromptCbt*`, `PromptGratitude*`, `PromptOpen*`, `PromptSavour*`) і питання рефлексії — бо це слова
   людини до себе. Кнопки й заголовки — інфінітив або іменник, без звертання. Охороняє `AppResourcesVoiceTests`.
2. **Порожній стан** = що зробити + що тут з'явиться, одне-два речення («Трекерів ще немає. Створіть
   перший, і тут з'являться відмічені дні.»). Не «нічого немає» без наступного кроку.
3. **Валідація** — одне речення без «Будь ласка» і без знаку оклику («Вкажіть назву книги.»).
