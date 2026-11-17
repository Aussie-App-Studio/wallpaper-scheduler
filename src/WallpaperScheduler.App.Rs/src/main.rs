#![windows_subsystem = "windows"]

#[path = "../../WallpaperScheduler.Agent.Rs/src/models.rs"]
mod models;
#[path = "../../WallpaperScheduler.Agent.Rs/src/solar.rs"]
mod solar;
#[path = "../../WallpaperScheduler.Agent.Rs/src/evaluator.rs"]
mod evaluator;
#[path = "../../WallpaperScheduler.Agent.Rs/src/wallpaper.rs"]
mod wallpaper;

mod cities;
mod platform;

use chrono::{Datelike, Timelike, Utc};
use models::{AppConfig, AppThemeMode, default_config_path, load_config, save_config};
use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::path::Path;
use std::sync::{Arc, Mutex};
use tao::dpi::LogicalSize;
use tao::event::{Event, WindowEvent};
use tao::event_loop::{ControlFlow, EventLoopBuilder};
use tao::platform::windows::WindowExtWindows;
use tao::window::WindowBuilder;
use windows::Win32::Foundation::{HWND, SIZE};
use windows::Win32::Graphics::Gdi::{
    BI_RGB, BITMAP, BITMAPINFO, BITMAPINFOHEADER, CreateCompatibleDC, DIB_RGB_COLORS, DeleteDC,
    DeleteObject, GetDIBits, GetObjectW,
};
use windows::Win32::System::Com::{
    COINIT_APARTMENTTHREADED, COINIT_DISABLE_OLE1DDE, COINIT_MULTITHREADED, CoInitializeEx,
};
use windows::Win32::UI::Shell::{
    IShellItemImageFactory, SHCreateItemFromParsingName, SIIGBF_RESIZETOFIT, SIIGBF_THUMBNAILONLY,
};
use windows::core::HSTRING;
use wry::http::{Response, header::CONTENT_TYPE};
use wry::{RequestAsyncResponder, WebContext, WebView, WebViewBuilder};

const HTML: &str = include_str!("ui.html");
const IMAGE_EXTS: &[&str] = &[
    "jpg", "jpeg", "png", "bmp", "gif", "tif", "tiff", "webp", "heic", "jxr",
];

#[derive(Deserialize)]
#[serde(tag = "type")]
enum IpcMsg {
    #[serde(rename = "ready")]
    Ready,
    #[serde(rename = "saveConfig")]
    SaveConfig { config: AppConfig },
    #[serde(rename = "saveConfigQuiet")]
    SaveConfigQuiet { config: AppConfig },
    #[serde(rename = "applyNow")]
    ApplyNow,
    #[serde(rename = "nextPicture")]
    NextPicture,
    #[serde(rename = "pauseUntilNext")]
    PauseUntilNext,
    #[serde(rename = "pickImage")]
    PickImage { slot: usize, monitor: Option<String> },
    #[serde(rename = "pickFolder")]
    PickFolder { slot: usize, monitor: Option<String> },
    #[serde(rename = "refreshMonitors")]
    RefreshMonitors,
    #[serde(rename = "setStartup")]
    SetStartup { enabled: bool },
    #[serde(rename = "setAppTheme")]
    SetAppTheme { mode: AppThemeMode },
    #[serde(rename = "exportConfig")]
    ExportConfig,
    #[serde(rename = "importConfig")]
    ImportConfig,
    #[serde(rename = "addShortcut")]
    AddShortcut { desktop: bool },
    #[serde(rename = "openUrl")]
    OpenUrl { url: String },
}

enum UserEvt {
    Ipc(String),
    Tick,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct UiState<'a> {
    config: &'a AppConfig,
    slot_infos: Vec<SlotInfo>,
    now: Option<NowInfo>,
    paused_until_next: bool,
    timeline: Vec<TlSeg>,
    now_frac: f64,
    monitors: Vec<wallpaper::MonitorInfo>,
    cities: &'static [cities::City],
    default_city: &'static cities::City,
    time_zones: Vec<String>,
    local_tz: String,
    startup: platform::StartupInfo,
    packaged: bool,
    system_dark: bool,
    mica: bool,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct SlotInfo {
    is_folder: bool,
    file_exists: bool,
    issue: Option<String>,
    conflict: bool,
    ordered_files: Vec<String>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct NowInfo {
    slot_name: String,
    next_change_text: String,
    issue: Option<String>,
    is_folder: bool,
    preview_path: Option<String>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct TlSeg {
    name: String,
    len: f64,
    slot_idx: usize,
}

struct Shared {
    config: AppConfig,
    monitors: Vec<wallpaper::MonitorInfo>,
    time_zones: Vec<String>,
}

fn main() {
    unsafe {
        let _ = CoInitializeEx(None, COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE);
    }

    let first_run = !default_config_path().exists();
    let cfg = load_config(&default_config_path());
    if first_run {
        platform::enable_startup_by_default();
    }
    platform::ensure_agent_running();

    let state = Arc::new(Mutex::new(Shared {
        config: cfg,
        monitors: wallpaper::get_monitors(),
        time_zones: cities::all_tz_ids(),
    }));

    let event_loop = EventLoopBuilder::<UserEvt>::with_user_event().build();
    let proxy = event_loop.create_proxy();

    let window = WindowBuilder::new()
        .with_title("Wallpaper Scheduler: Time of Day")
        .with_inner_size(LogicalSize::new(1100.0, 760.0))
        .with_min_inner_size(LogicalSize::new(720.0, 520.0))
        .build(&event_loop)
        .expect("Failed to create window");

    let hwnd = HWND(window.hwnd() as *mut _);
    platform::set_window_icon(hwnd);
    let initial_dark = {
        let st = state.lock().unwrap();
        platform::apply_dwm_theme(hwnd, st.config.app_theme);
        st.config.app_theme == AppThemeMode::Dark
            || (st.config.app_theme == AppThemeMode::System && platform::system_uses_dark())
    };
    let _tray = platform::TrayGuard::new(hwnd);

    let webview_dir = default_config_path()
        .parent()
        .map(|p| p.join("WebView2"))
        .unwrap_or_else(|| platform::app_dir().join("WebView2"));
    let _ = std::fs::create_dir_all(&webview_dir);
    let mut web_context = WebContext::new(Some(webview_dir));

    let thumb_cache: Arc<Mutex<HashMap<String, Vec<u8>>>> = Arc::new(Mutex::new(HashMap::new()));
    let bg = if initial_dark { (32, 32, 32, 255) } else { (243, 243, 243, 255) };
    let proxy_ipc = proxy.clone();
    let webview = WebViewBuilder::with_web_context(&mut web_context)
        .with_background_color(bg)
        .with_asynchronous_custom_protocol("app".into(), move |_id, req, responder| {
            let path = req.uri().path().to_string();
            let query = req.uri().query().map(|q| q.to_string());
            serve_app_async(&path, query.as_deref(), &thumb_cache, responder);
        })
        .with_url("http://app.localhost/")
        .with_ipc_handler(move |msg| {
            let _ = proxy_ipc.send_event(UserEvt::Ipc(msg.body().to_string()));
        })
        .build(&window)
        .expect("Failed to create WebView2");

    std::thread::spawn(move || loop {
        std::thread::sleep(std::time::Duration::from_secs(30));
        if proxy.send_event(UserEvt::Tick).is_err() {
            break;
        }
    });

    event_loop.run(move |event, _, control_flow| {
        *control_flow = ControlFlow::Wait;
        match event {
            Event::WindowEvent {
                event: WindowEvent::CloseRequested,
                ..
            } => *control_flow = ControlFlow::Exit,
            Event::UserEvent(UserEvt::Tick) => push_state(&webview, &state),
            Event::UserEvent(UserEvt::Ipc(raw)) => {
                handle_ipc(&raw, hwnd, &webview, &state);
            }
            _ => {}
        }
    });
}

fn handle_ipc(raw: &str, hwnd: HWND, webview: &WebView, state: &Arc<Mutex<Shared>>) {
    let Ok(msg) = serde_json::from_str::<IpcMsg>(raw) else {
        return;
    };
    match msg {
        IpcMsg::Ready => push_state(webview, state),
        IpcMsg::SaveConfig { config } => {
            {
                let mut st = state.lock().unwrap();
                st.config = config;
                let _ = save_config(&st.config, &default_config_path());
            }
            push_state(webview, state);
        }
        IpcMsg::SaveConfigQuiet { config } => {
            let mut st = state.lock().unwrap();
            st.config = config;
            let _ = save_config(&st.config, &default_config_path());
        }
        IpcMsg::ApplyNow => {
            let ok = {
                let st = state.lock().unwrap();
                let tz = evaluator::resolve_zone(st.config.location.as_ref());
                let now = tz.utc_to_local(Utc::now());
                evaluator::resolve(&st.config, now)
                    .is_some_and(|r| wallpaper::apply_slot(r.slot, st.config.same_on_all_displays))
            };
            push_status(webview, "apply", if ok { "Apply now" } else { "Choose a picture first" });
        }
        IpcMsg::NextPicture => wallpaper::advance_slideshow(),
        IpcMsg::PauseUntilNext => {
            {
                let mut st = state.lock().unwrap();
                let utc_now = Utc::now();
                let active_pause = st
                    .config
                    .is_paused_at(utc_now)
                    .is_some_and(|u| u.is_some());
                if active_pause {
                    st.config.paused_until_utc = None;
                } else {
                    let tz = evaluator::resolve_zone(st.config.location.as_ref());
                    let now = tz.utc_to_local(utc_now);
                    if let Some(res) = evaluator::resolve(&st.config, now) {
                        // Approximate UTC by offset from current utc_now & local now
                        let delta = res.next_change - now;
                        st.config.paused_until_utc = Some((utc_now + delta).to_rfc3339());
                    }
                }
                let _ = save_config(&st.config, &default_config_path());
            }
            push_state(webview, state);
        }
        IpcMsg::PickImage { slot, monitor } => {
            if let Some(path) = rfd::FileDialog::new()
                .add_filter("Pictures", IMAGE_EXTS)
                .pick_file()
            {
                let p = path.to_string_lossy().into_owned();
                {
                    let mut st = state.lock().unwrap();
                    if let Some(s) = st.config.slots.get_mut(slot) {
                        if let Some(m) = monitor {
                            s.monitor_paths.insert(m, p);
                        } else {
                            s.path = p;
                        }
                        let _ = save_config(&st.config, &default_config_path());
                    }
                }
                push_state(webview, state);
            }
        }
        IpcMsg::PickFolder { slot, monitor } => {
            if let Some(path) = rfd::FileDialog::new().pick_folder() {
                let p = path.to_string_lossy().into_owned();
                {
                    let mut st = state.lock().unwrap();
                    if let Some(s) = st.config.slots.get_mut(slot) {
                        if let Some(m) = monitor {
                            s.monitor_paths.insert(m, p);
                        } else {
                            s.path = p;
                        }
                        let _ = save_config(&st.config, &default_config_path());
                    }
                }
                push_state(webview, state);
            }
        }
        IpcMsg::RefreshMonitors => {
            {
                let mut st = state.lock().unwrap();
                st.monitors = wallpaper::get_monitors();
            }
            push_state(webview, state);
        }
        IpcMsg::SetStartup { enabled } => {
            platform::set_startup_enabled(enabled);
            push_state(webview, state);
        }
        IpcMsg::SetAppTheme { mode } => {
            platform::apply_dwm_theme(hwnd, mode);
            let dark = mode == AppThemeMode::Dark
                || (mode == AppThemeMode::System && platform::system_uses_dark());
            let _ = webview.set_background_color(if dark { (32, 32, 32, 255) } else { (243, 243, 243, 255) });
            push_state(webview, state);
        }
        IpcMsg::ExportConfig => {
            if let Some(path) = rfd::FileDialog::new()
                .set_file_name("wallpaper-scheduler-settings.json")
                .add_filter("JSON settings", &["json"])
                .save_file()
            {
                let st = state.lock().unwrap();
                if save_config(&st.config, &path).is_ok() {
                    push_status(webview, "backup", "Exported");
                }
            }
        }
        IpcMsg::ImportConfig => {
            if let Some(path) = rfd::FileDialog::new()
                .add_filter("JSON settings", &["json"])
                .pick_file()
            {
                let loaded = load_config(&path);
                let count = loaded.slots.len();
                {
                    let mut st = state.lock().unwrap();
                    st.config = loaded;
                    let _ = save_config(&st.config, &default_config_path());
                }
                push_state(webview, state);
                push_status(webview, "backup", &format!("Imported {count} slots"));
            }
        }
        IpcMsg::AddShortcut { desktop } => {
            if platform::create_shortcut(desktop) {
                push_status(
                    webview,
                    "shortcut",
                    if desktop { "Added to Desktop" } else { "Added to Start" },
                );
            }
        }
        IpcMsg::OpenUrl { url } => platform::open_url(&url),
    }
}

fn push_status(webview: &WebView, key: &str, text: &str) {
    let k = serde_json::to_string(key).unwrap_or_default();
    let v = serde_json::to_string(text).unwrap_or_default();
    let _ = webview.evaluate_script(&format!("window.__onStatus({k}, {v});"));
}

fn push_state(webview: &WebView, state: &Arc<Mutex<Shared>>) {
    let st = state.lock().unwrap();
    let cfg = &st.config;
    let utc_now = Utc::now();
    let tz = evaluator::resolve_zone(cfg.location.as_ref());
    let now = tz.utc_to_local(utc_now);
    let today = now.date();
    let conflicts = evaluator::find_time_conflicts(cfg, today);

    let slot_infos: Vec<SlotInfo> = cfg
        .slots
        .iter()
        .enumerate()
        .map(|(i, s)| {
            let is_folder = s.is_folder();
            let file_exists = !is_folder && !s.path.is_empty() && Path::new(&s.path).is_file();
            let conflict = conflicts[i];
            let issue = evaluator::validate_slot(s).or_else(|| {
                if conflict {
                    Some("Another slot starts at the same time.".into())
                } else {
                    None
                }
            });
            let ordered_files = if is_folder {
                evaluator::get_ordered_images(s)
                    .into_iter()
                    .map(|p| p.to_string_lossy().into_owned())
                    .collect()
            } else {
                Vec::new()
            };
            SlotInfo {
                is_folder,
                file_exists,
                issue,
                conflict,
                ordered_files,
            }
        })
        .collect();

    let now_info = evaluator::resolve(cfg, now).map(|res| {
        let preview_path = if res.slot.is_folder() {
            evaluator::get_ordered_images(res.slot)
                .into_iter()
                .next()
                .map(|p| p.to_string_lossy().into_owned())
        } else if Path::new(&res.slot.path).is_file() {
            Some(res.slot.path.clone())
        } else {
            None
        };
        NowInfo {
            slot_name: res.slot.name.clone(),
            next_change_text: res.next_change.format("%-I:%M %p").to_string(),
            issue: evaluator::validate_slot(res.slot),
            is_folder: res.slot.is_folder(),
            preview_path,
        }
    });

    // Build 24h timeline for today
    let mut starts: Vec<(f64, usize)> = Vec::new();
    let day_start = today.and_hms_opt(0, 0, 0).unwrap();
    for (i, s) in cfg.slots.iter().enumerate() {
        if !evaluator::matches_day(s.days, today.weekday()) {
            continue;
        }
        if let Some(at) = evaluator::start_on(s, today, cfg.location.as_ref(), &tz) {
            let mins = (at - day_start).num_minutes().clamp(0, 1439) as f64;
            starts.push((mins, i));
        }
    }
    starts.sort_by(|a, b| a.0.total_cmp(&b.0));
    let mut timeline = Vec::new();
    if starts.len() >= 2 {
        if starts[0].0 > 0.0 {
            let idx = starts.last().unwrap().1;
            timeline.push(TlSeg {
                name: cfg.slots[idx].name.clone(),
                len: starts[0].0,
                slot_idx: idx,
            });
        }
        for i in 0..starts.len() {
            let end = if i + 1 < starts.len() { starts[i + 1].0 } else { 1440.0 };
            let len = end - starts[i].0;
            if len > 0.0 {
                let idx = starts[i].1;
                timeline.push(TlSeg {
                    name: cfg.slots[idx].name.clone(),
                    len,
                    slot_idx: idx,
                });
            }
        }
    }

    let paused_until_next = !cfg.paused
        && cfg
            .is_paused_at(utc_now)
            .is_some_and(|u| u.is_some());

    let ui_state = UiState {
        config: cfg,
        slot_infos,
        now: now_info,
        paused_until_next,
        timeline,
        now_frac: now.time().num_seconds_from_midnight() as f64 / 86400.0,
        monitors: st.monitors.clone(),
        cities: cities::CITIES,
        default_city: cities::default_city(),
        time_zones: st.time_zones.clone(),
        local_tz: cities::local_tz_id(),
        startup: platform::get_startup_info(),
        packaged: platform::is_packaged(),
        system_dark: platform::system_uses_dark(),
        mica: false,
    };

    if let Ok(json) = serde_json::to_string(&ui_state) {
        let _ = webview.evaluate_script(&format!("window.__onState({json});"));
    }
}

fn serve_app_async(
    req_path: &str,
    query: Option<&str>,
    cache: &Arc<Mutex<HashMap<String, Vec<u8>>>>,
    responder: RequestAsyncResponder,
) {
    if req_path != "/img" {
        responder.respond(serve_app(req_path, query));
        return;
    }

    let raw_path = query
        .and_then(|q| q.strip_prefix("p="))
        .map(percent_decode)
        .unwrap_or_default();

    if let Some(cached) = cache.lock().unwrap().get(&raw_path).cloned() {
        responder.respond(
            Response::builder()
                .status(200)
                .header(CONTENT_TYPE, "image/bmp")
                .header("Cache-Control", "public, max-age=3600")
                .body(std::borrow::Cow::Owned(cached))
                .unwrap(),
        );
        return;
    }

    let cache = Arc::clone(cache);
    std::thread::spawn(move || {
        unsafe {
            let _ = CoInitializeEx(None, COINIT_MULTITHREADED);
        }
        let p = Path::new(&raw_path);
        if p.is_file() && evaluator::is_image(p) {
            if let Some(bmp) = thumbnail_bmp(p, 288, 162) {
                if let Ok(mut guard) = cache.lock() {
                    if guard.len() < 400 {
                        guard.insert(raw_path, bmp.clone());
                    }
                }
                responder.respond(
                    Response::builder()
                        .status(200)
                        .header(CONTENT_TYPE, "image/bmp")
                        .header("Cache-Control", "public, max-age=3600")
                        .body(std::borrow::Cow::Owned(bmp))
                        .unwrap(),
                );
                return;
            }
        }
        responder.respond(serve_image_raw(p));
    });
}

fn serve_app(req_path: &str, query: Option<&str>) -> Response<std::borrow::Cow<'static, [u8]>> {
    if req_path == "/img" {
        let raw_path = query
            .and_then(|q| q.strip_prefix("p="))
            .map(percent_decode)
            .unwrap_or_default();
        return serve_image_raw(Path::new(&raw_path));
    }
    Response::builder()
        .status(200)
        .header(CONTENT_TYPE, "text/html; charset=utf-8")
        .body(HTML.as_bytes().into())
        .unwrap()
}

fn serve_image_raw(p: &Path) -> Response<std::borrow::Cow<'static, [u8]>> {
    if p.is_file()
        && evaluator::is_image(p)
        && let Ok(bytes) = std::fs::read(p)
    {
        let mime = match p.extension().and_then(|e| e.to_str()).map(|s| s.to_ascii_lowercase()).as_deref() {
            Some("png") => "image/png",
            Some("webp") => "image/webp",
            Some("gif") => "image/gif",
            Some("bmp") => "image/bmp",
            _ => "image/jpeg",
        };
        return Response::builder()
            .status(200)
            .header(CONTENT_TYPE, mime)
            .header("Cache-Control", "public, max-age=3600")
            .body(bytes.into())
            .unwrap();
    }
    Response::builder().status(404).body(Vec::new().into()).unwrap()
}

/// Extracts a small 320x180 thumbnail via Windows Explorer's thumbnail cache (avoids decoding 5K images in WebView2).
fn thumbnail_bmp(p: &Path, w: i32, h: i32) -> Option<Vec<u8>> {
    let pw = HSTRING::from(p.to_string_lossy().as_ref());
    let factory: IShellItemImageFactory =
        unsafe { SHCreateItemFromParsingName(&pw, None).ok()? };
    let size = SIZE { cx: w, cy: h };
    let hbmp = unsafe {
        factory
            .GetImage(size, SIIGBF_RESIZETOFIT | SIIGBF_THUMBNAILONLY)
            .or_else(|_| factory.GetImage(size, SIIGBF_RESIZETOFIT))
            .ok()?
    };

    let res = unsafe {
        let mut bm = BITMAP::default();
        if GetObjectW(
            hbmp.into(),
            std::mem::size_of::<BITMAP>() as i32,
            Some(&mut bm as *mut _ as *mut _),
        ) == 0
            || bm.bmWidth <= 0
            || bm.bmHeight <= 0
        {
            let _ = DeleteObject(hbmp.into());
            return None;
        }

        let width = bm.bmWidth;
        let height = bm.bmHeight;
        let pixel_bytes = (width as usize) * (height as usize) * 4;
        let mut out = vec![0u8; 54 + pixel_bytes];

        // BITMAPFILEHEADER (14 bytes)
        let file_size = out.len() as u32;
        out[0] = b'B';
        out[1] = b'M';
        out[2..6].copy_from_slice(&file_size.to_le_bytes());
        out[10..14].copy_from_slice(&54u32.to_le_bytes());

        // BITMAPINFOHEADER (40 bytes, bottom-up 32-bit BGRA)
        let mut bmi = BITMAPINFO {
            bmiHeader: BITMAPINFOHEADER {
                biSize: std::mem::size_of::<BITMAPINFOHEADER>() as u32,
                biWidth: width,
                biHeight: height,
                biPlanes: 1,
                biBitCount: 32,
                biCompression: BI_RGB.0,
                biSizeImage: pixel_bytes as u32,
                ..Default::default()
            },
            ..Default::default()
        };
        let hdr_bytes = std::slice::from_raw_parts(
            &bmi.bmiHeader as *const _ as *const u8,
            std::mem::size_of::<BITMAPINFOHEADER>(),
        );
        out[14..54].copy_from_slice(hdr_bytes);

        let hdc = CreateCompatibleDC(None);
        let lines = GetDIBits(
            hdc,
            hbmp,
            0,
            height as u32,
            Some(out[54..].as_mut_ptr() as *mut _),
            &mut bmi,
            DIB_RGB_COLORS,
        );
        let _ = DeleteDC(hdc);
        let _ = DeleteObject(hbmp.into());
        if lines == 0 { None } else { Some(out) }
    };
    res
}

fn percent_decode(s: &str) -> String {
    let bytes = s.as_bytes();
    let mut out = Vec::with_capacity(bytes.len());
    let mut i = 0;
    while i < bytes.len() {
        if bytes[i] == b'%'
            && i + 2 < bytes.len()
            && let Ok(b) = u8::from_str_radix(std::str::from_utf8(&bytes[i + 1..i + 3]).unwrap_or(""), 16)
        {
            out.push(b);
            i += 3;
            continue;
        }
        out.push(if bytes[i] == b'+' { b' ' } else { bytes[i] });
        i += 1;
    }
    String::from_utf8_lossy(&out).into_owned()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn percent_decode_handles_spaces_and_plus() {
        assert_eq!(
            percent_decode("D%3A%5CPictures%5CDay%20Wallpapers%5Ca%2Bb.jpg"),
            r"D:\Pictures\Day Wallpapers\a+b.jpg"
        );
    }

    #[test]
    fn serve_app_and_image_work() {
        let html_resp = serve_app("/", None);
        assert_eq!(html_resp.status(), 200);
        assert!(!html_resp.body().is_empty());

        let tmp = std::env::temp_dir().join(format!("ws_ui_img_{}.png", std::process::id()));
        std::fs::write(&tmp, b"fake-png-bytes").unwrap();
        let query = format!("p={}", tmp.to_string_lossy().replace('\\', "%5C").replace(' ', "%20"));
        let img_resp = serve_app("/img", Some(&query));
        let _ = std::fs::remove_file(&tmp);
        assert_eq!(img_resp.status(), 200);
        assert_eq!(img_resp.body().as_ref(), b"fake-png-bytes");

        assert_eq!(serve_app("/img", Some("p=C%3A%5Cnonexistent_xyz.jpg")).status(), 404);
    }

    #[test]
    fn time_format_and_cities_valid() {
        let dt = chrono::NaiveDate::from_ymd_opt(2026, 11, 17)
            .unwrap()
            .and_hms_opt(17, 5, 0)
            .unwrap();
        assert_eq!(dt.format("%-I:%M %p").to_string(), "5:05 PM");
        assert!(!cities::CITIES.is_empty());
        assert!(!cities::all_tz_ids().is_empty());
    }

    #[test]
    fn config_round_trip_with_app_theme() {
        let tmp = std::env::temp_dir().join(format!("ws_ui_cfg_{}.json", std::process::id()));
        let mut cfg = AppConfig::default();
        cfg.app_theme = AppThemeMode::Dark;
        cfg.paused = true;
        save_config(&cfg, &tmp).unwrap();
        let loaded = load_config(&tmp);
        let _ = std::fs::remove_file(&tmp);
        assert_eq!(loaded.app_theme, AppThemeMode::Dark);
        assert!(loaded.paused);
    }

    #[test]
    fn shell_thumbnail_generates_small_bmp() {
        let sample = Path::new(r"D:\Pictures\Wallpapers\Day Wallpapers\Coast Day.jpg");
        if sample.is_file() {
            unsafe { let _ = CoInitializeEx(None, COINIT_APARTMENTTHREADED); }
            let bmp = thumbnail_bmp(sample, 288, 162).expect("thumbnail_bmp failed");
            assert!(bmp.starts_with(b"BM"));
            assert!(bmp.len() < 450_000, "bmp too large: {}", bmp.len());
        }
    }

    #[test]
    fn all_ipc_messages_deserialize_and_reorder_persists() {
        let msgs = [
            r#"{"type":"ready"}"#,
            r#"{"type":"applyNow"}"#,
            r#"{"type":"nextPicture"}"#,
            r#"{"type":"pauseUntilNext"}"#,
            r#"{"type":"pickImage","slot":0}"#,
            r#"{"type":"pickFolder","slot":1,"monitor":"mon1"}"#,
            r#"{"type":"refreshMonitors"}"#,
            r#"{"type":"setStartup","enabled":true}"#,
            r#"{"type":"setAppTheme","mode":"Dark"}"#,
            r#"{"type":"exportConfig"}"#,
            r#"{"type":"importConfig"}"#,
            r#"{"type":"addShortcut","desktop":true}"#,
            r#"{"type":"openUrl","url":"https://www.youtube.com/watch?v=p58vknxGR4I"}"#,
        ];
        for raw in msgs {
            assert!(serde_json::from_str::<IpcMsg>(raw).is_ok(), "failed: {raw}");
        }

        let tmp = std::env::temp_dir().join(format!("ws_export_import_{}.json", std::process::id()));
        let mut cfg = AppConfig::default();
        let mut slot = models::Slot::default();
        slot.name = "Evening".into();
        slot.path = r"D:\Pictures\Wallpapers\Night Wallpapers".into();
        slot.order = vec!["Peninsula Night.jpg".into(), "Evening Mountain.jpg".into()];
        slot.days = models::DayFilter::Weekends;
        cfg.slots.push(slot);
        save_config(&cfg, &tmp).unwrap();
        let back = load_config(&tmp);
        let _ = std::fs::remove_file(&tmp);
        assert_eq!(back.slots[0].order, vec!["Peninsula Night.jpg", "Evening Mountain.jpg"]);
        assert_eq!(back.slots[0].days, models::DayFilter::Weekends);
    }
}


