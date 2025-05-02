using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PTfinder.API.DATA.Modules;
using PTfinder.API.DATA;
using Microsoft.EntityFrameworkCore;

namespace PTfinder.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CityController : ControllerBase
    {
        private readonly AppDbContext _context;

        public CityController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/City/country/1
        [HttpGet("country/{countryId}")]
        public async Task<ActionResult<IEnumerable<City>>> GetCitiesByCountry(int countryId)
        {
            return await _context.Cities
                .Where(c => c.CountryId == countryId)
                .ToListAsync();
        }
    }

}
