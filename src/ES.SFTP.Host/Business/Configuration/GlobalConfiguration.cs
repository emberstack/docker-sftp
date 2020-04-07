using System.Collections.Generic;

namespace ES.SFTP.Host.Business.Configuration
{
    public class GlobalConfiguration
    {
        public ChrootDefinition Chroot { get; set; }
        public List<string> Directories { get; set; } = new List<string>();
        public LoggingDefinition Logging { get; set; }
    }
}