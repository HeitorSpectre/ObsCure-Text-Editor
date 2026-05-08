using System;
using System.Text;
#if WINDOWS
using System.Windows.Forms;
#endif

namespace LngTool;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        if (args.Length > 0)
            return Cli.Run(args);

#if WINDOWS
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
#else
        return Cli.Run(new[] { "--help" });
#endif
    }
}
