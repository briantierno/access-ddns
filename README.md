# Access DDNS - Actualizador de DNS Dinámico

Aplicación .NET Core para actualizar automáticamente tu dominio dinámico en CDmon y permitir acceso remoto a través de MikroTik.

## 🎯 Características

- ✅ **Servidor minimalista** - ASP.NET Core en puerto 80/443
- ✅ **Interfaz web moderna** - Dark mode con naranja + púrpura
- ✅ **IP pública visible** - Muestra tu IP actual de forma destacada
- ✅ **Auto-refresh** - Actualización automática cada 5 segundos
- ✅ **Validación segura** - Basic Auth + Query params
- ✅ **Servicio Windows** - Instalar como servicio permanente
- ✅ **Terminal compatible** - curl, wget, PowerShell
- ✅ **Compilable .exe** - Todo en un archivo ejecutable

## 🚀 Compilación

### Requisitos

- .NET 8 SDK (https://dotnet.microsoft.com/download)
- Windows 10+

### Paso 1: Descargar el proyecto

```bash
git clone https://github.com/briantierno/access-ddns-dotnet.git
cd access-ddns-dotnet
```

### Paso 2: Compilar para Windows

```bash
dotnet publish -c Release -r win-x64 --self-contained
```

Se creará: `bin/Release/net8.0/win-x64/publish/access-ddns.exe`

### Paso 3 (Alternativa): Compilar como .exe simple

```bash
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true --self-contained
```

## 💻 Instalación como Servicio Windows

### Opción A: Instalación automática (recomendado)

Crea un archivo `install.bat`:

```batch
@echo off
cd /D "%~dp0"
sc create AccessDDNS binPath= "%CD%\access-ddns.exe" start= auto
sc start AccessDDNS
echo Servicio instalado. Abre https://localhost/access
pause
```

Ejecuta como Administrador.

### Opción B: Instalación manual PowerShell

```powershell
$serviceName = "AccessDDNS"
$exePath = "C:\ruta\a\access-ddns.exe"

# Crea el servicio
New-Service -Name $serviceName -BinaryPathName $exePath -StartupType Automatic

# Inicia el servicio
Start-Service -Name $serviceName
```

### Desinstalar el servicio

```powershell
Stop-Service -Name AccessDDNS
Remove-Service -Name AccessDDNS
```

## 🔧 Configuración

### Variables de Entorno

```
ADMIN_USER=admin
ADMIN_PASS_MD5=5f4dcc3b5aa765d61d8327deb882cf99
```

Generar hash MD5:
```bash
# Linux/Mac
echo -n "tupassword" | md5sum

# Windows PowerShell
$String = "tupassword"
$MD5 = [System.Security.Cryptography.MD5]::Create()
$Hash = [System.Convert]::ToHexString($MD5.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($String)))
Write-Host $Hash
```

## 🌐 Uso

### Desde navegador

```
https://dmz.ar/access
```

Automáticamente:
1. Detecta tu IP pública
2. Actualiza en CDmon
3. Muestra estado en tiempo real

### Desde terminal (Linux/Mac)

```bash
IP=$(curl -s https://ipinfo.io/ip)
curl -u admin:password "https://dmz.ar/access/api/update?ip=$IP"
```

### Desde terminal (Windows PowerShell)

```powershell
$ip = (Invoke-WebRequest https://ipinfo.io/ip).Content
curl -u admin:password "https://dmz.ar/access/api/update?ip=$ip"
```

### Con credenciales en URL

```bash
curl "https://dmz.ar/access/api/update?user=admin&pass=password&ip=200.100.50.25"
```

### Con wget

```bash
IP=$(curl -s https://ipinfo.io/ip)
wget --user=admin --password=password "https://dmz.ar/access/api/update?ip=$IP" -O -
```

## 🔐 Configuración en MikroTik

### 1. Port forwarding a servidor Windows

```
/ip firewall nat add chain=dstnat dst-address=dmz.ar dst-port=80 protocol=tcp \
  to-addresses=192.168.1.100 to-ports=80 comment="Access DDNS"

/ip firewall nat add chain=dstnat dst-address=dmz.ar dst-port=443 protocol=tcp \
  to-addresses=192.168.1.100 to-ports=443 comment="Access DDNS HTTPS"
```

(Reemplaza `192.168.1.100` con la IP de tu servidor Windows)

### 2. Agregar dominio a firewall address-list

```
/ip firewall address-list add list=ACCESO address=access.dmz.ar comment="IP dinámica"
```

### 3. Usar en reglas de firewall

```
/ip firewall filter add chain=forward src-address-list=ACCESO action=accept
```

## 🔒 SSL/HTTPS

### Generar certificado Let's Encrypt en Windows

```bash
# Instalar Certbot
choco install certbot

# Generar certificado
certbot certonly --standalone -d dmz.ar -d access.dmz.ar
```

Los certificados estarán en:
```
C:\Certbot\live\dmz.ar\
```

## 📊 API Endpoints

### GET `/access`
Retorna la página HTML principal.

```
https://dmz.ar/access
```

### GET `/api/status`
Retorna estado actual en JSON.

```json
{
  "currentIp": "200.100.50.25",
  "lastUpdated": "2026-06-22 02:30:15",
  "status": "ok"
}
```

### GET/POST `/api/update`
Actualiza el DNS en CDmon.

```
https://dmz.ar/access/api/update?user=admin&pass=password&ip=200.100.50.25
```

**Con Basic Auth:**
```
curl -u admin:password "https://dmz.ar/access/api/update?ip=200.100.50.25"
```

## 🐛 Troubleshooting

**Error: "Port 80 already in use"**
```powershell
# Encuentra qué usa el puerto
netstat -ano | findstr :80

# Mata el proceso
taskkill /PID <PID> /F
```

**Credenciales inválidas**
- Verifica que el hash MD5 es correcto
- Asegúrate de usar `echo -n` (sin newline) para generar el hash

**No se resuelve access.dmz.ar**
- Verifica DNS: `nslookup access.dmz.ar`
- Comprueba que CDmon tiene la IP actualizada
- Fuerza refresh DNS en MikroTik: `/ip dns cache flush`

## 📝 Logs

Los logs de .NET Core se guardan en:
```
C:\Users\<Usuario>\AppData\Local\Temp\
```

O ejecuta con:
```powershell
dotnet run --project access-ddns.csproj
```

## 📦 Estructura

```
access-ddns-dotnet/
├── Program.cs              # Servidor ASP.NET Core
├── wwwroot/
│   └── index.html         # UI web
├── access-ddns.csproj     # Proyecto .NET
└── README.md              # Este archivo
```

## 🔄 Actualización

Para actualizar a nuevas versiones:

1. Detén el servicio: `net stop AccessDDNS`
2. Descarga nueva versión
3. Recompila: `dotnet publish -c Release -r win-x64 --self-contained`
4. Reemplaza el .exe
5. Inicia el servicio: `net start AccessDDNS`

## 📄 Licencia

MIT

## 🤝 Soporte

Para problemas o preguntas, abre un issue en GitHub.

---

**Hecho con ❤️ para actualizaciones de DNS dinámicas en MikroTik**
