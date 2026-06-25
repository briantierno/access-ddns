using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;

var builder = WebApplication.CreateBuilder(args);

// Cargar configuración desde appsettings.json
var appSettings = builder.Configuration.GetSection("AppSettings").Get<AppSettings>() ?? new AppSettings();

// Registrar servicios
builder.Services.AddHttpClient();
builder.Services.AddSingleton(appSettings);
builder.Services.AddHostedService<AutoResetService>();

// Leer puerto desde variable de entorno (sobrescribe appsettings.json)
if (int.TryParse(Environment.GetEnvironmentVariable("PORT"), out int envPort))
{
    appSettings.Port = envPort;
}

// Configurar servidor HTTP/HTTPS
var urlBuilder = appSettings.UseHttps ? "https" : "http";
builder.WebHost.UseUrls($"{urlBuilder}://0.0.0.0:{appSettings.Port}");

// Si HTTPS está habilitado, configurar certificado
if (appSettings.UseHttps && File.Exists(appSettings.CertificatePath))
{
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenAnyIP(appSettings.Port, listenOptions =>
        {
            listenOptions.UseHttps(appSettings.CertificatePath, appSettings.CertificatePassword);
        });
    });
}

var app = builder.Build();

var credFile = "./credentials.json";
var timestampFile = "./lastupdate.json";

// Obtener credenciales actuales (lee del archivo cada vez, sin caché)
(string user, string passMd5) GetCurrentCredentials()
{
    try
    {
        if (File.Exists(credFile))
        {
            var json = File.ReadAllText(credFile);
            var doc = JsonDocument.Parse(json);
            var user = doc.RootElement.GetProperty("user").GetString() ?? appSettings.Credentials.DefaultUser;
            var passMd5 = doc.RootElement.GetProperty("passMd5").GetString() ?? GetMd5Hash(appSettings.Credentials.DefaultPassword);
            return (user, passMd5);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error leyendo credenciales: {ex.Message}");
    }
    
    return (appSettings.Credentials.DefaultUser, GetMd5Hash(appSettings.Credentials.DefaultPassword));
}

void SaveCredentials(string user, string passMd5)
{
    try
    {
        var data = new { user = user, passMd5 = passMd5 };
        File.WriteAllText(credFile, JsonSerializer.Serialize(data));
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Credenciales guardadas: {user}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error guardando credenciales: {ex.Message}");
    }
}

bool IsDefaultCredentials()
{
    var (currentUser, currentPassMd5) = GetCurrentCredentials();
    return currentUser == appSettings.Credentials.DefaultUser && 
           currentPassMd5 == GetMd5Hash(appSettings.Credentials.DefaultPassword);
}

bool ValidateAuth(HttpContext context, out string user)
{
    user = "";
    var authHeader = context.Request.Headers["Authorization"].ToString();
    if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Basic "))
        return false;

    try
    {
        var (currentUser, currentPassMd5) = GetCurrentCredentials();
        
        var base64 = authHeader.Substring(6);
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        var parts = decoded.Split(':');
        user = parts[0];
        var pass = parts.Length > 1 ? parts[1] : "";
        var passHash = GetMd5Hash(pass);
        return user == currentUser && passHash == currentPassMd5;
    }
    catch { return false; }
}

void SaveTimestamp(DateTime dt)
{
    var data = new { lastUpdate = dt.ToString("O") };
    File.WriteAllText(timestampFile, JsonSerializer.Serialize(data));
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Timestamp guardado: {dt}");
}

DateTime? GetLastTimestamp()
{
    try
    {
        if (!File.Exists(timestampFile)) return null;
        var json = File.ReadAllText(timestampFile);
        var doc = JsonDocument.Parse(json);
        var lastUpdateStr = doc.RootElement.GetProperty("lastUpdate").GetString();
        return lastUpdateStr != null ? DateTime.Parse(lastUpdateStr) : null;
    }
    catch { return null; }
}

int GetAutoResetHours()
{
    try
    {
        if (File.Exists("./appsettings.json"))
        {
            var json = File.ReadAllText("./appsettings.json");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("AppSettings", out var appSettingsElement) &&
                appSettingsElement.TryGetProperty("AutoResetHours", out var hoursElement) &&
                hoursElement.TryGetInt32(out int hours))
            {
                return hours;
            }
        }
    }
    catch { }
    return 6; // Default
}

static string GetMd5Hash(string input)
{
    using (var md5 = System.Security.Cryptography.MD5.Create())
    {
        byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }
}

var staticFileOptions = new StaticFileOptions();
staticFileOptions.ServeUnknownFileTypes = true;
staticFileOptions.DefaultContentType = "application/octet-stream";

// Agregar MIME type para manifest.json
var provider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
provider.Mappings[".json"] = "application/json";
provider.Mappings[".webmanifest"] = "application/manifest+json";
staticFileOptions.ContentTypeProvider = provider;

app.UseStaticFiles(staticFileOptions);

// Mapear ruta raíz (/) y /access a la misma acción
Func<HttpContext, System.Threading.Tasks.Task> serveAccess = async (HttpContext context) =>
{
    if (appSettings.RequireAuthentication && !IsDefaultCredentials() && !ValidateAuth(context, out _))
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsync("Unauthorized");
        return;
    }

    context.Response.ContentType = "text/html; charset=utf-8";
    context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
    context.Response.Headers["Pragma"] = "no-cache";
    context.Response.Headers["Expires"] = "0";
    await context.Response.SendFileAsync("./wwwroot/index.html");
};

app.MapGet("/", serveAccess);
app.MapGet("/access", serveAccess);

app.MapGet("/api/pwa-status", async (HttpContext context) =>
{
    try
    {
        var manifestPath = "./wwwroot/manifest.json";
        var icon192Path = "./wwwroot/icon-192.png";
        var icon512Path = "./wwwroot/icon-512.png";
        var swPath = "./wwwroot/sw.js";

        bool manifestReadable = false;
        if (File.Exists(manifestPath))
        {
            try
            {
                var json = File.ReadAllText(manifestPath);
                var doc = JsonDocument.Parse(json);
                manifestReadable = true;
            }
            catch { }
        }

        var result = new
        {
            manifest = new
            {
                exists = File.Exists(manifestPath),
                size = File.Exists(manifestPath) ? new FileInfo(manifestPath).Length : 0,
                readable = manifestReadable
            },
            icons = new
            {
                icon_192 = new
                {
                    exists = File.Exists(icon192Path),
                    size = File.Exists(icon192Path) ? new FileInfo(icon192Path).Length : 0
                },
                icon_512 = new
                {
                    exists = File.Exists(icon512Path),
                    size = File.Exists(icon512Path) ? new FileInfo(icon512Path).Length : 0
                }
            },
            serviceWorker = new
            {
                exists = File.Exists(swPath),
                size = File.Exists(swPath) ? new FileInfo(swPath).Length : 0
            },
            https = context.Request.IsHttps,
            host = context.Request.Host.ToString()
        };

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(result);
    }
    catch (Exception ex)
    {
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});


app.MapGet("/api/config", async (HttpContext context) =>
{
    if (appSettings.RequireAuthentication && !IsDefaultCredentials() && !ValidateAuth(context, out _))
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
        return;
    }

    var (currentUser, _) = GetCurrentCredentials();
    context.Response.ContentType = "application/json";
    context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
    await context.Response.WriteAsJsonAsync(new { isDefault = IsDefaultCredentials(), user = currentUser });
});

app.MapGet("/api/status", async (HttpContext context) =>
{
    if (appSettings.RequireAuthentication && !IsDefaultCredentials() && !ValidateAuth(context, out _))
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
        return;
    }

    try
    {
        context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";

        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        var publicIp = await client.GetStringAsync("https://ipinfo.io/ip");
        publicIp = publicIp.Trim();
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] IP Pública: {publicIp}");

        string dnsIp = "No resuelto";
        bool isSynchronized = false;

        try
        {
            var dnsTask = Dns.GetHostEntryAsync(appSettings.DNS.Domain);
            var delayTask = System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(5));
            
            if (await System.Threading.Tasks.Task.WhenAny(dnsTask, delayTask) == dnsTask)
            {
                var ipHostInfo = await dnsTask;
                if (ipHostInfo.AddressList.Length > 0)
                {
                    dnsIp = ipHostInfo.AddressList[0].ToString();
                    isSynchronized = (publicIp == dnsIp);
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] DNS: {dnsIp}, Sync: {isSynchronized}");
                }
            }
            else
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] DNS timeout (5s)");
            }
        }
        catch (Exception dnsEx)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error DNS: {dnsEx.Message}");
        }

        var lastUpdate = GetLastTimestamp();
        var autoResetHours = GetAutoResetHours();
        var nextReset = lastUpdate?.AddHours(autoResetHours) ?? DateTime.Now;
        var timeRemaining = (long)(nextReset - DateTime.Now).TotalSeconds;

        // Si la IP en DNS ya es la de disconnect (1.1.1.1), el countdown está en 0
        if (dnsIp == appSettings.CDmon.DisconnectIP)
        {
            timeRemaining = 0;
        }

        var result = new
        {
            currentIp = publicIp,
            dnsIp = dnsIp,
            domain = appSettings.DNS.Domain,
            isSynchronized = isSynchronized,
            lastUpdated = lastUpdate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Nunca",
            nextResetIn = timeRemaining > 0 ? timeRemaining : 0,
            autoResetHours = autoResetHours,
            status = "ok"
        };

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(result);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error /api/status: {ex.Message}");
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

app.MapPost("/api/update", async (HttpContext context) =>
{
    if (appSettings.RequireAuthentication && !IsDefaultCredentials() && !ValidateAuth(context, out _))
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
        return;
    }

    try
    {
        var ip = context.Request.Query["ip"].ToString();
        if (string.IsNullOrEmpty(ip))
        {
            using var client = new HttpClient();
            ip = (await client.GetStringAsync("https://ipinfo.io/ip")).Trim();
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Update: {ip}");

        using var cdmonClient = new HttpClient();
        var cdmonUrl = $"{appSettings.CDmon.Endpoint}?enctype=MD5&n={appSettings.CDmon.User}&p={appSettings.CDmon.PasswordHash}&cip={ip}";
        var cdmonResponse = await cdmonClient.GetStringAsync(cdmonUrl);
        SaveTimestamp(DateTime.Now);

        context.Response.ContentType = "application/json";
        context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        await context.Response.WriteAsJsonAsync(new { success = true, message = $"IP {ip} actualizada", cdmonResponse = cdmonResponse.Trim() });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error /api/update: {ex.Message}");
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

app.MapGet("/api/update", async (HttpContext context) =>
{
    if (appSettings.RequireAuthentication && !IsDefaultCredentials() && !ValidateAuth(context, out _))
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
        return;
    }

    try
    {
        var ip = context.Request.Query["ip"].ToString();
        if (string.IsNullOrEmpty(ip))
        {
            using var client = new HttpClient();
            ip = (await client.GetStringAsync("https://ipinfo.io/ip")).Trim();
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Update (GET): {ip}");

        using var cdmonClient = new HttpClient();
        var cdmonUrl = $"{appSettings.CDmon.Endpoint}?enctype=MD5&n={appSettings.CDmon.User}&p={appSettings.CDmon.PasswordHash}&cip={ip}";
        var cdmonResponse = await cdmonClient.GetStringAsync(cdmonUrl);
        SaveTimestamp(DateTime.Now);

        context.Response.ContentType = "application/json";
        context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        await context.Response.WriteAsJsonAsync(new { success = true, message = $"IP {ip} actualizada", cdmonResponse = cdmonResponse.Trim() });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error /api/update (GET): {ex.Message}");
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

app.MapPost("/api/disconnect", async (HttpContext context) =>
{
    if (appSettings.RequireAuthentication && !IsDefaultCredentials() && !ValidateAuth(context, out _))
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
        return;
    }

    try
    {
        var disconnectIp = appSettings.CDmon.DisconnectIP;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Disconnect: {disconnectIp}");

        using var cdmonClient = new HttpClient();
        var cdmonUrl = $"{appSettings.CDmon.Endpoint}?enctype=MD5&n={appSettings.CDmon.User}&p={appSettings.CDmon.PasswordHash}&cip={disconnectIp}";
        var cdmonResponse = await cdmonClient.GetStringAsync(cdmonUrl);
        SaveTimestamp(DateTime.Now);

        context.Response.ContentType = "application/json";
        context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        await context.Response.WriteAsJsonAsync(new { success = true, message = $"Desconectado. IP reseteada a {disconnectIp}", cdmonResponse = cdmonResponse.Trim() });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error /api/disconnect: {ex.Message}");
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

app.MapGet("/api/disconnect", async (HttpContext context) =>
{
    if (appSettings.RequireAuthentication && !IsDefaultCredentials() && !ValidateAuth(context, out _))
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
        return;
    }

    try
    {
        var disconnectIp = appSettings.CDmon.DisconnectIP;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Disconnect (GET): {disconnectIp}");

        using var cdmonClient = new HttpClient();
        var cdmonUrl = $"{appSettings.CDmon.Endpoint}?enctype=MD5&n={appSettings.CDmon.User}&p={appSettings.CDmon.PasswordHash}&cip={disconnectIp}";
        var cdmonResponse = await cdmonClient.GetStringAsync(cdmonUrl);
        SaveTimestamp(DateTime.Now);

        context.Response.ContentType = "application/json";
        context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        await context.Response.WriteAsJsonAsync(new { success = true, message = $"Desconectado. IP reseteada a {disconnectIp}", cdmonResponse = cdmonResponse.Trim() });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error /api/disconnect (GET): {ex.Message}");
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

app.MapPost("/api/credentials", async (HttpContext context) =>
{
    if (appSettings.RequireAuthentication && !IsDefaultCredentials() && !ValidateAuth(context, out _))
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
        return;
    }

    try
    {
        context.Request.EnableBuffering();
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var newUser = root.GetProperty("user").GetString()?.Trim() ?? "";
        var newPass = root.GetProperty("password").GetString()?.Trim() ?? "";

        if (string.IsNullOrEmpty(newUser) || string.IsNullOrEmpty(newPass))
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { error = "Usuario y contraseña requeridos" });
            return;
        }

        var newPassMd5 = GetMd5Hash(newPass);
        SaveCredentials(newUser, newPassMd5);

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { success = true, message = "Credenciales actualizadas correctamente" });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error /api/credentials: {ex.Message}");
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

app.MapGet("/api/settings", async (HttpContext context) =>
{
    if (appSettings.RequireAuthentication && !IsDefaultCredentials() && !ValidateAuth(context, out _))
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
        return;
    }

    try
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new 
        { 
            autoResetHours = appSettings.AutoResetHours,
            domain = appSettings.DNS.Domain,
            requireAuthentication = appSettings.RequireAuthentication
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error /api/settings: {ex.Message}");
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

app.MapPost("/api/settings", async (HttpContext context) =>
{
    if (appSettings.RequireAuthentication && !IsDefaultCredentials() && !ValidateAuth(context, out _))
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
        return;
    }

    try
    {
        context.Request.EnableBuffering();
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (root.TryGetProperty("autoResetHours", out var hoursElement) && hoursElement.TryGetInt32(out int hours))
        {
            if (hours > 0 && hours <= 720) // Max 30 días
            {
                appSettings.AutoResetHours = hours;
                UpdateAppsettings("AutoResetHours", hours);
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Auto-reset cambiado a {hours} horas");
            }
            else
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new { error = "AutoResetHours debe estar entre 1 y 720" });
                return;
            }
        }

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { success = true, message = "Configuración actualizada", autoResetHours = appSettings.AutoResetHours });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error /api/settings POST: {ex.Message}");
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

void UpdateAppsettings(string key, object value)
{
    try
    {
        var appsettingsPath = "./appsettings.json";
        if (File.Exists(appsettingsPath))
        {
            var json = File.ReadAllText(appsettingsPath);
            using var doc = JsonDocument.Parse(json);
            var options = new JsonSerializerOptions { WriteIndented = true };
            var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();
            
            if (!dict.ContainsKey("AppSettings"))
                dict["AppSettings"] = new Dictionary<string, object>();
            
            var appSettingsDict = JsonSerializer.Deserialize<Dictionary<string, object>>(dict["AppSettings"].ToString() ?? "{}") ?? new Dictionary<string, object>();
            appSettingsDict[key] = value;
            dict["AppSettings"] = appSettingsDict;
            
            File.WriteAllText(appsettingsPath, JsonSerializer.Serialize(dict, options));
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error actualizando appsettings.json: {ex.Message}");
    }
}

Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ========================================");
Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ACCESS DDNS - Iniciado");
Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Puerto: {appSettings.Port}");
Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Dominio: {appSettings.DNS.Domain}");
Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Protocolo: {(appSettings.UseHttps ? "HTTPS" : "HTTP")}");
Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Autenticación: {(appSettings.RequireAuthentication ? "Habilitada" : "Deshabilitada")}");
Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ========================================");

await app.RunAsync();
