using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OpalLocation.Models
{
    public class TripInfo
    {
        public ulong tripID { get; set; }
        public string direction { get; set; }

        public TripInfo(ulong tripID, string direction)
        {
            this.tripID = tripID;
            this.direction = direction;
        }
    }

    public class Coordinate
    {
        public decimal latitude { get; set; }
        public decimal longitude { get; set; }
        public Coordinate(decimal lat, decimal lon)
        {
            latitude = lat;
            longitude = lon;
        }

        public override int GetHashCode()
        {
            return latitude.GetHashCode() ^ longitude.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (this == null && obj == null)
                return true;
            if (this == null && obj != null)
                return false;
            if (this != null && obj == null)
                return false;
            if (ReferenceEquals(this, obj))
                return true;
            if (!(obj is Coordinate))
                return false;
            var cor = obj as Coordinate;
            return (this.latitude == cor.latitude && this.longitude == cor.longitude);
        }
    }

    public class TripLoc
    {
        public Coordinate coordinate { get; set; }
        public string occupancy { get; set; }
    }

    public static class TripData
    {
        const string locationPattern = @"trip_id: ""([\w\.\-]+)""[\s\S]+?latitude: ([\d-\.]+)[\s\S]+?longitude: ([\d\.-]+)";
        static Regex locationRegex = new Regex(locationPattern, RegexOptions.Compiled | RegexOptions.Singleline);
        const string occuPattern = @"occupancy_status: ([\w-]+)";
        static Regex occuRegex = new Regex(occuPattern, RegexOptions.Compiled | RegexOptions.Singleline);

        const string positionUrl = @"https://api.transport.nsw.gov.au/v1/gtfs/vehiclepos/buses?debug=true";
        const string tripUrl = @"https://api.transport.nsw.gov.au/v1/gtfs/schedule/buses";

        const string bus = "buses";
        const string train = "sydneytrains";

        static Dictionary<ulong, List<TripLoc>> locations = new Dictionary<ulong, List<TripLoc>>();
        static Dictionary<string, List<TripInfo>> trips = new Dictionary<string, List<TripInfo>>();
        static Dictionary<uint, Coordinate> stopLocations = new Dictionary<uint, Coordinate>();
        static Dictionary<ulong, uint[]> tripStops = new Dictionary<ulong, uint[]>();

        static Task tripTsk = null;
        static object tripLoadLock = new object();
        static Task locTsk = null;
        static object locLoadLock = new object();

        static System.Timers.Timer tripTimer = new System.Timers.Timer();
        static System.Timers.Timer locTimer = new System.Timers.Timer();

        public static void init()
        {
            if (trips.Count == 0)
                loadTrip();
            if (locations.Count == 0)
                loadLoc();

            tripTimer.Elapsed += new System.Timers.ElapsedEventHandler((s, e) => loadTrip());
            tripTimer.Interval = 1000 * 3600;  //1 hour
            tripTimer.AutoReset = true;
            tripTimer.Start();
            locTimer.Elapsed += new System.Timers.ElapsedEventHandler((s, e) => loadLoc());
            locTimer.Interval = 1000 * 15;  //15s
            locTimer.AutoReset = true;
            locTimer.Start();
        }

        public static Dictionary<string, ulong[]> getTrip(string route)
        {
            if (trips.Count == 0 && tripTsk != null && !tripTsk.IsCompleted)
                tripTsk.Wait();
            if (locations.Count == 0 && locTsk != null && !locTsk.IsCompleted)
                locTsk.Wait();
            Dictionary<string, HashSet<ulong>> tripCollection = new Dictionary<string, HashSet<ulong>>();
            var routeTrimmed = route.Trim().ToUpper();
            if (trips.ContainsKey(routeTrimmed))
            {
                var tripLst = trips[routeTrimmed];

                foreach (var t in tripLst)
                    if (locations.ContainsKey(t.tripID))
                    {
                        if (!tripCollection.ContainsKey(t.direction))
                            tripCollection[t.direction] = new HashSet<ulong>();
                        tripCollection[t.direction].Add(t.tripID);
                    }
            }
            Dictionary<string, ulong[]> res = new Dictionary<string, ulong[]>();
            foreach (var kp in tripCollection)
                res[kp.Key] = kp.Value.ToArray();
            return res;
        }

        public static TripLoc[] getLoc(ulong[] tripIDs)
        {
            if (locations.Count == 0 && locTsk != null && !locTsk.IsCompleted)
                locTsk.Wait();
            List<TripLoc> res = new List<TripLoc>();
            foreach (var id in tripIDs)
                if (locations.ContainsKey(id))
                    res.AddRange(locations[id]);
            return res.ToArray();
        }

        public static Dictionary<ulong, Coordinate[]> getStops(ulong[] tripIDs)
        {
            if (trips.Count == 0 && tripTsk != null && !tripTsk.IsCompleted)
                tripTsk.Wait();
            Dictionary<ulong, Coordinate[]> res = new Dictionary<ulong, Coordinate[]>();
            foreach (var trip in tripIDs)
            {
                HashSet<Coordinate> coordinates = new HashSet<Coordinate>();
                if (tripStops.ContainsKey(trip))
                {
                    var stops = tripStops[trip];
                    foreach (var s in stops)
                        if (stopLocations.ContainsKey(s))
                        {
                            var coord = stopLocations[s];
                            coordinates.Add(coord);
                        }
                }
                res[trip] = coordinates.ToArray();
            }
            return res;
        }

        static void loadTrip()
        {
            if (tripTsk == null || tripTsk.IsCompleted)
                lock (tripLoadLock)
                    if (tripTsk == null || tripTsk.IsCompleted)
                        tripTsk = getTripInfo();
        }
        static void loadLoc()
        {
            if (locTsk == null || locTsk.IsCompleted)
                lock (locLoadLock)
                    if (locTsk == null || locTsk.IsCompleted)
                        locTsk = getLocation();
        }

        static async Task getLocation()
        {
            var loadingLocations = new Dictionary<ulong, List<TripLoc>>();
            await Task.WhenAll(new Task[]{
                 getLocation(bus, loadingLocations),
                 getLocation(train, loadingLocations)
            });
            Interlocked.Exchange(ref locations, loadingLocations);
        }

        static async Task getTripInfo()
        {
            var loadingStopLocations = new Dictionary<uint, Coordinate>();
            var loadingTrips = new Dictionary<string, List<TripInfo>>();
            var loadingTripStops = new Dictionary<ulong, uint[]>();
            await Task.WhenAll(new Task[]{
                 getTripInfo(bus,loadingStopLocations, loadingTrips, loadingTripStops),
                 getTripInfo(train, loadingStopLocations, loadingTrips, loadingTripStops)
            });
            Interlocked.Exchange(ref stopLocations, loadingStopLocations);
            Interlocked.Exchange(ref trips, loadingTrips);
            Interlocked.Exchange(ref tripStops, loadingTripStops);
        }

        static string stripID(string tripID)
        {
            int i = 0;
            for (; i < tripID.Length && tripID[i] != '.'; i++)
                ;       //skip the first section
            StringBuilder sb = new StringBuilder();
            for (; i < tripID.Length; i++)
                if (Char.IsDigit(tripID[i]))
                    sb.Append(tripID[i]);
            return sb.ToString();
        }

        static async Task getLocation(string type, Dictionary<ulong, List<TripLoc>> loadingLocations)
        {
            Dictionary<ulong, List<TripLoc>> newLoc = new Dictionary<ulong, List<TripLoc>>();
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("apikey", BusSettings.opalKey);
                var url = positionUrl;
                if (type != bus)
                    url = url.Replace(bus, type);
                var res = await client.GetAsync(url);
                using (var contentStream = await res.Content.ReadAsStreamAsync())
                using (StreamReader sr = new StreamReader(contentStream))
                {
                    var text = await sr.ReadToEndAsync();
                    MatchCollection locMatches = locationRegex.Matches(text);
                    MatchCollection occuMatches = null;
                    if (type == bus)
                        occuMatches = occuRegex.Matches(text);
                    for (int i = 0; i < locMatches.Count; i++)
                    {
                        Match m = locMatches[i];
                        if (m.Success && m.Groups.Count > 3)
                        {
                            var tripIDStr = m.Groups[1].Value;
                            var latitudeStr = m.Groups[2].Value;
                            var longitudeStr = m.Groups[3].Value;
                            string occup = null;
                            if (type == bus && occuMatches[i].Success && occuMatches[i].Groups.Count > 1)
                                occup = occuMatches[i].Groups[1].Value.Replace('_', ' ').ToLower().Replace("available", "").Trim();
                            if (type == train)
                                tripIDStr = stripID(tripIDStr);
                            if (ulong.TryParse(tripIDStr, out ulong tripID) &&
                                decimal.TryParse(latitudeStr, out decimal latitude) &&
                                decimal.TryParse(longitudeStr, out decimal longitude)
                                )
                            {
                                TripLoc trip = new TripLoc()
                                {
                                    coordinate = new Coordinate(latitude, longitude),
                                    occupancy = occup
                                };
                                if (!newLoc.ContainsKey(tripID))
                                    newLoc[tripID] = new List<TripLoc>();
                                newLoc[tripID].Add(trip);
                            }
                        }
                    }
                }
            }
            lock (loadingLocations)
                foreach (var kp in newLoc)
                    loadingLocations[kp.Key] = kp.Value;
        }

        static async Task getTripInfo(string type,
            Dictionary<uint, Coordinate> loadingStopLocations,
            Dictionary<string, List<TripInfo>> loadingTrips,
            Dictionary<ulong, uint[]> loadingTripStops
            )
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("apikey", "BusSettings.busStopKey");
                var url = tripUrl;
                if (type != bus)
                    url = url.Replace(bus, type);
                var res = await client.GetAsync(url);

                char[] buffer = new char[10000];
                Dictionary<string, string> routeID = new Dictionary<string, string>();

                using (var contentStream = await res.Content.ReadAsStreamAsync())
                using (ZipArchive zip = new ZipArchive(contentStream))
                {
                    foreach (var entry in zip.Entries)
                    {
                        if (entry.Name.StartsWith("routes"))
                        {
                            using (var stream = entry.Open())
                            using (StreamReader rd = new StreamReader(stream))
                            {
                                uint quoteCount = 0;
                                uint contentCount = 0;
                                uint cycle = 1;
                                List<char> content = new List<char>();
                                ulong i = 0;
                                string routeIDStr = null, shortName = null;
                                while (true)
                                {
                                    int chars = await rd.ReadAsync(buffer, 0, buffer.Length);
                                    if (chars == 0)
                                        break;
                                    var start = i;      //skip titles
                                    if (i == 0)
                                    {
                                        for (; i < (ulong)chars && buffer[i] != '\n'; i++)
                                            if (buffer[i] == ',')
                                                cycle++;
                                    }
                                    for (; i < (ulong)chars + start; i++)
                                    {
                                        if (buffer[i - start] == '"')
                                        {
                                            quoteCount++;
                                            if (quoteCount % 2 == 1)
                                            {
                                                contentCount++;
                                                content.Clear();
                                            }
                                            else
                                            {
                                                switch ((contentCount - 1) % cycle)
                                                {
                                                    case 0:
                                                        routeIDStr = new string(content.ToArray()).Trim().ToUpper();
                                                        break;
                                                    case 2:
                                                        shortName = new string(content.ToArray()).Trim().ToUpper();
                                                        if (routeIDStr != null && shortName != null)
                                                            routeID[routeIDStr] = shortName;
                                                        routeIDStr = null;
                                                        shortName = null;
                                                        break;
                                                }
                                            }
                                        }
                                        else if (quoteCount % 2 == 1)
                                            content.Add(buffer[i - start]);
                                    }
                                }
                            }
                            break;
                        }
                    }
                    foreach (var entry in zip.Entries)
                    {
                        if (entry.Name.StartsWith("trips"))
                        {
                            Dictionary<string, List<TripInfo>> newTrips = new Dictionary<string, List<TripInfo>>();
                            using (var stream = entry.Open())
                            using (StreamReader rd = new StreamReader(stream))
                            {
                                string route = null, desc = null;
                                ulong tripID = 0;
                                ulong i = 0;
                                uint cycle = 1;
                                uint quoteCount = 0;
                                uint contentCount = 0;
                                List<char> content = new List<char>();
                                while (true)
                                {
                                    int chars = await rd.ReadAsync(buffer, 0, buffer.Length);
                                    if (chars == 0)
                                        break;
                                    var start = i;      //skip titles
                                    if (i == 0)
                                    {
                                        for (; i < (ulong)chars && buffer[i] != '\n'; i++)
                                            if (buffer[i] == ',')
                                                cycle++;
                                    }
                                    for (; i < (ulong)chars + start; i++)
                                    {
                                        if (buffer[i - start] == '"')
                                        {
                                            quoteCount++;
                                            if (quoteCount % 2 == 1)
                                            {
                                                contentCount++;
                                                content.Clear();
                                            }
                                            else
                                            {
                                                switch ((contentCount - 1) % cycle)
                                                {
                                                    case 0:
                                                        var routeIDStr = new string(content.ToArray()).Trim().ToUpper();
                                                        if (routeID.ContainsKey(routeIDStr))
                                                            route = routeID[routeIDStr];
                                                        break;
                                                    case 2:
                                                        var tripStr = new string(content.ToArray());
                                                        if (type == train)
                                                            tripStr = stripID(tripStr);
                                                        ulong.TryParse(tripStr, out tripID);
                                                        break;
                                                    case 3:
                                                        if (type == train)
                                                        {
                                                            desc = new string(content.ToArray()).Trim();
                                                            if (!string.IsNullOrEmpty(route) &&
                                                                !string.IsNullOrEmpty(desc) && tripID != 0 &&
                                                                desc != "Empty Train")
                                                            {
                                                                if (!newTrips.ContainsKey(route))
                                                                    newTrips[route] = new List<TripInfo>();
                                                                newTrips[route].Add(new TripInfo(tripID, desc));
                                                            }
                                                            route = null;
                                                            desc = null;
                                                            tripID = 0;
                                                        }
                                                        break;
                                                    case 9:
                                                        if (type == bus)
                                                        {
                                                            desc = new string(content.ToArray());
                                                            if (!string.IsNullOrEmpty(route) && !string.IsNullOrEmpty(desc) && tripID != 0)
                                                            {
                                                                if (!newTrips.ContainsKey(route))
                                                                    newTrips[route] = new List<TripInfo>();
                                                                newTrips[route].Add(new TripInfo(tripID, desc));
                                                            }
                                                            route = null;
                                                            desc = null;
                                                            tripID = 0;
                                                        }
                                                        break;
                                                }
                                            }
                                        }
                                        else if (quoteCount % 2 == 1)
                                            content.Add(buffer[i - start]);
                                    }
                                }
                            }
                            lock (loadingTrips)
                                foreach (var kp in newTrips)
                                    loadingTrips[kp.Key] = kp.Value;
                            routeID.Clear();
                        }
                        else if (entry.Name.StartsWith("stop_times"))
                        {
                            Dictionary<ulong, HashSet<uint>> tempStops = new Dictionary<ulong, HashSet<uint>>();
                            using (var stream = entry.Open())
                            using (StreamReader rd = new StreamReader(stream))
                            {
                                uint quoteCount = 0;
                                uint contentCount = 0;
                                uint cycle = 1;
                                List<char> content = new List<char>();
                                ulong i = 0;
                                ulong tripID = 0;
                                uint stopID = 0;
                                while (true)
                                {
                                    int chars = await rd.ReadAsync(buffer, 0, buffer.Length);
                                    if (chars == 0)
                                        break;
                                    var start = i;      //skip titles
                                    if (i == 0)
                                    {
                                        for (; i < (ulong)chars && buffer[i] != '\n'; i++)
                                            if (buffer[i] == ',')
                                                cycle++;
                                    }
                                    for (; i < (ulong)chars + start; i++)
                                    {
                                        if (buffer[i - start] == '"')
                                        {
                                            quoteCount++;
                                            if (quoteCount % 2 == 1)
                                            {
                                                contentCount++;
                                                content.Clear();
                                            }
                                            else
                                            {
                                                switch ((contentCount - 1) % cycle)
                                                {
                                                    case 0:
                                                        var tripStr = new string(content.ToArray());
                                                        if (type == train)
                                                            tripStr = stripID(tripStr);
                                                        ulong.TryParse(tripStr, out tripID);
                                                        break;
                                                    case 3:
                                                        var stopStr = new string(content.ToArray());
                                                        uint.TryParse(stopStr, out stopID);
                                                        if (stopID != 0 && tripID != 0)
                                                        {
                                                            if (!tempStops.ContainsKey(tripID))
                                                                tempStops[tripID] = new HashSet<uint>();
                                                            tempStops[tripID].Add(stopID);
                                                        }
                                                        tripID = 0;
                                                        stopID = 0;
                                                        break;
                                                }
                                            }
                                        }
                                        else if (quoteCount % 2 == 1)
                                            content.Add(buffer[i - start]);
                                    }
                                }
                            }
                            lock (loadingTripStops)
                                foreach (var kp in tempStops)
                                    loadingTripStops[kp.Key] = kp.Value.ToArray();
                        }
                        else if (entry.Name.StartsWith("stops"))
                        {
                            Dictionary<uint, Coordinate> newStopLocations = new Dictionary<uint, Coordinate>();
                            using (var stream = entry.Open())
                            using (StreamReader rd = new StreamReader(stream))
                            {
                                uint quoteCount = 0;
                                uint contentCount = 0;
                                uint cycle = 1;
                                List<char> content = new List<char>();
                                ulong i = 0;
                                decimal lat = 0, lon = 0;
                                uint stopID = 0;
                                while (true)
                                {
                                    int chars = await rd.ReadAsync(buffer, 0, buffer.Length);
                                    if (chars == 0)
                                        break;
                                    var start = i;      //skip titles
                                    if (i == 0)
                                    {
                                        for (; i < (ulong)chars && buffer[i] != '\n'; i++)
                                            if (buffer[i] == ',')
                                                cycle++;
                                    }
                                    for (; i < (ulong)chars + start; i++)
                                    {
                                        if (buffer[i - start] == '"')
                                        {
                                            quoteCount++;
                                            if (quoteCount % 2 == 1)
                                            {
                                                contentCount++;
                                                content.Clear();
                                            }
                                            else
                                            {
                                                switch ((contentCount - 1) % cycle)
                                                {
                                                    case 0:
                                                        var stopStr = new string(content.ToArray());
                                                        uint.TryParse(stopStr, out stopID);
                                                        break;
                                                    case 2:
                                                        if (type == bus)
                                                        {
                                                            var latStr = new string(content.ToArray());
                                                            decimal.TryParse(latStr, out lat);
                                                        }
                                                        break;
                                                    case 3:
                                                        if (type == bus)
                                                        {
                                                            var lonStr = new string(content.ToArray());
                                                            decimal.TryParse(lonStr, out lon);
                                                            if (stopID != 0 && lat != 0 && lon != 0)
                                                                newStopLocations[stopID] = new Coordinate(lat, lon);
                                                            lat = 0;
                                                            lon = 0;
                                                            stopID = 0;
                                                        }
                                                        break;
                                                    case 4:
                                                        if (type == train)
                                                        {
                                                            var latStr = new string(content.ToArray());
                                                            decimal.TryParse(latStr, out lat);
                                                        }
                                                        break;
                                                    case 5:
                                                        if (type == train)
                                                        {
                                                            var lonStr = new string(content.ToArray());
                                                            decimal.TryParse(lonStr, out lon);
                                                            if (stopID != 0 && lat != 0 && lon != 0)
                                                                newStopLocations[stopID] = new Coordinate(lat, lon);
                                                            lat = 0;
                                                            lon = 0;
                                                            stopID = 0;
                                                        }
                                                        break;
                                                }
                                            }
                                        }
                                        else if (quoteCount % 2 == 1)
                                            content.Add(buffer[i - start]);
                                    }
                                }
                            }
                            lock (loadingStopLocations)
                                foreach (var kp in newStopLocations)
                                    loadingStopLocations[kp.Key] = kp.Value;
                        }
                    }
                }
            }
        }
    }
}
