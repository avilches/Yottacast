@../../../docs/user-settings.md
@../../../docs/user-settings-browser.md
@../../../docs/user-settings-terminal.md

## Tests

Al modificar esta area, actualizar los tests en `Yottacast.Core.Tests/Services/`:
- `UserSettingsTests.cs` — persistencia, migraciones y auto-reparacion de settings
- `BrowserTerminalDiscoveryTests.cs` — descubrimiento de navegadores y terminales instalados
- `ProcessRunnerTests.cs` — ejecucion de procesos y escaping de comandos
