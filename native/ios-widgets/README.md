# DiarionWidgets: розширення WidgetKit

Три віджети домашнього екрана й три грані екрана блокування (спека 20, W1–W3). Розширення — тонкий
презентер: читає `widget-snapshot.json` з App Group `group.org.diarion.app`, який пише застосунок
(`Platforms/iOS/IosWidgetSnapshotPublisher.cs`), і нічого не рахує саме. Усі підписи приходять у
зліпку вже локалізованими (D4); власні рядки розширення — лише назви й описи для галереї.

## Структура

| Шлях | Що це |
|---|---|
| `project.yml` | специфікація XcodeGen; `.xcodeproj`, `Info.plist` і `.entitlements` генеруються на CI й не комітяться |
| `DiarionWidgetsShared/` | Swift-пакет без WidgetKit: модель зліпка (дзеркало `WidgetSnapshot.cs`), читач App Group, правило «чи це сьогодні», зразки для галереї; тести `swift test` на macOS-хості |
| `DiarionWidgets/` | таргет розширення: `DiarionWidgetsBundle`, `SnapshotProvider`, `Views/` (три віджети, тема, спільні частини), `Resources/Localizable.xcstrings`, `PrivacyInfo.xcprivacy` |
| `scripts/build-appex.sh` | збирає `.appex` у `build/Release-<sdk>`, звідки його бере `AdditionalAppExtensions` у `Diarion.csproj` |

## Як це збирається

1. `xcodegen generate` → `DiarionWidgets.xcodeproj` (робить сам скрипт, якщо проєкту немає).
2. `scripts/build-appex.sh iphoneos` без підпису або з параметрами ручного підпису (сертифікат
   розповсюдження + профіль `Diarion Widgets App Store`).
3. `dotnet publish Diarion.csproj -f net10.0-ios -p:DiarionWidgets=true …` вбудовує `.appex` у
   `Diarion.app/PlugIns/` і підписує застосунок з `Platforms/iOS/Entitlements.Widgets.plist` (App Groups).

Без `-p:DiarionWidgets=true` застосунок збирається як раніше: без розширення, зі звичним
`Entitlements.plist`, і публікатор на iOS не знаходить контейнера й лишається вимкненим.

`swift-widgets.yml` ганяє тести пакета й непідписану збірку на кожен пуш у цю теку; `build-ios.yml`
збирає підписане розширення перед публікацією, коли ввід `widgets` увімкнено.

## Контракт даних

`widget-snapshot.json` описаний у спеці 20, R1; Swift-модель у
`DiarionWidgetsShared/Sources/DiarionWidgetsShared/WidgetSnapshot.swift`. Зміни лише додаванням;
інший `schemaVersion` дає грань «Відкрити щоденник», а не збій. Зліпок з іншої дати теж малюється
як ця грань: цифри вчорашнього дня не видаються за сьогоднішні.

## Що не відтворюється на CI

Справжній хостинг WidgetKit (tinted, StandBy, vibrant на екрані блокування, бюджет оновлень)
перевіряється лише на телефоні через TestFlight.
