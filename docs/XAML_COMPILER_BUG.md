# WinUI 3 XAML Compiler Bug Report: Silent Crash on Type Mismatch

## Summary

The WinUI 3 XAML compiler (`XamlCompiler.exe`) crashes with **exit code 1 and produces no error message** when the XAML root element type doesn't match the code-behind base class. This creates a frustrating debugging experience as developers receive no indication of what's wrong.

## Reproduction

**Minimal repro:** `D:\github\XamlCompilerCrashRepro.zip` (6 files, ~2KB)

### Steps
1. Create a WinUI 3 project with WinUIEx package
2. Create `MainWindow.xaml` using `<Window>` as root:
   ```xml
   <Window x:Class="CrashRepro.MainWindow" ...>
   ```
3. Create `MainWindow.xaml.cs` inheriting from `WindowEx`:
   ```csharp
   public sealed partial class MainWindow : WindowEx { ... }
   ```
4. Run `dotnet build`

### Expected
Clear error message like: *"Type mismatch: XAML root element 'Window' doesn't match code-behind base class 'WinUIEx.WindowEx'"*

### Actual
```
error MSB3073: The command "XamlCompiler.exe input.json output.json" exited with code 1.
```
No `output.json` file is created. No additional error details.

## Environment

| Component | Version |
|-----------|---------|
| Windows App SDK | 1.6.250602001 (also 1.8.x) |
| WinUIEx | 2.5.0+ |
| .NET | 9.0 |
| OS | Windows 11 (ARM64 and x64) |

## Workaround

Ensure XAML and code-behind types match:

**Option A:** Both use `Window`
```xml
<Window x:Class="...">
```
```csharp
public partial class MainWindow : Window
```

**Option B:** Both use `WindowEx`
```xml
<winex:WindowEx x:Class="..." xmlns:winex="using:WinUIEx">
```
```csharp
public partial class MainWindow : WindowEx
```

## Impact

- **Severity:** Medium-High (blocks development, wastes debugging time)
- **Discoverability:** Very poor (no error message)
- **Affected scenarios:** Any derived Window type (WindowEx, custom base classes)

## Related Issues

This may be related to existing XAML compiler issues around error reporting:
- microsoft/microsoft-ui-xaml#10027
- microsoft/microsoft-ui-xaml#9813

## Suggested Fix

The XAML compiler should:
1. Detect when the partial class base type differs from the XAML root element
2. Produce a clear error message with file/line information
3. Write error details to `output.json` even on failure
