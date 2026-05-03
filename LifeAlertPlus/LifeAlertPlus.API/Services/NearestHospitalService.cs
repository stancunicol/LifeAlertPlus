using Microsoft.Extensions.Logging;

namespace LifeAlertPlus.API.Services
{
    public class NearestHospitalService
    {
        private readonly ILogger<NearestHospitalService> _logger;

        private record HospitalData(string Name, string City, double Lat, double Lon);

        // ── Static hospital list ─────────────────────────────────────────────────
        private static readonly HospitalData[] Hospitals =
        [
            new("Spitalul Clinic de Urgență \"Floreasca\"",     "București",   44.4676, 26.0834),
            new("Spitalul Universitar de Urgență București",    "București",   44.4376, 26.0985),
            new("Spitalul Județean de Urgență Cluj-Napoca",     "Cluj-Napoca", 46.7774, 23.5820),
            new("Spitalul Județean de Urgență Timișoara",       "Timișoara",   45.7551, 21.2213),
            new("Spitalul Clinic de Urgență \"Sf. Spiridon\"", "Iași",        47.1600, 27.5934),
            new("Spitalul Județean de Urgență Constanța",       "Constanța",   44.1619, 28.6366),
            new("Spitalul Județean de Urgență Brașov",          "Brașov",      45.6543, 25.6043),
            new("Spitalul Județean de Urgență Craiova",         "Craiova",     44.3239, 23.8125),
            new("Spitalul Județean de Urgență Sibiu",           "Sibiu",       45.7958, 24.1506),
            new("Spitalul Județean de Urgență Oradea",          "Oradea",      47.0636, 21.9362),
            new("Spitalul Județean de Urgență Arad",            "Arad",        46.1854, 21.3197),
            new("Spitalul Județean de Urgență Bacău",           "Bacău",       46.5832, 26.9141),
            new("Spitalul Județean de Urgență Galați",          "Galați",      45.4474, 27.9976),
            new("Spitalul Județean de Urgență Pitești",         "Pitești",     44.8568, 24.8670),
            new("Spitalul Județean de Urgență Ploiești",        "Ploiești",    44.9445, 26.0327),
            new("Spitalul Județean de Urgență Suceava",         "Suceava",     47.6485, 26.2432),
            new("Spitalul Județean de Urgență Baia Mare",       "Baia Mare",   47.6565, 23.5659),
            new("Spitalul Clinic Județean de Urgență Mureș",   "Târgu Mureș", 46.5456, 24.5570),
            new("Spitalul Județean de Urgență Deva",            "Deva",        45.8812, 22.9118),
            new("Spitalul Județean de Urgență Alba Iulia",      "Alba Iulia",  46.0723, 23.5800),
        ];

        // ── Romanian city nodes ──────────────────────────────────────────────────
        private static readonly IReadOnlyDictionary<string, (double Lat, double Lon)> CityCoords =
            new Dictionary<string, (double, double)>
            {
                ["București"]               = (44.4268, 26.1025),
                ["Ploiești"]                = (44.9363, 26.0125),
                ["Brașov"]                  = (45.6480, 25.6070),
                ["Sibiu"]                   = (45.7983, 24.1256),
                ["Cluj-Napoca"]             = (46.7712, 23.6236),
                ["Timișoara"]               = (45.7489, 21.2087),
                ["Arad"]                    = (46.1866, 21.3126),
                ["Oradea"]                  = (47.0722, 21.9218),
                ["Baia Mare"]               = (47.6573, 23.5717),
                ["Satu Mare"]               = (47.7914, 22.8837),
                ["Deva"]                    = (45.8812, 22.9118),
                ["Alba Iulia"]              = (46.0723, 23.5800),
                ["Craiova"]                 = (44.3302, 23.7949),
                ["Târgu Jiu"]               = (45.0422, 23.2715),
                ["Drobeta-Turnu Severin"]   = (44.6328, 22.6565),
                ["Pitești"]                 = (44.8600, 24.8673),
                ["Râmnicu Vâlcea"]          = (45.0997, 24.3693),
                ["Iași"]                    = (47.1585, 27.6014),
                ["Bacău"]                   = (46.5774, 26.9148),
                ["Suceava"]                 = (47.6513, 26.2556),
                ["Botoșani"]                = (47.7487, 26.6601),
                ["Galați"]                  = (45.4353, 28.0080),
                ["Brăila"]                  = (45.2672, 27.9574),
                ["Constanța"]               = (44.1598, 28.6348),
                ["Tulcea"]                  = (45.1697, 28.8028),
                ["Vaslui"]                  = (46.6406, 27.7276),
                ["Focșani"]                 = (45.6969, 27.1868),
                ["Buzău"]                   = (45.1510, 26.8217),
                ["Urziceni"]                = (44.7213, 26.6432),
                ["Sfântu Gheorghe"]         = (45.8643, 25.7879),
                ["Târgu Mureș"]             = (46.5386, 24.5570),
                ["Bistrița"]                = (47.1333, 24.5000),
                ["Piatra Neamț"]            = (46.9267, 26.3716),
            };

        // ── Road edges: weight = driving minutes at normal speed ─────────────────
        private static readonly (string A, string B, int Min)[] RoadEdges =
        {
            ("București",           "Ploiești",             60),
            ("București",           "Pitești",              90),
            ("București",           "Urziceni",             45),
            ("Ploiești",            "Buzău",                60),
            ("Ploiești",            "Brașov",               75),
            ("Buzău",               "Urziceni",             45),
            ("Buzău",               "Focșani",              60),
            ("Buzău",               "Brăila",               75),
            ("Brăila",              "Galați",               30),
            ("Brăila",              "Focșani",              60),
            ("Focșani",             "Bacău",                75),
            ("Bacău",               "Piatra Neamț",         45),
            ("Bacău",               "Iași",                 90),
            ("Bacău",               "Suceava",              90),
            ("Bacău",               "Vaslui",               75),
            ("Suceava",             "Botoșani",             45),
            ("Suceava",             "Piatra Neamț",         90),
            ("Botoșani",            "Iași",                 90),
            ("Iași",                "Vaslui",               60),
            ("Vaslui",              "Galați",               90),
            ("Constanța",           "Tulcea",               90),
            ("Constanța",           "Brăila",               120),
            ("Brașov",              "Sfântu Gheorghe",      30),
            ("Brașov",              "Sibiu",                75),
            ("Sfântu Gheorghe",     "Târgu Mureș",          60),
            ("Sibiu",               "Alba Iulia",           60),
            ("Sibiu",               "Râmnicu Vâlcea",       90),
            ("Alba Iulia",          "Cluj-Napoca",          75),
            ("Alba Iulia",          "Deva",                 60),
            ("Deva",                "Cluj-Napoca",          90),
            ("Cluj-Napoca",         "Oradea",               120),
            ("Cluj-Napoca",         "Baia Mare",            120),
            ("Cluj-Napoca",         "Bistrița",             90),
            ("Cluj-Napoca",         "Târgu Mureș",          75),
            ("Târgu Mureș",         "Bistrița",             90),
            ("Oradea",              "Arad",                 90),
            ("Oradea",              "Satu Mare",            90),
            ("Arad",                "Timișoara",            45),
            ("Baia Mare",           "Satu Mare",            45),
            ("Timișoara",           "Drobeta-Turnu Severin",180),
            ("Râmnicu Vâlcea",      "Pitești",              60),
            ("Râmnicu Vâlcea",      "Târgu Jiu",            75),
            ("Târgu Jiu",           "Craiova",              90),
            ("Craiova",             "Drobeta-Turnu Severin",90),
            ("Craiova",             "Pitești",              120),
            ("Piatra Neamț",        "Suceava",              90),
        };

        // Built once at startup
        private static readonly IReadOnlyDictionary<string, List<(string Node, int Min)>> StaticGraph =
            BuildStaticGraph();

        private static Dictionary<string, List<(string, int)>> BuildStaticGraph()
        {
            var g = new Dictionary<string, List<(string, int)>>();
            foreach (var city in CityCoords.Keys)
                g[city] = new List<(string, int)>();
            foreach (var (a, b, min) in RoadEdges)
            {
                g[a].Add((b, min));
                g[b].Add((a, min));
            }
            return g;
        }

        public NearestHospitalService(ILogger<NearestHospitalService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Returns the single nearest emergency hospital from the given coordinates.
        /// Applies time-of-day traffic factor to road edges.
        /// </summary>
        public HospitalRouteResult? FindNearest(double patLat, double patLon)
        {
            try
            {
                double tf = GetTrafficFactor();

                // ── Build working graph with traffic factor applied ───────────────
                var adj = new Dictionary<string, List<(string Node, int Min)>>();
                foreach (var kv in StaticGraph)
                    adj[kv.Key] = kv.Value
                        .Select(e => (e.Node, (int)Math.Ceiling(e.Min * tf)))
                        .ToList();

                // Patient virtual node → 3 nearest cities (highway 80 km/h)
                adj["_patient"] = new();
                foreach (var (city, rawMin) in CityCoords
                    .Select(kv => (kv.Key, HaversineMin(patLat, patLon, kv.Value.Lat, kv.Value.Lon, 80)))
                    .OrderBy(x => x.Item2).Take(3))
                {
                    int min = (int)Math.Ceiling(rawMin * tf);
                    adj["_patient"].Add((city, min));
                    adj[city].Add(("_patient", min));
                }

                // Hospital leaf nodes
                for (int i = 0; i < Hospitals.Length; i++)
                {
                    var h = Hospitals[i];
                    var hKey = $"_h_{i}";
                    adj[hKey] = new();

                    string nearestCity;
                    int linkMin;

                    if (CityCoords.ContainsKey(h.City))
                    {
                        var (cLat, cLon) = CityCoords[h.City];
                        nearestCity = h.City;
                        linkMin = Math.Max(1, HaversineMin(h.Lat, h.Lon, cLat, cLon, 40));
                    }
                    else
                    {
                        var nearestEntry = CityCoords
                            .Select(kv => (kv.Key, HaversineMin(h.Lat, h.Lon, kv.Value.Lat, kv.Value.Lon, 40)))
                            .OrderBy(x => x.Item2).First();
                        nearestCity = nearestEntry.Key;
                        linkMin = nearestEntry.Item2;
                    }

                    adj[hKey].Add((nearestCity, linkMin));
                    adj[nearestCity].Add((hKey, linkMin));
                }

                // ── Dijkstra from "_patient" ─────────────────────────────────────
                var dist = new Dictionary<string, int>(adj.Count);
                var prev = new Dictionary<string, string?>(adj.Count);
                foreach (var node in adj.Keys) { dist[node] = int.MaxValue; prev[node] = null; }
                dist["_patient"] = 0;

                var pq = new PriorityQueue<string, int>();
                pq.Enqueue("_patient", 0);

                while (pq.Count > 0)
                {
                    var u = pq.Dequeue();
                    if (!adj.TryGetValue(u, out var neighbors)) continue;
                    int du = dist[u];
                    if (du == int.MaxValue) continue;

                    foreach (var (v, w) in neighbors)
                    {
                        int alt = du + w;
                        if (!dist.TryGetValue(v, out int dv)) dv = int.MaxValue;
                        if (alt < dv)
                        {
                            dist[v] = alt;
                            prev[v] = u;
                            pq.Enqueue(v, alt);
                        }
                    }
                }

                // ── Pick the single nearest hospital ─────────────────────────────
                HospitalRouteResult? best = null;
                int bestDist = int.MaxValue;

                for (int i = 0; i < Hospitals.Length; i++)
                {
                    var hKey = $"_h_{i}";
                    if (!dist.TryGetValue(hKey, out int d) || d >= bestDist) continue;
                    bestDist = d;
                    var h = Hospitals[i];
                    best = new HospitalRouteResult
                    {
                        HospitalName     = h.Name,
                        City             = h.City,
                        Latitude         = h.Lat,
                        Longitude        = h.Lon,
                        EstimatedMinutes = Math.Max(1, d),
                        Route            = ReconstructRoute(prev, hKey),
                    };
                }

                if (best != null)
                    _logger.LogInformation(
                        "Nearest hospital: {Name} ({City}) ~{Min} min",
                        best.HospitalName, best.City, best.EstimatedMinutes);

                return best;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "NearestHospitalService.FindNearest failed for ({Lat},{Lon}).", patLat, patLon);
                return null;
            }
        }

        // ── Traffic factor (UTC+2 approximation for Romania) ─────────────────────
        private static double GetTrafficFactor()
        {
            int hour = (DateTime.UtcNow.Hour + 2) % 24;
            if (hour is >= 7 and <= 9 or >= 16 and <= 19) return 1.4;
            if (hour is <= 5 or >= 22) return 0.8;
            return 1.0;
        }

        // ── Reconstruct city-only path (skip _patient / _h_ nodes) ──────────────
        private static List<string> ReconstructRoute(Dictionary<string, string?> prev, string target)
        {
            var path = new List<string>();
            string? cur = target;
            while (cur != null)
            {
                if (!cur.StartsWith("_"))
                    path.Add(cur);
                prev.TryGetValue(cur, out cur);
            }
            path.Reverse();
            return path;
        }

        /// <summary>Haversine × 1.3 road factor → minutes at speedKmh.</summary>
        private static int HaversineMin(double lat1, double lon1, double lat2, double lon2, double speedKmh)
        {
            const double R = 6371.0;
            double dLat = (lat2 - lat1) * Math.PI / 180.0;
            double dLon = (lon2 - lon1) * Math.PI / 180.0;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                     + Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0)
                     * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            double km = R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a)) * 1.3;
            return (int)Math.Ceiling(km / speedKmh * 60.0);
        }
    }

    public class HospitalRouteResult
    {
        public string HospitalName { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int EstimatedMinutes { get; set; }
        public List<string> Route { get; set; } = new();
    }
}
