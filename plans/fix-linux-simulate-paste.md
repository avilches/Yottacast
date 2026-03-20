# Fix: LinuxAppHandler — implementar SimulatePasteAsync

## Problema

`SimulatePasteAsync()` es un método virtual en `AppHandler` con implementación base no-op
(`Task.CompletedTask`). Mac y Windows la sobreescriben con lógica real:

- **Mac** (`MacAppHandler:47`): CGEventCreateKeyboardEvent + CGEventPost — simula Cmd+V
- **Windows** (`WindowsAppHandler:13`): keybd_event — simula Ctrl+V

`LinuxAppHandler` no la sobreescribe, por lo que en Linux el feature `PasteAfterActivate`
de EmojiSearch copia al clipboard pero no pega — fallo silencioso desde el punto de vista
del usuario (parece que no pasa nada).

## Opciones

### Opción A — Implementar con xdotool (recomendada si Linux es soportado)

`xdotool` es la herramienta estándar en Linux para simular input. Requiere que esté
instalada en el sistema (`apt install xdotool` / `pacman -S xdotool`).

```csharp
public override async Task SimulatePasteAsync() {
    await Task.Delay(150);
    // Simula Ctrl+V usando xdotool
    await Process.Start(new ProcessStartInfo {
        FileName = "xdotool",
        Arguments = "key --clearmodifiers ctrl+v",
        UseShellExecute = false,
    })!.WaitForExitAsync();
}
```

**Pros**: funciona igual que Mac/Windows
**Contras**: dependencia externa no garantizada; si `xdotool` no está instalado, falla silenciosamente

Si se elige esta opción, añadir un check previo:
```csharp
// En OnFrameworkInitializationCompleted o Start:
// Verificar que xdotool existe; si no, loggear advertencia
```

### Opción B — Documentar como limitación conocida (recomendada si Linux es best-effort)

Si Linux no es una plataforma prioritaria, lo correcto es ser explícito:

1. Añadir un comentario en `LinuxAppHandler`:
```csharp
// SimulatePasteAsync not overridden: paste simulation is not supported on Linux.
// The emoji is copied to the clipboard but must be pasted manually (Ctrl+V).
// Requires xdotool or a similar tool; not implemented to avoid an optional system dependency.
```

2. Opcionalmente, hacer que `EmojiSearch` cambie el `Subtitle` en Linux:
```csharp
// En EmojiSearch.MakeResult:
Subtitle = AppHandler.Instance.SupportsPasteSimulation
    ? "Press Enter to copy and paste"
    : "Press Enter to copy",
```

Para ello habría que añadir `bool SupportsPasteSimulation` a `AppHandler`.

### Opción C — Hacer SimulatePasteAsync abstracto (compile-time enforcement)

Cambiar de `virtual` a `abstract` en `AppHandler` obliga a que todas las plataformas
implementen el método o el código no compila. La implementación Linux sería un no-op
explícito en lugar de heredado:

```csharp
// En AppHandler.cs:
public abstract Task SimulatePasteAsync();

// En LinuxAppHandler.cs:
public override Task SimulatePasteAsync() => Task.CompletedTask; // not supported
```

**Pros**: evita que futuras plataformas hereden el no-op sin darse cuenta
**Contras**: cambio más invasivo; requiere tocar AppHandler y los 3 handlers

## Recomendación

**Opción B + C combinadas**: hacer `SimulatePasteAsync` abstracto para enforcement
en compile-time, y documentar explícitamente en `LinuxAppHandler` que es una
limitación conocida. Añadir `SupportsPasteSimulation` a `AppHandler` para que
`EmojiSearch` pueda adaptar el subtitle.

## Archivos a modificar

- `Yottacast/Services/AppHandler.cs` — cambiar `virtual` a `abstract` en `SimulatePasteAsync`; añadir `virtual bool SupportsPasteSimulation`
- `Yottacast/Services/LinuxAppHandler.cs` — implementar no-op con comentario; sobreescribir `SupportsPasteSimulation` a `false`
- `Yottacast/Services/MacAppHandler.cs` — `SupportsPasteSimulation` hereda `true` (default)
- `Yottacast/Services/WindowsAppHandler.cs` — `SupportsPasteSimulation` hereda `true` (default)
- `Yottacast.Core/Search/EmojiSearch.cs` — adaptar `Subtitle` según `SupportsPasteSimulation`

## Criterio de aceptación

- En macOS y Windows: comportamiento idéntico al actual (copia + pega)
- En Linux: copia al clipboard; subtitle dice "Press Enter to copy"; no hay excepción
- Si se elige Opción A: en Linux con xdotool instalado, copia y pega correctamente
