# Wallpaper Scheduler: Time of Day

A tiny (~0.6 MB), native Windows 11 utility that automatically switches your desktop wallpapers — and optionally your Windows Light/Dark theme — by time of day or sunrise and sunset.

**Free. Source-available for transparency. No ads, no accounts, no tracking, no internet access.**

## Install

Install directly from the **Microsoft Store** — no extra downloads or developer tools required:
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

## Privacy & Transparency

Wallpaper Scheduler: Time of Day declares **no internet capability** in [`Package.appxmanifest`](src/WallpaperScheduler.Package/Package.appxmanifest). Everything runs locally on your PC, and settings are stored in `%LocalAppData%\WallpaperScheduler\config.json`.

See [docs/privacy.html](docs/privacy.html) for the full privacy statement.

---

## License

Copyright (c) 2026 Aussie App Studio. **All rights reserved.** See [LICENSE](LICENSE).

This repository is **source-available** so anyone can inspect the code for privacy and security, while prohibiting copying, modification, redistribution, or derivative works by others.
