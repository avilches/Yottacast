using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Yottacast.Core.Services;

public class UpdateChecker {
    // Reemplazar con el endpoint real cuando esté disponible.
    // Formato esperado de respuesta: { "version": "1.2.0" }
    private const string UpdateApiUrl = "https://example.com/yottacast/latest.json";

    private readonly HttpClient _http;
    private readonly ILogger<UpdateChecker> _logger;

    public string CurrentVersion { get; }
    public string? LatestVersion { get; private set; }
    public bool UpdateAvailable { get; private set; }

    public UpdateChecker(ILogger<UpdateChecker> logger) {
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        CurrentVersion = System.Reflection.Assembly
            .GetExecutingAssembly()
            .GetName().Version?.ToString(3) ?? "0.0.0";
    }

    public async Task CheckAsync() {
        try {
            var json = await _http.GetStringAsync(UpdateApiUrl);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("version", out var vProp)) return;
            var latest = vProp.GetString();
            if (string.IsNullOrWhiteSpace(latest)) return;
            LatestVersion = latest;
            UpdateAvailable = IsNewer(latest, CurrentVersion);
            if (UpdateAvailable)
                _logger.LogInformation("Update available: {Latest} (current: {Current})", latest, CurrentVersion);
        } catch (Exception ex) {
            _logger.LogWarning("Update check failed: {Message}", ex.Message);
        }
    }

    private static bool IsNewer(string candidate, string current) {
        return Version.TryParse(candidate, out var c) && Version.TryParse(current, out var cur) && c > cur;
    }
}
