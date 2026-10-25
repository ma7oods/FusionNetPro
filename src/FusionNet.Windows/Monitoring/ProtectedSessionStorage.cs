using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace FusionNet.Windows.Monitoring;

internal sealed class ProtectedSessionStorage(Guid id, string userSid)
{
    public string StateDirectory => DirectoryPath(id);
    internal static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FusionNetPro.GameBoost");
    internal static string DirectoryPath(Guid sessionId) => Path.Combine(Root, sessionId.ToString("N"));
    public void Create()
    {
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        DirectorySecurity Security(SecurityIdentifier reader)
        {
            var acl = new DirectorySecurity();
            acl.SetOwner(admins);
            acl.SetAccessRuleProtection(true, false);
            foreach (var sid in new[] { admins, system })
                acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            acl.AddAccessRule(new FileSystemAccessRule(reader, FileSystemRights.ReadAndExecute, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            return acl;
        }
        var root = new DirectoryInfo(Path.GetDirectoryName(StateDirectory)!);
        if (!root.Exists) root.Create(Security(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null)));
        if ((root.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Recovery directory cannot be a link.");
        var existing = root.GetAccessControl();
        var owner = existing.GetOwner(typeof(SecurityIdentifier));
        if (!admins.Equals(owner) && !system.Equals(owner)) throw new IOException("Untrusted recovery owner.");
        var unsafeRights = FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        foreach (FileSystemAccessRule rule in existing.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if (rule.AccessControlType == AccessControlType.Allow && !admins.Equals(rule.IdentityReference) && !system.Equals(rule.IdentityReference) && (rule.FileSystemRights & unsafeRights) != 0)
                throw new IOException("Untrusted writable recovery directory.");
        var directory = new DirectoryInfo(StateDirectory);
        if (directory.Exists) throw new IOException("Session directory already exists.");
        directory.Create(Security(new SecurityIdentifier(userSid)));
    }

    public FileStream OpenLock()
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return new FileStream(Path.Combine(StateDirectory, "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 60) { Thread.Sleep(1000); }
        }
    }

    internal void ValidateExisting()
    {
        foreach (var path in new[] { Root, StateDirectory })
        {
            var directory = new DirectoryInfo(path);
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Recovery state cannot be a link.");
            var acl = directory.GetAccessControl();
            var owner = acl.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier ?? throw new IOException("Recovery owner is unavailable.");
            if (owner.Value is not ("S-1-5-32-544" or "S-1-5-18")) throw new IOException("Untrusted recovery state owner.");
            var writes = FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
            foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                if (rule.AccessControlType == AccessControlType.Allow && ((SecurityIdentifier)rule.IdentityReference).Value is not ("S-1-5-32-544" or "S-1-5-18") && (rule.FileSystemRights & writes) != 0)
                    throw new IOException("Recovery state has untrusted write permissions.");
        }
    }

    public void Write(string file, string text, bool createOnly = false)
    {
        var destination = Path.Combine(StateDirectory, file);
        var temporary = createOnly ? destination : destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { stream.Write(Encoding.UTF8.GetBytes(text)); stream.Flush(true); }
        if (!createOnly) File.Move(temporary, destination, true);
    }

}
