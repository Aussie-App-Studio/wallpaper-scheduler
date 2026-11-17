# Wallpaper Scheduler: Time of Day

A tiny (~0.6 MB), native Rust + WebView2 Windows 11 utility that automatically switches your desktop wallpapers — and optionally your Windows Light/Dark theme — by time of day or sunrise and sunset.

**Free. Open source. No ads, no accounts, no tracking, no internet access.**

## Install (For Users)

Install directly from the **Microsoft Store** — nothing else to download or configure:
- **Microsoft Store:** [apps.microsoft.com/detail/9N6PCZ8Z74LN](https://apps.microsoft.com/detail/9N6PCZ8Z74LN)
- **Landing & Privacy Page:** [aussie-app-studio.github.io/wallpaper-scheduler](https://aussie-app-studio.github.io/wallpaper-scheduler/)

---

## Features

- **Day & Night or custom time slots:** Switch between folders or single pictures at exact times, or automatically at **Sunrise** and **Sunset** (with optional minute offsets).
- **Windows Light & Dark theme sync:** Optionally switch Windows between Light and Dark mode whenever a slot begins.
- **Folder slideshows with custom play order:** Preview every picture in your folder, drag thumbnails to set the exact order, or enable shuffle.
- **Weekday & weekend schedules:** Run slots every day, weekdays only, or weekends only.
- **Multi-monitor support:** Use the same wallpaper everywhere or customize each display.
- **Offline solar calculation:** Sunrise and sunset are calculated locally on your PC using the NOAA solar algorithm (choose a nearby city or enter custom coordinates).
- **Backup & transfer:** Export your schedule to a JSON file and import it on any new PC in seconds.
- **Ultra-lightweight:** Native Rust UI + Rust background agent (~0.6 MB Store MSIX, ~1.3 MB installed, ~2.5 MB RAM & 0% CPU while sleeping until the next transition).

---

## Privacy

Wallpaper Scheduler: Time of Day declares **no internet capability** in [`Package.appxmanifest`](src/WallpaperScheduler.Package/Package.appxmanifest). Everything runs locally on your PC, and settings are stored in `%LocalAppData%\WallpaperScheduler\config.json`.

See [docs/privacy.html](docs/privacy.html) for the full privacy statement.

---

## For Developers: Building from Source

*(End users do not need any of these tools — they are only required if you want to modify and recompile the source code.)*

### Prerequisites
- Windows 10 (with Edge WebView2 Runtime) or Windows 11
- [Rust toolchain](https://rustup.rs/) (`stable-x86_64-pc-windows-msvc` + MSVC Build Tools)
- Windows 11 SDK (`10.0.26100.0` for `rc.exe` and `makeappx.exe`)

### Run Tests
```powershell
cargo test --manifest-path src/WallpaperScheduler.Agent.Rs/Cargo.toml
cargo test --manifest-path src/WallpaperScheduler.App.Rs/Cargo.toml
```

### Build MSIX Package
```powershell
# Self-signed for local sideload testing
.\src\WallpaperScheduler.Package\build-msix.ps1 -SelfSign

# Unsigned Store-ready MSIX
.\src\WallpaperScheduler.Package\build-msix.ps1
```

*(The original C# WinUI 3 prototype is preserved in git history under tag `v1.0-winui3`.)*
