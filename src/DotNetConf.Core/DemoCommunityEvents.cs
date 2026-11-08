#if DEBUG
namespace DotNetConf.Core;

/// <summary>Synthetic Debug-only map content; never part of the agenda or its cache.</summary>
public static class DemoCommunityEvents
{
    public static IReadOnlyList<CommunityEvent> Create()
    {
        (string City, string Country, double Latitude, double Longitude)[] cities =
        [
            ("Seattle", "United States", 47.6062, -122.3321),
            ("San Francisco", "United States", 37.7749, -122.4194),
            ("Los Angeles", "United States", 34.0522, -118.2437),
            ("Austin", "United States", 30.2672, -97.7431),
            ("Chicago", "United States", 41.8781, -87.6298),
            ("New York", "United States", 40.7128, -74.0060),
            ("Vancouver", "Canada", 49.2827, -123.1207),
            ("Toronto", "Canada", 43.6532, -79.3832),
            ("Montreal", "Canada", 45.5019, -73.5674),
            ("Mexico City", "Mexico", 19.4326, -99.1332),
            ("San Jose", "Costa Rica", 9.9281, -84.0907),
            ("Bogota", "Colombia", 4.7110, -74.0721),
            ("Lima", "Peru", -12.0464, -77.0428),
            ("Santiago", "Chile", -33.4489, -70.6693),
            ("Buenos Aires", "Argentina", -34.6037, -58.3816),
            ("Sao Paulo", "Brazil", -23.5505, -46.6333),
            ("Rio de Janeiro", "Brazil", -22.9068, -43.1729),
            ("London", "United Kingdom", 51.5074, -0.1278),
            ("Dublin", "Ireland", 53.3498, -6.2603),
            ("Amsterdam", "Netherlands", 52.3676, 4.9041),
            ("Paris", "France", 48.8566, 2.3522),
            ("Madrid", "Spain", 40.4168, -3.7038),
            ("Lisbon", "Portugal", 38.7223, -9.1393),
            ("Berlin", "Germany", 52.5200, 13.4050),
            ("Copenhagen", "Denmark", 55.6761, 12.5683),
            ("Stockholm", "Sweden", 59.3293, 18.0686),
            ("Helsinki", "Finland", 60.1699, 24.9384),
            ("Warsaw", "Poland", 52.2297, 21.0122),
            ("Prague", "Czechia", 50.0755, 14.4378),
            ("Rome", "Italy", 41.9028, 12.4964),
            ("Istanbul", "Turkiye", 41.0082, 28.9784),
            ("Cairo", "Egypt", 30.0444, 31.2357),
            ("Lagos", "Nigeria", 6.5244, 3.3792),
            ("Nairobi", "Kenya", -1.2921, 36.8219),
            ("Cape Town", "South Africa", -33.9249, 18.4241),
            ("Johannesburg", "South Africa", -26.2041, 28.0473),
            ("Dubai", "United Arab Emirates", 25.2048, 55.2708),
            ("Mumbai", "India", 19.0760, 72.8777),
            ("Bengaluru", "India", 12.9716, 77.5946),
            ("Delhi", "India", 28.6139, 77.2090),
            ("Singapore", "Singapore", 1.3521, 103.8198),
            ("Bangkok", "Thailand", 13.7563, 100.5018),
            ("Ho Chi Minh City", "Vietnam", 10.8231, 106.6297),
            ("Jakarta", "Indonesia", -6.2088, 106.8456),
            ("Manila", "Philippines", 14.5995, 120.9842),
            ("Seoul", "South Korea", 37.5665, 126.9780),
            ("Tokyo", "Japan", 35.6762, 139.6503),
            ("Sydney", "Australia", -33.8688, 151.2093),
            ("Melbourne", "Australia", -37.8136, 144.9631),
            ("Auckland", "New Zealand", -36.8485, 174.7633)
        ];

        return cities.SelectMany((city, index) => new[]
        {
            new CommunityEvent
            {
                Id = $"demo-watch-{index:D2}",
                Name = $"{city.City} watch party (demo)",
                Kind = "watch-party",
                Venue = "Example Community Studio",
                City = city.City,
                Country = city.Country,
                Latitude = city.Latitude,
                Longitude = city.Longitude,
                WebsiteUrl = "https://example.com/"
            },
            new CommunityEvent
            {
                Id = $"demo-talks-{index:D2}",
                Name = $"{city.City} developer night (demo)",
                Kind = "in-person",
                Venue = "Example Developer Loft",
                City = city.City,
                Country = city.Country,
                // Nearby, distinct venues make clusters resolve into individual pins as you zoom.
                Latitude = city.Latitude + 0.018,
                Longitude = city.Longitude + 0.024,
                WebsiteUrl = "https://example.com/"
            }
        }).ToArray();
    }
}
#endif
