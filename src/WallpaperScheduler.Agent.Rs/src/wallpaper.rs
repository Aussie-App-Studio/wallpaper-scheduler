use crate::evaluator::{get_ordered_images, is_image, slot_is_usable};
use crate::models::{FitMode, Slot, WindowsThemeMode};
use std::path::{Path, PathBuf};
use windows::Win32::Foundation::{LPARAM, WPARAM};
use windows::Win32::System::Com::{
    CLSCTX_ALL, COINIT_APARTMENTTHREADED, CoCreateInstance, CoInitializeEx, CoTaskMemFree,
};
use windows::Win32::System::Registry::{
    HKEY_CURRENT_USER, KEY_SET_VALUE, REG_DWORD, REG_OPTION_NON_VOLATILE, RegCreateKeyExW,
    RegSetValueExW,
};
use windows::Win32::UI::Shell::Common::ITEMIDLIST;
use windows::Win32::UI::Shell::{
    DESKTOP_SLIDESHOW_OPTIONS, DESKTOP_WALLPAPER_POSITION, DWPOS_CENTER, DWPOS_FILL, DWPOS_FIT,
    DWPOS_SPAN, DWPOS_STRETCH, DWPOS_TILE, DesktopWallpaper, IDesktopWallpaper, IShellItemArray,
    SHCreateItemFromParsingName, SHCreateShellItemArrayFromIDLists,
    SHCreateShellItemArrayFromShellItem, SHParseDisplayName,
};
use windows::Win32::UI::WindowsAndMessaging::{
    HWND_BROADCAST, SMTO_ABORTIFHUNG, SendMessageTimeoutW, WM_SETTINGCHANGE,
};
use windows::core::{HSTRING, PCWSTR};

pub fn init_com() {
    unsafe {
        let _ = CoInitializeEx(None, COINIT_APARTMENTTHREADED);
    }
}

pub fn apply_slot(slot: &Slot, same_on_all_displays: bool) -> bool {
    if !slot_is_usable(slot) {
        return false;
    }

    let ok = unsafe { apply_wallpaper_com(slot, same_on_all_displays).is_ok() };
    if ok {
        apply_theme(slot.theme);
    }
    ok
}

unsafe fn apply_wallpaper_com(slot: &Slot, same_on_all_displays: bool) -> windows::core::Result<()> {
    let dw: IDesktopWallpaper =
        unsafe { CoCreateInstance(&DesktopWallpaper, None, CLSCTX_ALL)? };

    unsafe { dw.SetPosition(to_position(slot.fit))? };

    if slot.is_folder() {
        let array = if slot.shuffle {
            unsafe { folder_array(&slot.path)? }
        } else {
            let files = get_ordered_images(slot);
            unsafe { ordered_array(&files)? }
        };
        unsafe {
            dw.SetSlideshow(&array)?;
            let tick_ms = (slot.slideshow_interval_minutes.max(1) as u32).saturating_mul(60_000);
            dw.SetSlideshowOptions(
                DESKTOP_SLIDESHOW_OPTIONS(if slot.shuffle { 1 } else { 0 }),
                tick_ms,
            )?;
        }
    } else {
        let path_w = HSTRING::from(&slot.path);
        unsafe { dw.SetWallpaper(PCWSTR::null(), &path_w)? };
    }

    if !same_on_all_displays {
        for (monitor_id, path) in &slot.monitor_paths {
            if let Some(file) = pick_image(path) {
                let mid = HSTRING::from(monitor_id);
                let fw = HSTRING::from(file.to_string_lossy().as_ref());
                let _ = unsafe { dw.SetWallpaper(&mid, &fw) };
            }
        }
    }

    Ok(())
}

unsafe fn folder_array(folder: &str) -> windows::core::Result<IShellItemArray> {
    let fw = HSTRING::from(folder);
    let item: windows::Win32::UI::Shell::IShellItem =
        unsafe { SHCreateItemFromParsingName(&fw, None)? };
    unsafe { SHCreateShellItemArrayFromShellItem(&item) }
}

unsafe fn ordered_array(files: &[PathBuf]) -> windows::core::Result<IShellItemArray> {
    let mut pidls: Vec<*mut ITEMIDLIST> = Vec::with_capacity(files.len());
    for f in files {
        let fw = HSTRING::from(f.to_string_lossy().as_ref());
        let mut pidl = std::ptr::null_mut();
        if unsafe { SHParseDisplayName(&fw, None, &mut pidl, 0, None) }.is_ok()
            && !pidl.is_null()
        {
            pidls.push(pidl);
        }
    }
    let const_pidls: Vec<*const ITEMIDLIST> = pidls.iter().map(|&p| p as *const _).collect();
    let res = unsafe { SHCreateShellItemArrayFromIDLists(&const_pidls) };
    for p in pidls {
        unsafe { CoTaskMemFree(Some(p as *const _)) };
    }
    res
}

fn pick_image(path: &str) -> Option<PathBuf> {
    let p = Path::new(path);
    if p.is_file() {
        return Some(p.to_path_buf());
    }
    if !p.is_dir() {
        return None;
    }
    let files: Vec<PathBuf> = std::fs::read_dir(p)
        .ok()?
        .flatten()
        .map(|e| e.path())
        .filter(|x| x.is_file() && is_image(x))
        .collect();
    if files.is_empty() {
        return None;
    }
    // Simple non-crypto index using current nanoseconds
    let nanos = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.subsec_nanos() as usize)
        .unwrap_or(0);
    Some(files[nanos % files.len()].clone())
}

fn to_position(fit: FitMode) -> DESKTOP_WALLPAPER_POSITION {
    match fit {
        FitMode::Fill => DWPOS_FILL,
        FitMode::Fit => DWPOS_FIT,
        FitMode::Stretch => DWPOS_STRETCH,
        FitMode::Tile => DWPOS_TILE,
        FitMode::Center => DWPOS_CENTER,
        FitMode::Span => DWPOS_SPAN,
    }
}

#[allow(dead_code)]
#[derive(Debug, Clone, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct MonitorInfo {
    pub id: String,
    pub width: i32,
    pub height: i32,
    pub current_wallpaper: String,
}

#[allow(dead_code)]
pub fn get_monitors() -> Vec<MonitorInfo> {
    unsafe { get_monitors_com().unwrap_or_default() }
}

unsafe fn get_monitors_com() -> windows::core::Result<Vec<MonitorInfo>> {
    let dw: IDesktopWallpaper =
        unsafe { CoCreateInstance(&DesktopWallpaper, None, CLSCTX_ALL)? };
    let count = unsafe { dw.GetMonitorDevicePathCount()? };
    let mut list = Vec::new();
    for i in 0..count {
        let id = unsafe { dw.GetMonitorDevicePathAt(i)? };
        let id_str = unsafe { id.to_string().unwrap_or_default() };
        if id_str.is_empty() {
            continue;
        }
        let rect = unsafe { dw.GetMonitorRECT(&HSTRING::from(&id_str))? };
        let wp = unsafe { dw.GetWallpaper(&HSTRING::from(&id_str))? };
        let wp_str = unsafe { wp.to_string().unwrap_or_default() };
        unsafe {
            CoTaskMemFree(Some(id.as_ptr() as *const _));
            CoTaskMemFree(Some(wp.as_ptr() as *const _));
        }
        list.push(MonitorInfo {
            id: id_str,
            width: rect.right - rect.left,
            height: rect.bottom - rect.top,
            current_wallpaper: wp_str,
        });
    }
    Ok(list)
}

#[allow(dead_code)]
pub fn advance_slideshow() {
    unsafe {
        if let Ok(dw) = CoCreateInstance::<_, IDesktopWallpaper>(&DesktopWallpaper, None, CLSCTX_ALL) {
            let _ = dw.AdvanceSlideshow(PCWSTR::null(), windows::Win32::UI::Shell::DSD_FORWARD);
        }
    }
}

pub fn apply_theme(mode: WindowsThemeMode) {
    if mode == WindowsThemeMode::LeaveAlone {
        return;
    }
    let light: u32 = if mode == WindowsThemeMode::Light { 1 } else { 0 };
    let subkey = HSTRING::from(r"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
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
            let bytes = light.to_le_bytes();
            let v1 = HSTRING::from("AppsUseLightTheme");
            let v2 = HSTRING::from("SystemUsesLightTheme");
            let _ = RegSetValueExW(hkey, &v1, Some(0), REG_DWORD, Some(&bytes));
            let _ = RegSetValueExW(hkey, &v2, Some(0), REG_DWORD, Some(&bytes));
            let _ = windows::Win32::System::Registry::RegCloseKey(hkey);
        }

        let param = HSTRING::from("ImmersiveColorSet");
        let _ = SendMessageTimeoutW(
            HWND_BROADCAST,
            WM_SETTINGCHANGE,
            WPARAM(0),
            LPARAM(param.as_ptr() as isize),
            SMTO_ABORTIFHUNG,
            500,
            None,
        );
    }
}
