using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
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

    public class TripLoc
    {
        public decimal latitude { get; set; }
        public decimal longitude { get; set; }
        public string occupancy { get; set; }
    }

    public static class TripData
    {
        const string locationPattern = @"entity\s*?{[\s\S]+?vehicle\s*?{[\s\S]+?trip\s*?{[\s\S]+?trip_id:\s*?""(\d+)""[\s\S]+?position\s*?{[\s\S]+?latitude:\s*?([\d-\.]+)[\s\S]+?longitude:\s*?([\d\.-]+)[\s\S]+?occupancy_status:\s*?([\w_]+)";
        static Regex locationRegex = new Regex(locationPattern, RegexOptions.Compiled | RegexOptions.Singleline);

        public static Dictionary<uint, List<TripLoc>> locations = new Dictionary<uint, List<TripLoc>>();

        //trips key: RouteNo e.g. "370", value: Tuple Item1: direction, Item2: tripID
        public static Dictionary<string, List<TripInfo>> trips = new Dictionary<string, List<TripInfo>>();

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
            locTimer.Elapsed += new System.Timers.ElapsedEventHandler((s, e) => loadLoc());
            locTimer.Interval = 1000 * 15;  //15s
            locTimer.AutoReset = true;
        }

        public static Dictionary<string, HashSet<uint>> getTrip(string route)
        {
            if (trips.Count == 0 && tripTsk != null && !tripTsk.IsCompleted)
                tripTsk.Wait();
            if (locations.Count == 0 && locTsk != null && !locTsk.IsCompleted)
                locTsk.Wait();
            Dictionary<string, HashSet<uint>> res = new Dictionary<string, HashSet<uint>>();
            var routeTrimmed = route.Trim().ToUpper();
            if (trips.ContainsKey(routeTrimmed))
            {
                var tripLst = trips[routeTrimmed];

                foreach (var t in tripLst)
                    if (locations.ContainsKey(t.tripID))
                    {
                        if (!res.ContainsKey(t.direction))
                            res[t.direction] = new HashSet<uint>();
                        res[t.direction].Add(t.tripID);
                    }                 
            }
            return res;
        }

        public static TripLoc[] getLoc(uint[] tripIDs)
        {
            if (locations.Count == 0 && locTsk != null && !locTsk.IsCompleted)
                locTsk.Wait();
            List<TripLoc> res = new List<TripLoc>();
            foreach(var id in tripIDs)
                res.AddRange(locations[id]);
            return res.ToArray();
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
                                    latitude = latitude,
                                    longitude = longitude,
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

        const string tripPattern = @"""([\s\S]*?)""";
        static Regex tripRegex = new Regex(tripPattern, RegexOptions.Compiled | RegexOptions.Singleline);

        static async Task getTripInfo()
        {
            Dictionary<string, List<TripInfo>> newTrips = new Dictionary<string, List<TripInfo>>();
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("apikey", "BusSettings.busStopKey");
                var res = await client.GetAsync(@"https://api.transport.nsw.gov.au/v1/gtfs/schedule/buses");
                using (var contentStream = await res.Content.ReadAsStreamAsync())
                using (ZipArchive zip = new ZipArchive(contentStream))
                    foreach (var entry in zip.Entries)
                        if (entry.Name.StartsWith("trips"))
                        {
                            using (var stream = entry.Open())
                            using (MemoryStream ms = new MemoryStream())
                            {
                                await stream.CopyToAsync(ms);
                                ms.Seek(0, SeekOrigin.Begin);
                                using (StreamReader rd = new StreamReader(ms))
                                {
                                    var text = await rd.ReadToEndAsync();
                                    Console.WriteLine(text);
                                    var matches = tripRegex.Matches(text);
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
                                                        Trace.WriteLine(route + ": " +  tripID.ToString());
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
                            break;
                        }
                Interlocked.Exchange(ref trips, newTrips);
            }
        }
    }
}
