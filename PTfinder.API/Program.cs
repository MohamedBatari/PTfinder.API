using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

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
        // Only add .AllowCredentials() if you actually use cookies on the frontend.
    });
});

// ===== DbContext =====
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("mycon")));

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

var app = builder.Build();

// ===== CORS MUST BE FIRST in the pipeline =====
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

// ===== EF migrations at startup =====
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

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

// Map controllers (CORS already applied globally via UseCors)
app.MapControllers();

// Health
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

