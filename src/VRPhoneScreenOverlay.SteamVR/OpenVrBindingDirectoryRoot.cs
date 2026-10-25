namespace VRPhoneScreenOverlay.SteamVR;

/// <summary>Resolves only the user-selected binding directory, never binding-file links.</summary>
internal static class OpenVrBindingDirectoryRoot
{
    private const int _maximumRedirects = 32;

    internal static string Resolve(string configuredDirectory)
    {
        string pending = Normalize(configuredDirectory);
        int redirects = 0;
        while (true)
        {
            string current = Path.GetPathRoot(pending)!;
            string[] components = pending[current.Length..].Split(Path.DirectorySeparatorChar,
                StringSplitOptions.RemoveEmptyEntries);
            bool redirected = false;
            for (int index = 0; index < components.Length; index++)
            {
                current = Path.Combine(current, components[index]);
                FileAttributes attributes;
                try { attributes = File.GetAttributes(current); }
                catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
                {
                    // A first run may not have steamvr/input yet. Existing ancestors
                    // have already been resolved; retain only the missing suffix.
                    current = Append(current, components, index + 1);
                    OpenVrBindingDefaults.AssertNoReparsePoints(current);
                    return current;
                }
                if ((attributes & FileAttributes.Directory) == 0)
                { throw new InvalidDataException("Binding directory contains a non-directory component."); }
                if ((attributes & FileAttributes.ReparsePoint) == 0) { continue; }
                if (++redirects > _maximumRedirects)
                { throw new InvalidDataException("Binding directory redirection exceeded its limit."); }
                FileSystemInfo target = new DirectoryInfo(current).ResolveLinkTarget(returnFinalTarget: false)
                    ?? throw new InvalidDataException("Binding directory contains an unsupported reparse point.");
                string resolved = Normalize(target.FullName);
                try
                {
                    if ((File.GetAttributes(resolved) & FileAttributes.Directory) == 0)
                    { throw new InvalidDataException("Binding directory target is not a directory."); }
                }
                catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
                { throw new InvalidDataException("Binding directory target is unavailable.", error); }
                pending = Append(resolved, components, index + 1);
                redirected = true;
                break;
            }
            if (redirected) { continue; }
            // Use this physical path throughout the transaction and its rollback.
            // Retargeting the original alias cannot redirect a later file write.
            OpenVrBindingDefaults.AssertNoReparsePoints(current);
            return current;
        }
    }

    internal static string Normalize(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) { path = @"\\" + path[8..]; }
        else if (path.StartsWith(@"\\?\", StringComparison.Ordinal) && path.Length >= 7 &&
            char.IsAsciiLetter(path[4]) && path[5] == ':' && path[6] == '\\') { path = path[4..]; }
        else if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal))
        { throw new InvalidDataException("Binding directory uses an unsupported device path."); }
        // Ordinary UNC roots retain their previous support and filesystem errors.
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static string Append(string root, string[] components, int start)
    {
        for (int index = start; index < components.Length; index++) { root = Path.Combine(root, components[index]); }
        return root;
    }
}
