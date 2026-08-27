using System.Text.Json;
using System.Text.Json.Serialization;
using SaveVault.Server.Configuration;
using SaveVault.Server.Endpoints;
using SaveVault.Server.Security;
using SaveVault.Server.Storage;

var builder = WebApplication.CreateBuilder(args);

// Konfiguration aus den SAVEVAULT_*-Umgebungsvariablen (tolerant: fehlt der Token, startet der
// Server trotzdem und verweigert API-Aufrufe mit klarer Meldung – siehe Auth-Middleware).
var config = ServerConfig.FromEnvironment();
builder.Services.AddSingleton(config);

// Bindet den Lauscht-Port aus SAVEVAULT_PORT, falls ASPNETCORE_URLS nicht bereits gesetzt ist
// (im Docker-Image setzt das Dockerfile ASPNETCORE_URLS; lokal greift SAVEVAULT_PORT/Default 8420).
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
    builder.WebHost.UseUrls($"http://0.0.0.0:{config.Port}");

// JSON-Optionen des Servers an den gemeinsamen API-Vertrag angleichen (camelCase, Enums als
// String, null beim Schreiben auslassen) – damit Server und Client identisch (de)serialisieren.
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.PropertyNameCaseInsensitive = true;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// Der Vault-Store ist zustandsbehaftet (Index im Speicher + Platte) → als Singleton.
builder.Services.AddSingleton(sp => new VaultStore(
    config.DataRoot,
    sp.GetRequiredService<ILoggerFactory>().CreateLogger<VaultStore>()));

var app = builder.Build();

// Store früh erzeugen, damit Startprobleme (z. B. Datenverzeichnis nicht schreibbar) sofort auffallen.
var startupLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SaveVault.Startup");
_ = app.Services.GetRequiredService<VaultStore>();
if (!config.IsConfigured)
{
    startupLog.LogWarning(
        "SAVEVAULT_TOKEN ist nicht gesetzt – der Server läuft, verweigert aber alle API-Aufrufe, " +
        "bis ein Token gesetzt und neu gestartet wurde.");
}
startupLog.LogInformation("SaveVault-Server bereit. Datenverzeichnis: {DataRoot}", config.DataRoot);

// Reihenfolge: zentrale Fehlerbehandlung ganz außen, dann Auth, dann statische Dateien + Endpunkte.
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<TokenAuthMiddleware>();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapSaveVault();

// Das eigentliche Dashboard (Schritt 4) liefert index.html in wwwroot; unbekannte Nicht-API-Pfade
// fallen darauf zurück (SPA-Verhalten). /api-Routen sind bereits abgebildet und werden nicht erfasst.
app.MapFallbackToFile("index.html");

app.Run();
