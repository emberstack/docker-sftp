using System.Collections.Generic;

namespace ES.SFTP.Host.Business.Configuration
{
    public class SftpConfiguration
    {
        public GlobalConfiguration Global { get; set; }
        public List<UserDefinition> Users { get; set; }
    }
}