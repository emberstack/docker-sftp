namespace ES.SFTP.Configuration.Elements;

public class SftpConfiguration
{
    public GlobalConfiguration Global { get; set; } = new();
    public List<UserDefinition> Users { get; set; } = new();
    public List<GroupDefinition> Groups { get; set; } = new();
}