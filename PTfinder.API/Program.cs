using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Data.SqlClient;
using System.Text;
using System.Diagnostics;

using DotNetEnv;
using PTfinder.API.DATA;
using PTfinder.API.Services;
using PTfinder.API.Settings;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Load .env (optional)
Env.Load();

// ===== Allowed Origins =====
var allowedOrigins = new[]
{
    "https://ptfindernow.com",
    "https://www.ptfindernow.com",
    "http://localhost:3000",
};

// ===== CORS (policy) =====
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
        // Only add .AllowCredentials() if you use cookies on the frontend.
    });
});

// ===== Connection String (fail fast if missing) =====
var cs = builder.Configuration.GetConnectionString("mycon")
         ?? throw new InvalidOperationException("Missing connection string 'mycon'.");

// ===== DbContext (resilient SQL + longer timeout) =====
builder.Services.AddDbContextPool<AppDbContext>(options =>
    options.UseSqlServer(cs, sql =>
    {
        sql.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: new[] { 40613, 40197, 40501, 10928, 10929 } // common Azure SQL transient errors
        );
        sql.CommandTimeout(60); // ride through serverless resume / cold DB
    })
);

// ===== Controllers & JSON =====
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.ReferenceHandler =
            System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

// ===== Other services =====
builder.Services.AddSingleton<BlobStorageService>();

// ===== API behavior =====
builder.Services.Configure<ApiBehaviorOptions>(o =>
{
    o.SuppressModelStateInvalidFilter = true;
});

// ===== Swagger =====
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ===== SMTP settings + email sender =====
builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<SmtpSettings>>().Value);
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();

// ===== Auth (JWT) =====
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

// (Optional) lightweight health checks (framework-native liveness)
builder.Services.AddHealthChecks();

var app = builder.Build();

// ===== TEMP: log DB target at startup (helps verify 'mycon') =====
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

// ===== CORS MUST BE EARLY in the pipeline =====
app.UseCors("AllowReactApp");

// (Optional) short-circuit raw OPTIONS so preflight always gets headers
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

// ===== EF migrations at startup (with simple retries to avoid resume races) =====
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var attempts = 0;
    while (true)
    {
        try
        {
            attempts++;
            db.Database.Migrate();
            break;
        }
        catch (Exception ex) when (attempts < 3)
        {
            app.Logger.LogWarning(ex, "Migrate failed (attempt {A}). Retrying...", attempts);
            await Task.Delay(1000 * attempts);
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "Migrate failed permanently.");
            throw;
        }
    }
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));


// ===== Global exception handler (echo CORS on errors) =====
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

// ===== Diagnostics: per-request timing + correlation id =====
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

// Swagger in Development
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseStaticFiles();

// ===== Warm the DB once after startup (reduces first-hit spike) =====
app.Lifetime.ApplicationStarted.Register(() =>
{
    _ = Task.Run(async () =>
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.CanConnectAsync();
            app.Logger.LogInformation("Warmup DB ping succeeded.");
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Warmup DB ping failed.");
        }
    });
});

// ===== Debug DB ping endpoint (safe to keep) =====
app.MapGet("/debug/dbping", async (AppDbContext db) =>
{
    try
    {
        var can = await db.Database.CanConnectAsync();
        var provider = db.Database.ProviderName;
        return Results.Ok(new { canConnect = can, provider });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: 500);
    }
});

// ===== Liveness endpoints =====
app.MapGet("/health", () => Results.Ok(new { status = "ok", t = DateTime.UtcNow }));
app.MapHealthChecks("/healthz");

// Map controllers (CORS already applied globally via UseCors)
app.MapControllers();

app.Run();
