use chrono::{DateTime, Datelike, Duration, NaiveDate, NaiveDateTime, NaiveTime, Timelike, Utc};
use windows::Win32::Foundation::SYSTEMTIME;
use windows::Win32::System::Time::{
    DYNAMIC_TIME_ZONE_INFORMATION, EnumDynamicTimeZoneInformation, GetDynamicTimeZoneInformation,
    SystemTimeToTzSpecificLocalTimeEx,
};

#[derive(Clone)]
pub struct WinTz(pub DYNAMIC_TIME_ZONE_INFORMATION);

impl WinTz {
    pub fn local() -> Self {
        let mut dtzi = DYNAMIC_TIME_ZONE_INFORMATION::default();
        unsafe {
            GetDynamicTimeZoneInformation(&mut dtzi);
        }
        Self(dtzi)
    }

    pub fn from_id(id: &str) -> Self {
        let target: Vec<u16> = id.encode_utf16().collect();
        let mut idx = 0u32;
        loop {
            let mut dtzi = DYNAMIC_TIME_ZONE_INFORMATION::default();
            let err = unsafe { EnumDynamicTimeZoneInformation(idx, &mut dtzi) };
            if err != 0 {
                break;
            }
            let key_len = dtzi
                .TimeZoneKeyName
                .iter()
                .position(|&c| c == 0)
                .unwrap_or(dtzi.TimeZoneKeyName.len());
            if dtzi.TimeZoneKeyName[..key_len] == target[..] {
                return Self(dtzi);
            }
            idx += 1;
        }
        Self::local()
    }

    pub fn utc_to_local(&self, utc: DateTime<Utc>) -> NaiveDateTime {
        let st_utc = SYSTEMTIME {
            wYear: utc.year() as u16,
            wMonth: utc.month() as u16,
            wDay: utc.day() as u16,
            wHour: utc.hour() as u16,
            wMinute: utc.minute() as u16,
            wSecond: utc.second() as u16,
            wMilliseconds: 0,
            wDayOfWeek: 0,
        };
        let mut st_local = SYSTEMTIME::default();
        let ok = unsafe { SystemTimeToTzSpecificLocalTimeEx(Some(&self.0), &st_utc, &mut st_local) };
        if ok.is_ok()
            && let Some(d) = NaiveDate::from_ymd_opt(
                st_local.wYear as i32,
                st_local.wMonth as u32,
                st_local.wDay as u32,
            )
            && let Some(t) = NaiveTime::from_hms_opt(
                st_local.wHour as u32,
                st_local.wMinute as u32,
                st_local.wSecond as u32,
            )
        {
            return NaiveDateTime::new(d, t);
        }
        utc.naive_utc()
    }
}

pub fn sunrise(date: NaiveDate, lat: f64, lon: f64, tz: &WinTz) -> Option<NaiveDateTime> {
    compute(date, lat, lon, tz, true)
}

pub fn sunset(date: NaiveDate, lat: f64, lon: f64, tz: &WinTz) -> Option<NaiveDateTime> {
    compute(date, lat, lon, tz, false)
}

fn compute(
    date: NaiveDate,
    lat: f64,
    lon: f64,
    tz: &WinTz,
    rise: bool,
) -> Option<NaiveDateTime> {
    use chrono::Datelike;
    let jd = julian_day(date.year(), date.month() as i32, date.day() as i32);
    let mut minutes = event_minutes_utc(jd, lat, lon, rise);
    if minutes.is_nan() {
        return None;
    }
    minutes = event_minutes_utc(jd + minutes / 1440.0, lat, lon, rise);
    if minutes.is_nan() {
        return None;
    }
    let utc_midnight = NaiveDateTime::new(date, NaiveTime::from_hms_opt(0, 0, 0)?).and_utc();
    let secs = (minutes * 60.0).round() as i64;
    let utc = utc_midnight + Duration::seconds(secs);
    Some(tz.utc_to_local(utc))
}

fn event_minutes_utc(jd: f64, lat: f64, lon: f64, rise: bool) -> f64 {
    let t = (jd - 2451545.0) / 36525.0;
    let eq_time = equation_of_time(t);
    let decl = sun_declination(t).to_radians();
    let lat_r = lat.to_radians();

    let cos_ha = 90.833f64.to_radians().cos() / (lat_r.cos() * decl.cos())
        - lat_r.tan() * decl.tan();
    if !(-1.0..=1.0).contains(&cos_ha) {
        return f64::NAN;
    }

    let mut ha = cos_ha.acos().to_degrees();
    if !rise {
        ha = -ha;
    }
    720.0 - 4.0 * (lon + ha) - eq_time
}

fn julian_day(mut y: i32, mut m: i32, d: i32) -> f64 {
    if m <= 2 {
        y -= 1;
        m += 12;
    }
    let a = y / 100;
    let b = 2 - a + a / 4;
    (365.25 * (y + 4716) as f64).floor()
        + (30.6001 * (m + 1) as f64).floor()
        + d as f64
        + b as f64
        - 1524.5
}

fn equation_of_time(t: f64) -> f64 {
    let l0 = ((280.46646 + t * (36000.76983 + t * 0.0003032)).rem_euclid(360.0)).to_radians();
    let m = (357.52911 + t * (35999.05029 - 0.0001537 * t)).to_radians();
    let e = 0.016708634 - t * (0.000042037 + 0.0000001267 * t);
    let eps = obliquity_corrected(t).to_radians();
    let y = (eps / 2.0).tan().powi(2);
    let eq = y * (2.0 * l0).sin()
        - 2.0 * e * m.sin()
        + 4.0 * e * y * m.sin() * (2.0 * l0).cos()
        - 0.5 * y * y * (4.0 * l0).sin()
        - 1.25 * e * e * (2.0 * m).sin();
    eq.to_degrees() * 4.0
}

fn sun_declination(t: f64) -> f64 {
    let eps = obliquity_corrected(t).to_radians();
    let lambda = sun_apparent_longitude(t).to_radians();
    (eps.sin() * lambda.sin()).asin().to_degrees()
}

fn sun_apparent_longitude(t: f64) -> f64 {
    let l0 = (280.46646 + t * (36000.76983 + t * 0.0003032)).rem_euclid(360.0);
    let m = (357.52911 + t * (35999.05029 - 0.0001537 * t)).to_radians();
    let c = m.sin() * (1.914602 - t * (0.004817 + 0.000014 * t))
        + (2.0 * m).sin() * (0.019993 - 0.000101 * t)
        + (3.0 * m).sin() * 0.000289;
    let true_long = l0 + c;
    let omega = 125.04 - 1934.136 * t;
    true_long - 0.00569 - 0.00478 * omega.to_radians().sin()
}

fn obliquity_corrected(t: f64) -> f64 {
    let seconds = 21.448 - t * (46.815 + t * (0.00059 - t * 0.001813));
    let e0 = 23.0 + (26.0 + seconds / 60.0) / 60.0;
    let omega = 125.04 - 1934.136 * t;
    e0 + 0.00256 * omega.to_radians().cos()
}
