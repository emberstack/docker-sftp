using System.Collections.Generic;

namespace ES.SFTP.Host.Configuration.Elements
{
    public class GroupDefinition
    {
        public string Name { get; set; }
        public int? GID { get; set; }
        public List<string> Users { get; set; } = new List<string>();
    }
}