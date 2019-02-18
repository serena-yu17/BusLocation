using ErrorMessage;
using Microsoft.Extensions.Logging;
using OpalLocation.Models;
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

namespace OpalLocation.Operations
{
    public class TripData
    {
        const string locationPattern = @"trip_id: ""([\w\.\-]+)""[\s\S]+?latitude: ([\d-\.]+)[\s\S]+?longitude: ([\d\.-]+)";
        static readonly Regex locationRegex = new Regex(locationPattern, RegexOptions.Compiled | RegexOptions.Singleline);
        const string occuPattern = @"occupancy_status: ([\w-]+)";
        static readonly Regex occuRegex = new Regex(occuPattern, RegexOptions.Compiled | RegexOptions.Singleline);

        const string positionUrl = @"https://api.transport.nsw.gov.au/v1/gtfs/vehiclepos/buses?debug=true";
        const string busTripUrl = @"https://api.transport.nsw.gov.au/v1/gtfs/schedule/buses";
        const string trainTripUrl = @"https://api.transport.nsw.gov.au/v1/gtfs/schedule/sydneytrains";

        static Dictionary<ulong, List<TripLoc>> locations = new Dictionary<ulong, List<TripLoc>>();
        static Dictionary<string, List<TripInfo>> trips = new Dictionary<string, List<TripInfo>>();
        static Dictionary<uint, Coordinate> stopLocations = new Dictionary<uint, Coordinate>();
        static Dictionary<ulong, uint[]> tripStops = new Dictionary<ulong, uint[]>();

        static Task tripTsk = null;
        static readonly object tripLoadLock = new object();
        static Task locTsk = null;
        static readonly object locLoadLock = new object();

        static System.Timers.Timer tripTimer = new System.Timers.Timer();
        static System.Timers.Timer locTimer = new System.Timers.Timer();

        private readonly ILogger<TripData> logger;

        enum VehicleType
        {
            buses,
            sydneytrains
        }

        public TripData(ILoggerFactory factory)
        {
            logger = factory.CreateLogger<TripData>();
        }

        public void init()
        {
            if (trips.Count == 0)
                loadTrip();
            if (locations.Count == 0)
                loadLoc();

            tripTimer.Elapsed += new System.Timers.ElapsedEventHandler((s, e) => loadTrip());
            tripTimer.Interval = 1000 * 3600 * 5;  //5 hour
            tripTimer.AutoReset = true;
            tripTimer.Start();
            locTimer.Elapsed += new System.Timers.ElapsedEventHandler((s, e) => loadLoc());
            locTimer.Interval = 1000 * 15;  //15s
            locTimer.AutoReset = true;
            locTimer.Start();
        }

        public Dictionary<string, ulong[]> getTrip(string route)
        {
            Dictionary<string, ulong[]> res = new Dictionary<string, ulong[]>();
            try
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
                foreach (var kp in tripCollection)
                    res[kp.Key] = kp.Value.ToArray();
            }
            catch (Exception ex)
            {
                logger.LogError(ErrorHandler.getInfoStringTrace(ex));
            }
            return res;
        }

        public TripLoc[] getLoc(ulong[] tripIDs)
        {
            try
            {
                if (locations.Count == 0 && locTsk != null && !locTsk.IsCompleted)
                    locTsk.Wait();
                List<TripLoc> res = new List<TripLoc>();
                foreach (var id in tripIDs)
                    if (locations.ContainsKey(id))
                        res.AddRange(locations[id]);
                return res.ToArray();
            }
            catch (Exception ex)
            {
                logger.LogError(ErrorHandler.getInfoStringTrace(ex));
                return Array.Empty<TripLoc>();
            }
        }

        public Dictionary<ulong, Coordinate[]> getStops(ulong[] tripIDs)
        {
            Dictionary<ulong, Coordinate[]> res = new Dictionary<ulong, Coordinate[]>();
            try
            {
                if (trips.Count == 0 && tripTsk != null && !tripTsk.IsCompleted)
                    tripTsk.Wait();
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
            }
            catch (Exception ex)
            {
                logger.LogError(ErrorHandler.getInfoStringTrace(ex));
            }
            return res;
        }

        void loadTrip()
        {
            if (tripTsk == null || tripTsk.IsCompleted)
                lock (tripLoadLock)
                    if (tripTsk == null || tripTsk.IsCompleted)
                        tripTsk = getTripInfo();
        }
        void loadLoc()
        {
            if (locTsk == null || locTsk.IsCompleted)
                lock (locLoadLock)
                    if (locTsk == null || locTsk.IsCompleted)
                        locTsk = getLocation();
        }

        async Task getLocation()
        {
            try
            {
                var loadingLocations = new Dictionary<ulong, List<TripLoc>>();
                await Task.WhenAll(new Task[]{
                 getLocation(VehicleType.buses, loadingLocations),
                 getLocation(VehicleType.sydneytrains, loadingLocations)
            });
                Interlocked.Exchange(ref locations, loadingLocations);
            }
            catch (Exception ex)
            {
                logger.LogError(ErrorHandler.getInfoStringTrace(ex));
            }
        }

        string stripID(string tripID)
        {
            int i = 0;
            for (; i < tripID.Length && tripID[i] != '.'; i++)
                ;       //skip the first section
            StringBuilder sb = new StringBuilder();
            for (; i < tripID.Length; i++)
                if (char.IsDigit(tripID[i]))
                    sb.Append(tripID[i]);
            return sb.ToString();
        }

        async Task getLocation(VehicleType type, Dictionary<ulong, List<TripLoc>> loadingLocations)
        {
            Dictionary<ulong, List<TripLoc>> newLoc = new Dictionary<ulong, List<TripLoc>>();
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("apikey", BusSettings.opalKey);
                var url = positionUrl;
                if (type == VehicleType.sydneytrains)
                    url = url.Replace(VehicleType.buses.ToString(), VehicleType.sydneytrains.ToString());
                var res = await client.GetAsync(url);
                using (var contentStream = await res.Content.ReadAsStreamAsync())
                using (StreamReader sr = new StreamReader(contentStream))
                {
                    var text = await sr.ReadToEndAsync();
                    MatchCollection locMatches = locationRegex.Matches(text);
                    MatchCollection occuMatches = null;
                    if (type == VehicleType.buses)
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
                            if (type == VehicleType.buses && occuMatches[i].Success && occuMatches[i].Groups.Count > 1)
                                occup = occuMatches[i].Groups[1].Value.Replace('_', ' ').ToLower().Replace("available", "").Trim();
                            if (type == VehicleType.sydneytrains)
                                tripIDStr = stripID(tripIDStr);
                            if (ulong.TryParse(tripIDStr, out ulong tripID) &&
                                float.TryParse(latitudeStr, out var latitude) &&
                                float.TryParse(longitudeStr, out var longitude)
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

        async Task<Dictionary<string, string>> readRoutes(ZipArchiveEntry entry)
        {
            Dictionary<string, string> routeID = new Dictionary<string, string>();
            char[] buffer = new char[10000];

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
            return routeID;
        }

        async Task<Dictionary<string, List<TripInfo>>> readTrips(ZipArchiveEntry tripEntry, ZipArchiveEntry routeEntry, VehicleType type)
        {
            var routeID = await readRoutes(routeEntry).ConfigureAwait(false);
            char[] buffer = new char[10000];

            Dictionary<string, List<TripInfo>> newTrips = new Dictionary<string, List<TripInfo>>();
            using (var stream = tripEntry.Open())
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
                                        if (type == VehicleType.sydneytrains)
                                            tripStr = stripID(tripStr);
                                        ulong.TryParse(tripStr, out tripID);
                                        break;
                                    case 3:
                                        if (type == VehicleType.sydneytrains)
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
                                        if (type == VehicleType.buses)
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
            return newTrips;
        }

        async Task<Dictionary<ulong, uint[]>> readStopTimes(ZipArchiveEntry entry, VehicleType type)
        {
            char[] buffer = new char[10000];
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
                                        if (type == VehicleType.sydneytrains)
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
            var loadingTripStops = new Dictionary<ulong, uint[]>();
            foreach (var kp in tempStops)
                loadingTripStops[kp.Key] = kp.Value.ToArray();
            return loadingTripStops;
        }

        async Task<Dictionary<uint, Coordinate>> readStops(ZipArchiveEntry entry, VehicleType type)
        {
            char[] buffer = new char[10000];
            Dictionary<uint, Coordinate> newStopLocations = new Dictionary<uint, Coordinate>();
            using (var stream = entry.Open())
            using (StreamReader rd = new StreamReader(stream))
            {
                uint quoteCount = 0;
                uint contentCount = 0;
                uint cycle = 1;
                List<char> content = new List<char>();
                ulong i = 0;
                float lat = 0, lon = 0;
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
                                        if (type == VehicleType.buses)
                                        {
                                            var latStr = new string(content.ToArray());
                                            float.TryParse(latStr, out lat);
                                        }
                                        break;
                                    case 3:
                                        if (type == VehicleType.buses)
                                        {
                                            var lonStr = new string(content.ToArray());
                                            float.TryParse(lonStr, out lon);
                                            if (stopID != 0 && lat != 0 && lon != 0)
                                                newStopLocations[stopID] = new Coordinate(lat, lon);
                                            lat = 0;
                                            lon = 0;
                                            stopID = 0;
                                        }
                                        break;
                                    case 4:
                                        if (type == VehicleType.sydneytrains)
                                        {
                                            var latStr = new string(content.ToArray());
                                            float.TryParse(latStr, out lat);
                                        }
                                        break;
                                    case 5:
                                        if (type == VehicleType.sydneytrains)
                                        {
                                            var lonStr = new string(content.ToArray());
                                            float.TryParse(lonStr, out lon);
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
            return newStopLocations;
        }

        async Task getTripInfo()
        {
            var busData = await _getTripInfo(VehicleType.buses).ConfigureAwait(false);
            var trainData = await _getTripInfo(VehicleType.sydneytrains).ConfigureAwait(false);

            var newTrips = busData.trips;
            foreach (var kp in trainData.trips)
                newTrips[kp.Key] = kp.Value;
            trainData.trips = null;
            Interlocked.Exchange(ref trips, newTrips);

            var newTripStop = busData.tripStops;
            foreach (var kp in trainData.tripStops)
                newTripStop[kp.Key] = kp.Value;
            trainData.tripStops = null;
            Interlocked.Exchange(ref tripStops, newTripStop)?.Clear();

            var newStops = busData.stops;
            foreach (var kp in trainData.stops)
                newStops[kp.Key] = kp.Value;
            trainData.stops = null;
            Interlocked.Exchange(ref stopLocations, newStops);
        }

        async Task<TripDataSet> _getTripInfo(VehicleType type)
        {
            try
            {
                HttpResponseMessage res;
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("apikey", BusSettings.busStopKey);
                    string url = null;
                    if (type == VehicleType.buses)
                        url = busTripUrl;
                    else if (type == VehicleType.sydneytrains)
                        url = trainTripUrl;
                    res = await client.GetAsync(url).ConfigureAwait(false);
                }

                using (var contentStream = await res.Content.ReadAsStreamAsync())
                using (var zip = new ZipArchive(contentStream))
                {
                    ZipArchiveEntry routeEntry = null;
                    ZipArchiveEntry tripEntry = null;
                    ZipArchiveEntry stopTimeEntry = null;
                    ZipArchiveEntry stopEntry = null;

                    foreach (var entry in zip.Entries)
                    {
                        if (entry.Name.StartsWith("routes"))
                            routeEntry = entry;
                        else if (entry.Name.StartsWith("trips"))
                            tripEntry = entry;
                        else if (entry.Name.StartsWith("stop_times"))
                            stopTimeEntry = entry;
                        else if (entry.Name.StartsWith("stops"))
                            stopEntry = entry;

                        if (routeEntry != null && tripEntry != null && stopTimeEntry != null && stopEntry != null)
                            break;
                    }
                    var tripTsk = Task.FromResult<Dictionary<string, List<TripInfo>>>(null);
                    var stopTimeTsk = Task.FromResult<Dictionary<ulong, uint[]>>(null);
                    var stopTsk = Task.FromResult<Dictionary<uint, Coordinate>>(null);

                    if (tripEntry != null && routeEntry != null)
                        tripTsk = readTrips(tripEntry, routeEntry, type);
                    if (stopTimeEntry != null)
                        stopTimeTsk = readStopTimes(stopTimeEntry, type);
                    if (stopEntry != null)
                        stopTsk = readStops(stopEntry, type);
                    var newTrips = await tripTsk.ConfigureAwait(false) ?? new Dictionary<string, List<TripInfo>>();
                    var newStopTimes = await stopTimeTsk.ConfigureAwait(false) ?? new Dictionary<ulong, uint[]>();
                    var newStops = await stopTsk.ConfigureAwait(false) ?? new Dictionary<uint, Coordinate>();
                    return new TripDataSet(newTrips, newStopTimes, newStops);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ErrorHandler.getInfoStringTrace(ex));
            }
            return new TripDataSet(new Dictionary<string, List<TripInfo>>(), new Dictionary<ulong, uint[]>(), new Dictionary<uint, Coordinate>());
        }

        public ulong[] strToUint(string str)
        {
            List<ulong> arr = new List<ulong>();
            if (str != null)
            {
                var tripIDSec = str.Split(new char[] { ',', ' ', ';' }, System.StringSplitOptions.RemoveEmptyEntries);
                foreach (var sec in tripIDSec)
                    if (ulong.TryParse(sec, out var id))
                        arr.Add(id);
            }
            return arr.ToArray();
        }

        public string getGoogleKey()
        {
            string result;
#if DEBUG
            result = BusSettings.googleDebugKey;
#else
            result = BusSettings.googleKey;
#endif
            return result;
        }
    }
}
