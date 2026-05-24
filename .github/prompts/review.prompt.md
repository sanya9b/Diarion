---
description: "Quickly review C# / MAUI code for Clean Architecture violations and potential memory leaks."
---

# Code Review: Clean Architecture & Memory Leaks

Please review the provided code (or the active file/selection) with a strict focus on the following two critical areas for the Diarion project.

## 1. Clean Architecture Violations
- **UI in Domain/Logic**: Are there any references to UI components (e.g., `Microsoft.Maui.Controls`, `Page`, `View`, `Shell`) inside Models, Services, or ViewModels? (ViewModels should only use standard data types or interfaces).
- **Hardcoded Dependencies**: Are dependencies being instantiated directly using the `new` keyword instead of being injected via the constructor?
- **Business Logic in Code-Behind**: Does the `*.xaml.cs` file contain business logic or direct database calls? It should only contain UI-specific setup (e.g., `BindingContext = viewModel;`).
- **Database Leaks**: Do UI components or ViewModels interact directly with `LiteDB` classes instead of going through the `IDiaryService` interface?

## 2. Memory Leaks in .NET MAUI
- **Event Handlers**: Are event handlers (`+=`) properly unsubscribed (`-=`) in the `Unloaded` event or `Dispose` method to prevent strong reference cycles?
- **MessagingCenter / WeakReferenceMessenger**: Are message subscriptions properly unregistered when the View/ViewModel is no longer needed?
- **Bindings**: Are there any potentially leaky bindings, or missing `x:DataType` (compiled bindings) in XAML which can cause performance/memory issues?
- **Async void**: Are there any `async void` methods (except for event handlers)? All async methods should return `Task`.
- **Disposables**: Are objects that implement `IDisposable` (especially related to streams, database connections, or unmanaged resources) properly wrapped in `using` blocks or manually disposed?

### Output Format
Provide a concise, bulleted report.
- **🔴 Critical Violations**: (Things that break Clean Architecture or cause definite memory leaks).
- **🟡 Warnings/Smells**: (Things that are not strictly bugs but violate best practices).
- **✅ Good**: (Briefly mention if the code perfectly adheres to the principles).

If suggesting fixes, provide minimal code snippets demonstrating the correct approach.
