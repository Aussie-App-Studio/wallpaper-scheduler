#![windows_subsystem = "windows"]

mod evaluator;
mod models;
mod solar;
mod wallpaper;

use chrono::{Duration, Utc};
use models::{AppConfig, default_config_path, load_config};
use std::sync::atomic::{AtomicBool, Ordering};
use windows::Win32::Foundation::{ERROR_ALREADY_EXISTS, GetLastError, HANDLE, HWND, LPARAM, LRESULT, WPARAM};
use windows::Win32::Storage::FileSystem::{
    FILE_NOTIFY_CHANGE_FILE_NAME, FILE_NOTIFY_CHANGE_LAST_WRITE, FILE_NOTIFY_CHANGE_SIZE,
    FindFirstChangeNotificationW, FindNextChangeNotification,
};
use windows::Win32::System::LibraryLoader::GetModuleHandleW;
use windows::Win32::System::RemoteDesktop::{NOTIFY_FOR_THIS_SESSION, WTSRegisterSessionNotification};
use windows::Win32::System::Threading::{CreateMutexW, INFINITE};
use windows::Win32::UI::WindowsAndMessaging::{
    CreateWindowExW, DefWindowProcW, DispatchMessageW, HMENU, MSG, MsgWaitForMultipleObjectsEx,
    PM_REMOVE, PeekMessageW, QS_ALLINPUT, RegisterClassW, TranslateMessage, WINDOW_EX_STYLE,
    WM_DISPLAYCHANGE, WM_POWERBROADCAST, WM_TIMECHANGE, WM_WTSSESSION_CHANGE, WNDCLASSW,
    WS_OVERLAPPED,
};
use windows::core::HSTRING;

static POKE: AtomicBool = AtomicBool::new(false);

fn main() {
    let args: Vec<String> = std::env::args().collect();
    let once = args.iter().any(|a| a.eq_ignore_ascii_case("--apply"));

    let mutex_name = HSTRING::from(r"Local\WallpaperSchedulerAgent");
    let _mutex = unsafe { CreateMutexW(None, true, &mutex_name) };
    let already_running = unsafe { GetLastError() } == ERROR_ALREADY_EXISTS;
    if already_running {
        return;
    }

    wallpaper::init_com();

    let mut last_key: Option<String> = None;
    if once {
        let _ = run_once(&mut last_key, true);
        return;
    }

    let cfg_path = default_config_path();
    if let Some(dir) = cfg_path.parent() {
        let _ = std::fs::create_dir_all(dir);
    }

    let hwnd = create_event_window();
    if let Some(h) = hwnd {
        unsafe {
            let _ = WTSRegisterSessionNotification(h, NOTIFY_FOR_THIS_SESSION);
        }
    }

    let dir_w = HSTRING::from(
        cfg_path
            .parent()
            .unwrap_or_else(|| std::path::Path::new("."))
            .to_string_lossy()
            .as_ref(),
    );
    let watch_handle = unsafe {
        FindFirstChangeNotificationW(
            &dir_w,
            false,
            FILE_NOTIFY_CHANGE_FILE_NAME | FILE_NOTIFY_CHANGE_LAST_WRITE | FILE_NOTIFY_CHANGE_SIZE,
        )
        .ok()
    };

    loop {
        let wait = run_once(&mut last_key, false);
        let delay = wait
            .clamp(Duration::seconds(5), Duration::minutes(30))
            .num_milliseconds() as u32;

        wait_for_events(watch_handle, delay);
        if POKE.swap(false, Ordering::SeqCst) {
            std::thread::sleep(std::time::Duration::from_millis(400));
        }
    }
}

fn wait_for_events(watch: Option<HANDLE>, timeout_ms: u32) {
    let handles: Vec<HANDLE> = watch.into_iter().collect();
    let deadline = std::time::Instant::now() + std::time::Duration::from_millis(timeout_ms as u64);

    loop {
        drain_messages();
        if POKE.load(Ordering::SeqCst) {
            return;
        }
        let rem = deadline
            .saturating_duration_since(std::time::Instant::now())
            .as_millis() as u32;
        if rem == 0 {
            return;
        }

        let r = unsafe {
            MsgWaitForMultipleObjectsEx(
                if handles.is_empty() { None } else { Some(&handles) },
                if handles.is_empty() { INFINITE.min(rem) } else { rem },
                QS_ALLINPUT,
                Default::default(),
            )
        };
        if r.0 == 0 && !handles.is_empty() {
            unsafe {
                let _ = FindNextChangeNotification(handles[0]);
            }
            POKE.store(true, Ordering::SeqCst);
            return;
        }
        drain_messages();
        if POKE.load(Ordering::SeqCst) {
            return;
        }
    }
}

fn drain_messages() {
    let mut msg = MSG::default();
    unsafe {
        while PeekMessageW(&mut msg, None, 0, 0, PM_REMOVE).as_bool() {
            let _ = TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
    }
}

fn create_event_window() -> Option<HWND> {
    let cls = HSTRING::from("WallpaperSchedulerAgentMsgWin");
    let hinst = unsafe { GetModuleHandleW(None).ok()? };
    let wc = WNDCLASSW {
        lpfnWndProc: Some(wnd_proc),
        hInstance: hinst.into(),
        lpszClassName: windows::core::PCWSTR(cls.as_ptr()),
        ..Default::default()
    };
    unsafe { RegisterClassW(&wc) };
    unsafe {
        CreateWindowExW(
            WINDOW_EX_STYLE(0),
            &cls,
            &cls,
            WS_OVERLAPPED,
            0,
            0,
            0,
            0,
            None,
            Some(HMENU::default()),
            Some(hinst.into()),
            None,
        )
        .ok()
    }
}

unsafe extern "system" fn wnd_proc(
    hwnd: HWND,
    msg: u32,
    wparam: WPARAM,
    lparam: LPARAM,
) -> LRESULT {
    match msg {
        WM_POWERBROADCAST | WM_WTSSESSION_CHANGE | WM_DISPLAYCHANGE | WM_TIMECHANGE => {
            POKE.store(true, Ordering::SeqCst);
        }
        _ => {}
    }
    unsafe { DefWindowProcW(hwnd, msg, wparam, lparam) }
}

fn run_once(last_key: &mut Option<String>, force: bool) -> Duration {
    let fallback = Duration::minutes(30);
    let cfg: AppConfig = load_config(&default_config_path());
    let utc_now = Utc::now();

    if let Some(until_opt) = cfg.is_paused_at(utc_now) {
        *last_key = None;
        return match until_opt {
            Some(until) => until - utc_now + Duration::seconds(1),
            None => fallback,
        };
    }

    let tz = evaluator::resolve_zone(cfg.location.as_ref());
    let now = tz.utc_to_local(utc_now);
    let Some(res) = evaluator::resolve(&cfg, now) else {
        return fallback;
    };

    let mut monitors: Vec<String> = res
        .slot
        .monitor_paths
        .iter()
        .map(|(k, v)| format!("{k}={v}"))
        .collect();
    monitors.sort();

    let key = format!(
        "{}|{:?}|{:?}|{:?}|{:?}|{}|{}|{}|{}|{}",
        res.active_since,
        res.slot.path,
        res.slot.fit,
        res.slot.theme,
        res.slot.days,
        res.slot.shuffle,
        res.slot.order.join(","),
        res.slot.slideshow_interval_minutes,
        cfg.same_on_all_displays,
        monitors.join(";")
    );

    if force || last_key.as_deref() != Some(&key) {
        if wallpaper::apply_slot(res.slot, cfg.same_on_all_displays) {
            *last_key = Some(key);
        }
    }

    res.next_change - now + Duration::seconds(1)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::models::{DayFilter, Slot, StartKind};
    use chrono::NaiveDate;

    #[test]
    fn sydney_summer_solstice_solar_times() {
        let tz = solar::WinTz::from_id("AUS Eastern Standard Time");
        let d = NaiveDate::from_ymd_opt(2026, 12, 21).unwrap();
        let rise = solar::sunrise(d, -33.8688, 151.2093, &tz).unwrap();
        let set = solar::sunset(d, -33.8688, 151.2093, &tz).unwrap();
        let r_min = rise.time().num_seconds_from_midnight() / 60;
        let s_min = set.time().num_seconds_from_midnight() / 60;
        assert!((5 * 60 + 35..=5 * 60 + 47).contains(&r_min), "rise={rise}");
        assert!((20 * 60..=20 * 60 + 10).contains(&s_min), "set={set}");
    }

    #[test]
    fn weekday_weekend_wrap() {
        let cfg = AppConfig {
            slots: vec![
                Slot {
                    name: "Work".into(),
                    time_of_day: "09:00:00".into(),
                    days: DayFilter::Weekdays,
                    ..Default::default()
                },
                Slot {
                    name: "Weekend".into(),
                    time_of_day: "10:00:00".into(),
                    days: DayFilter::Weekends,
                    ..Default::default()
                },
            ],
            ..Default::default()
        };
        let sat_morn = NaiveDate::from_ymd_opt(2026, 11, 14)
            .unwrap()
            .and_hms_opt(8, 0, 0)
            .unwrap();
        assert_eq!(evaluator::resolve(&cfg, sat_morn).unwrap().slot.name, "Work");

        let sat_noon = NaiveDate::from_ymd_opt(2026, 11, 14)
            .unwrap()
            .and_hms_opt(11, 0, 0)
            .unwrap();
        assert_eq!(evaluator::resolve(&cfg, sat_noon).unwrap().slot.name, "Weekend");
    }

    #[test]
    fn parses_real_csharp_config_schema() {
        let json = r#"{
          "Version": 1,
          "Paused": false,
          "SameOnAllDisplays": true,
          "Location": { "Name": "Sydney", "Latitude": -33.8688, "Longitude": 151.2093, "TimeZoneId": "AUS Eastern Standard Time" },
          "Slots": [
            { "Name": "Day", "Start": "Sunrise", "TimeOfDay": "04:00:00", "OffsetMinutes": -15, "Path": "D:\\Wallpapers", "Shuffle": false, "SlideshowIntervalMinutes": 30, "Fit": "Fill", "Theme": "Light", "Days": "EveryDay", "MonitorPaths": {}, "Order": ["a.jpg"] }
          ]
        }"#;
        let cfg: AppConfig = serde_json::from_str(json).unwrap();
        assert_eq!(cfg.slots[0].start, StartKind::Sunrise);
        assert_eq!(cfg.slots[0].offset_minutes, -15);
        assert_eq!(cfg.slots[0].theme, models::WindowsThemeMode::Light);
        assert_eq!(cfg.slots[0].order, vec!["a.jpg"]);
        assert!(cfg.location.as_ref().is_some_and(|l| l.name == "Sydney"));
    }

    #[test]
    fn paused_until_expires_at_target() {
        let mut cfg = AppConfig::default();
        let now = Utc::now();
        cfg.paused_until_utc = Some((now + Duration::minutes(30)).to_rfc3339());
        assert!(cfg.is_paused_at(now).is_some());
        assert!(cfg.is_paused_at(now + Duration::minutes(31)).is_none());
    }

    #[test]
    fn london_equinox_is_about_six() {
        let tz = solar::WinTz::from_id("GMT Standard Time");
        let d = NaiveDate::from_ymd_opt(2026, 3, 20).unwrap();
        let rise = solar::sunrise(d, 51.5074, -0.1278, &tz).unwrap();
        let r_min = rise.time().num_seconds_from_midnight() / 60;
        assert!((5 * 60 + 55..=6 * 60 + 10).contains(&r_min), "rise={rise}");
    }

    #[test]
    fn ordered_images_respects_natural_sort_and_custom_order() {
        let dir = std::env::temp_dir().join(format!("ws_rs_test_{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        for f in ["img10.jpg", "img2.jpg", "img1.png", "notes.txt"] {
            std::fs::write(dir.join(f), b"x").unwrap();
        }
        let mut slot = Slot {
            path: dir.to_string_lossy().into_owned(),
            ..Default::default()
        };
        let names = |s: &Slot| -> Vec<String> {
            evaluator::get_ordered_images(s)
                .into_iter()
                .map(|p| p.file_name().unwrap().to_string_lossy().into_owned())
                .collect()
        };
        assert_eq!(names(&slot), vec!["img1.png", "img2.jpg", "img10.jpg"]);

        slot.order = vec!["img10.jpg".into(), "missing.jpg".into()];
        assert_eq!(names(&slot), vec!["img10.jpg", "img1.png", "img2.jpg"]);
        let _ = std::fs::remove_dir_all(&dir);
    }

    use chrono::Timelike;
}
