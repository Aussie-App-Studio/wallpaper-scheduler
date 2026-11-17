use crate::models::AppThemeMode;
use std::path::PathBuf;
use windows::ApplicationModel::{Package, StartupTask, StartupTaskState};
use windows::Win32::Foundation::{HWND, LPARAM, POINT, WPARAM};
use windows::Win32::Graphics::Dwm::{DWMWA_USE_IMMERSIVE_DARK_MODE, DwmSetWindowAttribute};
use windows::Win32::System::Com::{CLSCTX_INPROC_SERVER, CoCreateInstance, IPersistFile};
use windows::Win32::System::Registry::{
    HKEY_CURRENT_USER, KEY_READ, KEY_SET_VALUE, REG_OPTION_NON_VOLATILE, REG_SZ, RegCloseKey,
    RegCreateKeyExW, RegDeleteValueW, RegOpenKeyExW, RegQueryValueExW, RegSetValueExW,
};
use windows::Win32::System::Threading::{
    CREATE_NO_WINDOW, CreateProcessW, PROCESS_INFORMATION, STARTUPINFOW,
};
use windows::Win32::UI::Shell::{
    CSIDL_DESKTOPDIRECTORY, CSIDL_PROGRAMS, DefSubclassProc, IShellLinkW, NIF_ICON, NIF_MESSAGE,
    NIF_TIP, NIM_ADD, NIM_DELETE, NOTIFYICONDATAW, RemoveWindowSubclass, SHGetFolderPathW,
    SetWindowSubclass, ShellExecuteW, ShellLink, Shell_NotifyIconW,
};
use windows::Win32::UI::WindowsAndMessaging::{
    AppendMenuW, CreatePopupMenu, DestroyIcon, DestroyMenu, GetCursorPos, HICON, IMAGE_ICON,
    LR_LOADFROMFILE, LoadImageW, MF_STRING, SW_SHOWNORMAL, SetForegroundWindow, TPM_RETURNCMD,
    TPM_RIGHTBUTTON, TrackPopupMenuEx, WM_LBUTTONUP, WM_RBUTTONUP, WM_USER,
};
use windows::core::{HSTRING, Interface, PWSTR};

const RUN_KEY: &str = r"Software\Microsoft\Windows\CurrentVersion\Run";
const RUN_VALUE: &str = "WallpaperSchedulerAgent";
const STARTUP_TASK_ID: &str = "WallpaperSchedulerStartup";
const WM_TRAY: u32 = WM_USER + 42;

pub fn app_dir() -> PathBuf {
    std::env::current_exe()
        .ok()
        .and_then(|p| p.parent().map(|d| d.to_path_buf()))
        .unwrap_or_else(|| PathBuf::from("."))
}

pub fn ensure_agent_running() {
    let exe = app_dir().join("WallpaperSchedulerAgent.exe");
    if !exe.is_file() {
        return;
    }
    let mut cmd: Vec<u16> = format!("\"{}\"", exe.display())
        .encode_utf16()
        .chain(Some(0))
        .collect();
    let si = STARTUPINFOW {
        cb: std::mem::size_of::<STARTUPINFOW>() as u32,
        ..Default::default()
    };
    let mut pi = PROCESS_INFORMATION::default();
    unsafe {
        let _ = CreateProcessW(
            None,
            Some(PWSTR(cmd.as_mut_ptr())),
            None,
            None,
            false,
            CREATE_NO_WINDOW,
            None,
            None,
            &si,
            &mut pi,
        );
    }
}

pub fn system_uses_dark() -> bool {
    let subkey = HSTRING::from(r"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
    let val_name = HSTRING::from("AppsUseLightTheme");
    let mut hkey = windows::Win32::System::Registry::HKEY::default();
    unsafe {
        if RegOpenKeyExW(HKEY_CURRENT_USER, &subkey, Some(0), KEY_READ, &mut hkey).is_ok() {
            let mut val: u32 = 1;
            let mut size = 4u32;
            let ok = RegQueryValueExW(
                hkey,
                &val_name,
                None,
                None,
                Some(&mut val as *mut u32 as *mut u8),
                Some(&mut size),
            )
            .is_ok();
            let _ = RegCloseKey(hkey);
            if ok {
                return val == 0;
            }
        }
    }
    false
}

pub fn apply_dwm_theme(hwnd: HWND, mode: AppThemeMode) {
    let dark: i32 = match mode {
        AppThemeMode::Light => 0,
        AppThemeMode::Dark => 1,
        AppThemeMode::System => {
            if system_uses_dark() { 1 } else { 0 }
        }
    };
    unsafe {
        let _ = DwmSetWindowAttribute(
            hwnd,
            DWMWA_USE_IMMERSIVE_DARK_MODE,
            &dark as *const _ as *const _,
            4,
        );
    }
}

pub fn set_window_icon(hwnd: HWND) {
    let ico_path = app_dir().join("Assets").join("AppIcon.ico");
    if !ico_path.is_file() {
        return;
    }
    unsafe {
        if let Ok(big) = LoadImageW(None, &HSTRING::from(ico_path.to_string_lossy().as_ref()), IMAGE_ICON, 32, 32, LR_LOADFROMFILE) {
            let _ = windows::Win32::UI::WindowsAndMessaging::SendMessageW(
                hwnd,
                windows::Win32::UI::WindowsAndMessaging::WM_SETICON,
                Some(WPARAM(1)),
                Some(LPARAM(big.0 as isize)),
            );
        }
        if let Ok(small) = LoadImageW(None, &HSTRING::from(ico_path.to_string_lossy().as_ref()), IMAGE_ICON, 16, 16, LR_LOADFROMFILE) {
            let _ = windows::Win32::UI::WindowsAndMessaging::SendMessageW(
                hwnd,
                windows::Win32::UI::WindowsAndMessaging::WM_SETICON,
                Some(WPARAM(0)),
                Some(LPARAM(small.0 as isize)),
            );
        }
    }
}

pub fn is_packaged() -> bool {
    Package::Current().is_ok()
}

pub fn package_family_name() -> Option<String> {
    Package::Current()
        .ok()
        .and_then(|p| p.Id().ok())
        .and_then(|id| id.FamilyName().ok())
        .map(|s| s.to_string())
}

#[derive(serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct StartupInfo {
    pub enabled: bool,
    pub locked: bool,
    pub note: Option<String>,
}

pub fn get_startup_info() -> StartupInfo {
    if is_packaged() {
        if let Ok(op) = StartupTask::GetAsync(&HSTRING::from(STARTUP_TASK_ID))
            && let Ok(task) = op.get()
            && let Ok(st) = task.State()
        {
            return StartupInfo {
                enabled: matches!(st, StartupTaskState::Enabled | StartupTaskState::EnabledByPolicy),
                locked: matches!(
                    st,
                    StartupTaskState::DisabledByPolicy
                        | StartupTaskState::DisabledByUser
                        | StartupTaskState::EnabledByPolicy
                ),
                note: if st == StartupTaskState::DisabledByUser {
                    Some("Disabled in Windows Settings → Apps → Startup. Re-enable it there.".into())
                } else {
                    None
                },
            };
        }
        return StartupInfo { enabled: false, locked: false, note: None };
    }
    StartupInfo {
        enabled: is_registry_startup_enabled(),
        locked: false,
        note: None,
    }
}

pub fn set_startup_enabled(on: bool) -> StartupInfo {
    if is_packaged() {
        if let Ok(op) = StartupTask::GetAsync(&HSTRING::from(STARTUP_TASK_ID))
            && let Ok(task) = op.get()
        {
            if on {
                if let Ok(req) = task.RequestEnableAsync() {
                    let _ = req.get();
                }
            } else {
                let _ = task.Disable();
            }
        }
        return get_startup_info();
    }
    set_registry_startup(on);
    get_startup_info()
}

pub fn enable_startup_by_default() {
    if is_packaged() {
        if let Ok(op) = StartupTask::GetAsync(&HSTRING::from(STARTUP_TASK_ID))
            && let Ok(task) = op.get()
            && task.State().ok() == Some(StartupTaskState::Disabled)
            && let Ok(req) = task.RequestEnableAsync()
        {
            let _ = req.get();
        }
    } else if !is_registry_startup_enabled() {
        set_registry_startup(true);
    }
}

fn is_registry_startup_enabled() -> bool {
    let subkey = HSTRING::from(RUN_KEY);
    let val_name = HSTRING::from(RUN_VALUE);
    let mut hkey = windows::Win32::System::Registry::HKEY::default();
    unsafe {
        if RegOpenKeyExW(HKEY_CURRENT_USER, &subkey, Some(0), KEY_READ, &mut hkey).is_ok() {
            let ok = RegQueryValueExW(hkey, &val_name, None, None, None, None).is_ok();
            let _ = RegCloseKey(hkey);
            return ok;
        }
    }
    false
}

fn set_registry_startup(on: bool) {
    let subkey = HSTRING::from(RUN_KEY);
    let val_name = HSTRING::from(RUN_VALUE);
    let mut hkey = windows::Win32::System::Registry::HKEY::default();
    unsafe {
        if RegCreateKeyExW(
            HKEY_CURRENT_USER,
            &subkey,
            Some(0),
            None,
            REG_OPTION_NON_VOLATILE,
            KEY_SET_VALUE,
            None,
            &mut hkey,
            None,
        )
        .is_ok()
        {
            if on {
                let exe = app_dir().join("WallpaperSchedulerAgent.exe");
                if exe.is_file() {
                    let val: Vec<u16> = format!("\"{}\"", exe.display())
                        .encode_utf16()
                        .chain(Some(0))
                        .collect();
                    let bytes =
                        std::slice::from_raw_parts(val.as_ptr() as *const u8, val.len() * 2);
                    let _ = RegSetValueExW(hkey, &val_name, Some(0), REG_SZ, Some(bytes));
                }
            } else {
                let _ = RegDeleteValueW(hkey, &val_name);
            }
            let _ = RegCloseKey(hkey);
        }
    }
}

pub fn create_shortcut(desktop: bool) -> bool {
    let csidl = if desktop { CSIDL_DESKTOPDIRECTORY } else { CSIDL_PROGRAMS };
    let mut buf = [0u16; 260];
    if unsafe { SHGetFolderPathW(None, csidl as i32, None, 0, &mut buf) }.is_err() {
        return false;
    }
    let len = buf.iter().position(|&c| c == 0).unwrap_or(buf.len());
    let folder = PathBuf::from(String::from_utf16_lossy(&buf[..len]));
    let lnk = folder.join("Wallpaper Scheduler.lnk");

    let target = if let Some(fam) = package_family_name() {
        format!(r"shell:AppsFolder\{fam}!App")
    } else {
        std::env::current_exe()
            .unwrap_or_else(|_| app_dir().join("WallpaperScheduler.exe"))
            .to_string_lossy()
            .into_owned()
    };
    let ico = app_dir().join("Assets").join("AppIcon.ico");

    unsafe {
        let Ok(sl) = CoCreateInstance::<_, IShellLinkW>(&ShellLink, None, CLSCTX_INPROC_SERVER) else {
            return false;
        };
        let _ = sl.SetPath(&HSTRING::from(&target));
        if !is_packaged() {
            let _ = sl.SetWorkingDirectory(&HSTRING::from(app_dir().to_string_lossy().as_ref()));
        }
        if ico.is_file() {
            let _ = sl.SetIconLocation(&HSTRING::from(ico.to_string_lossy().as_ref()), 0);
        } else if let Ok(exe) = std::env::current_exe() {
            let _ = sl.SetIconLocation(&HSTRING::from(exe.to_string_lossy().as_ref()), 0);
        }
        if let Ok(pf) = sl.cast::<IPersistFile>() {
            return pf.Save(&HSTRING::from(lnk.to_string_lossy().as_ref()), true).is_ok();
        }
    }
    false
}

pub fn open_url(url: &str) {
    unsafe {
        ShellExecuteW(
            None,
            &HSTRING::from("open"),
            &HSTRING::from(url),
            None,
            None,
            SW_SHOWNORMAL,
        );
    }
}

pub struct TrayGuard {
    hwnd: HWND,
    hicon: HICON,
    added: bool,
}

impl TrayGuard {
    pub fn new(hwnd: HWND) -> Self {
        let ico_path = app_dir().join("Assets").join("AppIcon.ico");
        let hicon = if ico_path.is_file() {
            unsafe {
                LoadImageW(
                    None,
                    &HSTRING::from(ico_path.to_string_lossy().as_ref()),
                    IMAGE_ICON,
                    32,
                    32,
                    LR_LOADFROMFILE,
                )
                .map(|h| HICON(h.0))
                .unwrap_or_default()
            }
        } else {
            HICON::default()
        };

        let mut nid = make_nid(hwnd, hicon);
        let tip = "Wallpaper Scheduler: Time of Day";
        for (i, ch) in tip.encode_utf16().take(127).enumerate() {
            nid.szTip[i] = ch;
        }
        let added = unsafe { Shell_NotifyIconW(NIM_ADD, &nid).as_bool() };
        unsafe {
            let _ = SetWindowSubclass(hwnd, Some(tray_subclass), 99, 0);
        }
        Self { hwnd, hicon, added }
    }
}

impl Drop for TrayGuard {
    fn drop(&mut self) {
        if self.added {
            let nid = make_nid(self.hwnd, self.hicon);
            unsafe {
                let _ = Shell_NotifyIconW(NIM_DELETE, &nid);
            }
        }
        unsafe {
            let _ = RemoveWindowSubclass(self.hwnd, Some(tray_subclass), 99);
            if !self.hicon.is_invalid() {
                let _ = DestroyIcon(self.hicon);
            }
        }
    }
}

fn make_nid(hwnd: HWND, hicon: HICON) -> NOTIFYICONDATAW {
    NOTIFYICONDATAW {
        cbSize: std::mem::size_of::<NOTIFYICONDATAW>() as u32,
        hWnd: hwnd,
        uID: 1,
        uFlags: NIF_MESSAGE | NIF_ICON | NIF_TIP,
        uCallbackMessage: WM_TRAY,
        hIcon: hicon,
        ..Default::default()
    }
}

unsafe extern "system" fn tray_subclass(
    hwnd: HWND,
    msg: u32,
    wparam: WPARAM,
    lparam: LPARAM,
    _uid: usize,
    _refdata: usize,
) -> windows::Win32::Foundation::LRESULT {
    if msg == WM_TRAY {
        let evt = (lparam.0 & 0xFFFF) as u32;
        if evt == WM_LBUTTONUP {
            unsafe {
                let _ = windows::Win32::UI::WindowsAndMessaging::ShowWindow(hwnd, SW_SHOWNORMAL);
                let _ = SetForegroundWindow(hwnd);
            }
        } else if evt == WM_RBUTTONUP {
            unsafe {
                if let Ok(menu) = CreatePopupMenu() {
                    let _ = AppendMenuW(menu, MF_STRING, 1, &HSTRING::from("Open Wallpaper Scheduler"));
                    let _ = AppendMenuW(menu, MF_STRING, 2, &HSTRING::from("Close window"));
                    let mut pt = POINT::default();
                    let _ = GetCursorPos(&mut pt);
                    let _ = SetForegroundWindow(hwnd);
                    let cmd = TrackPopupMenuEx(
                        menu,
                        (TPM_RETURNCMD | TPM_RIGHTBUTTON).0,
                        pt.x,
                        pt.y,
                        hwnd,
                        None,
                    );
                    let _ = DestroyMenu(menu);
                    if cmd.0 == 1 {
                        let _ = windows::Win32::UI::WindowsAndMessaging::ShowWindow(hwnd, SW_SHOWNORMAL);
                        let _ = SetForegroundWindow(hwnd);
                    } else if cmd.0 == 2 {
                        let _ = windows::Win32::UI::WindowsAndMessaging::PostMessageW(
                            Some(hwnd),
                            windows::Win32::UI::WindowsAndMessaging::WM_CLOSE,
                            WPARAM(0),
                            LPARAM(0),
                        );
                    }
                }
            }
        }
    }
    unsafe { DefSubclassProc(hwnd, msg, wparam, lparam) }
}
