# Wallpaper Scheduler: Time of Day

A lightweight, native Windows 11 utility that automatically switches your desktop wallpapers — and optionally your Windows Light/Dark theme — by time of day or sunrise and sunset.

**Free. Open source. No ads, no accounts, no tracking, no internet access.**

- **Microsoft Store:** [apps.microsoft.com/detail/9N6PCZ8Z74LN](https://apps.microsoft.com/detail/9N6PCZ8Z74LN)
- **Landing & Privacy Page:** [aussie-app-studio.github.io/wallpaper-scheduler](https://aussie-app-studio.github.io/wallpaper-scheduler/)

---

## Features

- **Day & Night or custom time slots:** Switch between folders or single pictures at exact times, or automatically at **Sunrise** and **Sunset** (with optional minute offsets).
- **Windows Light & Dark theme sync:** Optionally switch Windows between Light and Dark mode when a slot begins.
- **Folder slideshows with custom play order:** Preview every picture in your folder, drag thumbnails to set the exact order, or enable shuffle.
- **Weekday & weekend schedules:** Run slots every day, weekdays only, or weekends only.
- **Multi-monitor support:** Use the same wallpaper everywhere or customize each display.
- **Offline solar calculation:** Sunrise and sunset are calculated locally on your PC using the NOAA solar algorithm (choose a nearby city or enter custom coordinates).
- **Backup & transfer:** Export your schedule to a JSON file and import it on any new PC in seconds.
- **Lightweight background agent:** Built in native Rust — runs silently with no tray clutter (~2.5 MB private RAM, 0% CPU while sleeping until the next transition) and re-evaluates automatically on wake, unlock, or display changes.

---

## Privacy

Wallpaper Scheduler: Time of Day declares **no internet capability** in [`Package.appxmanifest`](src/WallpaperScheduler.Package/Package.appxmanifest). Everything runs locally on your PC, and settings are stored in `%LocalAppData%\WallpaperScheduler\config.json`.

See [docs/privacy.html](docs/privacy.html) for the full privacy statement.

---

## Building from Source

### Prerequisites
- Windows 10 (19041+) or Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Rust toolchain](https://rustup.rs/) (`stable-x86_64-pc-windows-msvc` + MSVC Build Tools)
- Windows 11 SDK (`10.0.26100.0` for `makeappx.exe`)

### Run Tests
```powershell
dotnet test tests/WallpaperScheduler.Core.Tests
cargo test --manifest-path src/WallpaperScheduler.Agent.Rs/Cargo.toml
```

### Build MSIX Package
```powershell
# Self-signed for local sideload testing
.\src\WallpaperScheduler.Package\build-msix.ps1 -SelfSign

# Unsigned Store-ready MSIX
.\src\WallpaperScheduler.Package\build-msix.ps1
```
