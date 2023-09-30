namespace ES.SFTP.Configuration.Elements;

public class GlobalConfiguration
{
    public ChrootDefinition Chroot { get; set; } = new();
    public List<string> Directories { get; set; } = new();
    public LoggingDefinition Logging { get; set; } = new();
    public HostKeysDefinition HostKeys { get; set; } = new();
    public HooksDefinition Hooks { get; set; } = new();
    public string PKIandPassword { get; set; }

    public string Ciphers { get; set; }
    public string HostKeyAlgorithms { get; set; }
    public string KexAlgorithms { get; set; }
    public string MACs { get; set; }
}