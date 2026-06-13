using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Yottacast.Core.Services;

public class UpdateChecker(ILogger<UpdateChecker> logger) : IDisposable {
    // PENDIENTE: activar cuando exista un endpoint real de versiones (ver UpdateApiUrl).
    private const bool UpdateCheckEnabled = false;

    // PLACEHOLDER: `example.com` no devuelve datos validos. La feature de comprobacion
    // de actualizaciones esta PENDIENTE de un endpoint real de versiones.
    // Reemplazar con el endpoint real cuando esté disponible y poner UpdateCheckEnabled = true.
    // Formato esperado de respuesta: { "version": "1.2.0" }
    private const string UpdateApiUrl = "https://example.com/yottacast/latest.json";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(AppDefaults.UpdateCheckTimeoutSeconds) };

    public string CurrentVersion { get; } = System.Reflection.Assembly
        .GetExecutingAssembly()
        .GetName().Version?.ToString(3) ?? "0.0.0";

    public string? LatestVersion { get; private set; }
    public bool UpdateAvailable { get; private set; }

    public async Task CheckAsync() {
        // PENDIENTE: la comprobacion esta desactivada porque UpdateApiUrl es un placeholder.
        // Early-return para no hacer ninguna peticion de red ni loguear warnings en cada arranque.
        if (!UpdateCheckEnabled) return;
        try {
            var json = await _http.GetStringAsync(UpdateApiUrl);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("version", out var vProp)) return;
            var latest = vProp.GetString();
            if (string.IsNullOrWhiteSpace(latest)) return;
            LatestVersion = latest;
            UpdateAvailable = IsNewer(latest, CurrentVersion);
            if (UpdateAvailable)
                logger.LogInformation("Update available: {Latest} (current: {Current})", latest, CurrentVersion);
        } catch (Exception ex) {
            logger.LogWarning("Update check failed: {Message}", ex.Message);
        }
    }

    private static bool IsNewer(string candidate, string current) {
        return Version.TryParse(candidate, out var c) && Version.TryParse(current, out var cur) && c > cur;
    }

    public void Dispose() {
        _http.Dispose();
    }
}
