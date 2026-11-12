using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;

internal static class Setup
{
    [STAThread]
    private static int Main()
    {
        try
        {
            using (var mutex = new Mutex(false, @"Global\CPUSTATS.Setup"))
            {
                try
                {
                    if (!mutex.WaitOne(0))
                    {
                        MessageBox.Show("CPUSTATS Setup is already running.", "CPUSTATS Setup");
                        return 1618;
                    }
                }
                catch (AbandonedMutexException) { }

                try { return RunInstaller(); }
                finally { mutex.ReleaseMutex(); }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "CPUSTATS Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1603;
        }
    }

    private static int RunInstaller()
    {
        string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "CPUSTATS Setup Cache");
        Directory.CreateDirectory(cache);
        if ((File.GetAttributes(cache) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Setup cannot use a redirected installer cache.");

        string version = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion;
        string package = Path.Combine(cache, "CPUSTATS-" + version + ".msi");
        using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("CPUSTATS.msi"))
        using (var sha = SHA256.Create())
        {
            if (!File.Exists(package))
            {
                string temporary = Path.Combine(cache, Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    using (var output = new FileStream(temporary, FileMode.CreateNew))
                        source.CopyTo(output);
                    File.Move(temporary, package);
                }
                finally { File.Delete(temporary); }
                source.Position = 0;
            }

            using (var cached = File.OpenRead(package))
            {
                if (!StructuralEquals(sha.ComputeHash(source), sha.ComputeHash(cached)))
                    throw new IOException("The cached installer differs from this copy of Setup.");

                var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "msiexec.exe"),
                    "/i \"" + package + "\"") { UseShellExecute = false, WorkingDirectory = cache };
                using (Process installer = Process.Start(start))
                {
                    installer.WaitForExit();
                    return installer.ExitCode;
                }
            }
        }
    }

    private static bool StructuralEquals(byte[] first, byte[] second)
    {
        return System.Collections.StructuralComparisons.StructuralEqualityComparer.Equals(first, second);
    }
}
