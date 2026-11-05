using System;
using Windows.UI.Xaml.Media.Imaging;

namespace LibreSpotUWP.Helpers
{
    public static class ImageUriHelper
    {
        private const string FallbackLogoUri = "ms-appx:///Assets/StoreLogo.scale-400.png";
        private const string SpotifyImagePrefix = "spotify:image:";
        private const string SpotifyCdnPrefix = "https://i.scdn.co/image/";

        // XPOTIFY FORK: Spotify's first image is about 640px, which decodes to 1.6 MB however
        // small it is drawn. Xbox allows 128 MB in the background, so decode near display size.
        public const int ThumbnailDecodeWidth = 320;
        public const int HeaderDecodeWidth = 640;

        public static string NormalizeImageUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            value = value.Trim();

            if (value.StartsWith(SpotifyImagePrefix, StringComparison.OrdinalIgnoreCase))
                return SpotifyCdnPrefix + value.Substring(SpotifyImagePrefix.Length);

            if (LooksLikeSpotifyImageId(value))
                return SpotifyCdnPrefix + value;

            return value;
        }

        public static Uri GetFallbackLogoUri()
        {
            return new Uri(FallbackLogoUri);
        }

        public static bool TryCreateImageUri(string value, out Uri uri)
        {
            uri = null;
            var normalized = NormalizeImageUrl(value);
            return !string.IsNullOrWhiteSpace(normalized) &&
                   Uri.TryCreate(normalized, UriKind.Absolute, out uri);
        }

        public static Uri GetImageUriOrFallback(string value)
        {
            if (TryCreateImageUri(value, out var uri))
                return uri;

            return GetFallbackLogoUri();
        }

        public static BitmapImage CreateBitmapImage(string value, bool useFallback = true, int decodeWidth = ThumbnailDecodeWidth)
        {
            try
            {
                if (TryCreateImageUri(value, out var uri))
                    return new BitmapImage { DecodePixelWidth = decodeWidth, UriSource = uri };
            }
            catch
            {
            }

            if (!useFallback)
                return null;

            try
            {
                return new BitmapImage(GetFallbackLogoUri());
            }
            catch
            {
                return null;
            }
        }

        private static bool LooksLikeSpotifyImageId(string value)
        {
            if (value.Length != 40)
                return false;

            foreach (var c in value)
            {
                if (!Uri.IsHexDigit(c))
                    return false;
            }

            return true;
        }
    }
}
