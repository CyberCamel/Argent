using Microsoft.AspNetCore.Mvc;

namespace Argent.Web.Controllers
{
    [ApiController]
    public class HealthController : Controller
    {
        [HttpGet]
        [Route("/health")]
        public IActionResult GetHealth()
        {
            return Ok(new { status = "Healthy" });
        }
    }
}
