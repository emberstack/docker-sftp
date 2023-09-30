using ES.SFTP.Interop;

namespace ES.SFTP.Security;

public class UserUtil
{
    public static async Task<bool> UserExists(string username)
    {
        var command = await ProcessUtil.QuickRun("getent", $"passwd {username}", false);
        return command.ExitCode == 0 && !string.IsNullOrWhiteSpace(command.Output);
    }

    public static async Task UserCreate(string username, bool noLoginShell = false, int? gid = null)
    {
        await ProcessUtil.QuickRun("useradd",
            $"--comment {username} {(noLoginShell ? "-s /usr/sbin/nologin " : string.Empty)}{(gid.HasValue ? "-g " + gid.Value + " " : string.Empty)}{username}");
    }

    public static async Task UserDelete(string username, bool throwOnError = true)
    {
        await ProcessUtil.QuickRun("userdel", username, throwOnError);
    }

    public static async Task UserSetId(string username, int id, bool nonUnique = true)
    {
        await ProcessUtil.QuickRun("pkill", $"-U {await UserGetId(username)}", false);
        await ProcessUtil.QuickRun("usermod",
            $"{(nonUnique ? "--non-unique" : string.Empty)} --uid {id} {username}");
    }

    public static async Task UserSetPassword(string username, string password, bool passwordIsEncrypted)
    {
        if (string.IsNullOrEmpty(password))
            await ProcessUtil.QuickRun("usermod", $"-p \"*\" {username}");
        else
            await ProcessUtil.QuickRun("bash",
                $"-c \"echo '{username}:{password}' | chpasswd {(passwordIsEncrypted ? "-e" : string.Empty)}\"");
    }

    public static async Task<int> UserGetId(string username)
    {
        var command = await ProcessUtil.QuickRun("id", $"-u {username}");
        return int.Parse(command.Output);
    }
}