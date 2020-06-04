using System.Collections.Generic;

namespace ES.SFTP.Host.Configuration.Elements
{
    public class GlobalConfiguration
    {
        public ChrootDefinition Chroot { get; set; } = new ChrootDefinition();
        public List<string> Directories { get; set; } = new List<string>();
        public LoggingDefinition Logging { get; set; } = new LoggingDefinition();
        public HostKeysDefinition HostKeys { get; set; } = new HostKeysDefinition();
        public HooksDefinition Hooks { get; set; } = new HooksDefinition();
    }
}