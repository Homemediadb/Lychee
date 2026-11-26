using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Lychee.Gui;

public static partial class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0 && FromConsole())
            return Cli.Run(args, Console.Out, Console.Error);
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread())
            );
            _ = new App();
        });
        return 0;
    }

    static bool FromConsole()
    {
        // ATTACH_PARENT_PROCESS
        if (AttachConsole(-1))
            return true;
        if (GetConsoleWindow() != 0)
        {
            if (GetConsoleProcessList(new uint[2], 2) > 1)
                return true;
            FreeConsole();
        }
        // STD_OUTPUT_HANDLE; IsOutputRedirected reads a missing one (a service, WMI) as redirected
        var output = GetStdHandle(-11);
        // FILE_TYPE_DISK, FILE_TYPE_PIPE
        return output is not (0 or -1) && GetFileType(output) is 1 or 3;
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FreeConsole();

    [LibraryImport("kernel32.dll")]
    private static partial nint GetConsoleWindow();

    [LibraryImport("kernel32.dll")]
    private static partial nint GetStdHandle(int which);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetFileType(nint handle);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetConsoleProcessList([Out] uint[] processIds, uint count);
}
