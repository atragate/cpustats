using System;
using System.Diagnostics;
using System.IO;

internal static class InstallDriver
{
    private static int Main()
    {
        string driverLibrary = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "PawnIO", "PawnIOLib.dll");
        Version version;
        if (File.Exists(driverLibrary) &&
            Version.TryParse(FileVersionInfo.GetVersionInfo(driverLibrary).FileVersion, out version) &&
            version.CompareTo(new Version(2, 2, 0)) >= 0)
            return 0;

        string payload = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PawnIO_setup.exe");
        var start = new ProcessStartInfo(payload, "-install -silent")
        {
            UseShellExecute = false,
            WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
            CreateNoWindow = true
        };
        using (Process installer = Process.Start(start))
        {
            installer.WaitForExit();
            return installer.ExitCode == 3010 ? 0 : installer.ExitCode;
        }
    }
}
