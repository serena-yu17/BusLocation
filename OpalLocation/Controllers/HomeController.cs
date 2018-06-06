using Microsoft.AspNetCore.Mvc;
using OpalLocation.Models;
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

        public IActionResult Location(uint[] tripIDs)
        {
            var loc = TripData.getLoc(tripIDs);
            return Json(loc);
        }

        public IActionResult Contact()
        {
            ViewData["Message"] = "Your contact page.";

            return View();
        }

        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
