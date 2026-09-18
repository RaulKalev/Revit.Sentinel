using System;
using System.IO;
using System.Linq;
using System.Windows;
using Newtonsoft.Json;

namespace Sentinel.UI
{
    /// <summary>Per-user UI preferences (theme, window placement, last page). Project data is never stored here.</summary>
    public class UiPreferences
    {
        public bool IsDarkMode { get; set; } = true;
        public double WindowWidth { get; set; } = 1280;
        public double WindowHeight { get; set; } = 780;
        public double WindowLeft { get; set; } = 80;
        public double WindowTop { get; set; } = 60;
        public string LastPage { get; set; } = "Doors";
        public double InspectorWidth { get; set; } = 380;
    }

    /// <summary>
    /// Swaps the Dark/Light brush dictionary on a window (only that dictionary; shared styles stay merged) and
    /// persists user preferences to %LocalAppData%\RK Tools\Sentinel\ui.json.
    /// </summary>
    public class ThemeManager
    {
        /// <summary>Preferences file. Overridable so test harnesses never touch the user's real preferences.</summary>
        public static string PrefsPath { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RK Tools", "Sentinel", "ui.json");

        private readonly Window _window;
        private ResourceDictionary _themeDictionary;

        public UiPreferences Preferences { get; private set; } = new UiPreferences();
        public bool IsDarkMode => Preferences.IsDarkMode;

        public event EventHandler ThemeChanged;

        public ThemeManager(Window window)
        {
            _window = window;
            Load();
        }

        public void ToggleTheme()
        {
            Preferences.IsDarkMode = !Preferences.IsDarkMode;
            ApplyTheme();
            Save();
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ApplyTheme() => ApplyTheme(_window.Resources);

        /// <summary>Applies the current theme to any resource dictionary (also used by dialogs).</summary>
        public void ApplyTheme(ResourceDictionary target)
        {
            var themeUri = new Uri(Preferences.IsDarkMode
                ? "pack://application:,,,/Sentinel;component/UI/Themes/DarkTheme.xaml"
                : "pack://application:,,,/Sentinel;component/UI/Themes/LightTheme.xaml", UriKind.Absolute);
            try
            {
                var dict = new ResourceDictionary { Source = themeUri };
                var old = target.MergedDictionaries.FirstOrDefault(d =>
                    d == _themeDictionary || (d.Source != null && d.Source.OriginalString.Contains("/UI/Themes/") &&
                                              (d.Source.OriginalString.EndsWith("DarkTheme.xaml") || d.Source.OriginalString.EndsWith("LightTheme.xaml"))));
                if (old != null) target.MergedDictionaries.Remove(old);
                target.MergedDictionaries.Insert(0, dict);
                if (target == _window.Resources) _themeDictionary = dict;
            }
            catch (Exception ex)
            {
                Infrastructure.SentinelLog.Error("Applying theme failed", ex);
            }
        }

        public void CaptureWindowPlacement()
        {
            if (_window.WindowState != WindowState.Normal) return;
            Preferences.WindowWidth = _window.Width;
            Preferences.WindowHeight = _window.Height;
            Preferences.WindowLeft = _window.Left;
            Preferences.WindowTop = _window.Top;
        }

        public void Load()
        {
            try
            {
                if (!File.Exists(PrefsPath)) return;
                var prefs = JsonConvert.DeserializeObject<UiPreferences>(File.ReadAllText(PrefsPath));
                if (prefs != null) Preferences = prefs;
            }
            catch
            {
                Preferences = new UiPreferences();
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PrefsPath));
                File.WriteAllText(PrefsPath, JsonConvert.SerializeObject(Preferences, Formatting.Indented));
            }
            catch
            {
                // preferences are best effort
            }
        }
    }
}
