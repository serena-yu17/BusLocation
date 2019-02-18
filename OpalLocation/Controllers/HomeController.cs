using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OpalLocation.Models;
using OpalLocation.Operations;
using System.Collections.Generic;

namespace OpalLocation.Controllers
{
    public class HomeController : Controller
    {
        private readonly TripData tripData;

        public HomeController(ILoggerFactory factory)
        {
            tripData = new TripData(factory);
        }

        public IActionResult Index()
        {
            ViewBag.key = tripData.getGoogleKey();
            return View();
        }

        //HttpGet
        public IActionResult Route(string route)
        {
            var trips = tripData.getTrip(route);
            var converted = new Dictionary<string, string[]>();
            foreach (var kp in trips)
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
            var tripArr = tripData.strToUint(tripIDs);
            var loc = tripData.getLoc(tripArr);
            return Json(loc);
        }

        public ActionResult Stop(string tripIDs)
        {
            var tripArr = tripData.strToUint(tripIDs);
            var coord = tripData.getStops(tripArr);
            var converted = new Dictionary<string, Coordinate[]>();
            foreach (var kp in coord)
                converted[kp.Key.ToString()] = kp.Value;
            return Json(converted);
        }

        public IActionResult Contact()
        {
            return View();
        }
    }
}
