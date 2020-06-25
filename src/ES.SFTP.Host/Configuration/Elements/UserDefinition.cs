using System.Collections.Generic;

namespace ES.SFTP.Host.Configuration.Elements
{
    public class UserDefinition
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public bool PasswordIsEncrypted { get; set; }
        public List<string> AllowedHosts { get; set; } = new List<string>();

        // ReSharper disable once InconsistentNaming
        public int? UID { get; set; }

        // ReSharper disable once InconsistentNaming
        public int? GID { get; set; }
        public ChrootDefinition Chroot { get; set; } = new ChrootDefinition();
        public List<string> Directories { get; set; } = new List<string>();
        public List<string> PublicKeys { get; set; } = new List<string>();
    }
}