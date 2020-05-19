using System.Collections.Generic;

namespace ES.SFTP.Host.Configuration.Elements
{
    public class SftpConfiguration
    {
        public GlobalConfiguration Global { get; set; } = new GlobalConfiguration();
        public List<UserDefinition> Users { get; set; } = new List<UserDefinition>();
        public List<GroupDefinition> Groups { get; set; } = new List<GroupDefinition>();
    }
}