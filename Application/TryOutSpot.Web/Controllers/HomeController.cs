using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TryOutSpot.Web.Models;

namespace TryOutSpot.Web.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    public IActionResult Index()
    {
        return View();
    }

    [HttpGet("/privacy-policy")]
    public IActionResult Privacy()
    {
        return View();
    }

    [HttpGet("/terms-and-conditions")]
    public IActionResult TermsAndConditions()
    {
        return View();
    }

    [HttpGet("/sms-consent")]
    public IActionResult SmsConsent()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
