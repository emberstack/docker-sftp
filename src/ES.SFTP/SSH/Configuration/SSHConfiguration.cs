using System.Text;

namespace ES.SFTP.SSH.Configuration;

public class SSHConfiguration
{
    public List<MatchBlock> MatchBlocks { get; } = new();

    public List<string> AllowUsers { get; } = new();

    public string Ciphers { get; set; }
    public string HostKeyAlgorithms { get; set; }
    public string KexAlgorithms { get; set; }
    public string MACs { get; set; }
    public string PKIandPassword { get; set; }

    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.AppendLine();
        builder.AppendLine("UsePAM yes");

        builder.AppendLine("# SSH Protocol");
        builder.AppendLine("Protocol 2");
        builder.AppendLine();
        builder.AppendLine("# Host Keys");
        builder.AppendLine("HostKey /etc/ssh/ssh_host_ed25519_key");
        builder.AppendLine("HostKey /etc/ssh/ssh_host_rsa_key");
        builder.AppendLine();
        builder.AppendLine("# Cryptographic policy");
        if (!string.IsNullOrWhiteSpace(Ciphers)) builder.AppendLine($"Ciphers {Ciphers}");
        if (!string.IsNullOrWhiteSpace(HostKeyAlgorithms)) builder.AppendLine($"HostKeyAlgorithms {HostKeyAlgorithms}");
        if (!string.IsNullOrWhiteSpace(KexAlgorithms)) builder.AppendLine($"KexAlgorithms {KexAlgorithms}");
        if (!string.IsNullOrWhiteSpace(MACs)) builder.AppendLine($"MACs {MACs}");
        builder.AppendLine();
        builder.AppendLine("# Disable DNS for fast connections");
        builder.AppendLine("UseDNS no");
        builder.AppendLine();
        builder.AppendLine("# Logging");
        builder.AppendLine("LogLevel INFO");
        builder.AppendLine();
        builder.AppendLine("# Subsystem");
        builder.AppendLine("Subsystem sftp internal-sftp");
        builder.AppendLine();
        builder.AppendLine("# Allowed users");
        builder.AppendLine($"AllowUsers {string.Join(" ", AllowUsers)}");
        builder.AppendLine();
        if (PKIandPassword == "true") builder.AppendLine("AuthenticationMethods \"publickey,password\"");
        builder.AppendLine();
        builder.AppendLine("# Match blocks");
        foreach (var matchBlock in MatchBlocks)
        {
            builder.Append(matchBlock);
            builder.AppendLine();
        }

        return builder.ToString();
    }
}