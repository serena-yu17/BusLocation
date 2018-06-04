using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OpalLocation.Models
{
    public class TripInfo
    {
        public decimal latitude { get; set; }
        public decimal longitude { get; set; }
        public string occupancy { get; set; }
    }
    public class TripCollector
    {
        const string locationPattern = @"entity\s*?{[\s\S]+?vehicle\s*?{[\s\S]+?trip\s*?{[\s\S]+?trip_id:\s*?""(\d+)""[\s\S]+?position\s*?{[\s\S]+?latitude:\s*?([\d-\.]+)[\s\S]+?longitude:\s*?([\d\.-]+)[\s\S]+?occupancy_status:\s*?([\w_]+)";
        static Regex locationRegex = new Regex(locationPattern, RegexOptions.Compiled | RegexOptions.Singleline);
        static async Task getLocation()
        {
            Dictionary<uint, TripInfo> locations = new Dictionary<uint, TripInfo>();
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
                                TripInfo trip = new TripInfo()
                                {
                                    latitude = latitude,
                                    longitude = longitude,
                                    occupancy = occup.Replace('_', ' ').ToLower()
                                };
                                locations[tripID] = trip;
                            }
                        }
                }
            }
        }

        const string tripPattern = @"""([ \w\-_ ()]+?)""";
        static Regex tripRegex = new Regex(tripPattern, RegexOptions.Compiled | RegexOptions.Singleline);

        static async Task getTripInfo()
        {
            Dictionary<string, List<Tuple<string, uint>>> trips = new Dictionary<string, List<Tuple<string, uint>>>();
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
                                            switch (i % 8)
                                            {
                                                case 0:
                                                    var routeStr = m.Groups[1].Value;
                                                    int j = 0;
                                                    while (j < routeStr.Length - 1 && routeStr[j] != '_')
                                                        j++;
                                                    if (j < routeStr.Length - 1)
                                                        route = routeStr.Substring(j + 1, routeStr.Length - j - 1);
                                                    break;
                                                case 2:
                                                    var tripStr = m.Groups[1].Value;
                                                    uint.TryParse(tripStr, out tripID);
                                                    break;
                                                case 7:
                                                    desc = m.Groups[1].Value;
                                                    if (!string.IsNullOrEmpty(route) && !string.IsNullOrEmpty(desc) && tripID != 0)
                                                    {
                                                        if (!trips.ContainsKey(route))
                                                            trips[route] = new List<Tuple<string, uint>>();
                                                        trips[route].Add(new Tuple<string, uint>(desc, tripID));
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
            }
        }
    }
}
