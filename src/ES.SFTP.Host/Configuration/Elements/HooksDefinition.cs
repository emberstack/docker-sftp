using System.Collections.Generic;

namespace ES.SFTP.Host.Configuration.Elements
{
    public class HooksDefinition
    {
        public List<string> OnServerStartup { get; set; } = new List<string>();
        public List<string> OnSessionChange { get; set; } = new List<string>();
    }
}