public class AppSettings
{
    public int Port { get; set; } = 5000;
    public bool UseHttps { get; set; } = false;
    public bool RequireAuthentication { get; set; } = true;
    public string CertificatePath { get; set; } = "./cert.pfx";
    public string CertificatePassword { get; set; } = "";
    public CredentialsConfig Credentials { get; set; } = new();
    public DNSConfig DNS { get; set; } = new();
    public CDmonConfig CDmon { get; set; } = new();
}

public class CredentialsConfig
{
    public string DefaultUser { get; set; } = "admin";
    public string DefaultPassword { get; set; } = "password";
}

public class DNSConfig
{
    public string Domain { get; set; } = "access.dmz.ar";
    public string Provider { get; set; } = "cdmon";
}

public class CDmonConfig
{
    public string Endpoint { get; set; } = "https://dinamico.cdmon.org/onlineService.php";
    public string User { get; set; } = "dmzaccess";
    public string PasswordHash { get; set; } = "e8a2d6bc68ffc4c0775435fbfc3cbadb";
    public string DisconnectIP { get; set; } = "1.1.1.1";
}
