using System;
using System.IO;

namespace Sentinel.Infrastructure
{
    /// <summary>
    /// Minimal best-effort file logger (same style as the other RK Tools plugins).
    /// Writes to %LocalAppData%\RK Tools\Sentinel\sentinel.log and never throws.
    /// </summary>
    internal static class SentinelLog
    {
        private static readonly object Lock = new object();

        public static string LogFilePath
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RK Tools", "Sentinel");
                return Path.Combine(dir, "sentinel.log");
            }
        }

        public static void Info(string message) => Write("INFO ", message);
        public static void Warn(string message) => Write("WARN ", message);

        public static void Error(string message, Exception ex = null) =>
            Write("ERROR", ex == null ? message : message + " :: " + ex.GetType().Name + ": " + ex.Message + Environment.NewLine + ex.StackTrace);

        private static void Write(string level, string message)
        {
            try
            {
                lock (Lock)
                {
                    var path = LogFilePath;
                    var dir = Path.GetDirectoryName(path);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    var fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > 4 * 1024 * 1024) fi.Delete();

                    File.AppendAllText(path,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] " + message + Environment.NewLine);
                }
            }
            catch
            {
                // logging must never break the plugin
            }
        }
    }
}
