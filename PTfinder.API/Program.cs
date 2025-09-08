using System.Diagnostics;
using System.Text;
using DotNetEnv;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PTfinder.API.DATA;
using PTfinder.API.Services;
using PTfinder.API.Settings;

var builder = WebApplication.CreateBuilder(args);

// ------------ Load .env (optional for local) ------------
Env.Load();

// ------------ Allowed Origins ------------
var allowedOrigins = new[]
{
    "https://ptfindernow.com",
    "https://www.ptfindernow.com",
    "http://localhost:3000",
};

// ------------ CORS policy ------------
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .SetIsOriginAllowedToAllowWildcardSubdomains()
            .AllowAnyHeader()
            .AllowAnyMethod()
            .SetPreflightMaxAge(TimeSpan.FromHours(12));
        // Only add .AllowCredentials() if you actually use cookies in the browser
    });
});

// ------------ Connection string (fail fast if missing) ------------
var cs = builder.Configuration.GetConnectionString("mycon")
         ?? throw new InvalidOperationException("Missing connection string 'mycon'.");

// ------------ DbContext (resilient SQL + sensible timeouts) ------------
builder.Services.AddDbContextPool<AppDbContext>(options =>
    options.UseSqlServer(cs, sql =>
    {
        sql.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: new[] { 40613, 40197, 40501, 10928, 10929 } // common Azure SQL transient errors
        );
        sql.CommandTimeout(30); // command timeout (seconds)
    })
);

// ------------ Controllers & JSON ------------
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.ReferenceHandler =
            System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

// ------------ Other app services ------------
builder.Services.AddSingleton<BlobStorageService>();

// ------------ API behavior tweaks ------------
builder.Services.Configure<ApiBehaviorOptions>(o =>
{
    o.SuppressModelStateInvalidFilter = true;
});

// ------------ Swagger (enabled during bring-up; disable later if you want) ------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ------------ SMTP settings + email sender ------------
builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<SmtpSettings>>().Value);
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();

// ------------ Auth (JWT) ------------
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Missing configuration: Jwt:Key");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
});

// ------------ Health checks (framework-native) ------------
builder.Services.AddHealthChecks();

var app = builder.Build();

// ------------ Log DB target at startup (helps verify 'mycon') ------------
try
{
    var b = new SqlConnectionStringBuilder(cs);
    app.Logger.LogInformation("DB target => Server: {Server}, DB: {DB}, Encrypt: {Encrypt}, TrustServerCert: {TSC}",
        b.DataSource, b.InitialCatalog, b.Encrypt, b.TrustServerCertificate);
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Could not parse connection string 'mycon'.");
}

// ------------ Global exception handler (echo CORS on errors) ------------
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";

        var origin = context.Request.Headers["Origin"].ToString();
        if (!string.IsNullOrEmpty(origin) && allowedOrigins.Contains(origin))
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = origin;
            context.Response.Headers["Vary"] = "Origin";
        }

        var err = context.Features.Get<IExceptionHandlerFeature>()?.Error?.Message ?? "Unhandled server error";
        await context.Response.WriteAsync($"{{\"error\":\"{err}\"}}");
    });
});

// ------------ Middleware order (keep this) ------------
app.UseHttpsRedirection();
app.UseCors("AllowReactApp");
app.UseAuthentication();
app.UseAuthorization();
app.UseStaticFiles();

// ------------ Optional: short-circuit raw OPTIONS (preflight) ------------
app.Use(async (ctx, next) =>
{
    if (string.Equals(ctx.Request.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
    {
        var origin = ctx.Request.Headers["Origin"].ToString();
        if (!string.IsNullOrEmpty(origin) && allowedOrigins.Contains(origin))
        {
            ctx.Response.Headers["Access-Control-Allow-Origin"] = origin;
            ctx.Response.Headers["Vary"] = "Origin";
            ctx.Response.Headers["Access-Control-Allow-Methods"] =
                ctx.Request.Headers["Access-Control-Request-Method"].ToString() ?? "GET,POST,PUT,DELETE,OPTIONS";
            ctx.Response.Headers["Access-Control-Allow-Headers"] =
                ctx.Request.Headers["Access-Control-Request-Headers"].ToString() ?? "*";
            // ctx.Response.Headers["Access-Control-Allow-Credentials"] = "true"; // only if you enabled credentials
        }
        ctx.Response.StatusCode = StatusCodes.Status204NoContent;
        return;
    }
    await next();
});

// ------------ Per-request timing + correlation id (nice for logs) ------------
app.Use(async (ctx, next) =>
{
    var sw = Stopwatch.StartNew();
    try { await next(); }
    finally
    {
        sw.Stop();
        app.Logger.LogInformation("HTTP {Method} {Path} => {Status} in {Ms} ms (cid {Cid})",
            ctx.Request.Method, ctx.Request.Path, ctx.Response.StatusCode, sw.ElapsedMilliseconds, ctx.TraceIdentifier);
        ctx.Response.Headers["x-correlation-id"] = ctx.TraceIdentifier;
    }
});

// ------------ Swagger UI (enable during bring-up; gate later if desired) ------------
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "PTfinder API v1");
});

// ------------ SAFE auto-migrate (won’t crash app if DB isn’t ready) ------------
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.SetCommandTimeout(120); // only for migration run
        db.Database.Migrate();
        app.Logger.LogInformation("Migrations applied.");
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Migrate failed; starting app anyway.");
    }
}

// ------------ Liveness & diagnostics endpoints ------------
app.MapGet("/health", () => Results.Ok(new { status = "ok", t = DateTime.UtcNow }));

app.MapHealthChecks("/healthz");

// Config peek (no secrets leaked)
app.MapGet("/debug/config", (IConfiguration cfg) =>
{
    var mycon = cfg.GetConnectionString("mycon") ?? "(null)";
    var masked = mycon.Replace("Password=", "Password=***");
    var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "(null)";
    return Results.Ok(new { hasMycon = mycon != "(null)", connectionStringMasked = masked, environment = env });
});

// DB ping (from inside the running app)
app.MapGet("/debug/dbping", async (IServiceProvider sp) =>
{
    try
    {
        var cfg = sp.GetRequiredService<IConfiguration>();
        var csLocal = cfg.GetConnectionString("mycon") ?? "(null)";
        using var conn = new Microsoft.Data.SqlClient.SqlConnection(csLocal);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DB_NAME() AS DbName, SUSER_SNAME() AS LoginName;";
        using var r = await cmd.ExecuteReaderAsync();
        string dbName = "", login = "";
        if (await r.ReadAsync()) { dbName = r.GetString(0); login = r.GetString(1); }

        return Results.Ok(new
        {
            canConnect = true,
            dataSource = conn.DataSource,
            database = conn.Database,
            login,
            reportedDb = dbName
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

// Migrations applied?
app.MapGet("/debug/migrations", async (AppDbContext db) =>
{
    try
    {
        var applied = new List<object>();
        await db.Database.OpenConnectionAsync();
        using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "IF OBJECT_ID('__EFMigrationsHistory') IS NOT NULL SELECT [MigrationId] FROM __EFMigrationsHistory ORDER BY [MigrationId];";
        using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync()) applied.Add(new { MigrationId = rd.GetString(0) });
        return Results.Ok(new { migrationsApplied = applied });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
    finally
    {
        await db.Database.CloseConnectionAsync();
    }
});

// ------------ Map controllers ------------
app.MapControllers();

// ------------ Warm the DB once after startup ------------
app.Lifetime.ApplicationStarted.Register(() =>
{
    _ = Task.Run(async () =>
    {
        try
        {
            using var scope2 = app.Services.CreateScope();
            var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
            await db2.Database.CanConnectAsync();
            app.Logger.LogInformation("Warmup DB ping succeeded.");
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Warmup DB ping failed.");
        }
    });
});

app.Run();

