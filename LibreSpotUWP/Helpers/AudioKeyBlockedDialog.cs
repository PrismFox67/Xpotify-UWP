using LibreSpotUWP.Services;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace LibreSpotUWP.Helpers
{
    /// <summary>
    /// Explains Spotify's audio-key block (librespot issue #1649): some accounts,
    /// mostly ones created from 2024 on, are refused the keys needed to play music
    /// in any librespot-based app. Shown at most once per app session.
    /// </summary>
    public static class AudioKeyBlockedDialog
    {
        public const string MoreInfoUrl = "https://github.com/librespot-org/librespot/issues/1649";

        private static int _shown;

        public static void ShowOnce()
        {
            if (Interlocked.Exchange(ref _shown, 1) != 0)
                return;

            try
            {
                var dispatcher = CoreApplication.MainView?.CoreWindow?.Dispatcher;
                if (dispatcher == null)
                    return;

                _ = dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => _ = ShowCoreAsync());
            }
            catch (Exception ex)
            {
                LogService.Warn($"[AudioKeyBlockedDialog.ShowOnce] Unable to show dialog: {ex.Message}");
            }
        }

        private static async Task ShowCoreAsync()
        {
            try
            {
                var content = new StackPanel();
                content.Children.Add(new TextBlock
                {
                    Text = "Spotify refused to send the key needed to play this song. If this happens for every song, Spotify is blocking playback for this account and Xpotify can't play music with it.",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 12)
                });
                content.Children.Add(new TextBlock
                {
                    Text = "This is a change on Spotify's side that affects every app built on librespot, mostly for accounts created in 2024 or later. It isn't caused by your console or settings, and there's no known fix yet. You can still browse your library.",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 12)
                });

                var linkButton = new HyperlinkButton
                {
                    Content = "More information",
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                linkButton.Click += async (sender, args) =>
                    await Launcher.LaunchUriAsync(new Uri(MoreInfoUrl));
                content.Children.Add(linkButton);

                var dialog = new ContentDialog
                {
                    Title = "Spotify blocked this song",
                    Content = content,
                    PrimaryButtonText = "Close"
                };

                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                // Another ContentDialog may already be open; the status message still records the cause.
                LogService.Warn($"[AudioKeyBlockedDialog.ShowCoreAsync] Unable to show dialog: {ex.Message}");
            }
        }
    }
}
