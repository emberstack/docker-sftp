namespace ES.SFTP.Configuration.Elements;

public class HooksDefinition
{
    public List<string> OnServerStartup { get; set; } = new();
    public List<string> OnSessionChange { get; set; } = new();
}