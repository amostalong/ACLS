using UnityEditor;
using ACLS.Logging;

namespace ACLS.Editor
{
    public static class LogLevelMenu
    {
        private const string MenuBase = "ACLS/Log Level/";
        private const string PerChannelLabel = "Per-Channel (default)";

        [MenuItem(MenuBase + PerChannelLabel, false, 130)]
        private static void SetGlobalNull()
        {
            Log.SetGlobalLevel(null);
        }

        [MenuItem(MenuBase + PerChannelLabel, true)]
        private static bool SetGlobalNullValidate()
        {
            Menu.SetChecked(MenuBase + PerChannelLabel, Log.GetGlobalLevel() == null);
            return true;
        }

        [MenuItem(MenuBase + "Debug", false, 131)]
        private static void SetGlobalDebug() => Log.SetGlobalLevel(LogLevel.Debug);

        [MenuItem(MenuBase + "Debug", true)]
        private static bool SetGlobalDebugValidate()
        {
            Menu.SetChecked(MenuBase + "Debug", Log.GetGlobalLevel() == LogLevel.Debug);
            return true;
        }

        [MenuItem(MenuBase + "Info", false, 133)]
        private static void SetGlobalInfo() => Log.SetGlobalLevel(LogLevel.Info);

        [MenuItem(MenuBase + "Info", true)]
        private static bool SetGlobalInfoValidate()
        {
            Menu.SetChecked(MenuBase + "Info", Log.GetGlobalLevel() == LogLevel.Info);
            return true;
        }

        [MenuItem(MenuBase + "Warn", false, 134)]
        private static void SetGlobalWarn() => Log.SetGlobalLevel(LogLevel.Warn);

        [MenuItem(MenuBase + "Warn", true)]
        private static bool SetGlobalWarnValidate()
        {
            Menu.SetChecked(MenuBase + "Warn", Log.GetGlobalLevel() == LogLevel.Warn);
            return true;
        }

        [MenuItem(MenuBase + "Error", false, 134)]
        private static void SetGlobalError() => Log.SetGlobalLevel(LogLevel.Error);

        [MenuItem(MenuBase + "Error", true)]
        private static bool SetGlobalErrorValidate()
        {
            Menu.SetChecked(MenuBase + "Error", Log.GetGlobalLevel() == LogLevel.Error);
            return true;
        }
    }
}
