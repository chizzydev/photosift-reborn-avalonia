using Avalonia;

namespace PhotoSiftReborn;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--self-test")
                return SelfTests.Run();
            if (args.Length == 1 && args[0] == "--self-test-r30-r2")
                return SelfTests.RunR30R2Development();
            if (args.Length == 1 && args[0] == "--self-test-r30-r3")
                return SelfTests.RunR30R3Development();
            if (args.Length == 1 && args[0] == "--create-generated-native-recovery-history")
            {
                Console.WriteLine("PHOTOSIFT_GENERATED_NATIVE_HISTORY_STATE=" +
                    GeneratedNativeRecoveryHistoryLab.Create());
                return 0;
            }
            return BuildApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("PHOTOSIFT_REBORN_FATAL=" + ex);
            return 3;
        }
    }

    private static AppBuilder BuildApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
