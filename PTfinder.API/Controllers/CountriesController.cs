using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PTfinder.API.DATA.Modules;
using PTfinder.API.DATA;
using Microsoft.EntityFrameworkCore;

namespace PTfinder.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CountryController : ControllerBase
    {
        private readonly AppDbContext _context;

        public CountryController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/Country
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Country>>> GetCountries()
        {
            return await _context.Countries.ToListAsync();
        }
    }


}
