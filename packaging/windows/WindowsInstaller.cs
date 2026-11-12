using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;

internal static class WindowsInstaller
{
    private const string UpgradeCode = "{80F8FB60-E445-4AEF-9689-CBD4E62309B2}";
    private const string SensorService = "CPUSTATSSensors";
    public static readonly string AppPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "CPUSTATS", "CPUSTATS.exe");

    public static bool Installed { get { return MsiQueryProductState(BuildVersion.ProductCode) == 5; } }
    public static bool HasRelatedProduct { get { return MsiEnumRelatedProducts(UpgradeCode, 0, 0, new StringBuilder(39)) == 0; } }

    public static int Run(string package, string operation, InstallerMessage handler)
    {
        bool stopped = false;
        uint result = 1603;
        using (var service = new ServiceController(SensorService))
        {
            try
            {
                try
                {
                    if (service.Status != ServiceControllerStatus.Stopped)
                    {
                        if (service.Status != ServiceControllerStatus.StopPending) service.Stop();
                        stopped = true;
                        service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
                    }
                }
                catch (InvalidOperationException ex)
                {
                    var error = ex.InnerException as System.ComponentModel.Win32Exception;
                    if (error == null || error.NativeErrorCode != 1060) throw;
                }

                MsiSetInternalUI(2, IntPtr.Zero);
                MsiSetExternalUI(handler, 0x07001fff, IntPtr.Zero);
                try
                {
                    string options = "REBOOT=ReallySuppress MSIRESTARTMANAGERCONTROL=Disable " + operation;
                    result = MsiInstallProduct(package, options);
                    return (int)result;
                }
                finally { MsiSetExternalUI(null, 0, IntPtr.Zero); }
            }
            finally
            {
                if (stopped && result != 0 && result != 3010)
                {
                    try
                    {
                        service.Refresh();
                        if (service.Status == ServiceControllerStatus.Stopped) service.Start();
                    }
                    catch (InvalidOperationException) { }
                }
            }
        }
    }

    public static void LaunchDesktop()
    {
        string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        Process.Start(new ProcessStartInfo(explorer, "\"" + AppPath + "\"") { UseShellExecute = false });
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    internal delegate int InstallerMessage(IntPtr context, uint messageType, [MarshalAs(UnmanagedType.LPWStr)] string message);
    [DllImport("msi.dll")] private static extern uint MsiSetInternalUI(uint level, IntPtr owner);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern IntPtr MsiSetExternalUI(InstallerMessage callback, uint filter, IntPtr context);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern uint MsiInstallProduct(string package, string properties);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern int MsiQueryProductState(string product);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern uint MsiEnumRelatedProducts(string upgradeCode, uint reserved, uint index, StringBuilder productCode);
}
