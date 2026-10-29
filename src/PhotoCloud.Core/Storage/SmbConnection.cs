using System.Runtime.InteropServices;
using PhotoCloud.Core.Configuration;

namespace PhotoCloud.Core.Storage;

public static class SmbConnection
{
    private const int ErrorSessionCredentialConflict = 1219;
    private const int ErrorAlreadyAssigned = 85;
    private const int ResourceTypeDisk = 1;

    /// <summary>Authenticates against the share (\\host\share) with the configured credentials.</summary>
    public static void Connect(CloudSettings settings)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Der SMB-Zugriff ist nur unter Windows implementiert.");
        }

        var resource = new NetResource { dwType = ResourceTypeDisk, lpRemoteName = settings.SharePath };
        var result = WNetAddConnection2(ref resource, settings.Password, settings.User, 0);

        if (result == ErrorSessionCredentialConflict)
        {
            throw new IOException(
                $"Für '{settings.SharePath}' besteht bereits eine SMB-Verbindung mit anderen Zugangsdaten. " +
                "Schließe die bestehende Verbindung oder verwende dieselben Zugangsdaten.",
                new System.ComponentModel.Win32Exception(ErrorSessionCredentialConflict));
        }

        // An existing mapping to this resource is fine; subsequent file access verifies access.
        if (result != 0 && result != ErrorAlreadyAssigned)
        {
            throw new IOException(
                $"Anmeldung an '{settings.SharePath}' fehlgeschlagen (Win32-Fehler {result}).",
                new System.ComponentModel.Win32Exception(result));
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NetResource
    {
        public int dwScope;
        public int dwType;
        public int dwDisplayType;
        public int dwUsage;
        public string? lpLocalName;
        public string lpRemoteName;
        public string? lpComment;
        public string? lpProvider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(ref NetResource resource, string password, string username, int flags);
}
