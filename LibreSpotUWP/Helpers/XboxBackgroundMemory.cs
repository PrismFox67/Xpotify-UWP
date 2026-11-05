using System;
using LibreSpotUWP.Services;
using Windows.ApplicationModel;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace LibreSpotUWP.Helpers
{
    /// <summary>
    /// XPOTIFY FORK: keeps music playing when the app is sent to the background on Xbox.
    ///
    /// Background audio itself is already handled upstream (backgroundMediaPlayback plus a
    /// silent MediaPlayer that sponsors the session, see MediaService.PrepareForSuspendingAsync).
    /// What Xbox adds is a much lower memory limit for background apps; an app over it is
    /// suspended or terminated and the music stops. This follows Microsoft's "reduce memory
    /// usage when your app moves to the background" pattern: drop the XAML tree when entering
    /// the background and rebuild it on return. Services are static on App, so playback,
    /// SMTC and the librespot session are untouched.
    ///
    /// No-op off Xbox unless built with XPOTIFY_FORCE_BG_UNLOAD (for testing on a PC).
    /// </summary>
    public static class XboxBackgroundMemory
    {
        private static bool _attached;
        private static bool _inBackground;
        private static bool _uiUnloaded;
        private static Type _unloadedPageType;
        private static readonly System.Collections.Generic.List<WeakReference<object>> _droppedPages =
            new System.Collections.Generic.List<WeakReference<object>>();

        public static bool IsEnabled
        {
            get
            {
#if XPOTIFY_FORCE_BG_UNLOAD
                return true;
#else
                return OSHelper.IsXboxFamily;
#endif
            }
        }

        public static void Attach(Application app)
        {
            if (_attached || !IsEnabled) { return; }
            _attached = true;

            try
            {
                app.EnteredBackground += OnEnteredBackground;
                app.LeavingBackground += OnLeavingBackground;
                MemoryManager.AppMemoryUsageLimitChanging += OnMemoryLimitChanging;
                MemoryManager.AppMemoryUsageIncreased += OnMemoryUsageIncreased;
                Log("attached");
            }
            catch (Exception ex)
            {
                LogService.Warn("[XboxBackgroundMemory] attach failed - " + ex.Message);
            }
        }

        private static void OnEnteredBackground(object sender, EnteredBackgroundEventArgs e)
        {
            var deferral = e.GetDeferral();
            try
            {
                _inBackground = true;
                Log("entered background");

                var page = CurrentPage();
                if (page != null && ShouldUnloadProactively(page.GetType()))
                {
                    Unload("entered background");
                }
            }
            catch (Exception ex)
            {
                LogService.Warn("[XboxBackgroundMemory] background transition failed - " + ex.Message);
            }
            finally
            {
                deferral.Complete();
            }
        }

        private static void OnLeavingBackground(object sender, LeavingBackgroundEventArgs e)
        {
            var deferral = e.GetDeferral();
            try
            {
                _inBackground = false;
                Log("leaving background");
                Restore();
            }
            catch (Exception ex)
            {
                LogService.Warn("[XboxBackgroundMemory] foreground restore failed - " + ex.Message);
            }
            finally
            {
                deferral.Complete();
            }
        }

        private static void OnMemoryLimitChanging(object sender, AppMemoryUsageLimitChangingEventArgs e)
        {
            Log($"limit changing {Mb(e.OldLimit)} -> {Mb(e.NewLimit)}");
            if (_inBackground && MemoryManager.AppMemoryUsage >= e.NewLimit)
            {
                RunOnUi(() => Unload("over new background limit"));
            }
        }

        private static void OnMemoryUsageIncreased(object sender, object e)
        {
            var level = MemoryManager.AppMemoryUsageLevel;
            if (_inBackground &&
                (level == AppMemoryUsageLevel.OverLimit || level == AppMemoryUsageLevel.High))
            {
                Log("usage level " + level);
                RunOnUi(() => Unload("usage level " + level));
            }
        }

        private static void Unload(string reason)
        {
            var window = Window.Current;
            if (window == null || window.Content == null) { return; }

            _unloadedPageType = CurrentPage()?.GetType();
            if (CurrentPage() != null) { _droppedPages.Add(new WeakReference<object>(CurrentPage())); }
            _uiUnloaded = true;

            var before = MemoryManager.AppMemoryUsage;
            ReleaseTree(window.Content as Frame);
            window.Content = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Log($"UI unloaded ({reason}); usage {Mb(before)} -> {Mb(MemoryManager.AppMemoryUsage)}");

            // XAML frees the visual tree on a later frame, so the immediate figure is
            // misleading. Log again once it has settled.
            _ = LogSettledUsageAsync();
        }

        // Clearing Window.Content alone leaves the tree alive if anything still references a
        // page, so break the links first: frame history, the page cache and the visual tree.
        private static void ReleaseTree(Frame rootFrame)
        {
            if (rootFrame == null) { return; }

            try
            {
                (rootFrame.Content as Views.Xpotify.XpotifyShellPage)?.ReleaseContentForUnload();
                ClearFrame(rootFrame);
                Windows.UI.Xaml.Media.VisualTreeHelper.DisconnectChildrenRecursive(rootFrame);
            }
            catch (Exception ex)
            {
                LogService.Warn("[XboxBackgroundMemory] tree release failed - " + ex.Message);
            }
        }

        public static void ClearFrame(Frame frame)
        {
            frame.BackStack.Clear();
            frame.ForwardStack.Clear();
            var cacheSize = frame.CacheSize;
            frame.CacheSize = 0;
            frame.CacheSize = cacheSize;
        }

        private static async System.Threading.Tasks.Task LogSettledUsageAsync()
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(3000);
                LogDroppedPages("settled after unload");
                // XAML can release native peers on a later frame, so check again before
                // concluding that a page is being held.
                await System.Threading.Tasks.Task.Delay(27000);
                LogDroppedPages("30s after unload");
            }
            catch (Exception ex)
            {
                LogService.Warn("[XboxBackgroundMemory] settled usage log failed - " + ex.Message);
            }
        }

        private static void LogDroppedPages(string stage)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var alive = 0;
            foreach (var page in _droppedPages)
            {
                if (page.TryGetTarget(out _)) { alive++; }
            }

            if (_uiUnloaded) { Log($"{stage}; dropped pages alive={alive}/{_droppedPages.Count}"); }
        }

        private static void Restore()
        {
            // LeavingBackground also fires on a cold start, before OnLaunched has created
            // the root frame. Only rebuild what this class removed.
            if (!_uiUnloaded) { return; }

            var window = Window.Current;
            if (window == null) { return; }
            _uiUnloaded = false;
            if (window.Content != null) { return; }

            var pageType = RestorePageType(_unloadedPageType);
            _unloadedPageType = null;

            var frame = new Frame();
            frame.NavigationFailed += (s, args) =>
                LogService.Warn("[XboxBackgroundMemory] restore navigation failed - " + args.Exception?.Message);
            window.Content = frame;
            frame.Navigate(pageType);

            Log("UI restored to " + pageType.Name);
        }

        private static object CurrentPage()
        {
            return (Window.Current?.Content as Frame)?.Content;
        }

        private static bool IsShellPage(Type type)
        {
            return type == typeof(MainPage) ||
                type == typeof(Views.Win11.ShellPage) ||
                type == typeof(Views.Xpotify.XpotifyShellPage);
        }

        private static bool ShouldUnloadProactively(Type type)
        {
#if XPOTIFY_FORCE_BG_UNLOAD
            // PC test builds exercise the unload/restore cycle from any page, including
            // sign-in, so it can be tested without an account.
            return true;
#else
            // Sign-in pages hold a live WebView2 flow; they are only dropped if over the limit.
            return IsShellPage(type);
#endif
        }

        private static Type RestorePageType(Type unloaded)
        {
            if (unloaded != null && (IsShellPage(unloaded) || unloaded == typeof(OobePage)))
            {
                return unloaded;
            }

            // Dropped from a transient page (e.g. XboxLoginPage): land where OnLaunched would.
            // Same test as App.IsCurrentUserSignedIn.
            var auth = App.SpotifyAuth?.Current;
            var signedIn = auth != null &&
                !string.IsNullOrEmpty(auth.AccessToken) &&
                (!auth.IsExpired || !ConnectivityHelper.HasInternetAccess());
            return signedIn ? NavigationHelper.GetPageType("Shell") : typeof(OobePage);
        }

        private static void RunOnUi(Action action)
        {
            try
            {
                var dispatcher = Window.Current?.Dispatcher
                    ?? Windows.ApplicationModel.Core.CoreApplication.MainView?.CoreWindow?.Dispatcher;
                if (dispatcher == null) { return; }

                if (dispatcher.HasThreadAccess)
                {
                    action();
                }
                else
                {
                    _ = dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.High, () => action());
                }
            }
            catch (Exception ex)
            {
                LogService.Warn("[XboxBackgroundMemory] UI dispatch failed - " + ex.Message);
            }
        }

        private static void Log(string message)
        {
            LogService.Info(
                $"[XboxBackgroundMemory] {message}. usage={Mb(MemoryManager.AppMemoryUsage)} " +
                $"managed={Mb((ulong)GC.GetTotalMemory(false))} " +
                $"limit={Mb(MemoryManager.AppMemoryUsageLimit)} level={MemoryManager.AppMemoryUsageLevel}");
        }

        private static string Mb(ulong bytes)
        {
            return (bytes / (1024.0 * 1024.0)).ToString("F1") + "MB";
        }
    }
}
