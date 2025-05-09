using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PTfinder.API.DATA.Modules;
using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA;
using PTfinder.API.DATA.DTO;

namespace PTfinder.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReviewsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ReviewsController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet("coach/{coachId}")]
        public async Task<IActionResult> GetReviewsForCoach(int coachId)
        {
            var reviews = await _context.Reviews
                .Where(r => r.CoachId == coachId)
                .OrderByDescending(r => r.Id)
                .ToListAsync();
            return Ok(reviews);
        }
        [HttpPost]
        public async Task<IActionResult> AddReview([FromBody] ReviewCreateDto reviewDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var review = new Review
            {
                StudentName = reviewDto.StudentName,
                Comment = reviewDto.Comment,
                Rating = reviewDto.Rating,
                CoachId = reviewDto.CoachId
            };

            _context.Reviews.Add(review);
            await _context.SaveChangesAsync();

            // Optionally return a DTO back (instead of the full Review)
            var response = new ReviewDto
            {
                Id = review.Id,
                StudentName = review.StudentName,
                Comment = review.Comment,
                Rating = review.Rating
            };

            return Ok(response);
        }

    }

}
