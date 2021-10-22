namespace ES.SFTP.Extensions;

public static class DirectoryInfoExtensions
{
    public static bool IsDescendentOf(this DirectoryInfo directory, DirectoryInfo parent)
    {
        if (parent == null) return false;
        if (directory.Parent == null) return false;
        if (directory.Parent.FullName == parent.FullName) return true;
        return directory.Parent.IsDescendentOf(parent);
    }
}