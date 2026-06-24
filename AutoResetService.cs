using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

public class AutoResetService : IHostedService
{
    private Timer _timer;
    private readonly string _timestampFile = "./lastupdate.json";
    private readonly AppSettings _appSettings;
    private readonly IHttpClientFactory _httpClientFactory;

    public AutoResetService(AppSettings appSettings, IHttpClientFactory httpClientFactory)
    {
        _appSettings = appSettings;
        _httpClientFactory = httpClientFactory;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] AutoResetService iniciado - Auto-reset cada {_appSettings.AutoResetHours} horas, chequeo cada 1 minuto");
        
        // Chequear cada 1 minuto si hay que resetear
        _timer = new Timer(CheckAndReset, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] AutoResetService detenido");
        return Task.CompletedTask;
    }

    private async void CheckAndReset(object state)
    {
        try
        {
            DateTime? lastUpdate = GetLastTimestamp();
            
            if (lastUpdate == null)
            {
                // Nunca se actualizó, no resetear aún
                return;
            }

            DateTime nextReset = lastUpdate.Value.AddHours(_appSettings.AutoResetHours);
            
            if (DateTime.Now >= nextReset)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ AUTO-RESET ACTIVADO - Reseteando IP a {_appSettings.CDmon.DisconnectIP}");
                
                await ExecuteReset();
                
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ AUTO-RESET COMPLETADO");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ERROR en AutoResetService: {ex.Message}");
        }
    }

    private async Task ExecuteReset()
    {
        try
        {
            using var client = new HttpClient();
            var cdmonUrl = $"{_appSettings.CDmon.Endpoint}?enctype=MD5&n={_appSettings.CDmon.User}&p={_appSettings.CDmon.PasswordHash}&cip={_appSettings.CDmon.DisconnectIP}";
            
            var response = await client.GetStringAsync(cdmonUrl);
            
            // Actualizar timestamp
            SaveTimestamp(DateTime.Now);
            
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Respuesta CDmon: {response.Trim()}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error en ExecuteReset: {ex.Message}");
            throw;
        }
    }

    private DateTime? GetLastTimestamp()
    {
        try
        {
            if (!File.Exists(_timestampFile)) return null;
            var json = File.ReadAllText(_timestampFile);
            var doc = JsonDocument.Parse(json);
            var lastUpdateStr = doc.RootElement.GetProperty("lastUpdate").GetString();
            return lastUpdateStr != null ? DateTime.Parse(lastUpdateStr) : null;
        }
        catch { return null; }
    }

    private void SaveTimestamp(DateTime dt)
    {
        try
        {
            var data = new { lastUpdate = dt.ToString("O") };
            File.WriteAllText(_timestampFile, JsonSerializer.Serialize(data));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error guardando timestamp: {ex.Message}");
        }
    }
}
