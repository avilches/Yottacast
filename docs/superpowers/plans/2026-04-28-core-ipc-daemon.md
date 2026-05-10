# Core IPC Daemon Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Crear `Yottacast.Ipc`, un proceso headless que expone `Yottacast.Core` vía gRPC sobre Unix domain socket, sin modificar el proyecto Avalonia existente.

**Architecture:** Nuevo proyecto `Yottacast.Ipc` (console app, `Sdk.Web`) que referencia `Yottacast.Core`. Kestrel escucha en Unix socket (`~/.cache/yottacast/core.sock`). Cuatro servicios gRPC: SearchService (búsqueda + acciones), SettingsService (lectura/escritura reactiva), IconService (iconos on-demand), LifecycleService (estado de arranque + shutdown). Un `SearchGrpcService` singleton mantiene el registry de resultados de la última búsqueda para poder ejecutar los delegates de acción.

**Tech Stack:** .NET 9, `Grpc.AspNetCore` 2.67.0, `Grpc.Tools` 2.67.0, `Google.Protobuf` 3.29.3, xUnit 2.9.2. Tests de mappers en `Yottacast.Ipc.Tests`. Verificación de integración con `grpcurl`.

---

## Mapa de ficheros

| Fichero | Responsabilidad |
|---------|----------------|
| `Yottacast.Ipc/Yottacast.Ipc.csproj` | Proyecto console Sdk.Web, dependencias gRPC |
| `Yottacast.Ipc.Tests/Yottacast.Ipc.Tests.csproj` | Tests xUnit de mappers |
| `Yottacast.Ipc/Proto/search.proto` | Contrato de búsqueda, resultados, acciones |
| `Yottacast.Ipc/Proto/settings.proto` | Contrato de settings completo |
| `Yottacast.Ipc/Proto/icons.proto` | Contrato de iconos on-demand |
| `Yottacast.Ipc/Proto/lifecycle.proto` | Contrato de estado de arranque y shutdown |
| `Yottacast.Core/AppPaths.cs` | +2 rutas: IpcSocket, IpcPidFile |
| `Yottacast.Ipc/Mapping/ResultMapper.cs` | `BaseResultItemViewModel` → `ResultMessage` |
| `Yottacast.Ipc/Mapping/SettingsMapper.cs` | `UserSettings` ↔ `SettingsMessage` |
| `Yottacast.Ipc/Services/LifecycleGrpcService.cs` | Estado STARTING/INSTANT_READY/FULLY_READY |
| `Yottacast.Ipc/Services/SearchGrpcService.cs` | Registry + SearchInstant + SearchDeferred + Activate + Navigate |
| `Yottacast.Ipc/Services/SettingsGrpcService.cs` | GetSettings + UpdateSettings + WatchSettings |
| `Yottacast.Ipc/Services/IconGrpcService.cs` | GetIcon + WatchIconsLoaded |
| `Yottacast.Ipc/Program.cs` | Host Kestrel, DI wiring, PID file, graceful shutdown |

---

## Task 1: Scaffold del proyecto

**Files:**
- Create: `Yottacast.Ipc/Yottacast.Ipc.csproj`
- Create: `Yottacast.Ipc.Tests/Yottacast.Ipc.Tests.csproj`
- Modify: `Yottacast.sln` (añadir los dos proyectos)

- [ ] **Step 1: Crear estructura de directorios y csproj del daemon**

```bash
mkdir -p "Yottacast.Ipc/Proto" "Yottacast.Ipc/Services" "Yottacast.Ipc/Mapping"
```

Crear `Yottacast.Ipc/Yottacast.Ipc.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>yottacast-core</AssemblyName>
    <RootNamespace>Yottacast.Ipc</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <Protobuf Include="Proto/**/*.proto" GrpcServices="Server" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Grpc.AspNetCore" Version="2.67.0" />
    <PackageReference Include="Grpc.Tools" Version="2.67.0" PrivateAssets="All" />
    <PackageReference Include="Google.Protobuf" Version="3.29.3" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Yottacast.Core\Yottacast.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Crear csproj del proyecto de tests**

```bash
mkdir -p "Yottacast.Ipc.Tests"
```

Crear `Yottacast.Ipc.Tests/Yottacast.Ipc.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Yottacast.Ipc.Tests</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <Protobuf Include="..\Yottacast.Ipc\Proto\**\*.proto"
              GrpcServices="None"
              Link="Proto/%(RecursiveDir)%(Filename)%(Extension)" />
    <PackageReference Include="Google.Protobuf" Version="3.29.3" />
    <PackageReference Include="Grpc.Tools" Version="2.67.0" PrivateAssets="All" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Yottacast.Ipc\Yottacast.Ipc.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Añadir ambos proyectos a la solución**

```bash
cd "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast"
dotnet sln add Yottacast.Ipc/Yottacast.Ipc.csproj
dotnet sln add Yottacast.Ipc.Tests/Yottacast.Ipc.Tests.csproj
```

- [ ] **Step 4: Crear Program.cs mínimo para que compile**

Crear `Yottacast.Ipc/Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGrpc();
var app = builder.Build();
app.Run();
```

- [ ] **Step 5: Verificar que compila**

```bash
cd "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast"
dotnet build Yottacast.Ipc/Yottacast.Ipc.csproj
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add Yottacast.Ipc/ Yottacast.Ipc.Tests/ Yottacast.sln
git commit -m "feat: scaffold Yottacast.Ipc gRPC daemon project"
```

---

## Task 2: AppPaths — rutas IPC

**Files:**
- Modify: `Yottacast.Core/AppPaths.cs`

- [ ] **Step 1: Añadir IpcSocket e IpcPidFile a AppPaths**

Añadir al final de la clase, antes del cierre `}` de `AppPaths`:

```csharp
    // ── IPC (gRPC daemon) ────────────────────────────────────────────────────

    /// <summary>Unix domain socket for IPC between the gRPC daemon and Swift UI.</summary>
    public static readonly string IpcSocket = Path.Combine(CacheDir, "core.sock");

    /// <summary>PID file to prevent multiple daemon instances.</summary>
    public static readonly string IpcPidFile = Path.Combine(CacheDir, "core.pid");
```

- [ ] **Step 2: Verificar que los tests existentes siguen pasando**

```bash
cd "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast/Yottacast.Core.Tests"
dotnet test
```

Expected: todos los tests pasan.

- [ ] **Step 3: Commit**

```bash
git add Yottacast.Core/AppPaths.cs
git commit -m "feat: add IpcSocket and IpcPidFile to AppPaths"
```

---

## Task 3: Proto files

**Files:**
- Create: `Yottacast.Ipc/Proto/search.proto`
- Create: `Yottacast.Ipc/Proto/settings.proto`
- Create: `Yottacast.Ipc/Proto/icons.proto`
- Create: `Yottacast.Ipc/Proto/lifecycle.proto`

- [ ] **Step 1: Crear search.proto**

Crear `Yottacast.Ipc/Proto/search.proto`:

```protobuf
syntax = "proto3";
option csharp_namespace = "Yottacast.Ipc.Proto";
package yottacast;

service SearchService {
  rpc SearchInstant(SearchRequest) returns (SearchResponse);
  rpc SearchDeferred(SearchRequest) returns (stream SearchResponse);
  rpc Activate(ActivateRequest) returns (ActivateResponse);
  rpc Navigate(NavigateRequest) returns (NavigateResponse);
}

message SearchRequest {
  string query = 1;
  int32 limit = 2;
}

message SearchResponse {
  repeated ResultMessage results = 1;
  string hint = 2;
  bool is_searching = 3;
}

message ResultMessage {
  string id = 1;
  // "app" | "calc" | "emoji_grid" | "web" | "file" | "dict" | "conversion"
  string type = 2;
  string title = 3;
  string subtitle = 4;
  string category = 5;
  // Icon key for IconService.GetIcon — empty for types that render icon as text (calc, web emoji)
  string icon_id = 6;
  double score = 7;
  bool bypass_limit = 8;
  bool paste_after_activate = 9;

  // Only for type="emoji_grid"
  repeated EmojiCellMessage emoji_cells = 10;
  int32 selected_emoji_index = 11;

  // Only for type="conversion"
  ConversionMessage conversion = 12;

  // Only for type="dict"
  repeated DictionaryDefinitionMessage definitions = 13;
}

message EmojiCellMessage {
  string char = 1;
  string name = 2;
  string category = 3;
  repeated string keywords = 4;
  int32 section = 5;  // 0=Favorite 1=MostUsed 2=Default
  int32 usage_count = 6;
  bool is_favorite = 7;
  bool is_placeholder = 8;
}

message ConversionMessage {
  string from_short = 1;
  string from_long = 2;
  string to_short = 3;
  string to_long = 4;
  string norm_from_short = 5;
  string norm_from_long = 6;
  bool from_was_normalized = 7;
  int32 selected_cell = 8;  // 0=To 1=NormFrom 2=OrigFrom
  bool rates_are_stale = 9;
}

message DictionaryDefinitionMessage {
  string part_of_speech = 1;
  string definition = 2;
  string example = 3;
  string example_translation = 4;
}

enum ActionType {
  DEFAULT = 0;
  COPY = 1;
  FAVORITE = 2;
}

message ActivateRequest {
  string result_id = 1;
  ActionType action = 2;
  int32 emoji_index = 3;  // only for type="emoji_grid"
}

message ActivateResponse {
  bool paste_after_activate = 1;
  string clipboard_text = 2;
}

enum Direction {
  LEFT = 0;
  RIGHT = 1;
  UP = 2;
  DOWN = 3;
}

message NavigateRequest {
  string result_id = 1;
  Direction direction = 2;
  int32 current_index = 3;
}

message NavigateResponse {
  bool consumed = 1;
  int32 new_index = 2;
}
```

- [ ] **Step 2: Crear settings.proto**

Crear `Yottacast.Ipc/Proto/settings.proto`:

```protobuf
syntax = "proto3";
option csharp_namespace = "Yottacast.Ipc.Proto";
package yottacast;

import "google/protobuf/empty.proto";
import "google/protobuf/wrappers.proto";

service SettingsService {
  rpc GetSettings(google.protobuf.Empty) returns (SettingsMessage);
  rpc UpdateSettings(UpdateSettingsRequest) returns (google.protobuf.Empty);
  rpc WatchSettings(google.protobuf.Empty) returns (stream SettingsMessage);
}

message SettingsMessage {
  string browser = 1;
  string terminal = 2;
  string theme = 3;
  string hotkey = 4;
  repeated string search_folders = 5;
  repeated string app_directories = 6;
  bool enable_app_search = 7;
  bool enable_calculator = 8;
  bool enable_clipboard = 9;
  bool enable_emoji = 10;
  bool enable_file_search = 11;
  bool enable_web_search = 12;
  bool show_disabled_web_search_engines = 13;
  bool file_search_only_specific_folders = 14;
  repeated WebSearchEngineMessage web_search_engines = 15;
  google.protobuf.Int32Value window_x = 16;
  google.protobuf.Int32Value window_y = 17;
  bool sticky_window = 18;
  string calculator_currency_a = 19;
  string calculator_currency_b = 20;
  int32 calculator_decimal_places = 21;
  bool calculator_include_metals = 22;
  bool calculator_include_crypto = 23;
  int32 exchange_rate_refresh_interval_hours = 24;
  bool enable_dictionary = 25;
  string dictionary_prefix = 26;
  bool dictionary_show_always = 27;
  repeated string dictionary_languages = 28;
  bool enable_history = 29;
  int32 history_max_items = 30;
  bool keep_value_when_hide = 31;
  int32 keep_value_when_hide_duration = 32;
  bool enable_system_settings = 33;
}

message WebSearchEngineMessage {
  string id = 1;
  bool enabled = 2;
  // 0=PrefixOnly 1=ShowAlways
  int32 mode = 3;
  string prefix = 4;
  string query_url = 5;
}

message UpdateSettingsRequest {
  SettingsMessage settings = 1;
}
```

- [ ] **Step 3: Crear icons.proto**

Crear `Yottacast.Ipc/Proto/icons.proto`:

```protobuf
syntax = "proto3";
option csharp_namespace = "Yottacast.Ipc.Proto";
package yottacast;

import "google/protobuf/empty.proto";

service IconService {
  rpc GetIcon(IconRequest) returns (IconResponse);
  rpc WatchIconsLoaded(google.protobuf.Empty) returns (stream IconLoadedEvent);
}

message IconRequest {
  string icon_id = 1;
  // "app" | "file" | "badge"
  string type = 2;
}

message IconResponse {
  bytes png_data = 1;
  bool available = 2;
}

message IconLoadedEvent {
  // Empty icon_id = generic notification (some icon is now available).
  // Swift should re-request icons for visible results on receiving this.
  string icon_id = 1;
}
```

- [ ] **Step 4: Crear lifecycle.proto**

Crear `Yottacast.Ipc/Proto/lifecycle.proto`:

```protobuf
syntax = "proto3";
option csharp_namespace = "Yottacast.Ipc.Proto";
package yottacast;

import "google/protobuf/empty.proto";

service LifecycleService {
  rpc GetStatus(google.protobuf.Empty) returns (StatusResponse);
  rpc WatchStatus(google.protobuf.Empty) returns (stream StatusResponse);
  rpc Shutdown(google.protobuf.Empty) returns (google.protobuf.Empty);
}

message StatusResponse {
  enum State {
    STARTING = 0;
    INSTANT_READY = 1;
    FULLY_READY = 2;
  }
  State state = 1;
}
```

- [ ] **Step 5: Verificar que el codegen compila**

```bash
cd "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast"
dotnet build Yottacast.Ipc/Yottacast.Ipc.csproj
```

Expected: `Build succeeded` (generará tipos C# en `obj/`).

- [ ] **Step 6: Commit**

```bash
git add Yottacast.Ipc/Proto/
git commit -m "feat: add gRPC proto contracts (search, settings, icons, lifecycle)"
```

---

## Task 4: ResultMapper

**Files:**
- Create: `Yottacast.Ipc/Mapping/ResultMapper.cs`
- Create: `Yottacast.Ipc.Tests/Mapping/ResultMapperTests.cs`

- [ ] **Step 1: Escribir los tests primero**

Crear `Yottacast.Ipc.Tests/Mapping/ResultMapperTests.cs`:

```csharp
using Yottacast.Core.ViewModels;
using Yottacast.Ipc.Mapping;
using Yottacast.Ipc.Proto;

namespace Yottacast.Ipc.Tests.Mapping;

public class ResultMapperTests {
    [Fact]
    public void Map_ResultItemViewModel_MapsBaseFields() {
        var vm = new ResultItemViewModel {
            Score = 0.9,
            Title = "Safari",
            Subtitle = "/Applications/Safari.app",
            Category = "Applications",
            Icon = "/Applications/Safari.app",
            BypassLimit = false,
            PasteAfterActivate = false,
        };

        var msg = ResultMapper.Map(vm, "0");

        Assert.Equal("0", msg.Id);
        Assert.Equal("app", msg.Type);
        Assert.Equal("Safari", msg.Title);
        Assert.Equal("/Applications/Safari.app", msg.Subtitle);
        Assert.Equal("Application", msg.Category);
        Assert.Equal("/Applications/Safari.app", msg.IconId);
        Assert.Equal(0.9, msg.Score, precision: 5);
    }

    [Fact]
    public void Map_CalculatorResultItemViewModel_SetsTypeCalc() {
        var vm = new CalculatorResultItemViewModel {
            Score = 1.0,
            Title = "42",
            Subtitle = "2 * 21",
            Category = "Calculator",
            Icon = "🧮",
        };

        var msg = ResultMapper.Map(vm, "1");

        Assert.Equal("calc", msg.Type);
        Assert.Equal("42", msg.Title);
        Assert.Equal("🧮", msg.IconId);
        Assert.Equal("2 * 21", msg.Subtitle);
    }

    [Fact]
    public void Map_EmojiGridResultViewModel_MapsAllCells() {
        var cells = new List<EmojiCellViewModel> {
            new() { Char = "😀", Name = "grinning", Category = "smileys", Keywords = ["happy"], Section = EmojiSection.Default },
            EmojiCellViewModel.Placeholder,
        };
        var vm = new EmojiGridResultViewModel {
            Score = 1.0,
            Title = "Emoji",
            Cells = cells,
            Icon = "",
        };

        var msg = ResultMapper.Map(vm, "2");

        Assert.Equal("emoji_grid", msg.Type);
        Assert.Equal(2, msg.EmojiCells.Count);
        Assert.Equal("😀", msg.EmojiCells[0].Char);
        Assert.Equal("grinning", msg.EmojiCells[0].Name);
        Assert.True(msg.EmojiCells[1].IsPlaceholder);
    }

    [Fact]
    public void Map_ConversionResultItemViewModel_MapsConversionBlock() {
        var vm = new ConversionResultItemViewModel {
            Score = 1.0,
            Title = "100 km → miles",
            Category = "Converter",
            Icon = "📐",
            FromShort = "100 km",
            FromLong = "100 kilometers",
            ToShort = "62.137 mi",
            ToLong = "62.137 miles",
            FromWasNormalized = false,
        };

        var msg = ResultMapper.Map(vm, "3");

        Assert.Equal("conversion", msg.Type);
        Assert.NotNull(msg.Conversion);
        Assert.Equal("100 km", msg.Conversion.FromShort);
        Assert.Equal("62.137 mi", msg.Conversion.ToShort);
        Assert.False(msg.Conversion.FromWasNormalized);
    }

    [Fact]
    public void Map_DictionaryResultViewModel_MapsDefinitions() {
        var vm = new DictionaryResultViewModel {
            Score = 0.8,
            Title = "apple",
            Definitions = [
                new DictionaryDefinitionEntry {
                    PartOfSpeech = "noun",
                    Definition = "A round fruit.",
                    Example = "I ate an apple.",
                }
            ],
        };

        var msg = ResultMapper.Map(vm, "4");

        Assert.Equal("dict", msg.Type);
        Assert.Single(msg.Definitions);
        Assert.Equal("noun", msg.Definitions[0].PartOfSpeech);
        Assert.Equal("A round fruit.", msg.Definitions[0].Definition);
        Assert.Equal("I ate an apple.", msg.Definitions[0].Example);
    }
}
```

- [ ] **Step 2: Ejecutar los tests para confirmar que fallan**

```bash
cd "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast/Yottacast.Ipc.Tests"
dotnet test
```

Expected: falla con `error CS0246: The type or namespace name 'ResultMapper' could not be found`.

- [ ] **Step 3: Implementar ResultMapper**

Crear `Yottacast.Ipc/Mapping/ResultMapper.cs`:

```csharp
using Yottacast.Core.ViewModels;
using Yottacast.Ipc.Proto;

namespace Yottacast.Ipc.Mapping;

public static class ResultMapper {
    public static ResultMessage Map(BaseResultItemViewModel vm, string id) {
        var msg = new ResultMessage {
            Id = id,
            Title = vm.Title,
            Score = vm.Score,
            BypassLimit = vm.BypassLimit,
            PasteAfterActivate = vm.PasteAfterActivate,
        };

        switch (vm) {
            case EmojiGridResultViewModel emoji:
                msg.Type = "emoji_grid";
                foreach (var cell in emoji.Cells)
                    msg.EmojiCells.Add(MapEmojiCell(cell));
                msg.SelectedEmojiIndex = emoji.SelectedEmojiIndex;
                break;

            case ConversionResultItemViewModel conv:
                msg.Type = "conversion";
                msg.Category = conv.Category;
                msg.IconId = conv.Icon;
                msg.Conversion = new ConversionMessage {
                    FromShort = conv.FromShort,
                    FromLong = conv.FromLong ?? "",
                    ToShort = conv.ToShort,
                    ToLong = conv.ToLong ?? "",
                    NormFromShort = conv.NormFromShort ?? "",
                    NormFromLong = conv.NormFromLong ?? "",
                    FromWasNormalized = conv.FromWasNormalized,
                    RatesAreStale = conv.RatesAreStale,
                    SelectedCell = (int)conv.SelectedCell,
                };
                break;

            case DictionaryResultViewModel dict:
                msg.Type = "dict";
                foreach (var def in dict.Definitions)
                    msg.Definitions.Add(new DictionaryDefinitionMessage {
                        PartOfSpeech = def.PartOfSpeech,
                        Definition = def.Definition,
                        Example = def.Example ?? "",
                        ExampleTranslation = def.ExampleTranslation ?? "",
                    });
                break;

            case CalculatorResultItemViewModel calc:
                msg.Type = "calc";
                msg.Subtitle = calc.Subtitle;
                msg.Category = calc.Category;
                msg.IconId = calc.Icon;
                break;

            case ResultItemViewModel item:
                msg.Type = DetermineType(item);
                msg.Subtitle = item.Subtitle;
                msg.Category = item.Category;
                // Icon is the app/file path for app and file types; emoji char for web
                msg.IconId = item.Icon;
                break;
        }

        return msg;
    }

    private static string DetermineType(ResultItemViewModel item) => item.Category switch {
        "Applications" => "app",
        "Files" or "Documents" => "file",
        "Web" or "Web Search" => "web",
        _ => "app",
    };

    private static EmojiCellMessage MapEmojiCell(EmojiCellViewModel cell) {
        var msg = new EmojiCellMessage {
            Char = cell.Char,
            Name = cell.Name,
            Category = cell.Category,
            Section = (int)cell.Section,
            UsageCount = cell.UsageCount,
            IsFavorite = cell.IsFavorite,
            IsPlaceholder = cell.IsPlaceholder,
        };
        msg.Keywords.AddRange(cell.Keywords);
        return msg;
    }
}
```

**Nota:** `EmojiGridResultViewModel.SelectedEmojiIndex` — si esta propiedad no existe en el código actual, búscala o usa `0` por defecto. Consulta `Yottacast.Core/ViewModels/EmojiGridResultViewModel.cs` para confirmar el nombre exacto.

- [ ] **Step 4: Ejecutar los tests**

```bash
cd "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast/Yottacast.Ipc.Tests"
dotnet test
```

Expected: todos los tests pasan.

- [ ] **Step 5: Commit**

```bash
git add Yottacast.Ipc/Mapping/ResultMapper.cs Yottacast.Ipc.Tests/Mapping/ResultMapperTests.cs
git commit -m "feat: ResultMapper — BaseResultItemViewModel to proto ResultMessage"
```

---

## Task 5: SettingsMapper

**Files:**
- Create: `Yottacast.Ipc/Mapping/SettingsMapper.cs`
- Create: `Yottacast.Ipc.Tests/Mapping/SettingsMapperTests.cs`

- [ ] **Step 1: Escribir los tests primero**

Crear `Yottacast.Ipc.Tests/Mapping/SettingsMapperTests.cs`:

```csharp
using Yottacast.Core.Platform;
using Yottacast.Core.Services;
using Yottacast.Ipc.Mapping;
using Yottacast.Ipc.Proto;

namespace Yottacast.Ipc.Tests.Mapping;

public class SettingsMapperTests {
    // Minimal stub to construct a UserSettings for testing
    private static UserSettings MakeSettings() {
        // UserSettings.Load requires a real PlatformProvider; use a temp file approach
        // or expose a test factory. For now, use the Load path with a minimal JSON.
        var tmpPath = Path.GetTempFileName();
        File.WriteAllText(tmpPath, """
            {
              "browser": "Safari",
              "theme": "dark-default",
              "hotkey": "Alt+Space",
              "enableCalculator": true,
              "enableEmoji": false,
              "calculatorCurrencyA": "EUR",
              "calculatorCurrencyB": "USD",
              "calculatorDecimalPlaces": 3,
              "stickyWindow": true,
              "dictionaryPrefix": "define",
              "enableHistory": true,
              "historyMaxItems": 50,
              "keepValueWhenHide": true,
              "keepValueWhenHideDuration": 60
            }
            """);
        var platform = new MacOsPlatformProvider();
        return UserSettings.Load(platform, settingsPath: tmpPath);
    }

    [Fact]
    public void ToProto_MapsAllScalarFields() {
        var settings = MakeSettings();

        var msg = SettingsMapper.ToProto(settings);

        Assert.Equal("Safari", msg.Browser);
        Assert.Equal("dark-default", msg.Theme);
        Assert.Equal("Alt+Space", msg.Hotkey);
        Assert.True(msg.EnableCalculator);
        Assert.False(msg.EnableEmoji);
        Assert.Equal("EUR", msg.CalculatorCurrencyA);
        Assert.Equal(3, msg.CalculatorDecimalPlaces);
        Assert.Equal(50, msg.HistoryMaxItems);
    }

    [Fact]
    public void ToProto_NullableWindowPosition_MapsToWrapper() {
        var settings = MakeSettings();
        settings.WindowX = 100;
        settings.WindowY = null;

        var msg = SettingsMapper.ToProto(settings);

        Assert.NotNull(msg.WindowX);
        Assert.Equal(100, msg.WindowX.Value);
        Assert.Null(msg.WindowY);
    }

    [Fact]
    public void ApplyProto_UpdatesSettingsFromMessage() {
        var settings = MakeSettings();
        var msg = SettingsMapper.ToProto(settings);
        msg.Theme = "light-blue";
        msg.EnableEmoji = true;
        msg.CalculatorDecimalPlaces = 5;

        SettingsMapper.ApplyProto(msg, settings);

        Assert.Equal("light-blue", settings.Theme);
        Assert.True(settings.EnableEmoji);
        Assert.Equal(5, settings.CalculatorDecimalPlaces);
    }

    [Fact]
    public void RoundTrip_PreservesData() {
        var settings = MakeSettings();
        settings.Theme = "dark-raycast";
        settings.EnableWebSearch = false;

        var msg = SettingsMapper.ToProto(settings);
        var settings2 = MakeSettings();
        SettingsMapper.ApplyProto(msg, settings2);

        Assert.Equal("dark-raycast", settings2.Theme);
        Assert.False(settings2.EnableWebSearch);
    }
}
```

- [ ] **Step 2: Ejecutar los tests para confirmar que fallan**

```bash
cd "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast/Yottacast.Ipc.Tests"
dotnet test --filter "SettingsMapper"
```

Expected: falla con `error CS0246: 'SettingsMapper'`.

- [ ] **Step 3: Implementar SettingsMapper**

Crear `Yottacast.Ipc/Mapping/SettingsMapper.cs`:

```csharp
using Google.Protobuf.WellKnownTypes;
using Yottacast.Core.Search.WebSearch;
using Yottacast.Core.Services;
using Yottacast.Ipc.Proto;

namespace Yottacast.Ipc.Mapping;

public static class SettingsMapper {
    public static SettingsMessage ToProto(UserSettings s) {
        var msg = new SettingsMessage {
            Browser = s.Browser,
            Terminal = s.Terminal,
            Theme = s.Theme,
            Hotkey = s.Hotkey,
            EnableAppSearch = s.EnableAppSearch,
            EnableCalculator = s.EnableCalculator,
            EnableClipboard = s.EnableClipboard,
            EnableEmoji = s.EnableEmoji,
            EnableFileSearch = s.EnableFileSearch,
            EnableWebSearch = s.EnableWebSearch,
            ShowDisabledWebSearchEngines = s.ShowDisabledWebSearchEngines,
            FileSearchOnlySpecificFolders = s.FileSearchOnlySpecificFolders,
            StickyWindow = s.StickyWindow,
            CalculatorCurrencyA = s.CalculatorCurrencyA,
            CalculatorCurrencyB = s.CalculatorCurrencyB,
            CalculatorDecimalPlaces = s.CalculatorDecimalPlaces,
            CalculatorIncludeMetals = s.CalculatorIncludeMetals,
            CalculatorIncludeCrypto = s.CalculatorIncludeCrypto,
            ExchangeRateRefreshIntervalHours = s.ExchangeRateRefreshIntervalHours,
            EnableDictionary = s.EnableDictionary,
            DictionaryPrefix = s.DictionaryPrefix,
            DictionaryShowAlways = s.DictionaryShowAlways,
            EnableHistory = s.EnableHistory,
            HistoryMaxItems = s.HistoryMaxItems,
            KeepValueWhenHide = s.KeepValueWhenHide,
            KeepValueWhenHideDuration = s.KeepValueWhenHideDuration,
            EnableSystemSettings = s.EnableSystemSettings,
            WindowX = s.WindowX.HasValue ? new Int32Value { Value = s.WindowX.Value } : null,
            WindowY = s.WindowY.HasValue ? new Int32Value { Value = s.WindowY.Value } : null,
        };
        msg.SearchFolders.AddRange(s.SearchFolders);
        msg.AppDirectories.AddRange(s.AppDirectories);
        msg.DictionaryLanguages.AddRange(s.DictionaryLanguages);
        foreach (var e in s.WebSearchEngines)
            msg.WebSearchEngines.Add(new WebSearchEngineMessage {
                Id = e.Id,
                Enabled = e.Enabled,
                Mode = (int)e.Mode,
                Prefix = e.Prefix,
                QueryUrl = e.QueryUrl ?? "",
            });
        return msg;
    }

    public static void ApplyProto(SettingsMessage msg, UserSettings s) {
        s.Browser = msg.Browser;
        s.Terminal = msg.Terminal;
        s.Theme = msg.Theme;
        s.Hotkey = msg.Hotkey;
        s.EnableAppSearch = msg.EnableAppSearch;
        s.EnableCalculator = msg.EnableCalculator;
        s.EnableClipboard = msg.EnableClipboard;
        s.EnableEmoji = msg.EnableEmoji;
        s.EnableFileSearch = msg.EnableFileSearch;
        s.EnableWebSearch = msg.EnableWebSearch;
        s.ShowDisabledWebSearchEngines = msg.ShowDisabledWebSearchEngines;
        s.FileSearchOnlySpecificFolders = msg.FileSearchOnlySpecificFolders;
        s.StickyWindow = msg.StickyWindow;
        s.CalculatorCurrencyA = msg.CalculatorCurrencyA;
        s.CalculatorCurrencyB = msg.CalculatorCurrencyB;
        s.CalculatorDecimalPlaces = msg.CalculatorDecimalPlaces;
        s.CalculatorIncludeMetals = msg.CalculatorIncludeMetals;
        s.CalculatorIncludeCrypto = msg.CalculatorIncludeCrypto;
        s.ExchangeRateRefreshIntervalHours = msg.ExchangeRateRefreshIntervalHours;
        s.EnableDictionary = msg.EnableDictionary;
        s.DictionaryPrefix = msg.DictionaryPrefix;
        s.DictionaryShowAlways = msg.DictionaryShowAlways;
        s.EnableHistory = msg.EnableHistory;
        s.HistoryMaxItems = msg.HistoryMaxItems;
        s.KeepValueWhenHide = msg.KeepValueWhenHide;
        s.KeepValueWhenHideDuration = msg.KeepValueWhenHideDuration;
        s.EnableSystemSettings = msg.EnableSystemSettings;
        s.WindowX = msg.WindowX?.Value;
        s.WindowY = msg.WindowY?.Value;
        s.SearchFolders = [..msg.SearchFolders];
        s.AppDirectories = [..msg.AppDirectories];
        s.DictionaryLanguages = [..msg.DictionaryLanguages];
        s.WebSearchEngines = msg.WebSearchEngines.Select(e => new WebSearchEngineSettings {
            Id = e.Id,
            Enabled = e.Enabled,
            Mode = (WebSearchMode)e.Mode,
            Prefix = e.Prefix,
            QueryUrl = string.IsNullOrEmpty(e.QueryUrl) ? null : e.QueryUrl,
        }).ToList();
    }
}
```

- [ ] **Step 4: Ejecutar los tests**

```bash
cd "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast/Yottacast.Ipc.Tests"
dotnet test
```

Expected: todos los tests pasan.

- [ ] **Step 5: Commit**

```bash
git add Yottacast.Ipc/Mapping/SettingsMapper.cs Yottacast.Ipc.Tests/Mapping/SettingsMapperTests.cs
git commit -m "feat: SettingsMapper — UserSettings to/from proto SettingsMessage"
```

---

## Task 6: LifecycleGrpcService

**Files:**
- Create: `Yottacast.Ipc/Services/LifecycleGrpcService.cs`

- [ ] **Step 1: Implementar LifecycleGrpcService**

Crear `Yottacast.Ipc/Services/LifecycleGrpcService.cs`:

```csharp
using Grpc.Core;
using Google.Protobuf.WellKnownTypes;
using Yottacast.Ipc.Proto;

namespace Yottacast.Ipc.Services;

/// <summary>
/// Tracks and broadcasts the daemon startup state.
/// SearchGrpcService calls SetInstantReady() and SetFullyReady() during startup.
/// </summary>
public class LifecycleGrpcService(IHostApplicationLifetime lifetime)
    : LifecycleService.LifecycleServiceBase {

    private StatusResponse.Types.State _state = StatusResponse.Types.State.Starting;
    private readonly List<IServerStreamWriter<StatusResponse>> _watchers = [];
    private readonly Lock _lock = new();

    public void SetInstantReady() => Transition(StatusResponse.Types.State.InstantReady);
    public void SetFullyReady()   => Transition(StatusResponse.Types.State.FullyReady);

    private void Transition(StatusResponse.Types.State next) {
        List<IServerStreamWriter<StatusResponse>> snapshot;
        lock (_lock) {
            _state = next;
            snapshot = [.._watchers];
        }
        var response = new StatusResponse { State = next };
        foreach (var writer in snapshot) {
            _ = writer.WriteAsync(response);  // fire-and-forget; dead streams will fail silently
        }
    }

    public override Task<StatusResponse> GetStatus(Empty request, ServerCallContext context) =>
        Task.FromResult(new StatusResponse { State = _state });

    public override async Task WatchStatus(
        Empty request,
        IServerStreamWriter<StatusResponse> responseStream,
        ServerCallContext context) {

        StatusResponse.Types.State current;
        lock (_lock) {
            current = _state;
            _watchers.Add(responseStream);
        }

        // Send current state immediately so the client doesn't miss it
        await responseStream.WriteAsync(new StatusResponse { State = current });

        try {
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        } catch (OperationCanceledException) { }
        finally {
            lock (_lock) { _watchers.Remove(responseStream); }
        }
    }

    public override Task<Empty> Shutdown(Empty request, ServerCallContext context) {
        lifetime.StopApplication();
        return Task.FromResult(new Empty());
    }
}
```

- [ ] **Step 2: Compilar para verificar**

```bash
cd "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast"
dotnet build Yottacast.Ipc/Yottacast.Ipc.csproj
```

Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git add Yottacast.Ipc/Services/LifecycleGrpcService.cs
git commit -m "feat: LifecycleGrpcService — startup state machine and WatchStatus stream"
```

---

## Task 7: SearchGrpcService — búsqueda

**Files:**
- Create: `Yottacast.Ipc/Services/SearchGrpcService.cs`

- [ ] **Step 1: Implementar SearchGrpcService (búsqueda + registry)**

Crear `Yottacast.Ipc/Services/SearchGrpcService.cs`:

```csharp
using System.Collections.Concurrent;
using Grpc.Core;
using Yottacast.Core.Search;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;
using Yottacast.Ipc.Mapping;
using Yottacast.Ipc.Proto;

namespace Yottacast.Ipc.Services;

/// <summary>
/// Singleton service that exposes GlobalSearch over gRPC.
/// Maintains a result registry (latest snapshot) to execute actions by result ID.
/// The clipboard callback captures copied text and returns it in ActivateResponse
/// instead of touching the system clipboard directly.
/// </summary>
public class SearchGrpcService(
    GlobalSearch globalSearch,
    ClipboardService clipboardService,
    ILogger<SearchGrpcService> logger) : SearchService.SearchServiceBase {

    // Registry: latest snapshot of results, keyed by sequential string ID ("0", "1", ...)
    private readonly ConcurrentDictionary<string, BaseResultItemViewModel> _registry = new();

    // Captured clipboard text from the last Activate call
    private string? _lastCopiedText;

    public void Initialize() {
        clipboardService.Initialize(text => _lastCopiedText = text);
    }

    private SearchResponse BuildResponse(
        IReadOnlyList<BaseResultItemViewModel> items,
        string? hint,
        bool isSearching) {

        _registry.Clear();
        var response = new SearchResponse {
            Hint = hint ?? "",
            IsSearching = isSearching,
        };

        for (int i = 0; i < items.Count; i++) {
            var id = i.ToString();
            _registry[id] = items[i];
            response.Results.Add(ResultMapper.Map(items[i], id));
        }

        return response;
    }

    public override Task<SearchResponse> SearchInstant(
        SearchRequest request,
        ServerCallContext context) {

        var (items, hint) = globalSearch.SearchInstant(request.Query, request.Limit);
        var response = BuildResponse(items, hint, isSearching: false);
        return Task.FromResult(response);
    }

    public override async Task SearchDeferred(
        SearchRequest request,
        IServerStreamWriter<SearchResponse> responseStream,
        ServerCallContext context) {

        var ct = context.CancellationToken;
        try {
            await foreach (var snapshot in globalSearch
                .SearchDeferredAsync(request.Query, request.Limit, ct)
                .WithCancellation(ct)) {

                var response = BuildResponse(snapshot, hint: null, isSearching: true);
                await responseStream.WriteAsync(response, ct);
            }

            // Final message: deferred search complete
            var final = new SearchResponse { IsSearching = false };
            final.Results.AddRange(_registry
                .OrderBy(kv => int.Parse(kv.Key))
                .Select(kv => ResultMapper.Map(kv.Value, kv.Key)));
            await responseStream.WriteAsync(final, ct);

        } catch (OperationCanceledException) {
            logger.LogDebug("Deferred search cancelled for query '{Query}'", request.Query);
        }
    }
}
```

- [ ] **Step 2: Compilar**

```bash
dotnet build Yottacast.Ipc/Yottacast.Ipc.csproj
```

Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git add Yottacast.Ipc/Services/SearchGrpcService.cs
git commit -m "feat: SearchGrpcService — SearchInstant and SearchDeferred with result registry"
```

---

## Task 8: SearchGrpcService — Activate y Navigate

**Files:**
- Modify: `Yottacast.Ipc/Services/SearchGrpcService.cs`

- [ ] **Step 1: Añadir Activate al SearchGrpcService**

Añadir el método `Activate` dentro de la clase `SearchGrpcService`, después de `SearchDeferred`:

```csharp
    public override Task<ActivateResponse> Activate(
        ActivateRequest request,
        ServerCallContext context) {

        if (!_registry.TryGetValue(request.ResultId, out var vm)) {
            throw new RpcException(new Status(StatusCode.NotFound,
                $"Result '{request.ResultId}' not found in current session"));
        }

        _lastCopiedText = null;

        // For emoji_grid, set selected index before activating
        if (vm is Yottacast.Core.ViewModels.EmojiGridResultViewModel emojiGrid) {
            emojiGrid.SelectByIndex(request.EmojiIndex);
        }

        switch (request.Action) {
            case ActionType.Default:
                vm.OnActivate?.Invoke();
                break;
            case ActionType.Copy:
                vm.OnCopy?.Invoke();
                break;
            case ActionType.Favorite:
                vm.OnToggleFavorite?.Invoke();
                break;
        }

        return Task.FromResult(new ActivateResponse {
            PasteAfterActivate = vm.PasteAfterActivate,
            ClipboardText = _lastCopiedText ?? "",
        });
    }
```

**Nota:** `EmojiGridResultViewModel.SelectByIndex(int)` — verifica si este método existe en `Yottacast.Core/ViewModels/EmojiGridResultViewModel.cs`. Si no existe, añadir el método en ese fichero:

```csharp
public void SelectByIndex(int index) {
    if (index >= 0 && index < Cells.Count)
        SelectedEmojiIndex = index;
}
```

Si `SelectedEmojiIndex` tampoco existe, buscar el mecanismo de selección en el ViewModel y adaptarlo.

- [ ] **Step 2: Añadir Navigate al SearchGrpcService**

Añadir el método `Navigate` a la clase `SearchGrpcService`:

```csharp
    public override Task<NavigateResponse> Navigate(
        NavigateRequest request,
        ServerCallContext context) {

        if (!_registry.TryGetValue(request.ResultId, out var vm)) {
            throw new RpcException(new Status(StatusCode.NotFound,
                $"Result '{request.ResultId}' not found in current session"));
        }

        bool consumed = request.Direction switch {
            Direction.Left  => vm.OnLeft?.Invoke()  ?? false,
            Direction.Right => vm.OnRight?.Invoke() ?? false,
            Direction.Up    => vm.OnUp?.Invoke()    ?? false,
            Direction.Down  => vm.OnDown?.Invoke()  ?? false,
            _ => false,
        };

        // For emoji_grid, return updated selected index after navigation
        int newIndex = request.CurrentIndex;
        if (vm is Yottacast.Core.ViewModels.EmojiGridResultViewModel emojiGrid)
            newIndex = emojiGrid.SelectedEmojiIndex;

        return Task.FromResult(new NavigateResponse {
            Consumed = consumed,
            NewIndex = newIndex,
        });
    }
```

- [ ] **Step 3: Compilar**

```bash
dotnet build Yottacast.Ipc/Yottacast.Ipc.csproj
```

Expected: `Build succeeded`.

- [ ] **Step 4: Commit**

```bash
git add Yottacast.Ipc/Services/SearchGrpcService.cs
git commit -m "feat: SearchGrpcService — Activate and Navigate actions"
```

---

## Task 9: SettingsGrpcService

**Files:**
- Create: `Yottacast.Ipc/Services/SettingsGrpcService.cs`

- [ ] **Step 1: Implementar SettingsGrpcService**

Crear `Yottacast.Ipc/Services/SettingsGrpcService.cs`:

```csharp
using Grpc.Core;
using Google.Protobuf.WellKnownTypes;
using Yottacast.Core.Services;
using Yottacast.Ipc.Mapping;
using Yottacast.Ipc.Proto;

namespace Yottacast.Ipc.Services;

public class SettingsGrpcService(
    UserSettings settings,
    ILogger<SettingsGrpcService> logger) : SettingsService.SettingsServiceBase {

    private readonly List<IServerStreamWriter<SettingsMessage>> _watchers = [];
    private readonly Lock _lock = new();

    public void Initialize() {
        settings.SearchSettingsChanged += BroadcastCurrentSettings;
        settings.AppDirectoriesChanged += BroadcastCurrentSettings;
    }

    private void BroadcastCurrentSettings() {
        List<IServerStreamWriter<SettingsMessage>> snapshot;
        lock (_lock) { snapshot = [.._watchers]; }

        var msg = SettingsMapper.ToProto(settings);
        foreach (var writer in snapshot)
            _ = writer.WriteAsync(msg);
    }

    public override Task<SettingsMessage> GetSettings(Empty request, ServerCallContext context) =>
        Task.FromResult(SettingsMapper.ToProto(settings));

    public override Task<Empty> UpdateSettings(
        UpdateSettingsRequest request,
        ServerCallContext context) {

        SettingsMapper.ApplyProto(request.Settings, settings);
        settings.Save();
        logger.LogInformation("Settings updated via IPC");
        settings.NotifySearchSettingsChanged();
        return Task.FromResult(new Empty());
    }

    public override async Task WatchSettings(
        Empty request,
        IServerStreamWriter<SettingsMessage> responseStream,
        ServerCallContext context) {

        lock (_lock) { _watchers.Add(responseStream); }

        // Send current state immediately
        await responseStream.WriteAsync(SettingsMapper.ToProto(settings));

        try {
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        } catch (OperationCanceledException) { }
        finally {
            lock (_lock) { _watchers.Remove(responseStream); }
        }
    }
}
```

- [ ] **Step 2: Compilar**

```bash
dotnet build Yottacast.Ipc/Yottacast.Ipc.csproj
```

Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git add Yottacast.Ipc/Services/SettingsGrpcService.cs
git commit -m "feat: SettingsGrpcService — GetSettings, UpdateSettings, WatchSettings"
```

---

## Task 10: IconGrpcService

**Files:**
- Create: `Yottacast.Ipc/Services/IconGrpcService.cs`

- [ ] **Step 1: Implementar IconGrpcService**

Crear `Yottacast.Ipc/Services/IconGrpcService.cs`:

```csharp
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Yottacast.Core.Search.UserDocuments;
using Yottacast.Core.Services;
using Yottacast.Ipc.Proto;

namespace Yottacast.Ipc.Services;

public class IconGrpcService(
    AppIconCache appIconCache,
    FileIconCache fileIconCache,
    UserDocumentSearch userDocumentSearch,
    ILogger<IconGrpcService> logger) : IconService.IconServiceBase {

    private readonly List<IServerStreamWriter<IconLoadedEvent>> _watchers = [];
    private readonly Lock _lock = new();

    public void Initialize() {
        appIconCache.IconLoaded += () => BroadcastIconLoaded("");
        fileIconCache.IconLoaded += () => BroadcastIconLoaded("");
        userDocumentSearch.BadgeIconLoaded += () => BroadcastIconLoaded("");
    }

    private void BroadcastIconLoaded(string iconId) {
        List<IServerStreamWriter<IconLoadedEvent>> snapshot;
        lock (_lock) { snapshot = [.._watchers]; }

        var evt = new IconLoadedEvent { IconId = iconId };
        foreach (var writer in snapshot)
            _ = writer.WriteAsync(evt);
    }

    public override Task<IconResponse> GetIcon(IconRequest request, ServerCallContext context) {
        byte[]? bytes = request.Type switch {
            "app"   => appIconCache.Get(request.IconId),
            "file"  => fileIconCache.Get(request.IconId),
            "badge" => userDocumentSearch.GetBadge(request.IconId),
            _       => null,
        };

        if (bytes is null) {
            // Trigger async preload so it will be available on next request
            if (request.Type == "app")
                appIconCache.PreloadAsync(request.IconId);

            return Task.FromResult(new IconResponse { Available = false });
        }

        return Task.FromResult(new IconResponse {
            Available = true,
            PngData = ByteString.CopyFrom(bytes),
        });
    }

    public override async Task WatchIconsLoaded(
        Empty request,
        IServerStreamWriter<IconLoadedEvent> responseStream,
        ServerCallContext context) {

        lock (_lock) { _watchers.Add(responseStream); }

        try {
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        } catch (OperationCanceledException) { }
        finally {
            lock (_lock) { _watchers.Remove(responseStream); }
        }
    }
}
```

- [ ] **Step 2: Compilar**

```bash
dotnet build Yottacast.Ipc/Yottacast.Ipc.csproj
```

Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git add Yottacast.Ipc/Services/IconGrpcService.cs
git commit -m "feat: IconGrpcService — GetIcon on-demand and WatchIconsLoaded stream"
```

---

## Task 11: Program.cs — host, DI, PID file, shutdown

**Files:**
- Modify: `Yottacast.Ipc/Program.cs`

- [ ] **Step 1: Reemplazar Program.cs con la implementación completa**

Reemplazar `Yottacast.Ipc/Program.cs` con:

```csharp
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Yottacast.Core;
using Yottacast.Core.Search;
using Yottacast.Core.Search.Application;
using Yottacast.Core.Search.Calculator;
using Yottacast.Core.Search.Dictionary;
using Yottacast.Core.Search.Emoji;
using Yottacast.Core.Search.UserDocuments;
using Yottacast.Core.Search.WebSearch;
using Yottacast.Core.Services;
using Yottacast.Core.Platform;
using Yottacast.Ipc.Services;

// ── PID file guard ───────────────────────────────────────────────────────────
Directory.CreateDirectory(AppPaths.CacheDir);

if (File.Exists(AppPaths.IpcPidFile)) {
    var pidStr = await File.ReadAllTextAsync(AppPaths.IpcPidFile);
    if (int.TryParse(pidStr.Trim(), out var existingPid)) {
        try {
            System.Diagnostics.Process.GetProcessById(existingPid);
            Console.Error.WriteLine(
                $"yottacast-core already running (PID {existingPid}). Exiting.");
            return 1;
        } catch (ArgumentException) {
            // Stale PID file — process no longer running
        }
    }
}
await File.WriteAllTextAsync(AppPaths.IpcPidFile, Environment.ProcessId.ToString());

// ── Host ─────────────────────────────────────────────────────────────────────
var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => {
    // Delete stale socket file if it exists
    if (File.Exists(AppPaths.IpcSocket))
        File.Delete(AppPaths.IpcSocket);

    options.ListenUnixSocket(AppPaths.IpcSocket, listenOptions => {
        listenOptions.Protocols = HttpProtocols.Http2;
    });
});

// ── Logging ──────────────────────────────────────────────────────────────────
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// ── DI ───────────────────────────────────────────────────────────────────────
builder.Services.AddGrpc();

// Platform
builder.Services.AddSingleton<PlatformProvider, MacOsPlatformProvider>();

// Core services
builder.Services.AddSingleton<ClipboardService>();
builder.Services.AddSingleton<AppIconCache>();
builder.Services.AddSingleton<FileIconCache>();
builder.Services.AddSingleton<PluginService>();
builder.Services.AddSingleton<HistoryService>();

// UserSettings (loaded from disk)
builder.Services.AddSingleton(sp => {
    var platform = sp.GetRequiredService<PlatformProvider>();
    var logger = sp.GetRequiredService<ILogger<UserSettings>>();
    return UserSettings.Load(platform, logger);
});

// Search sources
builder.Services.AddSingleton<ApplicationSearch>();
builder.Services.AddSingleton<CalculatorSearch>();
builder.Services.AddSingleton<EmojiSearch>();
builder.Services.AddSingleton<WebSearchSource>();
builder.Services.AddSingleton<UserDocumentSearch>();
builder.Services.AddSingleton<DictionarySource>();

// Aggregate interfaces
builder.Services.AddSingleton<IInstantSearchSource>(sp =>
    sp.GetRequiredService<ApplicationSearch>());
builder.Services.AddSingleton<IInstantSearchSource>(sp =>
    sp.GetRequiredService<CalculatorSearch>());
builder.Services.AddSingleton<IInstantSearchSource>(sp =>
    sp.GetRequiredService<EmojiSearch>());
builder.Services.AddSingleton<IInstantSearchSource>(sp =>
    sp.GetRequiredService<WebSearchSource>());
builder.Services.AddSingleton<IDeferredSearchSource>(sp =>
    sp.GetRequiredService<UserDocumentSearch>());
builder.Services.AddSingleton<IDeferredSearchSource>(sp =>
    sp.GetRequiredService<DictionarySource>());

builder.Services.AddSingleton<GlobalSearch>();

// gRPC services (singleton to maintain state)
builder.Services.AddSingleton<LifecycleGrpcService>();
builder.Services.AddSingleton<SearchGrpcService>();
builder.Services.AddSingleton<SettingsGrpcService>();
builder.Services.AddSingleton<IconGrpcService>();

// ── App ───────────────────────────────────────────────────────────────────────
var app = builder.Build();

app.MapGrpcService<LifecycleGrpcService>();
app.MapGrpcService<SearchGrpcService>();
app.MapGrpcService<SettingsGrpcService>();
app.MapGrpcService<IconGrpcService>();

// ── Startup sequence ─────────────────────────────────────────────────────────
var lifecycle   = app.Services.GetRequiredService<LifecycleGrpcService>();
var search      = app.Services.GetRequiredService<SearchGrpcService>();
var settingsSvc = app.Services.GetRequiredService<SettingsGrpcService>();
var iconSvc     = app.Services.GetRequiredService<IconGrpcService>();
var globalSearch = app.Services.GetRequiredService<GlobalSearch>();
var appLifetime  = app.Services.GetRequiredService<IHostApplicationLifetime>();

search.Initialize();
settingsSvc.Initialize();
iconSvc.Initialize();

// Start search sources in background, notify lifecycle when ready
_ = Task.Run(async () => {
    globalSearch.Start();
    await globalSearch.WhenInstantReady();
    lifecycle.SetInstantReady();
    await globalSearch.WhenReady();
    lifecycle.SetFullyReady();
});

// ── Graceful shutdown ─────────────────────────────────────────────────────────
appLifetime.ApplicationStopping.Register(() => {
    globalSearch.Stop().GetAwaiter().GetResult();
    try { File.Delete(AppPaths.IpcPidFile); } catch { }
    try { File.Delete(AppPaths.IpcSocket); }  catch { }
});

// ── Handle SIGTERM ────────────────────────────────────────────────────────────
Console.CancelKeyPress += (_, e) => {
    e.Cancel = true;
    appLifetime.StopApplication();
};

app.Run();
return 0;
```

- [ ] **Step 2: Compilar**

```bash
cd "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast"
dotnet build Yottacast.Ipc/Yottacast.Ipc.csproj
```

Expected: `Build succeeded. 0 Error(s)`

**Si hay errores de DI** (tipos no encontrados en `Yottacast.Core`):
- Verificar los namespaces exactos en `Yottacast.Core/Search/` para `GlobalSearch`, `IInstantSearchSource`, `IDeferredSearchSource`
- Verificar namespace de `MacOsPlatformProvider` en `Yottacast.Core/Platform/`
- Ajustar los `using` correspondientes

- [ ] **Step 3: Verificar que los tests existentes siguen pasando**

```bash
cd "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast/Yottacast.Core.Tests"
dotnet test
```

Expected: todos los tests pasan.

- [ ] **Step 4: Commit**

```bash
git add Yottacast.Ipc/Program.cs
git commit -m "feat: Program.cs — Kestrel Unix socket host, DI wiring, PID file, graceful shutdown"
```

---

## Task 12: Verificación de integración

- [ ] **Step 1: Instalar grpcurl si no está disponible**

```bash
brew install grpcurl
```

- [ ] **Step 2: Arrancar el daemon**

```bash
cd "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast/Yottacast.Ipc"
dotnet run
```

Expected: el proceso arranca y escucha. Verificar que no hay errores en la consola. Dejar corriendo y abrir otra terminal.

- [ ] **Step 3: Verificar que el socket existe**

```bash
ls -la ~/.cache/yottacast/core.sock
```

Expected: `srwxr-xr-x ... core.sock`

- [ ] **Step 4: Comprobar el PID file**

```bash
cat ~/.cache/yottacast/core.pid
```

Expected: un número de PID válido.

- [ ] **Step 5: Consultar estado del daemon**

```bash
grpcurl -plaintext -unix -proto \
  "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast/Yottacast.Ipc/Proto/lifecycle.proto" \
  -import-path "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast/Yottacast.Ipc/Proto" \
  "$(cat ~/.cache/yottacast/core.sock | xargs -I{} echo {})" \
  yottacast.LifecycleService/GetStatus
```

Forma más simple (usando socket path directamente):

```bash
SOCK=~/.cache/yottacast/core.sock
PROTO_DIR="/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast/Yottacast.Ipc/Proto"

grpcurl -plaintext -unix "$SOCK" \
  -import-path "$PROTO_DIR" \
  -proto lifecycle.proto \
  yottacast.LifecycleService/GetStatus
```

Expected:
```json
{
  "state": "INSTANT_READY"
}
```

- [ ] **Step 6: Búsqueda instant**

```bash
grpcurl -plaintext -unix "$SOCK" \
  -import-path "$PROTO_DIR" \
  -proto search.proto \
  -d '{"query": "safari", "limit": 10}' \
  yottacast.SearchService/SearchInstant
```

Expected: respuesta JSON con array `results` conteniendo al menos una app Safari con `type: "app"`.

- [ ] **Step 7: Leer settings**

```bash
grpcurl -plaintext -unix "$SOCK" \
  -import-path "$PROTO_DIR" \
  -proto settings.proto \
  yottacast.SettingsService/GetSettings
```

Expected: JSON con todas las propiedades de settings del usuario.

- [ ] **Step 8: Solicitar un icono**

Con el `icon_id` obtenido en el Step 6 (el path de Safari, ej. `/Applications/Safari.app`):

```bash
grpcurl -plaintext -unix "$SOCK" \
  -import-path "$PROTO_DIR" \
  -proto icons.proto \
  -d '{"icon_id": "/Applications/Safari.app", "type": "app"}' \
  yottacast.IconService/GetIcon
```

Expected: `{"available": true, "pngData": "<base64>"}` o `{"available": false}` si aún está cargando.

- [ ] **Step 9: Probar shutdown**

```bash
grpcurl -plaintext -unix "$SOCK" \
  -import-path "$PROTO_DIR" \
  -proto lifecycle.proto \
  yottacast.LifecycleService/Shutdown
```

Expected: el proceso del daemon termina limpiamente. Verificar que el PID file y el socket son borrados:

```bash
ls ~/.cache/yottacast/core.pid  # debe fallar
ls ~/.cache/yottacast/core.sock # debe fallar
```

- [ ] **Step 10: Verificar que los tests de Core y IPC siguen pasando**

```bash
cd "/Users/avilches/Library/Mobile Documents/com~apple~CloudDocs/Shared/Proy/Yottacast"
dotnet test Yottacast.Core.Tests/
dotnet test Yottacast.Ipc.Tests/
```

Expected: todos los tests pasan.

- [ ] **Step 11: Commit final**

```bash
git add -A
git commit -m "chore: verify IPC daemon integration with grpcurl"
```

---

## Notas de implementación

**`EmojiGridResultViewModel.SelectedEmojiIndex`**: La clase tiene `_viewportStartCell` y navegación interna. Si `SelectedEmojiIndex` no existe como propiedad pública, buscar el mecanismo equivalente en `Yottacast.Core/ViewModels/EmojiGridResultViewModel.cs` y adaptar el mapper y el servicio de navegación en consecuencia.

**DI de `IInstantSearchSource`/`IDeferredSearchSource`**: `GlobalSearch` puede recibir estas interfaces como `IEnumerable<T>`. Verificar en `Yottacast.Core/Search/GlobalSearch.cs` cómo está declarado el constructor para ajustar el registro DI si es necesario.

**Namespaces exactos**: Verificar en los ficheros del Core antes de asumir. Los usings en `Program.cs` son los más probables según la estructura, pero pueden variar.
