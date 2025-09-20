using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PTfinder.API.DATA;
using PTfinder.API.Services;
using PTfinder.API.Settings;


namespace PTfinder.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BillingController : ControllerBase
    {
        private readonly BillingService _billing;
        private readonly AppDbContext _db;
        private readonly StripeSettings _cfg;

        public BillingController(BillingService billing, AppDbContext db, IOptions<StripeSettings> cfg)
        {
            _billing = billing; _db = db; _cfg = cfg.Value;
        }

        // FREELANCER checkout
        // POST /api/billing/freelancer/checkout?coachId=123&tier=premium&interval=month
        [HttpPost("freelancer/checkout")]
        public async Task<IActionResult> FreelancerCheckout([FromQuery] int coachId, [FromQuery] string tier = "premium", [FromQuery] string interval = "month")
        {
            var url = await _billing.CreateFreelancerCheckoutAsync(coachId, tier, interval);
            return Ok(new { url });
        }

        // PARTNER checkout
        // POST /api/billing/partner/checkout?partnerId=10&plan=medium&interval=month
        [HttpPost("partner/checkout")]
        public async Task<IActionResult> PartnerCheckout([FromQuery] int partnerId, [FromQuery] string plan = "small", [FromQuery] string interval = "month")
        {
            var url = await _billing.CreatePartnerCheckoutAsync(partnerId, plan, interval);
            return Ok(new { url });
        }

        // Self-serve portals
        [HttpGet("portal/coach/{coachId:int}")]
        public async Task<IActionResult> CoachPortal(int coachId)
        {
            var coach = await _db.Coaches.FirstOrDefaultAsync(c => c.Id == coachId);
            if (coach == null || string.IsNullOrEmpty(coach.StripeCustomerId)) return NotFound();
            var url = await _billing.CreateBillingPortalAsync(coach.StripeCustomerId, _cfg.SuccessUrl);
            return Ok(new { url });
        }

        [HttpGet("portal/partner/{partnerId:int}")]
        public async Task<IActionResult> PartnerPortal(int partnerId)
        {
            var partner = await _db.Partners.FirstOrDefaultAsync(p => p.Id == partnerId);
            if (partner == null || string.IsNullOrEmpty(partner.StripeCustomerId)) return NotFound();
            var url = await _billing.CreateBillingPortalAsync(partner.StripeCustomerId, _cfg.SuccessUrl);
            return Ok(new { url });
        }
    }
}

