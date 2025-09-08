using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA;

namespace PTfinder.API.Controllers;

[ApiController]
[Route("debug")]
public class DebugController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<DebugController> _logger;

    public DebugController(AppDbContext db, ILogger<DebugController> logger)
    {
        _db = db;
        _logger = logger;
    }

    // GET /debug/dbping  -> tells us if SQL is reachable
    [HttpGet("dbping")]
    public async Task<IActionResult> DbPing()
    {
        try
        {
            var canConnect = await _db.Database.CanConnectAsync();
            var conn = _db.Database.GetDbConnection();
            return Ok(new
            {
                canConnect,
                provider = _db.Database.ProviderName,
                database = conn.Database,
                dataSource = conn.DataSource
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DB ping failed");
            return StatusCode(500, new { error = ex.Message, stack = ex.StackTrace });
        }
    }

    // GET /debug/migrations -> shows applied and pending migrations
    [HttpGet("migrations")]
    public async Task<IActionResult> Migrations()
    {
        try
        {
            var applied = await _db.Database.GetAppliedMigrationsAsync();
            var pending = await _db.Database.GetPendingMigrationsAsync();
            return Ok(new { applied, pending });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Migrations query failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

