using ES.SFTP.Interop;

namespace ES.SFTP.Security;

public class GroupUtil
{
    public static async Task<bool> GroupExists(string groupNameOrId)
    {
        var command = await ProcessUtil.QuickRun("getent", $"group {groupNameOrId}", false);
        return command.ExitCode == 0 && !string.IsNullOrWhiteSpace(command.Output);
    }

    public static async Task GroupCreate(string name, bool force = false, int? groupId = null,
        bool nonUniqueGroupId = true)
    {
        await ProcessUtil.QuickRun("groupadd",
            $"{(force ? "-f" : string.Empty)} {(groupId != null ? $"-g {groupId} {(nonUniqueGroupId ? "-o" : string.Empty)}" : string.Empty)} {name}");
    }

    public static async Task GroupAddUser(string group, string username)
    {
        await ProcessUtil.QuickRun("usermod", $"-a -G {group} {username}");
    }


    public static async Task GroupRemoveUser(string group, string username)
    {
        await ProcessUtil.QuickRun("usermod", $"-G {group} {username}");
    }

    public static async Task<IReadOnlyList<string>> GroupListUsers(string group)
    {
        var command = await ProcessUtil.QuickRun("members", group, false);
        if (command.ExitCode != 0 && command.ExitCode != 1 && !string.IsNullOrWhiteSpace(command.Output))
            throw new Exception($"Get group members command failed with exit code {command.ExitCode} and message:" +
                                $"{Environment.NewLine}{command.Output}");
        return command.Output.Split(' ', StringSplitOptions.RemoveEmptyEntries).OrderBy(s => s).ToList();
    }

    public static async Task<int> GroupGetId(string groupNameOrId)
    {
        var command = await ProcessUtil.QuickRun("getent", $"group {groupNameOrId}");
        var groupEntryValues = command.Output.Split(":");
        return int.Parse(groupEntryValues[2]);
    }

    public static async Task GroupSetId(string groupNameOrId, int id)
    {
        await ProcessUtil.QuickRun("groupmod", $"-g {id} {groupNameOrId}");
    }
}