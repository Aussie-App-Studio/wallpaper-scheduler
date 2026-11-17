use crate::models::{AppConfig, DayFilter, GeoLocation, Slot, StartKind};
use crate::solar::{self, WinTz};
use chrono::{Datelike, Duration, NaiveDate, NaiveDateTime, Weekday};
use std::collections::HashMap;
use std::path::{Path, PathBuf};
use windows::core::PCWSTR;

const IMAGE_EXTS: &[&str] = &[
    "jpg", "jpeg", "png", "bmp", "gif", "tif", "tiff", "webp", "heic", "jxr",
];
pub const MAX_IMAGES: usize = 500;

#[derive(Debug, Clone)]
pub struct Resolution<'a> {
    pub slot: &'a Slot,
    pub active_since: NaiveDateTime,
    pub next_change: NaiveDateTime,
}

pub fn resolve_zone(loc: Option<&GeoLocation>) -> WinTz {
    if let Some(l) = loc
        && let Some(ref id) = l.time_zone_id
        && !id.is_empty()
    {
        return WinTz::from_id(id);
    }
    WinTz::local()
}

pub fn matches_day(filter: DayFilter, day: Weekday) -> bool {
    match filter {
        DayFilter::EveryDay => true,
        DayFilter::Weekdays => !matches!(day, Weekday::Sat | Weekday::Sun),
        DayFilter::Weekends => matches!(day, Weekday::Sat | Weekday::Sun),
    }
}

pub fn start_on(
    slot: &Slot,
    date: NaiveDate,
    loc: Option<&GeoLocation>,
    tz: &WinTz,
) -> Option<NaiveDateTime> {
    match slot.start {
        StartKind::Time => Some(NaiveDateTime::new(date, slot.parsed_time())),
        StartKind::Sunrise | StartKind::Sunset => {
            let l = loc?;
            let t = if slot.start == StartKind::Sunrise {
                solar::sunrise(date, l.latitude, l.longitude, tz)?
            } else {
                solar::sunset(date, l.latitude, l.longitude, tz)?
            };
            Some(t + Duration::minutes(slot.offset_minutes as i64))
        }
    }
}

pub fn resolve<'a>(config: &'a AppConfig, now: NaiveDateTime) -> Option<Resolution<'a>> {
    if config.slots.is_empty() {
        return None;
    }
    let tz = resolve_zone(config.location.as_ref());
    let day = now.date();
    let mut starts: Vec<(NaiveDateTime, usize)> = Vec::new();

    for offset in -7i64..=7 {
        let d = day + Duration::days(offset);
        for (idx, slot) in config.slots.iter().enumerate() {
            if !matches_day(slot.days, d.weekday()) {
                continue;
            }
            if let Some(at) = start_on(slot, d, config.location.as_ref(), &tz) {
                starts.push((at, idx));
            }
        }
    }

    if starts.is_empty() {
        return None;
    }
    starts.sort_by_key(|a| a.0);

    let mut active_pos = None;
    for (i, (at, _)) in starts.iter().enumerate() {
        if *at <= now {
            active_pos = Some(i);
        } else {
            break;
        }
    }
    let pos = active_pos?;
    if pos + 1 >= starts.len() {
        return None;
    }

    let (active_since, slot_idx) = starts[pos];
    let next_change = starts[pos + 1..]
        .iter()
        .find(|(_, idx)| *idx != slot_idx)
        .map(|(at, _)| *at)
        .unwrap_or(starts[pos + 1].0);

    Some(Resolution {
        slot: &config.slots[slot_idx],
        active_since,
        next_change,
    })
}

pub fn is_image(path: &Path) -> bool {
    path.extension()
        .and_then(|e| e.to_str())
        .is_some_and(|ext| IMAGE_EXTS.iter().any(|x| ext.eq_ignore_ascii_case(x)))
}

#[link(name = "shlwapi")]
unsafe extern "system" {
    fn StrCmpLogicalW(psz1: PCWSTR, psz2: PCWSTR) -> i32;
}

fn cmp_logical(a: &str, b: &str) -> std::cmp::Ordering {
    let aw: Vec<u16> = a.encode_utf16().chain(Some(0)).collect();
    let bw: Vec<u16> = b.encode_utf16().chain(Some(0)).collect();
    let r = unsafe { StrCmpLogicalW(PCWSTR(aw.as_ptr()), PCWSTR(bw.as_ptr())) };
    r.cmp(&0)
}

/// Returns ordered folder pictures (custom Order first, then Explorer-sorted remainder, max 500).
pub fn get_ordered_images(slot: &Slot) -> Vec<PathBuf> {
    if !slot.is_folder() {
        return Vec::new();
    }
    let Ok(entries) = std::fs::read_dir(&slot.path) else {
        return Vec::new();
    };
    let mut files: Vec<PathBuf> = entries
        .flatten()
        .map(|e| e.path())
        .filter(|p| p.is_file() && is_image(p))
        .collect();

    files.sort_by(|a, b| {
        let an = a.file_name().and_then(|s| s.to_str()).unwrap_or("");
        let bn = b.file_name().and_then(|s| s.to_str()).unwrap_or("");
        cmp_logical(an, bn)
    });

    let mut by_name: HashMap<String, PathBuf> = files
        .iter()
        .filter_map(|p| {
            p.file_name()
                .and_then(|s| s.to_str())
                .map(|n| (n.to_lowercase(), p.clone()))
        })
        .collect();

    let mut result = Vec::new();
    for name in &slot.order {
        if let Some(p) = by_name.remove(&name.to_lowercase()) {
            result.push(p);
        }
    }
    for p in files {
        let key = p
            .file_name()
            .and_then(|s| s.to_str())
            .unwrap_or("")
            .to_lowercase();
        if by_name.contains_key(&key) {
            result.push(p);
        }
    }
    result.truncate(MAX_IMAGES);
    result
}

pub fn slot_is_usable(slot: &Slot) -> bool {
    if slot.is_folder() {
        std::fs::read_dir(&slot.path)
            .ok()
            .is_some_and(|rd| rd.flatten().any(|e| e.path().is_file() && is_image(&e.path())))
    } else {
        let p = Path::new(&slot.path);
        p.is_file() && is_image(p)
    }
}
