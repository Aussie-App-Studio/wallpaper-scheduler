use chrono::{DateTime, NaiveTime, Utc};
use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::path::{Path, PathBuf};

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, Default)]
pub enum StartKind {
    #[default]
    Time,
    Sunrise,
    Sunset,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, Default)]
pub enum FitMode {
    #[default]
    Fill,
    Fit,
    Stretch,
    Tile,
    Center,
    Span,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, Default)]
pub enum WindowsThemeMode {
    #[default]
    LeaveAlone,
    Light,
    Dark,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, Default)]
pub enum AppThemeMode {
    #[default]
    System,
    Light,
    Dark,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, Default)]
pub enum DayFilter {
    #[default]
    EveryDay,
    Weekdays,
    Weekends,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase", default)]
pub struct Slot {
    pub name: String,
    pub start: StartKind,
    pub time_of_day: String,
    pub offset_minutes: i32,
    pub path: String,
    pub shuffle: bool,
    pub slideshow_interval_minutes: i32,
    pub fit: FitMode,
    pub theme: WindowsThemeMode,
    pub days: DayFilter,
    pub monitor_paths: HashMap<String, String>,
    pub order: Vec<String>,
}

impl Default for Slot {
    fn default() -> Self {
        Self {
            name: String::new(),
            start: StartKind::Time,
            time_of_day: "08:00:00".into(),
            offset_minutes: 0,
            path: String::new(),
            shuffle: false,
            slideshow_interval_minutes: 30,
            fit: FitMode::Fill,
            theme: WindowsThemeMode::LeaveAlone,
            days: DayFilter::EveryDay,
            monitor_paths: HashMap::new(),
            order: Vec::new(),
        }
    }
}

impl Slot {
    pub fn is_folder(&self) -> bool {
        !self.path.is_empty() && Path::new(&self.path).is_dir()
    }

    pub fn parsed_time(&self) -> NaiveTime {
        parse_dotnet_timespan(&self.time_of_day)
            .unwrap_or_else(|| NaiveTime::from_hms_opt(8, 0, 0).unwrap())
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "PascalCase", default)]
pub struct GeoLocation {
    pub name: String,
    pub latitude: f64,
    pub longitude: f64,
    pub time_zone_id: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase", default)]
pub struct AppConfig {
    pub version: i32,
    pub paused: bool,
    pub app_theme: AppThemeMode,
    pub paused_until_utc: Option<String>,
    pub same_on_all_displays: bool,
    pub location: Option<GeoLocation>,
    pub slots: Vec<Slot>,
}

impl Default for AppConfig {
    fn default() -> Self {
        Self {
            version: 1,
            paused: false,
            app_theme: AppThemeMode::System,
            paused_until_utc: None,
            same_on_all_displays: true,
            location: None,
            slots: Vec::new(),
        }
    }
}

impl AppConfig {
    pub fn is_paused_at(&self, utc_now: DateTime<Utc>) -> Option<Option<DateTime<Utc>>> {
        if self.paused {
            return Some(None);
        }
        if let Some(ref s) = self.paused_until_utc
            && let Ok(until) = DateTime::parse_from_rfc3339(s)
        {
            let until_utc = until.with_timezone(&Utc);
            if utc_now < until_utc {
                return Some(Some(until_utc));
            }
        }
        None
    }
}

pub fn default_config_path() -> PathBuf {
    let base = std::env::var_os("LOCALAPPDATA")
        .map(PathBuf::from)
        .unwrap_or_else(|| PathBuf::from("."));
    base.join("WallpaperScheduler").join("config.json")
}

pub fn load_config(path: &Path) -> AppConfig {
    let Ok(text) = std::fs::read_to_string(path) else {
        return AppConfig {
            version: 1,
            same_on_all_displays: true,
            ..Default::default()
        };
    };
    let text = text.trim_start_matches('\u{feff}');
    serde_json::from_str(text).unwrap_or_else(|_| AppConfig {
        version: 1,
        same_on_all_displays: true,
        ..Default::default()
    })
}

/// Parses .NET TimeSpan string like "08:00:00" or "08:00:00.1234567".
pub fn parse_dotnet_timespan(s: &str) -> Option<NaiveTime> {
    let main = s.split('.').next()?;
    NaiveTime::parse_from_str(main, "%H:%M:%S").ok()
}

#[allow(dead_code)]
pub fn save_config(config: &AppConfig, path: &Path) -> std::io::Result<()> {
    if let Some(dir) = path.parent() {
        std::fs::create_dir_all(dir)?;
    }
    let tmp = path.with_extension("json.tmp");
    let json = serde_json::to_string_pretty(config).map_err(std::io::Error::other)?;
    std::fs::write(&tmp, json)?;
    std::fs::rename(&tmp, path)?;
    Ok(())
}
