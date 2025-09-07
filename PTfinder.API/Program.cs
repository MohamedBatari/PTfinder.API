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

// Load .env (optional; user-secrets/appsettings still override)
Env.Load();

// ===== CORS: allow your sites =====
var allowedOrigins = new[]
{
    "https://ptfindernow.com",
    "https://www.ptfindernow.com",
    "http://localhost:3000"
};

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .SetIsOriginAllowedToAllowWildcardSubdomains()
            .AllowAnyHeader()
            .AllowAnyMethod();
        // NOTE: Don't call AllowCredentials() unless you actually use cookies.
    });
});

// ===== DbContext =====
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("mycon")));

// ===== Controllers & JSON =====
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler =
            System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

// ===== Other services =====
builder.Services.AddSingleton<BlobStorageService>();

// ===== API behavior =====
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.SuppressModelStateInvalidFilter = true;
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

// ===== Apply EF migrations at startup =====
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

// ===== Global exception handler (adds CORS headers on errors too) =====
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";

        // Reflect CORS so the browser can read the error body
        var origin = context.Request.Headers["Origin"].ToString();
        if (!string.IsNullOrEmpty(origin) && allowedOrigins.Contains(origin))
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = origin;
            context.Response.Headers["Vary"] = "Origin";
            // Only add Allow-Credentials if you enabled it in the policy
            // context.Response.Headers["Access-Control-Allow-Credentials"] = "true";
        }

        var err = context.Features.Get<IExceptionHandlerFeature>()?.Error?.Message ?? "Unhandled server error";
        await context.Response.WriteAsync($"{{\"error\":\"{err}\"}}");
    });
});

// ===== Swagger in Development =====
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// CORS MUST run early to catch preflights
app.UseCors("AllowReactApp");

app.UseAuthentication();
app.UseAuthorization();

app.UseStaticFiles();

// Handle preflight OPTIONS for any API route (so browser gets CORS headers)
app.MapMethods("/api/{**path}", new[] { "OPTIONS" }, () => Results.NoContent())
   .RequireCors("AllowReactApp");

// Controllers
app.MapControllers().RequireCors("AllowReactApp");

// (Optional) quick health ping
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

