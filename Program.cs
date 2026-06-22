using System;
using System.Net.Http;
using System.Text;
using System.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

var adminUser = Environment.GetEnvironmentVariable("ADMIN_USER") ?? "admin";
var adminPassMd5 = Environment.GetEnvironmentVariable("ADMIN_PASS_MD5") ?? "5f4dcc3b5aa765d61d8327deb882cf99";
var cdmonEndpoint = "https://dinamico.cdmon.org/onlineService.php";
var cdmonParams = "enctype=MD5&n=dmzaccess&p=e8a2d6bc68ffc4c0775435fbfc3cbadb";

app.UseStaticFiles();

// GET /access - Retorna HTML
app.MapGet("/access", async (HttpContext context) =>
{
    await context.Response.SendFileAsync("./wwwroot/index.html");
});

// GET /api/status - Retorna status actual
app.MapGet("/api/status", async (HttpContext context) =>
{
    try
    {
        using var client = new HttpClient();
        var publicIp = await client.GetStringAsync("https://ipinfo.io/ip");
        publicIp = publicIp.Trim();

        var result = new
        {
            currentIp = publicIp,
            lastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            status = "ok"
        };

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(result);
    }
    catch (Exception ex)
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

// POST /api/update - Actualiza DNS en CDmon
app.MapPost("/api/update", async (HttpContext context) =>
{
    try
    {
        var user = context.Request.Query["user"].ToString();
        var pass = context.Request.Query["pass"].ToString();
        var ip = context.Request.Query["ip"].ToString();

        // Si no hay query params, intenta Basic Auth
        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
        {
            var authHeader = context.Request.Headers["Authorization"].ToString();
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Basic "))
            {
                var base64 = authHeader.Substring(6);
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
                var parts = decoded.Split(':');
                user = parts[0];
                pass = parts.Length > 1 ? parts[1] : "";
            }
        }

        // Obtener IP del cliente si no está especificada
        if (string.IsNullOrEmpty(ip))
        {
            using var client = new HttpClient();
            ip = (await client.GetStringAsync("https://ipinfo.io/ip")).Trim();
        }

        // Validar credenciales
        var passHash = GetMd5Hash(pass);
        if (user != adminUser || passHash != adminPassMd5)
        {
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = "Invalid credentials" });
            return;
        }

        // Llamar CDmon
        using var cdmonClient = new HttpClient();
        var cdmonUrl = $"{cdmonEndpoint}?{cdmonParams}&cip={ip}";
        var cdmonResponse = await cdmonClient.GetStringAsync(cdmonUrl);

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            success = true,
            message = $"IP {ip} actualizada",
            cdmonResponse = cdmonResponse.Trim()
        });
    }
    catch (Exception ex)
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

// GET /api/update - También acepta GET con credenciales en query
app.MapGet("/api/update", async (HttpContext context) =>
{
    try
    {
        var user = context.Request.Query["user"].ToString();
        var pass = context.Request.Query["pass"].ToString();
        var ip = context.Request.Query["ip"].ToString();

        // Si no hay query params, intenta Basic Auth
        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
        {
            var authHeader = context.Request.Headers["Authorization"].ToString();
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Basic "))
            {
                var base64 = authHeader.Substring(6);
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
                var parts = decoded.Split(':');
                user = parts[0];
                pass = parts.Length > 1 ? parts[1] : "";
            }
        }

        // Obtener IP del cliente si no está especificada
        if (string.IsNullOrEmpty(ip))
        {
            using var client = new HttpClient();
            ip = (await client.GetStringAsync("https://ipinfo.io/ip")).Trim();
        }

        // Validar credenciales
        var passHash = GetMd5Hash(pass);
        if (user != adminUser || passHash != adminPassMd5)
        {
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = "Invalid credentials" });
            return;
        }

        // Llamar CDmon
        using var cdmonClient = new HttpClient();
        var cdmonUrl = $"{cdmonEndpoint}?{cdmonParams}&cip={ip}";
        var cdmonResponse = await cdmonClient.GetStringAsync(cdmonUrl);

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            success = true,
            message = $"IP {ip} actualizada",
            cdmonResponse = cdmonResponse.Trim()
        });
    }
    catch (Exception ex)
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

app.MapGet("/", async (HttpContext context) =>
{
    context.Response.Redirect("/access");
});

await app.RunAsync();

// Helper para generar hash MD5
static string GetMd5Hash(string input)
{
    using (var md5 = System.Security.Cryptography.MD5.Create())
    {
        byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }
}
