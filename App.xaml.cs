using System.Windows;

namespace BiliRemote;

public partial class App : Application
{
    public static bool SelfTest => Environment.GetCommandLineArgs().Contains("--self-test");
}
