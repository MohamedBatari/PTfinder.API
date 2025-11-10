using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA;
using PTfinder.API.DATA.Modules;

namespace PTfinder.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GiftsController : ControllerBase
    {
        private readonly AppDbContext _db;
        public GiftsController(AppDbContext db) { _db = db; }

        // Small helper for consistent parsing
        private static bool TryCoachId(string s, out int id) => int.TryParse((s ?? "").Trim(), out id);

        // -----------------------------
        // GET: /api/Gifts/coach/{coachId}
        // -----------------------------
        [HttpGet("coach/{coachId}")]
        public async Task<ActionResult<IEnumerable<object>>> GetCoachGifts([FromRoute] string coachId)
        {
            if (!TryCoachId(coachId, out var cid)) return BadRequest(new { message = "coachId must be an integer" });

            // 200 [] if none – frontend expects 200
            var gifts = await _db.CoachGifts
                .AsNoTracking()
                .Where(g => g.CoachId == cid)
                .OrderByDescending(g => g.CreatedUtc)
                .Take(500)
                .Select(g => new
                {
                    id = g.Id,
                    coachId = g.CoachId,
                    amount = (decimal)g.AmountMinor / 100m, // AED
                    amountMinor = g.AmountMinor,
                    currency = g.Currency,
                    note = g.Note,
                    donorEmail = g.DonorEmail,
                    status = g.Status,
                    stripeSessionId = g.StripeSessionId,
                    stripePaymentIntentId = g.StripePaymentIntentId,
                    createdUtc = g.CreatedUtc
                })
                .ToListAsync();

            return Ok(gifts);
        }

        // ------------------------------------------
        // GET: /api/Gifts/coach/{coachId}/stats?range=30d
        // range supports: "7d", "30d", "90d"
        // ------------------------------------------
        public class GiftsStatsDto
        {
            public decimal TotalAllTime { get; set; }
            public decimal TotalThisMonth { get; set; }
            public decimal Total7d { get; set; }
            public decimal AvgGift { get; set; }
            public decimal TopGift { get; set; }
            public IEnumerable<object> Series { get; set; } = Array.Empty<object>();
        }

        [HttpGet("coach/{coachId}/stats")]
        public async Task<ActionResult<GiftsStatsDto>> GetStats([FromRoute] string coachId, [FromQuery] string? range = "30d")
        {
            if (!TryCoachId(coachId, out var cid)) return BadRequest(new { message = "coachId must be an integer" });

            var nowUtc = DateTime.UtcNow;
            int days = range?.Trim().ToLowerInvariant() switch
            {
                "7d" => 7,
                "90d" => 90,
                _ => 30
            };
            var fromUtc = nowUtc.Date.AddDays(-(days - 1)); // inclusive day window

            // Query base
            var q = _db.CoachGifts.AsNoTracking().Where(g => g.CoachId == cid && g.Status == "succeeded");

            // Totals
            var allTimeMinor = await q.SumAsync(g => (long?)g.AmountMinor) ?? 0L;
            var monthStart = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var thisMonthMinor = await q.Where(g => g.CreatedUtc >= monthStart).SumAsync(g => (long?)g.AmountMinor) ?? 0L;

            var sevenDaysFrom = nowUtc.Date.AddDays(-6);
            var last7Minor = await q.Where(g => g.CreatedUtc >= sevenDaysFrom).SumAsync(g => (long?)g.AmountMinor) ?? 0L;

            var count = await q.CountAsync();
            var topMinor = await q.MaxAsync(g => (long?)g.AmountMinor) ?? 0L;

            // Series – by day
            var seriesData = await q
                .Where(g => g.CreatedUtc >= fromUtc)
                .GroupBy(g => g.CreatedUtc.Date)
                .Select(grp => new { Day = grp.Key, SumMinor = grp.Sum(x => x.AmountMinor) })
                .ToListAsync();

            // Ensure every day in window emitted
            var series = new List<object>(days);
            for (var d = 0; d < days; d++)
            {
                var day = fromUtc.Date.AddDays(d);
                var row = seriesData.FirstOrDefault(x => x.Day == day);
                var minor = row?.SumMinor ?? 0L;
                series.Add(new
                {
                    date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    amount = (decimal)minor / 100m
                });
            }

            var dto = new GiftsStatsDto
            {
                TotalAllTime = (decimal)allTimeMinor / 100m,
                TotalThisMonth = (decimal)thisMonthMinor / 100m,
                Total7d = (decimal)last7Minor / 100m,
                AvgGift = count == 0 ? 0m : (decimal)allTimeMinor / 100m / count,
                TopGift = (decimal)topMinor / 100m,
                Series = series
            };

            return Ok(dto);
        }

        // ---------------------------------------
        // GET: /api/Gifts/coach/{coachId}/export
        // ---------------------------------------
        [HttpGet("coach/{coachId}/export")]
        public async Task<IActionResult> ExportCsv([FromRoute] string coachId)
        {
            if (!TryCoachId(coachId, out var cid)) return BadRequest(new { message = "coachId must be an integer" });

            var gifts = await _db.CoachGifts
                .AsNoTracking()
                .Where(g => g.CoachId == cid)
                .OrderByDescending(g => g.CreatedUtc)
                .ToListAsync();

            var sb = new StringBuilder();
            sb.AppendLine("Id,CoachId,AmountAED,AmountMinor,Currency,Note,DonorEmail,Status,StripePaymentIntentId,CreatedUtc");

            foreach (var g in gifts)
            {
                var amount = ((decimal)g.AmountMinor / 100m).ToString("0.00", CultureInfo.InvariantCulture);
                var note = (g.Note ?? "").Replace("\"", "\"\"");
                var donor = (g.DonorEmail ?? "").Replace("\"", "\"\"");
                sb.AppendLine($"{g.Id},{g.CoachId},{amount},{g.AmountMinor},{g.Currency},\"{note}\",\"{donor}\",{g.Status},{g.StripePaymentIntentId},{g.CreatedUtc:O}");
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var file = new FileContentResult(bytes, "text/csv");
            file.FileDownloadName = $"gifts_{coachId}_{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
            return file;
        }

        // ------------------------------------------------
        // (Optional) Settings for the dashboard UI toggles
        // ------------------------------------------------
        public class GiftSettingsDto
        {
            public bool AutoThank { get; set; }
            public string? Template { get; set; }
        }

        // For now, return defaults (no DB storage). Wire up to a table later if needed.
        [HttpGet("coach/{coachId}/settings")]
        public ActionResult<GiftSettingsDto> GetSettings([FromRoute] string coachId)
        {
            if (!TryCoachId(coachId, out _)) return BadRequest(new { message = "coachId must be an integer" });
            return Ok(new GiftSettingsDto { AutoThank = false, Template = "Thank you {{name}} for your gift of AED {{amount}}! 🙏" });
        }

        [HttpPost("coach/{coachId}/settings")]
        public ActionResult<object> SaveSettings([FromRoute] string coachId, [FromBody] GiftSettingsDto dto)
        {
            if (!TryCoachId(coachId, out _)) return BadRequest(new { message = "coachId must be an integer" });
            // TODO: persist dto to DB table if you create one (CoachGiftSettings)
            return Ok(new { saved = true });
        }
    }
}
