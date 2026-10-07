# .NET Conf companion

Use the SDK/workload versions pinned in global.json. This is a native MAUI app targeting iOS and Android; desktop and optional showcase integrations are deferred.

Keep conference/search/schedule logic in DotNetConf.Core and presentation in DotNetConf.App. Preserve typed compiled XAML bindings, native navigation, semantic light/dark tokens, font scaling, and visible error states. All app-owned UI icons use IconFont.Maui.FluentIcons typed glyphs. All bundled agenda data is fictional and must remain visibly labeled.

For live inspection, restore the repo-local tool and use `dotnet tool run maui -- devflow`. Inspect `devflow list` first and select the intended agent port. Read the UI tree before interactions; use stable AutomationIds and a single `devflow batch` for related mutations to avoid lease conflicts. The DevFlow package, registration, and any development-only settings must remain Debug-only. Never inspect or change another app's data.

Run the core tests and relevant platform build after changes. Keep generated screenshots/logs outside the repository. See README.md for build, inspection, and data-contract instructions.
