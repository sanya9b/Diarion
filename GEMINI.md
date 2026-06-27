# Diarion Project Guidelines

This file contains the foundational mandates, conventions, and architectural rules for the Diarion project. These rules take absolute precedence during all operations.

## 📌 Project Context
- **Project**: Diarion - a cross-platform (iOS/Android) smart diary with local storage, task management, and emotion tracking.
- **Tech Stack**: C# (.NET 10+), .NET MAUI, Strict MVVM (CommunityToolkit.Mvvm), LiteDB (NoSQL, local only).

## 🏛 Architectural & Coding Principles
1. **Clean Architecture**: Domain/Core logic must be completely separated from UI (Presentation) and DB (Infrastructure). Use interfaces for LiteDB. No UI references (`Microsoft.Maui.Controls`, `Page`, etc.) inside Models, Services, or ViewModels.
2. **Dependency Injection (DI)**: Register ALL services, ViewModels, and pages via `IServiceCollection` in `MauiProgram.cs`. Do not instantiate dependencies with `new` keyword.
3. **Strict MVVM**: Logic goes into ViewModels. Code-behind (`*.xaml.cs`) should only contain UI-specific setup (e.g., `BindingContext = viewModel;`).
4. **Encoding (CRITICAL)**: ALL generated/modified text files MUST be saved in **UTF-8 without BOM** for correct Ukrainian language display.

## 🌍 Localization (Bilingual Ukr/Eng)
- **NO HARDCODED TEXT**: All user-visible strings in `*.xaml` and `*ViewModel.cs` MUST be retrieved from resources.
- **Resource Files**: 
  - English (default): `Resources/Localization/AppResources.resx`
  - Ukrainian: `Resources/Localization/AppResources.uk.resx`
- **XAML Usage**: Use `Text="{x:Static localization:AppResources.MyKey}"`.
- **C# Usage**: Use `AppResources.MyKey`.

## 🎨 UI/UX & Theming
- **Monochromatic Style**: UI elements must be strictly monochromatic based on the base palette. Do not use colorful alerts (e.g., red for delete).
- **Themes**: Must support Light and Dark modes via `.resx` or `ResourceDictionary`.
- **Color Palette**:
  - `Midnight`: #22282C
  - `Ocean`: #929FA7
  - `Earth`: #E9E7E1
  - `Dust`: #E0E6EA
  - `Snow`: #FFFFFF
- **Interactive Scales**: Use the custom `RatingView` component for 1-10 rating scales. Data type should be `int`. Inner elements of custom controls must have `InputTransparent="True"` to ensure hitbox reliability.

## 🧪 Testing
- **Frameworks**: xUnit, Moq, FluentAssertions.
- **LiteDB**: ALWAYS use in-memory mode (`new LiteDatabase(new MemoryStream())`) for unit testing services to avoid locks. Ensure proper disposal.
- **ViewModels**: Mock dependencies (like `IDiaryService`) using Moq. Verify ViewModel state changes (e.g., `IsBusy`) and command execution.
- **Naming**: `[ClassName]Tests.cs` and `[MethodName]_[Scenario]_[ExpectedBehavior]`.

## 🛡️ Performance and Memory Safety
- **No `async void`**: All async methods must return `Task` (except for event handlers).
- **Memory Leaks**: Ensure event handlers (`+=`) are unsubscribed (`-=`). Unregister message subscriptions. Wrap `IDisposable` resources in `using` blocks.
- **Bindings**: Ensure compiled bindings (`x:DataType`) are used in XAML to prevent performance issues.
