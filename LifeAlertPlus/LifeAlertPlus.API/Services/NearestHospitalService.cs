using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;

namespace LifeAlertPlus.API.Services
{
    // Serviciu pentru găsirea celui mai apropiat spital de urgență față de coordonatele GPS ale pacientului.
    // Strategia în două niveluri:
    //   1. Interogare live Overpass API (OpenStreetMap) — caută spitale în raza de 50 km
    //   2. Fallback static cu 20 spitale județene majore din România (dacă Overpass nu răspunde)
    // Rezultatele sunt cașate 2 ore per locație (rotunjită la 2 zecimale → ~1km grid)
    // pentru a reduce apelurile externe la Overpass API.
    // Distanța se calculează prin formula Haversine cu factor de detour 1.3 (drumurile nu sunt în linie dreaptă).
    public class NearestHospitalService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<NearestHospitalService> _logger;

        // Cache thread-safe: cheie = "lat,lon" (2 zecimale), valoare = (momentul cașării, rezultat)
        private readonly ConcurrentDictionary<string, (DateTime CachedAt, HospitalRouteResult? Result)> _cache = new();
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(2); // Rezultatele se refolosesc 2 ore

        // Lista statică de fallback cu spitalele județene de urgență principale din România
        // Folosite când Overpass API nu este accesibil sau returnează eroare
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
            _logger            = logger;
        }

        // Supraîncărcată fără CancellationToken (apeluri din cod asincron simplu)
        public Task<HospitalRouteResult?> FindNearestAsync(double patLat, double patLon)
            => FindNearestAsync(patLat, patLon, CancellationToken.None);

        // Găsește cel mai apropiat spital față de coordonatele GPS ale pacientului
        public async Task<HospitalRouteResult?> FindNearestAsync(double patLat, double patLon, CancellationToken cancellationToken)
        {
            // Cheia de cache: coordonate rotunjite la 2 zecimale (~1km precizie)
            var cacheKey = $"{patLat:F2},{patLon:F2}";
            if (_cache.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow - cached.CachedAt < CacheTtl)
                return cached.Result; // Returnăm din cache

            // Interogăm Overpass API → dacă eșuează, folosim lista statică
            var result = await TryOverpassAsync(patLat, patLon, cancellationToken)
                         ?? FindNearestStatic(patLat, patLon);

            _cache[cacheKey] = (DateTime.UtcNow, result); // Salvăm în cache

            if (result != null)
                _logger.LogInformation("Nearest hospital: {Name} — {Km} km, ~{Min} min",
                    result.HospitalName, result.DistanceKm, result.EstimatedMinutes);

            return result;
        }

        // Caută spitale prin Overpass API (OpenStreetMap) în raza de 50 km
        private async Task<HospitalRouteResult?> TryOverpassAsync(double patLat, double patLon, CancellationToken cancellationToken)
        {
            try
            {
                // Folosim InvariantCulture pentru a genera "44.4676" nu "44,4676" (separator zecimal)
                var latStr = patLat.ToString(CultureInfo.InvariantCulture);
                var lonStr = patLon.ToString(CultureInfo.InvariantCulture);
                // Query Overpass QL: caută noduri și drumuri cu tag amenity=hospital în raza de 50 km
                var query = $"[out:json][timeout:15];" +
                            $"(node[\"amenity\"=\"hospital\"](around:50000,{latStr},{lonStr});" +
                            $"way[\"amenity\"=\"hospital\"](around:50000,{latStr},{lonStr}););" +
                            $"out center;"; // "out center" returnează centrul geometric al clădirii

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
                    return null; // Niciun spital în raza de 50 km

                double tf = GetTrafficFactor(); // Factorul de trafic bazat pe ora curentă
                HospitalRouteResult? best = null;
                double bestKm = double.MaxValue;

                foreach (var el in data.Elements)
                {
                    // Nodurile au lat/lon direct; drumurile (way) au doar "center"
                    double hLat = el.Lat ?? el.Center?.Lat ?? 0;
                    double hLon = el.Lon ?? el.Center?.Lon ?? 0;
                    if (hLat == 0 && hLon == 0) continue; // Date incomplete

                    string name = el.Tags?.Name ?? el.Tags?.NameRo ?? "Spital"; // Preferăm "name:ro"
                    double km = HaversineKm(patLat, patLon, hLat, hLon); // Distanța în km

                    if (km < bestKm)
                    {
                        bestKm = km;
                        // Timp estimat: km / 60 kmh * 60 min * factor trafic
                        int minutes = (int)Math.Ceiling(km / 60.0 * 60.0 * tf);
                        best = new HospitalRouteResult
                        {
                            HospitalName     = name,
                            Latitude         = hLat,
                            Longitude        = hLon,
                            EstimatedMinutes = Math.Max(1, minutes), // Minimum 1 minut
                            DistanceKm       = Math.Round(km, 1)
                        };
                    }
                }

                return best;
            }
            catch (Exception ex)
            {
                // Overpass nu răspunde sau timeout → trecem la fallback static
                _logger.LogWarning(ex, "Overpass API lookup failed for ({Lat},{Lon}), using static fallback", patLat, patLon);
                return null;
            }
        }

        // Caută cel mai apropiat spital din lista statică (fallback când Overpass eșuează)
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

        // Factorul de trafic bazat pe ora locală (UTC+2 pentru România)
        // Ore de vârf (7-9, 16-19): x1.4 (mai lent); noapte (22-5): x0.8 (mai rapid); altfel: x1.0
        private static double GetTrafficFactor()
        {
            int hour = (DateTime.UtcNow.Hour + 2) % 24; // Convertim UTC → ora locală RO (UTC+2)
            if (hour is >= 7 and <= 9 or >= 16 and <= 19) return 1.4; // Ore de vârf
            if (hour is <= 5 or >= 22) return 0.8;                   // Noapte
            return 1.0;                                               // Ore normale
        }

        // Formula Haversine: calculează distanța în km pe suprafața sferică a Pământului
        // Multiplică cu 1.3 ca factor de detour (drumurile reale sunt cu ~30% mai lungi decât linia dreaptă)
        private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371.0; // Raza Pământului în km
            double dLat = (lat2 - lat1) * Math.PI / 180.0;
            double dLon = (lon2 - lon1) * Math.PI / 180.0;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                     + Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0)
                     * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a)) * 1.3; // x1.3 factor detour
        }

        // Modelele de răspuns Overpass API (deserializate din JSON)
        private sealed class OverpassResponse
        {
            [JsonPropertyName("elements")]
            public OverpassElement[]? Elements { get; set; } // Lista elementelor (spitale găsite)
        }

        private sealed class OverpassElement
        {
            [JsonPropertyName("lat")]
            public double? Lat { get; set; } // Latitudine (pentru noduri OSM)

            [JsonPropertyName("lon")]
            public double? Lon { get; set; } // Longitudine (pentru noduri OSM)

            [JsonPropertyName("center")]
            public OverpassCenter? Center { get; set; } // Centrul geometric (pentru drumuri/clădiri OSM)

            [JsonPropertyName("tags")]
            public OverpassTags? Tags { get; set; } // Metadata: nume etc.
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
            public string? Name { get; set; } // Numele spitalului (orice limbă)

            [JsonPropertyName("name:ro")]
            public string? NameRo { get; set; } // Numele în română (prioritizat dacă există)
        }
    }

    // Rezultatul căutării: spitalul cel mai apropiat cu distanța și timpul estimat de deplasare
    public class HospitalRouteResult
    {
        public string HospitalName { get; set; } = string.Empty; // Numele spitalului
        public double Latitude { get; set; }                     // Coordonata GPS (latitudine)
        public double Longitude { get; set; }                    // Coordonata GPS (longitudine)
        public int EstimatedMinutes { get; set; }                // Timp estimat de deplasare cu mașina (minute)
        public double DistanceKm { get; set; }                   // Distanța în km (cu detour 1.3x)
    }
}
