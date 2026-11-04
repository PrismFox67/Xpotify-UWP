#if XPOTIFY_UI_PREVIEW
using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace LibreSpotUWP.Views.Xpotify
{
    /// <summary>
    /// XPOTIFY FORK: placeholder data so the layout can be reviewed without an account.
    /// Only compiled with -p:XpotifyUiPreview=true. Never ship that build.
    /// </summary>
    public sealed partial class XpotifyShellPage
    {
        private static readonly (string Name, Color A, Color B)[] PreviewPlaylists =
        {
            ("Morning Commute", Color.FromArgb(255, 232, 115, 64), Color.FromArgb(255, 120, 40, 90)),
            ("Late Night Drive", Color.FromArgb(255, 40, 60, 160), Color.FromArgb(255, 20, 20, 60)),
            ("Workout Mix", Color.FromArgb(255, 210, 40, 70), Color.FromArgb(255, 90, 10, 40)),
            ("Focus Flow", Color.FromArgb(255, 30, 150, 140), Color.FromArgb(255, 10, 60, 80)),
            ("Weekend Vibes", Color.FromArgb(255, 240, 190, 60), Color.FromArgb(255, 200, 90, 40)),
            ("Gaming Soundtracks", Color.FromArgb(255, 120, 70, 200), Color.FromArgb(255, 40, 20, 90)),
            ("Road Trip", Color.FromArgb(255, 70, 160, 220), Color.FromArgb(255, 30, 70, 130)),
            ("Chill Evening", Color.FromArgb(255, 150, 100, 170), Color.FromArgb(255, 60, 40, 90)),
        };

        private void LoadPreview()
        {
            foreach (var p in PreviewPlaylists)
            {
                _playlists.Add(new LibraryPlaylistItem
                {
                    Id = p.Name,
                    Name = p.Name,
                    Subtitle = "Playlist · Sample User",
                    Image = Swatch(p.A, p.B)
                });
            }

            var art = Swatch(PreviewPlaylists[0].A, PreviewPlaylists[0].B);
            NowPlayingContextText.Text = "Morning Commute";
            NowPlayingTitle.Text = "Sample Track";
            NowPlayingArtist.Text = "Sample Artist";
            NowPlayingArt.Source = art;
            NowPlayingArtPlaceholder.Visibility = Visibility.Collapsed;
            PlaylistList.SelectedIndex = 0;

            ContentFrame.Content = BuildPreviewHome();

            // The bar repaints from live (empty) media state after load, so fill it afterwards.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (s, e) => FillPreviewPlayerBar(art);
            timer.Start();
        }

        private void FillPreviewPlayerBar(ImageSource art)
        {
            if (WideMediaBar.FindName("TrackTitle") is TextBlock title) title.Text = "Sample Track";
            if (WideMediaBar.FindName("TrackArtist") is TextBlock artist) artist.Text = "Sample Artist";
            if (WideMediaBar.FindName("AlbumArt") is Image image) image.Source = art;
            if (WideMediaBar.FindName("CurrentTime") is TextBlock current) current.Text = "1:12";
            if (WideMediaBar.FindName("TotalTime") is TextBlock total) total.Text = "3:45";
            if (WideMediaBar.FindName("PositionSlider") is Slider slider)
            {
                slider.Maximum = 225;
                slider.Value = 72;
            }
        }

        private static UIElement BuildPreviewHome()
        {
            var root = new StackPanel { Padding = new Thickness(24), Spacing = 16 };
            root.Children.Add(Heading("Good evening", 30));

            var quick = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
            quick.ColumnDefinitions.Add(new ColumnDefinition());
            quick.ColumnDefinitions.Add(new ColumnDefinition());
            for (var i = 0; i < 6; i++)
            {
                if (i % 2 == 0) quick.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var tile = QuickTile(PreviewPlaylists[i]);
                Grid.SetColumn(tile, i % 2);
                Grid.SetRow(tile, i / 2);
                quick.Children.Add(tile);
            }
            root.Children.Add(quick);

            root.Children.Add(Heading("Made for you", 22));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            for (var i = 2; i < 7; i++)
            {
                row.Children.Add(Card(PreviewPlaylists[i]));
            }
            root.Children.Add(new ScrollViewer
            {
                Content = row,
                HorizontalScrollMode = ScrollMode.Enabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
            });

            return new ScrollViewer { Content = root };
        }

        private static TextBlock Heading(string text, double size)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = size,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            };
        }

        private static Border QuickTile((string Name, Color A, Color B) p)
        {
            var grid = new Grid { ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(new Image { Source = Swatch(p.A, p.B), Width = 56, Height = 56 });
            var label = new TextBlock
            {
                Text = p.Name,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);
            return new Border
            {
                Child = grid,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromArgb(255, 42, 42, 42))
            };
        }

        private static StackPanel Card((string Name, Color A, Color B) p)
        {
            var card = new StackPanel { Width = 150, Spacing = 8 };
            card.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(6),
                Child = new Image { Source = Swatch(p.A, p.B), Width = 150, Height = 150 }
            });
            card.Children.Add(new TextBlock
            {
                Text = p.Name,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            card.Children.Add(new TextBlock
            {
                Text = "Sample Artist, Another Artist",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 179, 179, 179)),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            return card;
        }

        private static ImageSource Swatch(Color a, Color b)
        {
            const int size = 64;
            var pixels = new byte[size * size * 4];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var t = (x + y) / (2.0 * (size - 1));
                    var i = (y * size + x) * 4;
                    pixels[i] = (byte)(a.B + (b.B - a.B) * t);
                    pixels[i + 1] = (byte)(a.G + (b.G - a.G) * t);
                    pixels[i + 2] = (byte)(a.R + (b.R - a.R) * t);
                    pixels[i + 3] = 255;
                }
            }

            var bitmap = new WriteableBitmap(size, size);
            using (var stream = bitmap.PixelBuffer.AsStream())
            {
                stream.Write(pixels, 0, pixels.Length);
            }
            bitmap.Invalidate();
            return bitmap;
        }
    }
}
#endif
