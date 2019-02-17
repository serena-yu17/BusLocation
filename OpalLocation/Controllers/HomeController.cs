using Microsoft.AspNetCore.Mvc;
using OpalLocation.Models;
using System.Collections.Generic;
using System.Linq;

namespace OpalLocation.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            ViewBag.key = getGoogleKey();
            return View();
        }

        //HttpGet
        public IActionResult Route(string route)
        {
            var trips = TripData.getTrip(route);
            var converted = new Dictionary<string, string[]>();
            foreach(var kp in trips)
            {
                string[] arr = new string[kp.Value.Length];
                for (int i = 0; i < kp.Value.Length; i++)
                    arr[i] = kp.Value[i].ToString();
                converted[kp.Key] = arr;
            }
            return Json(converted);
        }

        public IActionResult Location(string tripIDs)
        {
            var tripArr = strToUint(tripIDs);
            var loc = TripData.getLoc(tripArr);
            return Json(loc);
        }

        public ActionResult Stop(string tripIDs)
        {
            var tripArr = strToUint(tripIDs);
            var coord = TripData.getStops(tripArr);
            var converted = new Dictionary<string, Coordinate[]>();
            foreach(var kp in coord)
                converted[kp.Key.ToString()] = kp.Value;
            return Json(converted);
        }

        private ulong[] strToUint(string str)
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

        public IActionResult Contact()
        {
            return View();
        }

        private string getGoogleKey()
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
