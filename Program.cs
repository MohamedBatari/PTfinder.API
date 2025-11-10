using DotNetEnv;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PTfinder.API.DATA;
using PTfinder.API.Hubs;
using PTfinder.API.Services;
using PTfinder.API.Settings;
using AppBillingService = PTfinder.API.Services.BillingService; // alias to avoid Stripe.BillingService collision
using Stripe;
using System.Diagnostics;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ------------ Load .env (optional for local) ------------
Env.Load();

// ------------ SignalR & notifications ------------
builder.Services.AddSignalR();
builder.Services.AddScoped<INotificationService, NotificationService>();


var stripeSecret =
    builder.Configuration["Stripe:SecretKey"];
if (string.IsNullOrWhiteSpace(stripeSecret))
{
    throw new InvalidOperationException(
        "Stripe secret key is not configured. Set 'Stripe:SecretKey' ."
    );
}

StripeConfiguration.ApiKey = stripeSecret;
// ------------ Allowed Origins ------------
var allowedOrigins = new[]
{
    "https://ptfindernow.com",
    "https://www.ptfindernow.com",
    "http://localhost:3000",
};

// CORS must allow WebSockets + credentials from your web origins
builder.Services.AddCors(o => o.AddPolicy("web",
    p => p
        .WithOrigins("http://localhost:3000", "https://ptfindernow.com")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()
));

// ------------ Stripe settings & config ------------
builder.Services.Configure<StripeSettings>(builder.Configuration.GetSection("Stripe"));
StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"];

// Register your BillingService using the alias (prevents ambiguity with Stripe.BillingService)
builder.Services.AddScoped<AppBillingService>();

// ------------ CORS policy (for general API calls) ------------
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
        sql.CommandTimeout(30); // seconds
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
builder.Services.AddScoped<PartnerService>(); // Partner seat enforcement & coach linking

// ------------ API behavior tweaks ------------
builder.Services.Configure<ApiBehaviorOptions>(o =>
{
    o.SuppressModelStateInvalidFilter = true;
});

// ------------ Swagger (hardened) ------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "PTfinder API", Version = "v1" });

    // Avoid type-name collisions across namespaces
    c.CustomSchemaIds(t => t.FullName);

    // If two actions resolve to the same route/verb, pick the first (prevents throw)
    c.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());

    // Optional: JWT in Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter: Bearer {your JWT}"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

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

// ------------ Health checks ------------
builder.Services.AddHealthChecks();

var app = builder.Build();

// ------------ Log DB target at startup ------------
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

// ------------ Error pages / handler by environment ------------
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    // Global exception handler (safe header writes)
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            var origin = context.Request.Headers["Origin"].ToString();

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/json";

                if (!string.IsNullOrEmpty(origin) && allowedOrigins.Contains(origin))
                {
                    context.Response.Headers["Access-Control-Allow-Origin"] = origin;
                    context.Response.Headers["Vary"] = "Origin";
                }

                var err = context.Features.Get<IExceptionHandlerFeature>()?.Error?.Message
                          ?? "Unhandled server error";
                await context.Response.WriteAsync($"{{\"error\":\"{err}\"}}");
            }
            else
            {
                var err = context.Features.Get<IExceptionHandlerFeature>()?.Error;
                app.Logger.LogError(err, "Unhandled exception after response started.");
            }
        });
    });
}

// ------------ Middleware order ------------
app.UseHttpsRedirection();
app.UseCors("AllowReactApp"); // primary CORS for API requests
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

// ------------ Per-request timing + correlation id (set BEFORE next) ------------
app.Use(async (ctx, next) =>
{
    // set early so it's always present and safe
    ctx.Response.Headers["x-correlation-id"] = ctx.TraceIdentifier;

    var sw = Stopwatch.StartNew();
    try { await next(); }
    finally
    {
        sw.Stop();
        app.Logger.LogInformation("HTTP {Method} {Path} => {Status} in {Ms} ms (cid {Cid})",
            ctx.Request.Method, ctx.Request.Path, ctx.Response.StatusCode, sw.ElapsedMilliseconds, ctx.TraceIdentifier);
        // do not set headers here; response may have started
    }
});

// ------------ Swagger UI ------------
app.UseSwagger();
if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "PTfinder API v1"));
}
else
{
    app.UseSwaggerUI(); // keep UI in prod if you want; can lock behind auth/gateway
}

// ------------ SAFE auto-migrate ------------
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

// Hubs
app.MapHub<NotifyHub>("/hubs/notify");

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
