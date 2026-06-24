# ACCESS DDNS

Actualiza dinámicamente registros DNS en CDmon para mantener un dominio apuntando a tu IP pública actual 
para garantizar acceso en mikrotik desde cualquier equipo remoto eventual.

## **Instalación y Configuración**

### **1. Requisitos**
- .NET 8 SDK instalado
- ASP.NET Core Runtime

### **2. Configurar `appsettings.json`**

Edita el archivo `appsettings.json` en la raíz del proyecto:

```json
{
  "AppSettings": {
    "Port": 5000,
    "UseHttps": false,
    "CertificatePath": "./cert.pfx",
    "CertificatePassword": "",
    "Credentials": {
      "DefaultUser": "admin",
      "DefaultPassword": "password"
    },
    "DNS": {
      "Domain": "access.dmz.ar",
      "Provider": "cdmon"
    },
    "CDmon": {
      "Endpoint": "https://dinamico.cdmon.org/onlineService.php",
      "User": "tu_usuario_cdmon",
      "PasswordHash": "hash_md5_tu_password",
      "DisconnectIP": "1.1.1.1"
    }
  }
}
```

**Parámetros:**
- `Port`: Puerto de escucha (default: 5000)
- `UseHttps`: Habilitar HTTPS (requiere certificado)
- `CertificatePath`: Ruta al certificado .pfx
- `CertificatePassword`: Password del certificado (vacío si no tiene)
- `Domain`: Tu dominio DNS a resolver
- `PasswordHash`: Hash MD5 de tu contraseña CDmon
- `DisconnectIP`: IP a la que resetear en disconnect (default: 1.1.1.1)

---

## **Ejecución**

### **Puerto por defecto (5000)**
```bash
dotnet watch run
```

### **Puerto custom (variable de entorno)**

**Windows (CMD):**
```cmd
set PORT=3000 && dotnet watch run
```

**Windows (PowerShell):**
```powershell
$env:PORT=3000; dotnet watch run
```

**Mac/Linux:**
```bash
PORT=3000 dotnet watch run
```

---

## **Acceso a la interfaz**

Una vez iniciado, accede desde tu PC o desde la red:

### **Desde la misma PC:**
```
http://localhost:5000
http://localhost:5000/access
```

### **Desde otra PC en la red:**
```
http://[TU_IP_LOCAL]:5000
http://[TU_IP_LOCAL]:5000/access
```

Ejemplo: `http://192.168.1.100:5000`

### **Sin puerto (si usas puerto 80):**
```
http://192.168.1.100
```

---

## **HTTPS (Puerto 443)**

### **Generar certificado autofirmado**

**Windows (PowerShell como Admin):**
```powershell
$cert = New-SelfSignedCertificate -CertStoreLocation "cert:\LocalMachine\My" `
  -DnsName "access.dmz.ar" -FriendlyName "Access DDNS" -NotAfter (Get-Date).AddYears(1)

Export-PfxCertificate -Cert $cert -FilePath "cert.pfx" -Password (ConvertTo-SecureString -String "password" -AsPlainText -Force)
```

**Linux/Mac:**
```bash
openssl req -x509 -newkey rsa:4096 -keyout key.pem -out cert.pem -days 365 -nodes
openssl pkcs12 -export -out cert.pfx -inkey key.pem -in cert.pem -password pass:password
```

### **Configurar en appsettings.json:**
```json
{
  "AppSettings": {
    "Port": 443,
    "UseHttps": true,
    "CertificatePath": "./cert.pfx",
    "CertificatePassword": "password"
  }
}
```

---

## **Puerto 80 en Windows**

Para usar puerto 80 sin ser administrador, usa `netsh`:

```cmd
netsh http add urlacl url=http://+:80/ user=DOMAIN\username listen=yes
```

Reemplaza `DOMAIN\username` con tu usuario.

O si quieres permitir a todos:
```cmd
netsh http add urlacl url=http://+:80/ user=EVERYONE listen=yes
```

Luego configura en `appsettings.json`:
```json
{
  "AppSettings": {
    "Port": 80
  }
}
```

---

## **Puerto <1024 en Linux**

En Linux, puertos menores a 1024 requieren `sudo`. Dos opciones:

### **Opción 1: Usar sudo**
```bash
sudo PORT=80 dotnet run
```

### **Opción 2: Redirigir con iptables**
```bash
sudo iptables -t nat -A PREROUTING -p tcp --dport 80 -j REDIRECT --to-port 5000
```

---

## **APIs disponibles**

### **GET /api/config**
```bash
curl -u admin:password http://localhost:5000/api/config
```

### **GET /api/status**
```bash
curl -u admin:password http://localhost:5000/api/status
```

### **POST /api/update**
```bash
curl -u admin:password -X POST "http://localhost:5000/api/update?ip=200.1.1.1"
```

### **POST /api/disconnect**
```bash
curl -u admin:password -X POST http://localhost:5000/api/disconnect
```

### **POST /api/credentials**
```bash
curl -u admin:password -X POST http://localhost:5000/api/credentials \
  -H "Content-Type: application/json" \
  -d '{"user":"newuser","password":"newpass"}'
```

---

## **Variables de entorno**

| Variable | Descripción | Ejemplo |
|----------|-------------|---------|
| `PORT` | Puerto de escucha | `PORT=8080` |

Sobrescribe el valor en `appsettings.json`.

---

## **Solución de problemas**

### **No conecta desde otra PC**
- Verifica que el firewall permita el puerto (ver comando arriba)
- Usa la IP local (ej: `192.168.1.100`) no `localhost`
- Comprueba `netstat -an` que el puerto esté escuchando

### **HTTPS no funciona**
- Verifica que `cert.pfx` existe en la raíz del proyecto
- Comprueba la password del certificado
- En navegador, acepta el certificado autofirmado

### **No sincroniza con DNS**
- Comprueba que CDmon esté actualizado
- Espera 60 segundos (propagación DNS)
- Usa el comando `/api/disconnect` y luego `/api/update`

---

## **Estructura de archivos**

```
access-ddns/
├── Program.cs
├── AppSettings.cs
├── appsettings.json          ← Configuración
├── credentials.json          ← Credenciales (auto-generado)
├── lastupdate.json           ← Timestamp (auto-generado)
├── wwwroot/
│   └── index.html           ← Interfaz web
├── README.md
└── .gitignore
```

---

## **Licencia**

MIT

