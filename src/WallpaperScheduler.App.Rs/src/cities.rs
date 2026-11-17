use serde::Serialize;
use windows::Win32::System::Time::{
    DYNAMIC_TIME_ZONE_INFORMATION, EnumDynamicTimeZoneInformation, GetDynamicTimeZoneInformation,
};

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct City {
    pub name: &'static str,
    pub lat: f64,
    pub lon: f64,
    pub time_zone_id: &'static str,
}

pub const CITIES: &[City] = &[
    City { name: "Sydney, Australia", lat: -33.8688, lon: 151.2093, time_zone_id: "AUS Eastern Standard Time" },
    City { name: "Melbourne, Australia", lat: -37.8136, lon: 144.9631, time_zone_id: "AUS Eastern Standard Time" },
    City { name: "Brisbane, Australia", lat: -27.4698, lon: 153.0251, time_zone_id: "E. Australia Standard Time" },
    City { name: "Adelaide, Australia", lat: -34.9285, lon: 138.6007, time_zone_id: "Cen. Australia Standard Time" },
    City { name: "Perth, Australia", lat: -31.9505, lon: 115.8605, time_zone_id: "W. Australia Standard Time" },
    City { name: "Auckland, New Zealand", lat: -36.8485, lon: 174.7633, time_zone_id: "New Zealand Standard Time" },
    City { name: "Tokyo, Japan", lat: 35.6762, lon: 139.6503, time_zone_id: "Tokyo Standard Time" },
    City { name: "Seoul, South Korea", lat: 37.5665, lon: 126.9780, time_zone_id: "Korea Standard Time" },
    City { name: "Beijing, China", lat: 39.9042, lon: 116.4074, time_zone_id: "China Standard Time" },
    City { name: "Singapore", lat: 1.3521, lon: 103.8198, time_zone_id: "Singapore Standard Time" },
    City { name: "Bangkok, Thailand", lat: 13.7563, lon: 100.5018, time_zone_id: "SE Asia Standard Time" },
    City { name: "Mumbai, India", lat: 19.0760, lon: 72.8777, time_zone_id: "India Standard Time" },
    City { name: "Dubai, UAE", lat: 25.2048, lon: 55.2708, time_zone_id: "Arabian Standard Time" },
    City { name: "Istanbul, Türkiye", lat: 41.0082, lon: 28.9784, time_zone_id: "Turkey Standard Time" },
    City { name: "Moscow, Russia", lat: 55.7558, lon: 37.6173, time_zone_id: "Russian Standard Time" },
    City { name: "Cairo, Egypt", lat: 30.0444, lon: 31.2357, time_zone_id: "Egypt Standard Time" },
    City { name: "Johannesburg, South Africa", lat: -26.2041, lon: 28.0473, time_zone_id: "South Africa Standard Time" },
    City { name: "London, United Kingdom", lat: 51.5074, lon: -0.1278, time_zone_id: "GMT Standard Time" },
    City { name: "Dublin, Ireland", lat: 53.3498, lon: -6.2603, time_zone_id: "GMT Standard Time" },
    City { name: "Paris, France", lat: 48.8566, lon: 2.3522, time_zone_id: "Romance Standard Time" },
    City { name: "Madrid, Spain", lat: 40.4168, lon: -3.7038, time_zone_id: "Romance Standard Time" },
    City { name: "Berlin, Germany", lat: 52.5200, lon: 13.4050, time_zone_id: "W. Europe Standard Time" },
    City { name: "Rome, Italy", lat: 41.9028, lon: 12.4964, time_zone_id: "W. Europe Standard Time" },
    City { name: "Stockholm, Sweden", lat: 59.3293, lon: 18.0686, time_zone_id: "W. Europe Standard Time" },
    City { name: "Athens, Greece", lat: 37.9838, lon: 23.7275, time_zone_id: "GTB Standard Time" },
    City { name: "São Paulo, Brazil", lat: -23.5505, lon: -46.6333, time_zone_id: "E. South America Standard Time" },
    City { name: "Buenos Aires, Argentina", lat: -34.6037, lon: -58.3816, time_zone_id: "Argentina Standard Time" },
    City { name: "Mexico City, Mexico", lat: 19.4326, lon: -99.1332, time_zone_id: "Central Standard Time (Mexico)" },
    City { name: "New York, USA", lat: 40.7128, lon: -74.0060, time_zone_id: "Eastern Standard Time" },
    City { name: "Toronto, Canada", lat: 43.6532, lon: -79.3832, time_zone_id: "Eastern Standard Time" },
    City { name: "Chicago, USA", lat: 41.8781, lon: -87.6298, time_zone_id: "Central Standard Time" },
    City { name: "Denver, USA", lat: 39.7392, lon: -104.9903, time_zone_id: "Mountain Standard Time" },
    City { name: "Los Angeles, USA", lat: 34.0522, lon: -118.2437, time_zone_id: "Pacific Standard Time" },
    City { name: "Vancouver, Canada", lat: 49.2827, lon: -123.1207, time_zone_id: "Pacific Standard Time" },
    City { name: "Anchorage, USA", lat: 61.2181, lon: -149.9003, time_zone_id: "Alaskan Standard Time" },
    City { name: "Honolulu, USA", lat: 21.3069, lon: -157.8583, time_zone_id: "Hawaiian Standard Time" },
];

pub fn local_tz_id() -> String {
    let mut dtzi = DYNAMIC_TIME_ZONE_INFORMATION::default();
    unsafe { GetDynamicTimeZoneInformation(&mut dtzi) };
    utf16_z(&dtzi.TimeZoneKeyName)
}

pub fn default_city() -> &'static City {
    let local = local_tz_id();
    CITIES
        .iter()
        .find(|c| c.time_zone_id == local)
        .unwrap_or(&CITIES[0])
}

pub fn all_tz_ids() -> Vec<String> {
    let mut list = Vec::new();
    let mut idx = 0u32;
    loop {
        let mut dtzi = DYNAMIC_TIME_ZONE_INFORMATION::default();
        if unsafe { EnumDynamicTimeZoneInformation(idx, &mut dtzi) } != 0 {
            break;
        }
        let id = utf16_z(&dtzi.TimeZoneKeyName);
        if !id.is_empty() {
            list.push(id);
        }
        idx += 1;
    }
    list.sort();
    list
}

fn utf16_z(buf: &[u16]) -> String {
    let len = buf.iter().position(|&c| c == 0).unwrap_or(buf.len());
    String::from_utf16_lossy(&buf[..len])
}
