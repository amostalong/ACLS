using System;
using System.IO;
using System.Text;
using ACLS.Logging;
using UnityEngine;

namespace ACLS.Authoring
{
    // Captures everything Unity logs (Debug.Log/Warning/Error/Exception, plus
    // any uncaught exceptions Unity routes through Application.logMessage*) to
    // a flat text file under <project>/Logs/acls-runtime.log. The file is
    // truncated at the start of each Play session so each run is self-contained.
    //
    // Self-installs via RuntimeInitializeOnLoadMethod(BeforeSceneLoad) so it
    // catches messages emitted before GameBootstrap.Awake runs.
    public sealed class RuntimeLogger : MonoBehaviour
    {
        private const string LogFileName = "acls-runtime.log";

        public static string LogPath { get; private set; }

        private static readonly object writeLock = new object();
        private static StreamWriter writer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoInstall()
        {
            if (FindObjectOfType<RuntimeLogger>() != null) return;
            var go = new GameObject("[RuntimeLogger]");
            DontDestroyOnLoad(go);
            go.AddComponent<RuntimeLogger>();
        }

        private void Awake()
        {
            try
            {
                string logDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs"));
                Directory.CreateDirectory(logDir);
                LogPath = Path.Combine(logDir, LogFileName);

                // Truncate fresh each Play.
                var stream = new FileStream(LogPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                writer = new StreamWriter(stream, new UTF8Encoding(false))
                {
                    AutoFlush = true,
                    NewLine = "\n",
                };
                writer.WriteLine($"=== ACLS runtime log @ {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                writer.WriteLine($"Unity {Application.unityVersion} / {Application.platform} / {SystemInfo.operatingSystem}");
                writer.WriteLine($"DataPath: {Application.dataPath}");
                writer.WriteLine();

                Application.logMessageReceivedThreaded += OnLog;
                Application.quitting += OnQuit;

                // Surface the path so I can find it from the editor console too.
                Log.Info(Log.Channels.System, "writing to {0}", LogPath);
            }
            catch (Exception ex)
            {
                // Don't cascade: fall back to console-only.
                Log.Error(Log.Channels.System, "init failed: {0}", ex);
                writer = null;
            }
        }

        private void OnDestroy()
        {
            Application.logMessageReceivedThreaded -= OnLog;
            Application.quitting -= OnQuit;
            FlushAndClose();
        }

        private static void OnQuit() => FlushAndClose();

        private static void FlushAndClose()
        {
            lock (writeLock)
            {
                try
                {
                    writer?.Flush();
                    writer?.Dispose();
                }
                catch { /* best-effort */ }
                writer = null;
            }
        }

        private static void OnLog(string message, string stackTrace, LogType type)
        {
            // logMessageReceivedThreaded can fire from worker threads (e.g.
            // HttpClient response in AnthropicClient). Lock around the write.
            if (writer == null) return;
            lock (writeLock)
            {
                if (writer == null) return;
                try
                {
                    writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [{Tag(type)}] {message}");
                    if (type == LogType.Exception || type == LogType.Error)
                    {
                        if (!string.IsNullOrEmpty(stackTrace))
                        {
                            // Indent the stack so it visually folds under the message.
                            foreach (var line in stackTrace.Split('\n'))
                            {
                                if (string.IsNullOrWhiteSpace(line)) continue;
                                writer.Write("    ");
                                writer.WriteLine(line.TrimEnd());
                            }
                        }
                    }
                }
                catch { /* don't recurse */ }
            }
        }

        private static string Tag(LogType t) => t switch
        {
            LogType.Log => "INFO",
            LogType.Warning => "WARN",
            LogType.Error => "ERR ",
            LogType.Assert => "ASRT",
            LogType.Exception => "EXC ",
            _ => "?   ",
        };
    }
}
