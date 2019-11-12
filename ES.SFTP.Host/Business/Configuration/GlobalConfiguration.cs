using System.Collections.Generic;

namespace ES.SFTP.Host.Business.Configuration
{
    public class GlobalConfiguration
    {
        public string HomeDirectory { get; set; }
        public ChrootDefinition Chroot { get; set; }
        public List<string> Directories { get; set; } = new List<string>();
    }
}