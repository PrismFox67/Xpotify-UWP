# Xpotify UWP — Spotify on Xbox Developer Mode

Xpotify UWP is a fork of **Spotbox**, which is itself a fork of
**[LibreSpotUWP](https://github.com/megabytesme/LibreSpotUWP)**. The goal is a Spotify
client that installs on an Xbox Series X|S in Developer Mode and keeps playing in the
background — on the dashboard, in other apps and in homebrew — the way the retail Spotify
app does.

## Credit

This project is a thin layer on top of other people's work.

- **[megabytesme](https://github.com/megabytesme)** wrote LibreSpotUWP: the Spotify client
  itself, its playback pipeline, and the UWP-safe native build of librespot that makes any
  of this possible. Please star [their repository](https://github.com/megabytesme/LibreSpotUWP).
- **Spotbox** ([maayan080/LibreSpotUWP-XBOX-edition](https://github.com/maayan080/LibreSpotUWP-XBOX-edition))
  did the Xbox port: controller-friendly sign-in through an in-app WebView2, the Xbox
  playback authorization flow, and the presentation fixes. Its changes are documented in
  [README-SPOTBOX-FORK.md](README-SPOTBOX-FORK.md), which is kept here unchanged.
- **[librespot](https://github.com/librespot-org/librespot)** implements the Spotify
  protocol.

## Licence

**CC BY-NC-SA 4.0** (Creative Commons Attribution-NonCommercial-ShareAlike 4.0
International), inherited from upstream LibreSpotUWP. The original [LICENSE.md](LICENSE.md)
is included unmodified.

- **Attribution** — keep the credit above if you fork this further.
- **NonCommercial** — personal use only; do not sell it or bundle it into anything sold.
- **ShareAlike** — derivatives must use the same licence and say what they changed.

## What this fork changes

Changes are kept small, and anything Xbox-specific sits behind a device-family check, in
the same spirit as Spotbox.

| File | Change |
| --- | --- |
| `LibreSpotUWP/Package.appxmanifest` | New identity so it installs alongside Spotbox and LibreSpotUWP. |
| `README-XPOTIFY.md` | This file. |
| `LibreSpotUWP/Helpers/XboxBackgroundMemory.cs` | **New.** Frees the UI when the app goes to the background on Xbox (see below). |
| `LibreSpotUWP/App.xaml.cs` | One call to `XboxBackgroundMemory.Attach(this)` in the constructor. |
| `LibreSpotUWP/LibreSpotUWP.csproj` | Registers the new files; optional `XpotifyForceBackgroundUnload` and `XpotifyUiPreview` test switches. |
| `LibreSpotUWP/Views/Xpotify/`, `LibreSpotUWP/Themes/Xpotify.xaml` | **New.** Spotify-desktop-style shell and palette: top bar, Your Library sidebar, Now Playing panel. |
| `LibreSpotUWP/Helpers/XpotifyLayout.cs` | **New.** Switch for that layout, on by default. |
| `UserSettings.cs`, `LibrespotService.cs`, the three `SettingsPage_*` files | "Streaming quality" setting (96, 160 or 320 kbps; default 320). |
| `SpotifyAccountControl.xaml.cs`, `XpotifyShellPage.xaml.cs` | Release the account control and inner frames when the UI is dropped; without this every old shell page stayed in memory. |
| `ImageUriHelper.cs`, `HeaderImageControl.xaml.cs`, `HomePage_Win11.xaml` | Decode artwork near display size instead of at about 640px, to avoid memory spikes on returning from the background. |
| `AppearanceService.cs`, `NavigationHelper.cs`, `App.xaml.cs`, the three `SettingsPage_*` files | Hook up the layout and add "Xpotify (Spotify style)" under Settings > Appearance. |

Package identity:

```
Name       PrismFox67.XpotifyUWP   (Spotbox: Spotbox.LibreSpotXbox)
Publisher  CN=PrismFox67           (Spotbox: CN=Spotbox)
Display    Xpotify UWP
```

The `librespotuwp://` protocol is unchanged on purpose: `librespotuwp://callback/` is the
redirect URI registered on the upstream Spotify client ID. On Xbox the in-app WebView2
intercepts the redirect, so the protocol is never actually activated there.

The table will grow as the remaining work lands; each change gets a line here.

## Goal: background playback

On retail, Spotify keeps playing when you leave it, and the Guide shows what's playing with
play/pause/skip. Xpotify aims for the same thing in Dev Mode. Here is what the inherited code
already does, and what still has to be confirmed on hardware:

- **Already in place:** the `backgroundMediaPlayback` capability, and System Media Transport
  Controls fed from `MediaService` (metadata, play/pause/next/previous, playback status).
  SMTC is what the Xbox Guide's media controls talk to.
- **To verify:** audio is produced by librespot's native XAudio2 backend (the default) or
  by the `AudioGraph` ring buffer, *not* by the `MediaPlayer` that owns the SMTC. Whether
  the Xbox keeps the audio going in the background in that setup is the main thing to test.
- **Background audio sponsorship is upstream's:** while playing, `MediaService` keeps a
  `MediaPlayer` running a silent stream so the system treats the app as playing media and
  doesn't suspend it; the audible PCM goes through librespot's renderer.
- **Memory (this fork):** Xbox caps background apps' memory much lower than foreground apps;
  an app over the cap is suspended or terminated and the music stops. `XboxBackgroundMemory`
  follows Microsoft's pattern for this: on `EnteredBackground` it drops the XAML tree
  (`Window.Current.Content = null`) when the main shell is showing, and it drops any page if
  the system lowers the limit below current usage. On return it rebuilds the shell. Services
  are static on `App`, so playback, SMTC and the librespot session are untouched. Navigation
  inside the shell resets to Home. It logs memory usage and limit at each step to
  `TempState\log.txt`, which is the first thing to read after a hardware test.
- **Testing on a PC:** build with `-p:XpotifyForceBackgroundUnload=true` to enable it off
  Xbox (from any page, so no account is needed); minimize and restore the window to cycle it.
  Never ship that build.
- **To verify on hardware:** that playback continues on the dashboard, and the log's memory
  figures against the background limit.

## Layout preview

Build with `-p:XpotifyUiPreview=true` to open the Xpotify layout with placeholder data and no
account. It cannot sign in or play music, so never ship that build.

## Building

Requirements: MSBuild with the UWP workload, Windows SDK 10.0.19041.0, x64.

`librespot.dll` is not in the repo. Extract it from the x64 package in an upstream
[LibreSpotUWP release](https://github.com/megabytesme/LibreSpotUWP/releases) and place it in
`LibreSpotUWP\`. (Building it from Rust for `x86_64-uwp-windows-msvc` is the fallback.)

```powershell
msbuild LibreSpotUWP.sln -t:Restore -p:Configuration=Release -p:Platform=x64
msbuild LibreSpotUWP.sln -t:Build   -p:Configuration=Release -p:Platform=x64 -p:AppxBundle=Never
```

If only a newer Windows SDK is installed, add `-p:TargetPlatformVersion=<version>` to the
build command (tested with `10.0.26100.0`); the project only uses 19041 as its default.

The prebuilt `librespot.dll` from v1.0.5 imports `XAudio2_8.dll` for its native audio
backend. If that backend fails on Xbox, the app's `AudioGraph` ring-buffer backend is the
alternative.

Signing uses `LibreSpotUWP\LibreSpotUWP_TemporaryKey.pfx` (gitignored). Create a
self-signed certificate whose subject is `CN=PrismFox67` — matching the manifest
`Publisher` — and export it to that path.

## Deploying

Install through Device Portal (`https://<console-ip>:11443`): add the app `.msix` and, in the
same submission, its framework dependencies (`Microsoft.UI.Xaml.2.8`, `Microsoft.VCLibs`,
`Microsoft.VCLibs.Desktop` and `Microsoft.NET.Native.Framework`/`Runtime` for x64).

## Notes

- **Spotify Premium is required.** librespot cannot play on free accounts.
- **Unofficial client.** It uses a reverse-engineered protocol, which is against Spotify's
  terms of service. Use it at your own risk.
- **Some accounts can't play music at all.** Spotify refuses audio keys to many accounts
  created in 2024 or later, in every librespot-based app
  ([librespot #1649](https://github.com/librespot-org/librespot/issues/1649)). Browsing works,
  but Play does nothing and songs won't start. There is no known fix; Xpotify shows a message
  when it happens.

## Status

Early release. Background playback across several songs confirmed on Xbox Series X in Dev Mode.
Spotify DJ works from 1.1.8.0, started from the phone, on Xbox Series X and on PC.
