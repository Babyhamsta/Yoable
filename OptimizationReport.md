# Optimization Report

Date: 2026-01-20

Scope: Scanned the YoableWPF project (all .cs/.xaml, managers, models, dialogs, and settings) for correctness risks, performance bottlenecks, and cleanup/consolidation opportunities. No code changes were made.

## High-priority correctness/stability findings

- Model mappings can be lost on async saves: `ExportProjectDataAsync` does not include `LoadedModelPaths` or `ModelClassMappings`, while the sync export path clears/sets these fields. Any save that goes through `SaveProjectAsync` can drop model config. Consider aligning async export with sync export. `YoableWPF/Managers/ProjectManager.cs:563` and `YoableWPF/Managers/ProjectManager.cs:645`
- Auto-save setting appears ignored: `EnableAutoSave` only affects UI, but `MarkDirty` always triggers a debounced save and `StartAutoSave` ignores the toggle. If the user disables auto-save, saves still occur. Gate both the debounce save and the timer on `EnableAutoSave`. `YoableWPF/Managers/ProjectManager.cs:427` and `YoableWPF/Managers/ProjectManager.cs:474` and `YoableWPF/MainWindow.xaml.cs:529`
- Frame extraction can hang on invalid FPS: `frameInterval` can become 0 when `fps` is 0/NaN or rounding yields 0, causing an infinite loop. Add a guard for `fps <= 0` and ensure `frameInterval >= 1`. `YoableWPF/Managers/YoutubeDownloader.cs:108`
- Undo stack trimming reverses order: `SaveUndoState` trims the stack by `ToArray()` then re-pushes in forward order, which inverts the undo history after trimming. Preserve order when trimming to keep correct undo/redo behavior. `YoableWPF/UI/DrawingCanvas.cs:86`
- `LoadImages` is `async void`: this is a public method used outside event handlers, so exceptions are unobserved and callers can't await completion. Convert to `Task` and propagate errors. `YoableWPF/MainWindow.xaml.cs:1375`
- Save concurrency not synchronized: `isSaving` is a plain bool accessed from multiple timer/event threads. Two saves can still race. Use `SemaphoreSlim`/`Interlocked` or a single-flight queue. `YoableWPF/Managers/ProjectManager.cs:278`

## Performance opportunities

- Project save re-opens every image on each save: `ExportProjectDataAsync` calls `ExportLabelsToYolo`, which loads each image from disk. You already have cached dimensions (`ImageManager.ImageInfo`) and a batch export that avoids disk reads. Consider switching saves to `ExportLabelsBatchAsync` or using cached dimensions. `YoableWPF/Managers/ProjectManager.cs:563` and `YoableWPF/Managers/LabelManager.cs:332`
- Project load does per-image `Task.Run` without batching: `ImportProjectDataAsync` loops through images and spins a task per file but still runs sequentially; it also sets `BatchSize` but doesn't use batch APIs. Consider using `ImageManager.LoadImagesFromDirectoryAsync` or a controlled parallel loop. `YoableWPF/Managers/ProjectManager.cs:760`
- Nested tasking in image loading: `LoadImagesFromDirectoryAsync` wraps the whole method in `Task.Run` and also `Task.Run`s per image. This creates many small tasks and adds overhead. Consider `Parallel.ForEachAsync` or a bounded `Channel` + worker pool. `YoableWPF/Managers/ImageManager.cs:40`
- Double-parallelization in label loading: `LoadYoloLabelsBatchAsync` runs inside `Task.Run` and uses PLINQ, which can oversubscribe cores. Prefer either `Task.Run` + sequential or pure PLINQ, not both. `YoableWPF/Managers/LabelManager.cs:139`
- Import loads labels via temp directory copy: copying all label files to a temp folder doubles disk I/O and storage, then re-reads. Consider loading from original paths (including external ones) without copying. `YoableWPF/Managers/ProjectManager.cs:802`
- UI brush allocation hotspots: `UpdateStatusCounts` and `UpdateFilterButtonStyles` create new `SolidColorBrush`/`BrushConverter` instances on every update. Cache brushes in resources or static fields to reduce allocations. `YoableWPF/Managers/UIStateManager.cs:38`
- `ParseFloat` allocates new `CultureInfo` per call; this runs per token per label. Cache `CultureInfo` instances statically. `YoableWPF/Managers/LabelManager.cs:63`

## Cleanup / consolidation opportunities

- Duplicate language reload logic in `MainWindow` and `SettingsWindow`. Consider a shared helper or base class to avoid diverging behavior. `YoableWPF/MainWindow.xaml.cs:72` and `YoableWPF/SettingsWindow.xaml.cs:23`
- `UIStateManager` reaches into `MainWindow` with reflection for `projectClasses`. Expose a read-only property or pass the list in. `YoableWPF/Managers/UIStateManager.cs:60`
- Unused field: `saveCancellationToken` is declared but never set/used. Remove or implement actual cancellation for saves. `YoableWPF/Managers/ProjectManager.cs:35`
- Settings-driven batch sizes are duplicated (`ProcessingBatchSize`, `LabelLoadBatchSize`, `UIBatchSize`) but used inconsistently; consider consolidating or documenting their intended scopes to avoid confusion. `YoableWPF/Managers/ProjectManager.cs:760` and `YoableWPF/Managers/ImageManager.cs:18` and `YoableWPF/MainWindow.xaml.cs:1390`

## Notes / potential follow-ups

- Several methods swallow exceptions silently (e.g., label import/export). For diagnosability, consider optional logging/telemetry in debug builds.
- `UpdateManager` uses a hard-coded app version string in `MainWindow`. Consider reading `AssemblyInformationalVersion` to keep update logic aligned with build output. `YoableWPF/MainWindow.xaml.cs:46`

If you want, I can turn any subset of these into concrete fixes or provide a prioritized implementation plan.

## GitHub Update Comments
- Fixed async saves dropping model paths/mappings and tightened save concurrency
- Respected auto‑save toggle for both debounce saves and timed saves
- Avoided re‑opening images during label export by using cached dimensions
- Simplified project import (batch image load, direct label file load, no temp copies)
- Reduced UI churn by caching brushes and removing reflection for class lookups
- Added shared language reload helper and cleaned up unused save cancellation field
- Hardened YouTube frame extraction against invalid FPS metadata
- Made update checks more resilient to missing assets and pre‑release version tags
- Centralized app version in the project file and surfaced it from assembly metadata
- Replaced a silent UI text update failure with debug logging for easier diagnosis
- Bound the splash screen version text to the same shared version source
- Migrated user settings on version bumps to keep recent projects and preferences
- Fixed settings upgrade call to avoid `Application.Properties` name collision
