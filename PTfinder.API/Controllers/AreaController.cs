using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PTfinder.API.DATA.Modules;
using PTfinder.API.DATA;
using Microsoft.EntityFrameworkCore;

namespace PTfinder.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AreaController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AreaController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/Area/city/1
        [HttpGet("city/{cityId}")]
        public async Task<ActionResult<IEnumerable<Area>>> GetAreasByCity(int cityId)
        {
            return await _context.Areas
                .Where(a => a.CityId == cityId)
                .ToListAsync();
        }
    }

}
