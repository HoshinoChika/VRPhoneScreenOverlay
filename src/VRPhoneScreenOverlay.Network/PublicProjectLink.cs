using System.Diagnostics;

namespace VRPhoneScreenOverlay.Network;

public static class PublicProjectLink
{
    public static bool TryOpen()
    {
        Uri? repository = ClientServiceConfiguration.LoadDefault().ProjectRepositoryUri;
        if (repository is null) { return false; }
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo(repository.AbsoluteUri) { UseShellExecute = true });
            return process is not null;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}
