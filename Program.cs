using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var adminUser = Environment.GetEnvironmentVariable("ADMIN_USER") ?? "admin";
var adminPassMd5 = Environment.GetEnvironmentVariable("ADMIN_PASS_MD5") ?? "5f4dcc3b5aa765d61d8327deb882cf99";
var cdmonEndpoint = "https://dinamico.cdmon.org/onlineService.php";
var cdmonParams = "enctype=MD5&n=dmzaccess&p=e8a2d6bc68ffc4c0775435fbfc3cbadb";
var domainToCheck = "access.dmz.ar";
var timestampFile = "./lastupdate.json";
var autoResetHours = 6;

// Guardar timestamp de última actualización
void SaveTimestamp(DateTime dt)
{
    var data = new { lastUpdate = dt.ToString("O") };
    File.WriteAllText(timestampFile, JsonSerializer.Serialize(data));
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Timestamp guardado: {dt}");
}

// Obtener timestamp de última actualización
DateTime? GetLastTimestamp()
{
    try
    {
        if (!File.Exists(timestampFile)) return null;
        var json = File.ReadAllText(timestampFile);
        var doc = JsonDocument.Parse(json);
        var lastUpdateStr = doc.RootElement.GetProperty("lastUpdate").GetString();
        return DateTime.Parse(lastUpdateStr);
    }
    catch { return null; }
}

// Validar Basic Auth
bool ValidateAuth(HttpContext context, out string user)
{
    user = "";
    var authHeader = context.Request.Headers["Authorization"].ToString();
    if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Basic "))
        return false;

    try
    {
        var base64 = authHeader.Substring(6);
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        var parts = decoded.Split(':');
        user = parts[0];
        var pass = parts.Length > 1 ? parts[1] : "";
        var passHash = GetMd5Hash(pass);

        return user == adminUser && passHash == adminPassMd5;
    }
    catch { return false; }
}

app.UseStaticFiles();

// GET /access - Requiere autenticación
app.MapGet("/access", async (HttpContext context) =>
{
    if (!ValidateAuth(context, out _))
    {
        context.Response.StatusCode = 401;
        context.Response.Headers.Add("WWW-Authenticate", "Basic realm=\"access.dmz.ar\"");
        await context.Response.WriteAsync("Unauthorized");
        return;
    }

    context.Response.Headers.Add("Cache-Control", "no-cache, no-store, must-revalidate");
    context.Response.Headers.Add("Pragma", "no-cache");
    context.Response.Headers.Add("Expires", "0");
    await context.Response.SendFileAsync("./wwwroot/index.html");
});

// GET /api/status
app.MapGet("/api/status", async (HttpContext context) =>
{
    if (!ValidateAuth(context, out _))
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
        return;
    }

    try
    {
        context.Response.Headers.Add("Cache-Control", "no-cache, no-store, must-revalidate");

        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(10);

        var publicIp = await client.GetStringAsync("https://ipinfo.io/ip");
        publicIp = publicIp.Trim();

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] IP Pública: {publicIp}");

        string dnsIp = "No resuelto";
        bool isSynchronized = false;

        try
        {
            var ipHostInfo = Dns.GetHostEntry(domainToCheck);
            if (ipHostInfo.AddressList.Length > 0)
            {
                dnsIp = ipHostInfo.AddressList[0].ToString();
                isSynchronized = (publicIp == dnsIp);
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] DNS: {dnsIp}, Sync: {isSynchronized}");
            }
        }
        catch (Exception dnsEx)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error DNS: {dnsEx.Message}");
        }

        var lastUpdate = GetLastTimestamp();
        var nextReset = lastUpdate?.AddHours(autoResetHours) ?? DateTime.Now;
        var timeRemaining = (long)(nextReset - DateTime.Now).TotalSeconds;

        var result = new
        {
            currentIp = publicIp,
            dnsIp = dnsIp,
            domain = domainToCheck,
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
        context.Response.Headers.Add("Cache-Control", "no-cache, no-store, must-revalidate");
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

// POST /api/update
app.MapPost("/api/update", async (HttpContext context) =>
{
    if (!ValidateAuth(context, out _))
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
        var cdmonUrl = $"{cdmonEndpoint}?{cdmonParams}&cip={ip}";
        var cdmonResponse = await cdmonClient.GetStringAsync(cdmonUrl);

        SaveTimestamp(DateTime.Now);

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Actualizado correctamente");

        context.Response.ContentType = "application/json";
        context.Response.Headers.Add("Cache-Control", "no-cache, no-store, must-revalidate");
        await context.Response.WriteAsJsonAsync(new
        {
            success = true,
            message = $"IP {ip} actualizada",
            cdmonResponse = cdmonResponse.Trim()
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error /api/update: {ex.Message}");
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        context.Response.Headers.Add("Cache-Control", "no-cache, no-store, must-revalidate");
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

// GET /api/update (también GET)
app.MapGet("/api/update", async (HttpContext context) =>
{
    if (!ValidateAuth(context, out _))
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
        var cdmonUrl = $"{cdmonEndpoint}?{cdmonParams}&cip={ip}";
        var cdmonResponse = await cdmonClient.GetStringAsync(cdmonUrl);

        SaveTimestamp(DateTime.Now);

        context.Response.ContentType = "application/json";
        context.Response.Headers.Add("Cache-Control", "no-cache, no-store, must-revalidate");
        await context.Response.WriteAsJsonAsync(new
        {
            success = true,
            message = $"IP {ip} actualizada",
            cdmonResponse = cdmonResponse.Trim()
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error /api/update (GET): {ex.Message}");
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        context.Response.Headers.Add("Cache-Control", "no-cache, no-store, must-revalidate");
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

// POST /api/disconnect - Setea 1.1.1.1
app.MapPost("/api/disconnect", async (HttpContext context) =>
{
    if (!ValidateAuth(context, out _))
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
        return;
    }

    try
    {
        var disconnectIp = "1.1.1.1";
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Disconnect: {disconnectIp}");

        using var cdmonClient = new HttpClient();
        var cdmonUrl = $"{cdmonEndpoint}?{cdmonParams}&cip={disconnectIp}";
        var cdmonResponse = await cdmonClient.GetStringAsync(cdmonUrl);

        SaveTimestamp(DateTime.Now);

        context.Response.ContentType = "application/json";
        context.Response.Headers.Add("Cache-Control", "no-cache, no-store, must-revalidate");
        await context.Response.WriteAsJsonAsync(new
        {
            success = true,
            message = "Desconectado. IP reseteada a 1.1.1.1",
            cdmonResponse = cdmonResponse.Trim()
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error /api/disconnect: {ex.Message}");
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        context.Response.Headers.Add("Cache-Control", "no-cache, no-store, must-revalidate");
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

// GET /api/update (también GET)
app.MapGet("/api/disconnect", async (HttpContext context) =>
{
    if (!ValidateAuth(context, out _))
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
        return;
    }

    try
    {
        var disconnectIp = "1.1.1.1";
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Disconnect (GET): {disconnectIp}");

        using var cdmonClient = new HttpClient();
        var cdmonUrl = $"{cdmonEndpoint}?{cdmonParams}&cip={disconnectIp}";
        var cdmonResponse = await cdmonClient.GetStringAsync(cdmonUrl);

        SaveTimestamp(DateTime.Now);

        context.Response.ContentType = "application/json";
        context.Response.Headers.Add("Cache-Control", "no-cache, no-store, must-revalidate");
        await context.Response.WriteAsJsonAsync(new
        {
            success = true,
            message = "Desconectado. IP reseteada a 1.1.1.1",
            cdmonResponse = cdmonResponse.Trim()
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error /api/disconnect (GET): {ex.Message}");
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        context.Response.Headers.Add("Cache-Control", "no-cache, no-store, must-revalidate");
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

app.MapGet("/", async (HttpContext context) =>
{
    context.Response.Redirect("/access");
});

await app.RunAsync();

static string GetMd5Hash(string input)
{
    using (var md5 = System.Security.Cryptography.MD5.Create())
    {
        byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }
}
