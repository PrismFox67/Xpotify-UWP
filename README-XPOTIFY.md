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
- **To verify:** Xbox caps background apps' memory much lower than foreground apps. If the
  app goes over it in the background it is suspended or terminated, and the music stops. Nothing
  currently frees UI memory on `EnteredBackground`; that is the most likely change needed.

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
- **Known upstream issue:** some newer accounts hit an audio-key block
  ([librespot #1649](https://github.com/librespot-org/librespot/issues/1649)).

## Status

Early. Not yet confirmed working on real hardware.
