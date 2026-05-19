using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;

namespace LifeAlertPlus.API.Services
{
    public class NearestHospitalService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<NearestHospitalService> _logger;

        private readonly ConcurrentDictionary<string, (DateTime CachedAt, HospitalRouteResult? Result)> _cache = new();
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(2);

        // Static fallback: major Romanian emergency hospitals used when Overpass API is unavailable.
        private static readonly (string Name, double Lat, double Lon)[] StaticHospitals =
        [
            ("Spitalul Clinic de Urgență Floreasca",          44.4676, 26.0834),
            ("Spitalul Universitar de Urgență București",      44.4376, 26.0985),
            ("Spitalul Județean de Urgență Cluj-Napoca",       46.7774, 23.5820),
            ("Spitalul Județean de Urgență Timișoara",         45.7551, 21.2213),
            ("Spitalul Clinic de Urgență Sf. Spiridon Iași",  47.1600, 27.5934),
            ("Spitalul Județean de Urgență Constanța",         44.1619, 28.6366),
            ("Spitalul Județean de Urgență Brașov",            45.6543, 25.6043),
            ("Spitalul Județean de Urgență Craiova",           44.3239, 23.8125),
            ("Spitalul Județean de Urgență Sibiu",             45.7958, 24.1506),
            ("Spitalul Județean de Urgență Oradea",            47.0636, 21.9362),
            ("Spitalul Județean de Urgență Arad",              46.1854, 21.3197),
            ("Spitalul Județean de Urgență Bacău",             46.5832, 26.9141),
            ("Spitalul Județean de Urgență Galați",            45.4474, 27.9976),
            ("Spitalul Județean de Urgență Pitești",           44.8568, 24.8670),
            ("Spitalul Județean de Urgență Ploiești",          44.9445, 26.0327),
            ("Spitalul Județean de Urgență Suceava",           47.6485, 26.2432),
            ("Spitalul Județean de Urgență Baia Mare",         47.6565, 23.5659),
            ("Spitalul Clinic Județean de Urgență Mureș",      46.5456, 24.5570),
            ("Spitalul Județean de Urgență Deva",              45.8812, 22.9118),
            ("Spitalul Județean de Urgență Alba Iulia",        46.0723, 23.5800),
        ];

        public NearestHospitalService(IHttpClientFactory httpClientFactory, ILogger<NearestHospitalService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public Task<HospitalRouteResult?> FindNearestAsync(double patLat, double patLon)
            => FindNearestAsync(patLat, patLon, CancellationToken.None);

        public async Task<HospitalRouteResult?> FindNearestAsync(double patLat, double patLon, CancellationToken cancellationToken)
        {
            var cacheKey = $"{patLat:F2},{patLon:F2}";
            if (_cache.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow - cached.CachedAt < CacheTtl)
                return cached.Result;

            // Try live Overpass API; fall back to static list on any failure.
            var result = await TryOverpassAsync(patLat, patLon, cancellationToken)
                         ?? FindNearestStatic(patLat, patLon);

            _cache[cacheKey] = (DateTime.UtcNow, result);

            if (result != null)
                _logger.LogInformation("Nearest hospital: {Name} — {Km} km, ~{Min} min",
                    result.HospitalName, result.DistanceKm, result.EstimatedMinutes);

            return result;
        }

        private async Task<HospitalRouteResult?> TryOverpassAsync(double patLat, double patLon, CancellationToken cancellationToken)
        {
            try
            {
                var latStr = patLat.ToString(CultureInfo.InvariantCulture);
                var lonStr = patLon.ToString(CultureInfo.InvariantCulture);
                var query = $"[out:json][timeout:15];" +
                            $"(node[\"amenity\"=\"hospital\"](around:50000,{latStr},{lonStr});" +
                            $"way[\"amenity\"=\"hospital\"](around:50000,{latStr},{lonStr}););" +
                            $"out center;";

                using var http = _httpClientFactory.CreateClient("Overpass");
                var response = await http.PostAsync(
                    "https://overpass-api.de/api/interpreter",
                    new StringContent(query, Encoding.UTF8, "text/plain"),
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Overpass API returned {StatusCode} for ({Lat},{Lon})", response.StatusCode, patLat, patLon);
                    return null;
                }

                var data = await response.Content.ReadFromJsonAsync<OverpassResponse>(cancellationToken: cancellationToken);
                if (data?.Elements == null || data.Elements.Length == 0)
                    return null;

                double tf = GetTrafficFactor();
                HospitalRouteResult? best = null;
                double bestKm = double.MaxValue;

                foreach (var el in data.Elements)
                {
                    double hLat = el.Lat ?? el.Center?.Lat ?? 0;
                    double hLon = el.Lon ?? el.Center?.Lon ?? 0;
                    if (hLat == 0 && hLon == 0) continue;

                    string name = el.Tags?.Name ?? el.Tags?.NameRo ?? "Spital";
                    double km = HaversineKm(patLat, patLon, hLat, hLon);

                    if (km < bestKm)
                    {
                        bestKm = km;
                        int minutes = (int)Math.Ceiling(km / 60.0 * 60.0 * tf);
                        best = new HospitalRouteResult
                        {
                            HospitalName     = name,
                            Latitude         = hLat,
                            Longitude        = hLon,
                            EstimatedMinutes = Math.Max(1, minutes),
                            DistanceKm       = Math.Round(km, 1)
                        };
                    }
                }

                return best;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Overpass API lookup failed for ({Lat},{Lon}), using static fallback", patLat, patLon);
                return null;
            }
        }

        private HospitalRouteResult? FindNearestStatic(double patLat, double patLon)
        {
            double tf = GetTrafficFactor();
            HospitalRouteResult? best = null;
            double bestKm = double.MaxValue;

            foreach (var (name, hLat, hLon) in StaticHospitals)
            {
                double km = HaversineKm(patLat, patLon, hLat, hLon);
                if (km < bestKm)
                {
                    bestKm = km;
                    int minutes = (int)Math.Ceiling(km / 60.0 * 60.0 * tf);
                    best = new HospitalRouteResult
                    {
                        HospitalName     = name,
                        Latitude         = hLat,
                        Longitude        = hLon,
                        EstimatedMinutes = Math.Max(1, minutes),
                        DistanceKm       = Math.Round(km, 1)
                    };
                }
            }

            return best;
        }

        private static double GetTrafficFactor()
        {
            int hour = (DateTime.UtcNow.Hour + 2) % 24;
            if (hour is >= 7 and <= 9 or >= 16 and <= 19) return 1.4;
            if (hour is <= 5 or >= 22) return 0.8;
            return 1.0;
        }

        private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371.0;
            double dLat = (lat2 - lat1) * Math.PI / 180.0;
            double dLon = (lon2 - lon1) * Math.PI / 180.0;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                     + Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0)
                     * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a)) * 1.3;
        }

        private sealed class OverpassResponse
        {
            [JsonPropertyName("elements")]
            public OverpassElement[]? Elements { get; set; }
        }

        private sealed class OverpassElement
        {
            [JsonPropertyName("lat")]
            public double? Lat { get; set; }

            [JsonPropertyName("lon")]
            public double? Lon { get; set; }

            [JsonPropertyName("center")]
            public OverpassCenter? Center { get; set; }

            [JsonPropertyName("tags")]
            public OverpassTags? Tags { get; set; }
        }

        private sealed class OverpassCenter
        {
            [JsonPropertyName("lat")]
            public double Lat { get; set; }

            [JsonPropertyName("lon")]
            public double Lon { get; set; }
        }

        private sealed class OverpassTags
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("name:ro")]
            public string? NameRo { get; set; }
        }
    }

    public class HospitalRouteResult
    {
        public string HospitalName { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int EstimatedMinutes { get; set; }
        public double DistanceKm { get; set; }
    }
}
