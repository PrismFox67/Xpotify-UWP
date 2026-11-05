using LibreSpotUWP.Controls;
using LibreSpotUWP.Helpers;
using LibreSpotUWP.Interfaces;
using LibreSpotUWP.Models;
using LibreSpotUWP.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Networking.Connectivity;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace LibreSpotUWP.Views.Xpotify
{
    /// <summary>Row in the "Your Library" sidebar.</summary>
    public sealed class LibraryPlaylistItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Subtitle { get; set; }
        public ImageSource Image { get; set; }
        public SpotifyAPI.Web.FullPlaylist Playlist { get; set; }
    }

    /// <summary>
    /// XPOTIFY FORK: Spotify-desktop-style shell. Navigation, history, cache status and
    /// recovery behaviour are ported from Views/Win11/ShellPage; the NavigationView is
    /// replaced by a top bar, a library sidebar and a Now Playing panel.
    /// </summary>
    public sealed partial class XpotifyShellPage : Page, IAppShell
    {
        private const string LibraryVisibleKey = "XpotifyLibraryVisible";
        private const string NowPlayingVisibleKey = "XpotifyNowPlayingVisible";
        private const double LibraryMinWindowWidth = 640;
        private const double NowPlayingMinWindowWidth = 900;
        private const double CompactPanelsBelowWidth = 1200;

        private static readonly string[] PlayerBarIconButtons =
        {
            "ShuffleButton", "PrevButton", "NextButton", "RepeatButton",
            "PersistButton", "SpotifyConnectDeviceButton", "OutputDeviceButton"
        };

        private readonly List<string> _history = new List<string>();
        private readonly ObservableCollection<LibraryPlaylistItem> _playlists = new ObservableCollection<LibraryPlaylistItem>();
        private Func<Task> _cacheRefreshAction;
        private string _cacheStatusTooltip;
        private EventHandler<MediaState> _mediaStateChangedHandler;
        private bool _connectivityEventsRegistered;
        private bool _lastKnownInternetAccess;
        private int _connectivityUiRefreshVersion;
        private int _libraryLoadVersion;
        private bool _libraryLoaded;
        private string _nowPlayingArtistId;
        private string _nowPlayingArtUri;

        public XpotifyShellPage()
        {
            InitializeComponent();
            PlaylistList.ItemsSource = _playlists;
            Loaded += XpotifyShellPage_Loaded;
            Unloaded += XpotifyShellPage_Unloaded;
        }

        private static bool LibraryVisible
        {
            get => GetSetting(LibraryVisibleKey, true);
            set => ApplicationData.Current.LocalSettings.Values[LibraryVisibleKey] = value;
        }

        // A 1080p TV is ~960 effective pixels wide, so the extra panel starts hidden on Xbox.
        private static bool NowPlayingVisible
        {
            get => GetSetting(NowPlayingVisibleKey, !OSHelper.IsXboxFamily);
            set => ApplicationData.Current.LocalSettings.Values[NowPlayingVisibleKey] = value;
        }

        private static bool GetSetting(string key, bool fallback)
        {
            return ApplicationData.Current.LocalSettings.Values.TryGetValue(key, out object value) && value is bool b
                ? b
                : fallback;
        }

        private void XpotifyShellPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Subscribed here (not in the constructor) and removed in Unloaded so the page can
            // be collected when XboxBackgroundMemory drops the UI.
            SystemNavigationManager.GetForCurrentView().BackRequested += OnBackRequested;
            Window.Current.SizeChanged += Window_SizeChanged;
            RegisterConnectivityEvents();
            SetTitleBarColors(Windows.UI.Colors.Black, Windows.UI.Colors.White);

#if XPOTIFY_UI_PREVIEW
            ApplyPlayerBarStyle();
            UpdateLayoutForWidth();
            LoadPreview();
            return;
#else
            if (App.Media != null && _mediaStateChangedHandler == null)
            {
                _mediaStateChangedHandler = (s, state) =>
                {
                    UiWorkScheduler.RunLatest(this, Dispatcher, () =>
                    {
                        UpdatePlaybackRecoveryBanner(state);
                        UpdateNowPlaying(state);
                    });
                };
                App.Media.MediaStateChanged += _mediaStateChangedHandler;
            }

            ApplyPlayerBarStyle();
            UpdateLayoutForWidth();
            UpdatePlaybackRecoveryBanner(App.Media?.Current);
            UpdateNowPlaying(App.Media?.Current);
            _ = LoadLibraryAsync(forceRefresh: false);
#endif
        }

        // Called by XboxBackgroundMemory before the UI is dropped. The inner frames keep their
        // own history and page cache, which would otherwise hold Home and its images.
        public void ReleaseContentForUnload()
        {
            HeaderAccountControl.ReleaseSubscriptions();
            XboxBackgroundMemory.ClearFrame(ContentFrame);
            XboxBackgroundMemory.ClearFrame(FullWindowFrame);
            ContentFrame.Content = null;
            FullWindowFrame.Content = null;
        }

        private void XpotifyShellPage_Unloaded(object sender, RoutedEventArgs e)
        {
            LogService.Info("[XpotifyShellPage] Unloaded fired; releasing app-lifetime subscriptions.");
            SystemNavigationManager.GetForCurrentView().BackRequested -= OnBackRequested;
            Window.Current.SizeChanged -= Window_SizeChanged;
            UnregisterConnectivityEvents();
            SetTitleBarColors(null, null);

            if (App.Media != null && _mediaStateChangedHandler != null)
            {
                App.Media.MediaStateChanged -= _mediaStateChangedHandler;
                _mediaStateChangedHandler = null;
            }
        }

        // No title bar on Xbox; null restores the system colours for the original layouts.
        private static void SetTitleBarColors(Windows.UI.Color? background, Windows.UI.Color? foreground)
        {
            var titleBar = Windows.UI.ViewManagement.ApplicationView.GetForCurrentView()?.TitleBar;
            if (titleBar == null)
                return;

            titleBar.BackgroundColor = background;
            titleBar.ForegroundColor = foreground;
            titleBar.InactiveBackgroundColor = background;
            titleBar.ButtonBackgroundColor = background;
            titleBar.ButtonForegroundColor = foreground;
            titleBar.ButtonInactiveBackgroundColor = background;
        }

        private void Window_SizeChanged(object sender, WindowSizeChangedEventArgs e)
        {
            UpdateLayoutForWidth();
        }

        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            try
            {
#if !XPOTIFY_UI_PREVIEW
                await HeaderAccountControl.Initialize();

                if (ContentFrame.Content == null)
                {
                    NavigateTo(GetStartupPageTag(e.Parameter as string), true);
                }
#endif
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "[XpotifyShellPage.OnNavigatedTo] Failed during shell startup.");
            }
        }

        // ---- Layout -------------------------------------------------------------------

        private void UpdateLayoutForWidth()
        {
            var width = Window.Current.Bounds.Width;
            var fullWindow = FullWindowFrame.Visibility == Visibility.Visible;

            var showLibrary = LibraryVisible && width >= LibraryMinWindowWidth;
            var showNowPlaying = NowPlayingVisible && width >= NowPlayingMinWindowWidth;
            var compact = width < CompactPanelsBelowWidth;

            LibraryPanel.Visibility = showLibrary ? Visibility.Visible : Visibility.Collapsed;
            LibraryColumn.Width = showLibrary ? new GridLength(compact ? 240 : 300) : new GridLength(0);
            NowPlayingPanel.Visibility = showNowPlaying ? Visibility.Visible : Visibility.Collapsed;
            NowPlayingColumn.Width = showNowPlaying ? new GridLength(compact ? 260 : 320) : new GridLength(0);

            LibraryToggleButton.Visibility = width >= LibraryMinWindowWidth ? Visibility.Visible : Visibility.Collapsed;
            NowPlayingToggleButton.Visibility = width >= NowPlayingMinWindowWidth ? Visibility.Visible : Visibility.Collapsed;
            NavigationSearchBox.Width = width < 900 ? 220 : 420;

            if (fullWindow)
            {
                WideMediaBar.Visibility = Visibility.Collapsed;
                NarrowMediaBar.Visibility = Visibility.Collapsed;
                return;
            }

            WideMediaBar.Visibility = width < 612 ? Visibility.Collapsed : Visibility.Visible;
            NarrowMediaBar.Visibility = width < 612 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ApplyPlayerBarStyle()
        {
            try
            {
                // Style lives in Themes/Xpotify.xaml (application resources); the shared
                // MediaControllerBar XAML is left as upstream wrote it.
                if (WideMediaBar.FindName("PlayPauseButton") is Button playButton &&
                    Application.Current.Resources.TryGetValue("XpotifyPlayButtonStyle", out object style) &&
                    style is Style playStyle)
                {
                    playButton.Style = playStyle;
                }

                // Plain icons rather than square buttons around the transport controls.
                foreach (var name in PlayerBarIconButtons)
                {
                    if (WideMediaBar.FindName(name) is Button button)
                    {
                        button.Background = new SolidColorBrush(Windows.UI.Colors.Transparent);
                        button.BorderThickness = new Thickness(0);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Warn("[XpotifyShellPage] Unable to style the player bar: " + ex.Message);
            }
        }

        private void LibraryToggleButton_Click(object sender, RoutedEventArgs e)
        {
            LibraryVisible = !LibraryVisible;
            UpdateLayoutForWidth();
        }

        private void NowPlayingToggleButton_Click(object sender, RoutedEventArgs e)
        {
            NowPlayingVisible = !NowPlayingVisible;
            UpdateLayoutForWidth();
        }

        // ---- Library ------------------------------------------------------------------

        private async Task LoadLibraryAsync(bool forceRefresh)
        {
            if (App.SpotifyWeb == null || (_libraryLoaded && !forceRefresh))
                return;

            var version = Interlocked.Increment(ref _libraryLoadVersion);
            LibraryLoadingRing.IsActive = _playlists.Count == 0;
            LibraryEmptyText.Visibility = Visibility.Collapsed;

            try
            {
                if (!await EnsureAuthenticatedAsync())
                    return;

                var response = await App.SpotifyWeb.GetCurrentUserPlaylistsAsync(forceRefresh);
                if (version != _libraryLoadVersion)
                    return;

                var playlists = response?.Value?.Items ?? new List<SpotifyAPI.Web.FullPlaylist>();
                _playlists.Clear();
                foreach (var playlist in playlists.Where(p => p != null && !string.IsNullOrEmpty(p.Id)))
                {
                    var owner = playlist.Owner?.DisplayName ?? playlist.Owner?.Id;
                    var imageUrl = playlist.Images?.FirstOrDefault()?.Url;
                    _playlists.Add(new LibraryPlaylistItem
                    {
                        Id = playlist.Id,
                        Name = playlist.Name,
                        Subtitle = string.IsNullOrWhiteSpace(owner) ? "Playlist" : "Playlist · " + owner,
                        Image = ImageUriHelper.CreateBitmapImage(imageUrl),
                        Playlist = playlist
                    });
                }

                _libraryLoaded = true;
                LibraryEmptyText.Visibility = _playlists.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                UpdateLibrarySelection(_history.LastOrDefault());
            }
            catch (Exception ex)
            {
                LogService.Warn("[XpotifyShellPage.LoadLibraryAsync] Unable to load playlists: " + ex.Message);
                LibraryEmptyText.Visibility = _playlists.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            finally
            {
                if (version == _libraryLoadVersion)
                    LibraryLoadingRing.IsActive = false;
            }
        }

        private void LibraryRefreshButton_Click(object sender, RoutedEventArgs e)
        {
            _ = LoadLibraryAsync(forceRefresh: true);
        }

        private async void PlaylistList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (!(e.ClickedItem is LibraryPlaylistItem item))
                return;

            try
            {
                if (!await SpotifyDjHelper.StartPlaybackIfDjAsync(item.Playlist, this))
                    NavigateToPlaylist(item.Id);
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "[XpotifyShellPage.PlaylistList_ItemClick] Failed.");
            }
        }

        private async void PinnedList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var container = e.ClickedItem as ListViewItem
                ?? PinnedList.ContainerFromItem(e.ClickedItem) as ListViewItem;
            var tag = container?.Tag as string;

            if (tag == "SpotifyDj")
            {
                if (await EnsureAuthenticatedAsync())
                    await SpotifyDjHelper.StartHomeDjAsync(this);
                return;
            }

            if (!string.IsNullOrEmpty(tag))
                NavigateTo(tag, true);
        }

        private void UpdateLibrarySelection(string pageTag)
        {
            string playlistId = null;
            if (pageTag != null && pageTag.StartsWith("Playlist:", StringComparison.OrdinalIgnoreCase))
                playlistId = pageTag.Substring("Playlist:".Length);

            PlaylistList.SelectedItem = playlistId == null
                ? null
                : _playlists.FirstOrDefault(p => string.Equals(p.Id, playlistId, StringComparison.Ordinal));
        }

        // ---- Now Playing --------------------------------------------------------------

        private void UpdateNowPlaying(MediaState state)
        {
            var hasTrack = state != null && !string.IsNullOrWhiteSpace(state.DisplayTitle) &&
                !string.Equals(state.DisplayTitle, "Unknown Track", StringComparison.Ordinal);

            NowPlayingTitle.Text = hasTrack ? state.DisplayTitle : "Nothing playing";
            NowPlayingArtist.Text = hasTrack ? state.DisplayArtist ?? string.Empty : string.Empty;
            NowPlayingContextText.Text = !string.IsNullOrWhiteSpace(state?.ContextName)
                ? state.ContextName
                : "Now Playing";

            _nowPlayingArtistId = state?.Metadata?.Artists?.FirstOrDefault()?.Id;
            NowPlayingArtistButton.IsEnabled = !string.IsNullOrEmpty(_nowPlayingArtistId);

            var artUri = hasTrack ? state.ArtworkUri : null;
            if (!string.Equals(artUri, _nowPlayingArtUri, StringComparison.Ordinal))
            {
                _nowPlayingArtUri = artUri;
                NowPlayingArt.Source = string.IsNullOrWhiteSpace(artUri)
                    ? null
                    : ImageUriHelper.CreateBitmapImage(artUri, useFallback: false);
            }

            NowPlayingArtPlaceholder.Visibility = NowPlayingArt.Source == null
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void NowPlayingArtistButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_nowPlayingArtistId))
                NavigateToArtist(_nowPlayingArtistId);
        }

        private void NowPlayingLyricsButton_Click(object sender, RoutedEventArgs e) => NavigateTo("Lyrics", true);

        private void NowPlayingPlayerButton_Click(object sender, RoutedEventArgs e) => NavigateTo("Player", true);

        // ---- Top bar ------------------------------------------------------------------

        private void HomeButton_Click(object sender, RoutedEventArgs e) => NavigateTo("Home", true);

        private void SettingsButton_Click(object sender, RoutedEventArgs e) => NavigateTo("Settings", true);

        private void BackButton_Click(object sender, RoutedEventArgs e) => GoBack();

        private void NavigationSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            var query = (args.QueryText ?? string.Empty).Trim();
            NavigateTo(string.IsNullOrWhiteSpace(query) ? "Search" : "Search:" + query, true);
        }

        private void HeaderAccountControl_LoadingStateChanged(object sender, bool isLoading)
        {
            AccountLoadingRing.IsActive = isLoading;
            AccountPersonPicture.Opacity = isLoading ? 0.35 : 1.0;
        }

        private void HeaderAccountControl_UserChanged(object sender, SpotifyAccountControl.UserChangedEventArgs e)
        {
            try
            {
                var user = e.User;
                if (user != null)
                {
                    var imageUrl = user.Images != null && user.Images.Count > 0 ? user.Images[0].Url : null;
                    AccountPersonPicture.ProfilePicture = ImageUriHelper.CreateBitmapImage(imageUrl, useFallback: false);
                    AccountPersonPicture.DisplayName = user.DisplayName ?? user.Id;
                    AccountNameText.Text = !string.IsNullOrWhiteSpace(user.DisplayName) ? user.DisplayName : user.Id;
                    _ = LoadLibraryAsync(forceRefresh: false);
                }
                else
                {
                    AccountPersonPicture.ProfilePicture = null;
                    AccountPersonPicture.DisplayName = null;
                    AccountNameText.Text = "Account";
                    _playlists.Clear();
                    _libraryLoaded = false;
                }
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "[XpotifyShellPage.HeaderAccountControl_UserChanged] Failed.");
            }
        }

        // ---- Navigation (ported from Win11 ShellPage) ---------------------------------

        private string GetStartupPageTag(string requestedPageTag)
        {
            if (App.SpotifyAuth?.Current == null || App.SpotifyAuth.Current.IsExpired)
                return "Settings";

            if (IsLiveTileNavigationTag(requestedPageTag))
                return requestedPageTag;

            if (UserSettings.RememberLastPage)
            {
                var lastPage = ApplicationData.Current.LocalSettings.Values.TryGetValue("LastOpenPage", out object value)
                    ? value as string
                    : null;

                if (!string.IsNullOrWhiteSpace(lastPage) &&
                    !string.Equals(lastPage, "Player", StringComparison.OrdinalIgnoreCase))
                    return lastPage;
            }

            return "Home";
        }

        private static bool IsLiveTileNavigationTag(string pageTag)
        {
            return !string.IsNullOrWhiteSpace(pageTag) &&
                (pageTag.StartsWith("Artist:", StringComparison.OrdinalIgnoreCase) ||
                 pageTag.StartsWith("Album:", StringComparison.OrdinalIgnoreCase) ||
                 pageTag.StartsWith("Playlist:", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsParameterizedTag(string pageTag)
        {
            return pageTag.StartsWith("Search:", StringComparison.OrdinalIgnoreCase) ||
                pageTag.StartsWith("Album:", StringComparison.OrdinalIgnoreCase) ||
                pageTag.StartsWith("Artist:", StringComparison.OrdinalIgnoreCase) ||
                pageTag.StartsWith("Playlist:", StringComparison.OrdinalIgnoreCase) ||
                pageTag.StartsWith("User:", StringComparison.OrdinalIgnoreCase);
        }

        public void NavigateToAlbum(string id) => NavigateTo("Album:" + id);
        public void NavigateToArtist(string id) => NavigateTo("Artist:" + id);
        public void NavigateToPlaylist(string id) => NavigateTo("Playlist:" + id);
        public void NavigateToUserProfile(string id) => NavigateTo("User:" + id);

        public async void NavigateTo(string pageTag, bool forceReload = false)
        {
            try
            {
                UiResponsivenessTelemetry.SetCurrentPage(pageTag);
                ClearCacheStatus();

                if (_history.Count == 0 || _history[_history.Count - 1] != pageTag)
                    _history.Add(pageTag);
                PersistLastOpenPage(pageTag);

                if (string.Equals(pageTag, "Player", StringComparison.Ordinal))
                {
                    var playerType = NavigationHelper.GetPageType(pageTag);
                    if (forceReload || FullWindowFrame.CurrentSourcePageType != playerType)
                        FullWindowFrame.Navigate(playerType);

                    SetFullWindowMode(true);
                    UpdateBackButton();
                    return;
                }

                SetFullWindowMode(false);

                if (pageTag == "Home" && !await EnsureAuthenticatedAsync())
                {
                    var settingsType = NavigationHelper.GetPageType("Settings");
                    if (ContentFrame.CurrentSourcePageType != settingsType)
                        ContentFrame.Navigate(settingsType);

                    UiResponsivenessTelemetry.SetCurrentPage("Settings");
                    UpdateBackButton();
                    return;
                }

                if (IsParameterizedTag(pageTag))
                {
                    var parameter = pageTag.Substring(pageTag.IndexOf(':') + 1);
                    ContentFrame.Navigate(NavigationHelper.GetPageType(pageTag), parameter);
                }
                else
                {
                    var pageType = NavigationHelper.GetPageType(pageTag);
                    if (forceReload || ContentFrame.CurrentSourcePageType != pageType)
                        ContentFrame.Navigate(pageType);
                }

                UpdateLibrarySelection(pageTag);
                UpdateBackButton();
            }
            catch (Exception ex)
            {
                LogService.Error(ex, $"[XpotifyShellPage.NavigateTo] Failed to navigate to {pageTag}.");
            }
        }

        private void OnBackRequested(object sender, BackRequestedEventArgs e)
        {
            if (_history.Count <= 1)
                return;

            e.Handled = true;
            GoBack();
        }

        private void GoBack()
        {
            if (_history.Count <= 1)
                return;

            _history.RemoveAt(_history.Count - 1);
            var previous = _history[_history.Count - 1];

            if (string.Equals(previous, "Player", StringComparison.Ordinal))
            {
                var playerType = NavigationHelper.GetPageType(previous);
                if (FullWindowFrame.CurrentSourcePageType != playerType)
                    FullWindowFrame.Navigate(playerType);

                SetFullWindowMode(true);
            }
            else
            {
                SetFullWindowMode(false);
                if (IsParameterizedTag(previous))
                {
                    var parameter = previous.Substring(previous.IndexOf(':') + 1);
                    ContentFrame.Navigate(NavigationHelper.GetPageType(previous), parameter);
                }
                else
                {
                    ContentFrame.Navigate(NavigationHelper.GetPageType(previous));
                }

                UpdateLibrarySelection(previous);
            }

            UpdateBackButton();
        }

        private void UpdateBackButton()
        {
            var canGoBack = _history.Count > 1;
            BackButton.IsEnabled = canGoBack;
            SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility =
                canGoBack ? AppViewBackButtonVisibility.Visible : AppViewBackButtonVisibility.Disabled;
        }

        private void SetFullWindowMode(bool enabled)
        {
            FullWindowFrame.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            TopBar.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
            MainGrid.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
            MediaBarHost.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
            UpdateLayoutForWidth();
        }

        private static void PersistLastOpenPage(string pageTag)
        {
            if (!UserSettings.RememberLastPage ||
                string.Equals(pageTag, "Player", StringComparison.OrdinalIgnoreCase))
                return;

            ApplicationData.Current.LocalSettings.Values["LastOpenPage"] = pageTag;
        }

        private async Task<bool> EnsureAuthenticatedAsync()
        {
            var token = await App.SpotifyAuth.EnsureValidAccessTokenAsync();
            return !string.IsNullOrEmpty(token);
        }

        // ---- Cache status / recovery / connectivity (ported) --------------------------

        public void SetCacheStatus(string tooltip, bool showRefreshButton, Func<Task> refreshAction)
        {
            CacheStatusPanel.Visibility = Visibility.Visible;
            CacheStatusText.Text = "Cached Content";
            CacheRefreshButton.Visibility = showRefreshButton ? Visibility.Visible : Visibility.Collapsed;
            ToolTipService.SetToolTip(CacheStatusPanel, tooltip);
            ToolTipService.SetToolTip(CacheRefreshButton, tooltip);
            _cacheStatusTooltip = tooltip;
            _cacheRefreshAction = refreshAction;
        }

        public void ClearCacheStatus()
        {
            CacheStatusPanel.Visibility = Visibility.Collapsed;
            ToolTipService.SetToolTip(CacheStatusPanel, null);
            ToolTipService.SetToolTip(CacheRefreshButton, null);
            _cacheStatusTooltip = null;
            _cacheRefreshAction = null;
        }

        private async void CacheStatusText_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_cacheStatusTooltip))
                return;

            await new ContentDialog
            {
                Title = "Cached Content",
                Content = _cacheStatusTooltip,
                PrimaryButtonText = "OK"
            }.ShowAsync();
        }

        private async void CacheRefreshButton_Click(object sender, RoutedEventArgs e)
        {
            var action = _cacheRefreshAction;
            if (action == null)
                return;

            try
            {
                await action();
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "Cache refresh action failed");
            }
        }

        private void UpdatePlaybackRecoveryBanner(MediaState state)
        {
            var show = state?.IsRecoveringOnlinePlayback == true;
            PlaybackRecoveryText.Text = string.IsNullOrWhiteSpace(state?.StatusMessage)
                ? "Reconnecting to Spotify..."
                : state.StatusMessage;
            PlaybackRecoveryRing.IsActive = show;
            PlaybackRecoveryBanner.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RegisterConnectivityEvents()
        {
            _lastKnownInternetAccess = ConnectivityHelper.HasInternetAccess();
            if (_connectivityEventsRegistered)
                return;

            ConnectivityHelper.ConnectivityStatusChanged += OnConnectivityStatusChanged;
            NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
            _connectivityEventsRegistered = true;
        }

        private void UnregisterConnectivityEvents()
        {
            if (!_connectivityEventsRegistered)
                return;

            ConnectivityHelper.ConnectivityStatusChanged -= OnConnectivityStatusChanged;
            NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged;
            _connectivityEventsRegistered = false;
        }

        private void OnConnectivityStatusChanged(object sender, EventArgs e) => QueueConnectivityUiRefresh();

        private void OnNetworkStatusChanged(object sender) => QueueConnectivityUiRefresh();

        private void QueueConnectivityUiRefresh()
        {
            var version = Interlocked.Increment(ref _connectivityUiRefreshVersion);
            var ignored = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                await Task.Delay(300);
                if (version != _connectivityUiRefreshVersion)
                    return;

                var hasInternet = ConnectivityHelper.HasInternetAccess();
                var wasOnline = _lastKnownInternetAccess;
                _lastKnownInternetAccess = hasInternet;

                UpdatePlaybackRecoveryBanner(App.Media?.Current);
                if (!string.IsNullOrWhiteSpace(_cacheStatusTooltip) && _cacheRefreshAction != null)
                    CacheRefreshButton.Visibility = hasInternet ? Visibility.Visible : Visibility.Collapsed;

                if (!hasInternet || wasOnline || FullWindowFrame.Visibility == Visibility.Visible)
                    return;

                try
                {
                    await HeaderAccountControl.Initialize();
                    _ = LoadLibraryAsync(forceRefresh: true);
                }
                catch (Exception ex)
                {
                    LogService.Warn($"[XpotifyShellPage] Unable to refresh after reconnect: {ex.Message}");
                }
            });
        }
    }
}
