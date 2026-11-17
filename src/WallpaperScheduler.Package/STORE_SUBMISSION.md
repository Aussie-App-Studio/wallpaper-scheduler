# Microsoft Store Submission Checklist — Wallpaper Scheduler: Time of Day

## 1. Partner Center Identity
1. Reserve **Wallpaper Scheduler: Time of Day** in Microsoft Partner Center.
2. Copy your **Package/Identity/Name** and **Package/Identity/Publisher** (`CN=...`) from *Product management → Product identity* into `src\WallpaperScheduler.Package\Package.appxmanifest`.
3. Increment the 4-part `Version` (e.g. `1.0.0.0` → `1.0.0.1`) on each submission.
4. Build the upload package:
   ```powershell
   .\src\WallpaperScheduler.Package\build-msix.ps1
   ```
   Upload `src\WallpaperScheduler.Package\out\WallpaperScheduler_1.0.0.0_x64.msix` (Microsoft signs it during certification).

## 2. Certification Notes (copy-paste for `runFullTrust`)
> **Why `runFullTrust` is required:**
> This desktop utility uses the Windows `IDesktopWallpaper` COM API to set per-monitor wallpapers and Windows slideshow folders, and optionally updates the user's Windows Light/Dark personalization registry setting (`HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`). It has no network capabilities and does not collect or transmit any user data.

## 3. Store Listing Copy

### Short description
> Automatically switch your wallpapers and Windows Light/Dark theme by time of day or sunrise and sunset. Free, lightweight, no ads, no tracking.

### Description
> **Wallpaper Scheduler: Time of Day** keeps your desktop fresh from morning to night with a clean, native Windows 11 Settings-style interface.
>
> **Key features:**
> - **Day & Night or custom time slots:** Switch between folders or individual pictures at exact times, or automatically at sunrise and sunset.
> - **Light & Dark theme sync:** Optionally switch Windows between Light and Dark mode when a slot begins.
> - **Folder slideshows with custom order:** Preview every picture in your folder, drag to set the play order, or enable shuffle.
> - **Weekday & weekend schedules:** Use different wallpapers on workdays vs. weekends.
> - **Multi-monitor support:** Use the same wallpaper everywhere or customize each display.
> - **Backup & transfer:** Export your schedule to a JSON file and import it on any new PC in seconds.
>
> **Private & lightweight:**
> - No ads, no accounts, no subscriptions, and no tracking.
> - No internet access required — sunrise and sunset are calculated offline on your device.
> - Runs a tiny, idle background agent that wakes only when a wallpaper needs to change.

### Search terms (up to 7)
`wallpaper scheduler`, `day night wallpaper`, `time of day wallpaper`, `sunrise sunset wallpaper`, `wallpaper changer`, `auto dark mode wallpaper`, `desktop slideshow`

## 4. Privacy Policy
Because the app declares no network capability and collects zero personal or telemetry data, host a simple static page (e.g., on GitHub Pages) stating:
> **Privacy Policy for Wallpaper Scheduler: Time of Day**
> Wallpaper Scheduler: Time of Day does not collect, store externally, or transmit any personal information, usage analytics, or device data. The application has no internet capability and performs all wallpaper scheduling and sunrise/sunset calculations locally on your device. All settings are stored locally in `%LocalAppData%\WallpaperScheduler\config.json`.
