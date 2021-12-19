namespace ES.SFTP.Configuration.Elements;

public class GroupDefinition
{
    public string Name { get; set; }
    public int? GID { get; set; }
    public List<string> Users { get; set; } = new();
}