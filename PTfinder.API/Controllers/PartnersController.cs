using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PTfinder.API.DATA;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA;
using PTfinder.API.DATA.Modules;
using PTfinder.API.Services;

namespace PTfinder.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PartnersController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly PartnerService _partnerService;

        public PartnersController(AppDbContext context, PartnerService partnerService)
        {
            _context = context;
            _partnerService = partnerService;
        }

        // DTOs (keep minimal for now)
        public class PartnerCreateUpdateDto
        {
            public string Name { get; set; }
            public string? LogoUrl { get; set; }
            public string? Description { get; set; }
            public string? Email { get; set; }
            public string? Phone { get; set; }
            public string? Address { get; set; }

            public string PlanName { get; set; }      // Small / Medium / Large / Enterprise
            public int MaxCoaches { get; set; }       // 10 / 25 / 50 / custom
            public decimal PricePerMonth { get; set; }
            public decimal PricePerYear { get; set; }
        }

        public class PartnerReadDto
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string? LogoUrl { get; set; }
            public string PlanName { get; set; }
            public int MaxCoaches { get; set; }
            public bool IsActive { get; set; }
            public int CoachCount { get; set; }
        }

        // GET: api/partners
        [HttpGet]
        public async Task<ActionResult<IEnumerable<PartnerReadDto>>> GetPartners()
        {
            var partners = await _context.Partners
                .Select(p => new PartnerReadDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    LogoUrl = p.LogoUrl,
                    PlanName = p.PlanName,
                    MaxCoaches = p.MaxCoaches,
                    IsActive = p.IsActive,
                    CoachCount = p.Coaches.Count
                })
                .ToListAsync();

            return Ok(partners);
        }

        // GET: api/partners/{id}
        [HttpGet("{id:int}")]
        public async Task<ActionResult<Partner>> GetPartner(int id)
        {
            var partner = await _context.Partners
                .Include(p => p.Coaches)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (partner == null) return NotFound();
            return Ok(partner);
        }

        // POST: api/partners
        [HttpPost]
        public async Task<ActionResult<Partner>> CreatePartner([FromBody] PartnerCreateUpdateDto dto)
        {
            var partner = new Partner
            {
                Name = dto.Name,
                LogoUrl = dto.LogoUrl,
                Description = dto.Description,
                Email = dto.Email,
                Phone = dto.Phone,
                Address = dto.Address,
                PlanName = dto.PlanName,
                MaxCoaches = dto.MaxCoaches,
                PricePerMonth = dto.PricePerMonth,
                PricePerYear = dto.PricePerYear,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            };

            _context.Partners.Add(partner);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetPartner), new { id = partner.Id }, partner);
        }

        // PUT: api/partners/{id}
        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdatePartner(int id, [FromBody] PartnerCreateUpdateDto dto)
        {
            var partner = await _context.Partners.FindAsync(id);
            if (partner == null) return NotFound();

            partner.Name = dto.Name;
            partner.LogoUrl = dto.LogoUrl;
            partner.Description = dto.Description;
            partner.Email = dto.Email;
            partner.Phone = dto.Phone;
            partner.Address = dto.Address;
            partner.PlanName = dto.PlanName;
            partner.MaxCoaches = dto.MaxCoaches;
            partner.PricePerMonth = dto.PricePerMonth;
            partner.PricePerYear = dto.PricePerYear;
            partner.UpdatedAtUtc = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // DELETE: api/partners/{id}
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeletePartner(int id)
        {
            var partner = await _context.Partners.FindAsync(id);
            if (partner == null) return NotFound();

            _context.Partners.Remove(partner);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // GET: api/partners/{id}/coaches
        [HttpGet("{id:int}/coaches")]
        public async Task<IActionResult> GetPartnerCoaches(int id)
        {
            var exists = await _context.Partners.AnyAsync(p => p.Id == id);
            if (!exists) return NotFound("Partner not found.");

            var coaches = await _context.Coaches
                .Where(c => c.PartnerId == id)
                .Select(c => new
                {
                    c.Id,
                    c.FullName,
                    c.Email,
                    c.PhoneNumber,
                    c.IsActive,
                    c.SubscriptionTier,
                    c.SubscriptionStatus
                })
                .ToListAsync();

            return Ok(coaches);
        }

        // POST: api/partners/{partnerId}/assign-coach/{coachId}
        [HttpPost("{partnerId:int}/assign-coach/{coachId:int}")]
        public async Task<IActionResult> AssignCoach(int partnerId, int coachId)
        {
            var (ok, error) = await _partnerService.AssignCoachAsync(partnerId, coachId);
            if (!ok) return Conflict(new { message = error });
            return Ok(new { message = "Coach assigned to partner and set to Premium." });
        }

        // DELETE: api/partners/{partnerId}/remove-coach/{coachId}
        [HttpDelete("{partnerId:int}/remove-coach/{coachId:int}")]
        public async Task<IActionResult> RemoveCoach(int partnerId, int coachId)
        {
            var (ok, error) = await _partnerService.RemoveCoachAsync(partnerId, coachId);
            if (!ok) return NotFound(new { message = error });
            return Ok(new { message = "Coach removed from partner." });
        }

        // GET: api/partners/{id}/stats
        [HttpGet("{id:int}/stats")]
        public async Task<IActionResult> GetStats(int id)
        {
            var partner = await _context.Partners.FindAsync(id);
            if (partner == null) return NotFound("Partner not found.");

            var (total, active) = await _partnerService.GetCoachCountsAsync(id);

            var remaining = partner.MaxCoaches > 0
                ? Math.Max(0, partner.MaxCoaches - total)
                : int.MaxValue;

            return Ok(new
            {
                partner.Id,
                partner.Name,
                partner.PlanName,
                partner.MaxCoaches,
                CoachesTotal = total,
                CoachesActive = active,
                RemainingSeats = remaining,
                partner.IsActive,
                partner.CurrentPeriodEndUtc
            });
        }
    }
}
