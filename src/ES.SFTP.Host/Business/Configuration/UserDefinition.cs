using System.Collections.Generic;

namespace ES.SFTP.Host.Business.Configuration
{
    public class UserDefinition
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public bool PasswordIsEncrypted { get; set; }
        public int? UID { get; set; }
        public int? GID { get; set; }
        public ChrootDefinition Chroot { get; set; }
        public List<string> Directories { get; set; } = new List<string>();
        public List<string> PublicKeys { get; set; } = new List<string>();
    }
}