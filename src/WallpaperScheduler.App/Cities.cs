namespace WallpaperScheduler.App;

public sealed record City(string Name, double Lat, double Lon, string TimeZoneId);

/// <summary>Small offline city list so sunrise/sunset needs no internet and no location permission.</summary>
public static class Cities
{
    public static readonly City[] All =
    {
        new("Sydney, Australia", -33.8688, 151.2093, "AUS Eastern Standard Time"),
        new("Melbourne, Australia", -37.8136, 144.9631, "AUS Eastern Standard Time"),
        new("Brisbane, Australia", -27.4698, 153.0251, "E. Australia Standard Time"),
        new("Adelaide, Australia", -34.9285, 138.6007, "Cen. Australia Standard Time"),
        new("Perth, Australia", -31.9505, 115.8605, "W. Australia Standard Time"),
        new("Auckland, New Zealand", -36.8485, 174.7633, "New Zealand Standard Time"),
        new("Tokyo, Japan", 35.6762, 139.6503, "Tokyo Standard Time"),
        new("Seoul, South Korea", 37.5665, 126.9780, "Korea Standard Time"),
        new("Beijing, China", 39.9042, 116.4074, "China Standard Time"),
        new("Singapore", 1.3521, 103.8198, "Singapore Standard Time"),
        new("Bangkok, Thailand", 13.7563, 100.5018, "SE Asia Standard Time"),
        new("Mumbai, India", 19.0760, 72.8777, "India Standard Time"),
        new("Dubai, UAE", 25.2048, 55.2708, "Arabian Standard Time"),
        new("Istanbul, Türkiye", 41.0082, 28.9784, "Turkey Standard Time"),
        new("Moscow, Russia", 55.7558, 37.6173, "Russian Standard Time"),
        new("Cairo, Egypt", 30.0444, 31.2357, "Egypt Standard Time"),
        new("Johannesburg, South Africa", -26.2041, 28.0473, "South Africa Standard Time"),
        new("London, United Kingdom", 51.5074, -0.1278, "GMT Standard Time"),
        new("Dublin, Ireland", 53.3498, -6.2603, "GMT Standard Time"),
        new("Paris, France", 48.8566, 2.3522, "Romance Standard Time"),
        new("Madrid, Spain", 40.4168, -3.7038, "Romance Standard Time"),
        new("Berlin, Germany", 52.5200, 13.4050, "W. Europe Standard Time"),
        new("Rome, Italy", 41.9028, 12.4964, "W. Europe Standard Time"),
        new("Stockholm, Sweden", 59.3293, 18.0686, "W. Europe Standard Time"),
        new("Athens, Greece", 37.9838, 23.7275, "GTB Standard Time"),
        new("São Paulo, Brazil", -23.5505, -46.6333, "E. South America Standard Time"),
        new("Buenos Aires, Argentina", -34.6037, -58.3816, "Argentina Standard Time"),
        new("Mexico City, Mexico", 19.4326, -99.1332, "Central Standard Time (Mexico)"),
        new("New York, USA", 40.7128, -74.0060, "Eastern Standard Time"),
        new("Toronto, Canada", 43.6532, -79.3832, "Eastern Standard Time"),
        new("Chicago, USA", 41.8781, -87.6298, "Central Standard Time"),
        new("Denver, USA", 39.7392, -104.9903, "Mountain Standard Time"),
        new("Los Angeles, USA", 34.0522, -118.2437, "Pacific Standard Time"),
        new("Vancouver, Canada", 49.2827, -123.1207, "Pacific Standard Time"),
        new("Anchorage, USA", 61.2181, -149.9003, "Alaskan Standard Time"),
        new("Honolulu, USA", 21.3069, -157.8583, "Hawaiian Standard Time"),
    };

    /// <summary>Best default: a city in the machine's current time zone.</summary>
    public static City Default() =>
        All.FirstOrDefault(c => c.TimeZoneId == TimeZoneInfo.Local.Id) ?? All[0];
}
