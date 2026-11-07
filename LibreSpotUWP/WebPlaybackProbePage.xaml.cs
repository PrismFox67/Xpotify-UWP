using LibreSpotUWP.Helpers;
using LibreSpotUWP.Services;
using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using Windows.ApplicationModel;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace LibreSpotUWP
{
    /// <summary>
    /// XPOTIFY FORK: feasibility test for accounts that Spotify refuses audio keys to
    /// (librespot #1649). Loads Spotify's official Web Playback SDK in WebView2 so the
    /// browser's own DRM handles decryption, plays one song, and logs whether DRM is
    /// available, whether the SDK connects, and whether playback keeps going in the
    /// background. Every line the page reports is written to log.txt with a [WebProbe] tag.
    /// </summary>
    public sealed partial class WebPlaybackProbePage : Page
    {
        private const string ProbeHost = "probe.xpotify.example";
        private bool _released;

        public WebPlaybackProbePage()
        {
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            LogService.Info("[WebProbe] Opening web player test page.");

            try
            {
                await ProbeView.EnsureCoreWebView2Async();
                if (_released) { return; }

                var core = ProbeView.CoreWebView2;
                if (core == null)
                {
                    SetStatus("The WebView2 runtime is not available on this console.");
                    LogService.Warn("[WebProbe] CoreWebView2 is null.");
                    return;
                }

                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsStatusBarEnabled = false;

                // A virtual https host gives the page a secure context, which EME (DRM) requires.
                var folder = Path.Combine(Package.Current.InstalledLocation.Path, "Assets", "WebProbe");
                core.SetVirtualHostNameToFolderMapping(ProbeHost, folder, CoreWebView2HostResourceAccessKind.Allow);

                core.WebMessageReceived += Core_WebMessageReceived;
                core.NavigationCompleted += (s, args) =>
                {
                    LogService.Info($"[WebProbe] Page loaded success={args.IsSuccess} status={args.WebErrorStatus}.");
                    if (!args.IsSuccess) { SetStatus($"The test page failed to load ({args.WebErrorStatus}). The test failed."); }
                };
                core.ProcessFailed += (s, args) =>
                {
                    LogService.Warn($"[WebProbe] Browser process failed: {args.ProcessFailedKind}.");
                    _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
                        () => SetStatus($"The browser crashed ({args.ProcessFailedKind}). The test failed."));
                };

                XboxBackgroundMemory.LogUsage("web player test started");
                SetStatus("Running test. Results are also written to the app log.");
                core.Navigate($"https://{ProbeHost}/probe.html");
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "[WebProbe] Failed to start");
                SetStatus("The test could not start: " + ex.Message);
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            Release();
        }

        private async void Core_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            try
            {
                if (!JsonObject.TryParse(args.TryGetWebMessageAsString(), out var msg)) { return; }
                var type = msg.GetNamedString("type", "");

                if (type == "log")
                {
                    var kind = msg.GetNamedString("kind", "");
                    var text = msg.GetNamedString("text", "");
                    LogService.Info($"[WebProbe] {kind} {text}");
                    if (kind == "VERDICT") { SetStatus(text); }
                }
                else if (type == "needToken")
                {
                    // The Web API token from the PKCE sign-in already carries the 'streaming' scope.
                    var token = await App.SpotifyAuth.EnsureValidAccessTokenAsync();
                    if (_released || ProbeView.CoreWebView2 == null) { return; }
                    if (string.IsNullOrEmpty(token))
                    {
                        LogService.Warn("[WebProbe] No access token available.");
                        SetStatus("Not signed in, so the web player cannot start.");
                        return;
                    }

                    var reply = new JsonObject
                    {
                        ["type"] = JsonValue.CreateStringValue("token"),
                        ["token"] = JsonValue.CreateStringValue(token)
                    };
                    ProbeView.CoreWebView2.PostWebMessageAsString(reply.Stringify());
                }
            }
            catch (Exception ex)
            {
                LogService.Warn($"[WebProbe] Message handling failed: {ex.Message}");
            }
        }

        private void SetStatus(string text)
        {
            StatusText.Text = text;
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (Frame != null && Frame.CanGoBack) { Frame.GoBack(); }
        }

        private void Release()
        {
            if (_released) { return; }
            _released = true;
            try
            {
                if (ProbeView.CoreWebView2 != null)
                {
                    ProbeView.CoreWebView2.WebMessageReceived -= Core_WebMessageReceived;
                }
                ProbeView.Close();
            }
            catch (Exception ex)
            {
                LogService.Warn($"[WebProbe] Release failed: {ex.Message}");
            }
            LogService.Info("[WebProbe] Test page closed.");
        }
    }
}
