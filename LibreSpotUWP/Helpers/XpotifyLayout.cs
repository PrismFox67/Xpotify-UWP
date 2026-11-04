using Windows.Storage;

namespace LibreSpotUWP.Helpers
{
    /// <summary>
    /// XPOTIFY FORK: the Spotify-desktop-style layout (Views/Xpotify, Themes/Xpotify.xaml).
    ///
    /// On by default. While enabled, the "Shell" page is XpotifyShellPage and content pages
    /// come from the Win11 set (they reach the shell only through IAppShell). Choosing one of
    /// the original appearances in Settings turns it off, so upstream's layouts stay intact.
    /// </summary>
    public static class XpotifyLayout
    {
        private const string Key = "XpotifyLayoutEnabled";

        public const string SettingsTag = "xpotify";

        public static bool IsEnabled
        {
            get
            {
                return !ApplicationData.Current.LocalSettings.Values.TryGetValue(Key, out object value) ||
                    !(value is bool enabled) ||
                    enabled;
            }
            set
            {
                ApplicationData.Current.LocalSettings.Values[Key] = value;
            }
        }
    }
}
