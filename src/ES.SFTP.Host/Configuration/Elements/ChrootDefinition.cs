namespace ES.SFTP.Host.Configuration.Elements
{
    public class ChrootDefinition
    {
        public string Directory { get; set; } = "%h";
        public string StartPath { get; set; }
    }
}