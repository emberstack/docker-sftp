namespace ES.SFTP.Configuration.Elements;

public class UserDefinition
{
    public string Username { get; set; }
    public string Password { get; set; }
    public bool PasswordIsEncrypted { get; set; }
    public List<string> AllowedHosts { get; set; } = new();

    // ReSharper disable once InconsistentNaming
    public int? UID { get; set; }

    // ReSharper disable once InconsistentNaming
    public int? GID { get; set; }
    public ChrootDefinition Chroot { get; set; } = new();
    public List<string> Directories { get; set; } = new();
    public List<string> PublicKeys { get; set; } = new();

    // Umask property for user-specific file permissions
    public string Umask { get; set; }
}