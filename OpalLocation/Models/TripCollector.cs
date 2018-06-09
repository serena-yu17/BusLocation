using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OpalLocation.Models
{
    public class TripInfo
    {
        public uint tripID { get; set; }
        public string direction { get; set; }

        public TripInfo(uint tripID, string direction)
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
        const string locationPattern = @"entity\s*?{[\s\S]+?vehicle\s*?{[\s\S]+?trip\s*?{[\s\S]+?trip_id:\s*?""(\d+)""[\s\S]+?position\s*?{[\s\S]+?latitude:\s*?([\d-\.]+)[\s\S]+?longitude:\s*?([\d\.-]+)[\s\S]+?occupancy_status:\s*?([\w_]+)";
        static Regex locationRegex = new Regex(locationPattern, RegexOptions.Compiled | RegexOptions.Singleline);

        public static Dictionary<uint, List<TripLoc>> locations = new Dictionary<uint, List<TripLoc>>();

        //trips key: RouteNo e.g. "370", value: Tuple Item1: direction, Item2: tripID
        static Dictionary<string, List<TripInfo>> trips = new Dictionary<string, List<TripInfo>>();
        static Dictionary<uint, Coordinate> stopLocations = new Dictionary<uint, Coordinate>();
        static Dictionary<uint, List<uint>> tripStops = new Dictionary<uint, List<uint>>();

        static Task tripTsk = null;
        static object tripLock = new object();
        static Task locTsk = null;
        static object locLock = new object();

        static System.Timers.Timer tripTimer = new System.Timers.Timer();
        static System.Timers.Timer locTimer = new System.Timers.Timer();

        public static void init()
        {
            if (trips.Count == 0)
                loadTrip();
            if (locations.Count == 0)
                loadLoc();

            tripTimer.Elapsed += new System.Timers.ElapsedEventHandler((s, e) => loadTrip());
            tripTimer.Interval = 1000 * 3600 * 24;  //1 day
            tripTimer.AutoReset = true;
            tripTimer.Start();
            locTimer.Elapsed += new System.Timers.ElapsedEventHandler((s, e) => loadLoc());
            locTimer.Interval = 1000 * 15;  //15s
            locTimer.AutoReset = true;
            locTimer.Start();
        }

        public static Dictionary<string, uint[]> getTrip(string route)
        {
            if (trips.Count == 0 && tripTsk != null && !tripTsk.IsCompleted)
                tripTsk.Wait();
            if (locations.Count == 0 && locTsk != null && !locTsk.IsCompleted)
                locTsk.Wait();
            Dictionary<string, HashSet<uint>> tripCollection = new Dictionary<string, HashSet<uint>>();
            var routeTrimmed = route.Trim().ToUpper();
            if (trips.ContainsKey(routeTrimmed))
            {
                var tripLst = trips[routeTrimmed];

                foreach (var t in tripLst)
                    if (locations.ContainsKey(t.tripID))
                    {
                        if (!tripCollection.ContainsKey(t.direction))
                            tripCollection[t.direction] = new HashSet<uint>();
                        tripCollection[t.direction].Add(t.tripID);
                    }
            }
            Dictionary<string, uint[]> res = new Dictionary<string, uint[]>();
            foreach (var kp in tripCollection)
                res[kp.Key] = kp.Value.ToArray();
            return res;
        }

        public static TripLoc[] getLoc(uint[] tripIDs)
        {
            if (locations.Count == 0 && locTsk != null && !locTsk.IsCompleted)
                locTsk.Wait();
            List<TripLoc> res = new List<TripLoc>();
            foreach (var id in tripIDs)
                if (locations.ContainsKey(id))
                    res.AddRange(locations[id]);
            return res.ToArray();
        }

        public static Coordinate[] getStops(uint[] tripIDs)
        {
            if (trips.Count == 0 && tripTsk != null && !tripTsk.IsCompleted)
                tripTsk.Wait();
            HashSet<Coordinate> coordinates = new HashSet<Coordinate>();
            foreach (var trip in tripIDs)
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
            return coordinates.ToArray();
        }

        static void loadTrip()
        {
            if (tripTsk == null || tripTsk.IsCompleted)
                lock (tripLock)
                    if (tripTsk == null || tripTsk.IsCompleted)
                        tripTsk = getTripInfo();
        }
        static void loadLoc()
        {
            if (locTsk == null || locTsk.IsCompleted)
                lock (locLock)
                    if (locTsk == null || locTsk.IsCompleted)
                        locTsk = getLocation();
        }

        static async Task getLocation()
        {
            Dictionary<uint, List<TripLoc>> newLoc = new Dictionary<uint, List<TripLoc>>();
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("apikey", "");
                var res = await client.GetAsync(@"https://api.transport.nsw.gov.au/v1/gtfs/vehiclepos/buses?debug=true");
                using (var contentStream = await res.Content.ReadAsStreamAsync())
                using (StreamReader sr = new StreamReader(contentStream))
                {
                    var text = await sr.ReadToEndAsync();
                    Console.WriteLine(text);
                    var matches = locationRegex.Matches(text);
                    foreach (Match m in matches)
                        if (m.Success && m.Groups.Count > 4)
                        {
                            var tripIDStr = m.Groups[1].Value;
                            var latitudeStr = m.Groups[2].Value;
                            var longitudeStr = m.Groups[3].Value;
                            var occup = m.Groups[4].Value;
                            if (uint.TryParse(tripIDStr, out uint tripID) &&
                                decimal.TryParse(latitudeStr, out decimal latitude) &&
                                decimal.TryParse(longitudeStr, out decimal longitude))
                            {
                                TripLoc trip = new TripLoc()
                                {
                                    coordinate = new Coordinate(latitude, longitude),
                                    occupancy = occup.Replace('_', ' ').ToLower()
                                };
                                if (!newLoc.ContainsKey(tripID))
                                    newLoc[tripID] = new List<TripLoc>();
                                newLoc[tripID].Add(trip);
                            }
                        }
                }
            }
            Interlocked.Exchange(ref locations, newLoc);
        }

        const string quotePattern = @"""([^""]*?)""";
        static Regex quoteRegex = new Regex(quotePattern, RegexOptions.Compiled | RegexOptions.Singleline);

        static async Task getTripInfo()
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("apikey", "BusSettings.busStopKey");
                var res = await client.GetAsync(@"https://api.transport.nsw.gov.au/v1/gtfs/schedule/buses");
                using (var contentStream = await res.Content.ReadAsStreamAsync())
                using (ZipArchive zip = new ZipArchive(contentStream))
                    foreach (var entry in zip.Entries)
                    {
                        if (entry.Name.StartsWith("trips"))
                        {
                            Dictionary<string, List<TripInfo>> newTrips = new Dictionary<string, List<TripInfo>>();
                            using (var stream = entry.Open())
                            using (MemoryStream ms = new MemoryStream())
                            {
                                await stream.CopyToAsync(ms);
                                ms.Seek(0, SeekOrigin.Begin);
                                using (StreamReader rd = new StreamReader(ms))
                                {
                                    var text = await rd.ReadToEndAsync();
                                    var matches = quoteRegex.Matches(text);
                                    int i = 0;
                                    string route = null, desc = null;
                                    uint tripID = 0;
                                    foreach (Match m in matches)
                                    {
                                        if (m.Success && m.Groups.Count > 1)
                                            switch (i % 10)
                                            {
                                                case 0:
                                                    var routeStr = m.Groups[1].Value;
                                                    int j = 0;
                                                    while (j < routeStr.Length - 1 && routeStr[j] != '_')
                                                        j++;
                                                    if (j < routeStr.Length - 1)
                                                        route = routeStr.Substring(j + 1, routeStr.Length - j - 1).Trim().ToUpper();
                                                    break;
                                                case 2:
                                                    var tripStr = m.Groups[1].Value;
                                                    uint.TryParse(tripStr, out tripID);
                                                    if (tripID >= 607400 && tripID < 607642)
                                                        Trace.WriteLine(route + ": " + tripID.ToString());
                                                    break;
                                                case 9:
                                                    desc = m.Groups[1].Value;
                                                    if (!string.IsNullOrEmpty(route) && !string.IsNullOrEmpty(desc) && tripID != 0)
                                                    {
                                                        if (!newTrips.ContainsKey(route))
                                                            newTrips[route] = new List<TripInfo>();
                                                        newTrips[route].Add(new TripInfo(tripID, desc));
                                                        route = null;
                                                        desc = null;
                                                        tripID = 0;
                                                    }
                                                    break;
                                            }
                                        i++;
                                    }
                                }
                            }
                            Interlocked.Exchange(ref trips, newTrips);
                        }
                        else if (entry.Name.StartsWith("stop_times"))
                        {
                            Dictionary<uint, HashSet<uint>> tempStops = new Dictionary<uint, HashSet<uint>>();
                            using (var stream = entry.Open())
                            using (MemoryStream ms = new MemoryStream())
                            {
                                await stream.CopyToAsync(ms);
                                ms.Seek(0, SeekOrigin.Begin);
                                using (StreamReader rd = new StreamReader(ms))
                                {
                                    var text = await rd.ReadToEndAsync();
                                    var matches = quoteRegex.Matches(text);
                                    int i = 0;
                                    uint tripID = 0, stopID = 0;
                                    foreach (Match m in matches)
                                    {
                                        if (m.Success && m.Groups.Count > 1)
                                            switch (i % 11)
                                            {
                                                case 0:
                                                    var tripStr = m.Groups[1].Value;
                                                    uint.TryParse(tripStr, out tripID);
                                                    break;
                                                case 3:
                                                    var stopStr = m.Groups[1].Value;
                                                    uint.TryParse(stopStr, out stopID);
                                                    if (stopID != 0 && tripID != 0)
                                                    {
                                                        if (!tempStops.ContainsKey(tripID))
                                                            tempStops[tripID] = new HashSet<uint>();
                                                        tempStops[tripID].Add(stopID);
                                                    }
                                                    break;
                                            }
                                        i++;
                                    }
                                }
                            }
                            Dictionary<uint, List<uint>> newTripStops = new Dictionary<uint, List<uint>>();
                            foreach (var kp in tempStops)
                                newTripStops[kp.Key] = new List<uint>(kp.Value);
                            Interlocked.Exchange(ref tripStops, newTripStops);
                        }
                        else if (entry.Name.StartsWith("stops"))
                        {
                            Dictionary<uint, Coordinate> newStopLocations = new Dictionary<uint, Coordinate>();
                            using (var stream = entry.Open())
                            using (MemoryStream ms = new MemoryStream())
                            {
                                await stream.CopyToAsync(ms);
                                ms.Seek(0, SeekOrigin.Begin);
                                using (StreamReader rd = new StreamReader(ms))
                                {
                                    var text = await rd.ReadToEndAsync();
                                    var matches = quoteRegex.Matches(text);
                                    int i = 0;
                                    decimal lat = 0, lon = 0;
                                    uint stopID = 0;
                                    foreach (Match m in matches)
                                    {
                                        if (m.Success && m.Groups.Count > 1)
                                            switch (i % 7)
                                            {
                                                case 0:
                                                    var stopStr = m.Groups[1].Value;
                                                    uint.TryParse(stopStr, out stopID);
                                                    break;
                                                case 2:
                                                    var latStr = m.Groups[1].Value;
                                                    decimal.TryParse(latStr, out lat);
                                                    break;
                                                case 3:
                                                    var lonStr = m.Groups[1].Value;
                                                    decimal.TryParse(lonStr, out lon);
                                                    if (stopID != 0 && lat != 0 && lon != 0)
                                                        newStopLocations[stopID] = new Coordinate(lat, lon);
                                                    break;
                                            }
                                        i++;
                                    }
                                }
                            }
                            Interlocked.Exchange(ref stopLocations, newStopLocations);
                        }
                    }
            }
        }
    }
}
