using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using OpalLocation.Models;
using System.Collections.Generic;
using System.Diagnostics;

namespace OpalLocation.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }

        //HttpGet
        public IActionResult Route(string route)
        {
            var trips = TripData.getTrip(route);
            return Json(trips);
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
            return Json(coord);
        }

        private uint[] strToUint(string str)
        {
            List<uint> arr = new List<uint>();
            var tripIDSec = str.Split(new char[] { ',', ' ', ';' }, System.StringSplitOptions.RemoveEmptyEntries);
            foreach (var sec in tripIDSec)
                if (uint.TryParse(sec, out var id))
                    arr.Add(id);
            return arr.ToArray();
        }

        public IActionResult Contact()
        {
            ViewData["Message"] = "Your contact page.";

            return View();
        }

        //public IActionResult Error()
        //{
        //    return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        //}
    }
}
