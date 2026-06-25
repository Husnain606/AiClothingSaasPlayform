using FashionSaaS.API.Extensions;
using FashionSaaS.API.Handlers;
using FashionSaaS.API.Logging;
using FashionSaaS.API.Middleware;
using FashionSaaS.Infrastructure;
using FluentValidation.AspNetCore;
using Microsoft.OpenApi;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ──────────────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .Destructure.With(new SensitiveDataDestructuringPolicy())
    .WriteTo.Console()
    .WriteTo.File("logs/fashionsaas-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// ── Services ─────────────────────────────────────────────────────────────────

// Infrastructure: DbContext, repos, UoW, security services, email, audit, hosted jobs
builder.Services.AddInfrastructure(builder.Configuration);

// Application services (AuthService, TenantService, UserService, etc.)
builder.Services.AddApplicationServices();

// JWT bearer auth (HS256)
builder.Services.AddJwtAuthentication(builder.Configuration);

// Authorization policies — MfaVerified requires mfa_verified=true claim in JWT
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("MfaVerified", policy =>
        policy.RequireClaim("mfa_verified", "true"));
});

// Rate limiting: PublicPolicy / AuthenticatedPolicy / SuperAdminPolicy
builder.Services.AddRateLimiting();

// MediatR + ValidationBehavior + LoggingBehavior
builder.Services.AddMediatRWithBehaviors();

// FluentValidation auto-validation on controllers
builder.Services.AddFluentValidationAutoValidation();

// AutoMapper — profiles will be scanned via AddAutoMapper(cfg => cfg.AddMaps(...)) in Tasks 22-25
// Register with empty config for now; profiles added per controller assembly later
builder.Services.AddAutoMapper(cfg => { });

// Global exception handler (CONVENTIONS §3)
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// Controllers + Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "FashionSaaS API", Version = "v1" });

    // Swashbuckle 10 / OpenApi 2.x: AddSecurityDefinition takes IOpenApiSecurityScheme
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token (without the 'Bearer ' prefix)."
    });

    // AddSecurityRequirement takes Func<OpenApiDocument, OpenApiSecurityRequirement>
    // OpenApiSecurityRequirement is Dictionary<OpenApiSecuritySchemeReference, List<string>>
    c.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer")] = []
    });
});

// CORS — allowed origins from config, fall back to local Angular dev server
builder.Services.AddCors(options =>
{
    options.AddPolicy("FashionSaaSCors", policy =>
    {
        var allowed = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? ["http://localhost:4200"];

        policy.WithOrigins(allowed)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// ── Middleware pipeline — ORDER MATTERS ───────────────────────────────────────
app.UseHttpsRedirection();
app.UseHsts();

// 1. Security headers (X-Frame-Options, CSP, HSTS etc.)   — Task 21 completes
app.UseMiddleware<SecurityHeadersMiddleware>();

// 2. Global exception handler (CONVENTIONS §3 — replaces ExceptionHandlingMiddleware)
app.UseExceptionHandler();

// 3. CORS
app.UseCors("FashionSaaSCors");

// 4. Rate limiting
app.UseRateLimiter();

// 5. Authentication — must run before tenant resolution so the JWT tenant_id claim
//    is available when TenantResolutionMiddleware reads HttpContext.User
app.UseAuthentication();

// 6. Tenant resolution from JWT claim / X-Tenant-Slug.
//    Placed after UseAuthentication so the JWT is already decoded and the tenant_id
//    claim is populated; storefront slug resolution also works here (route values
//    are available post-routing regardless of auth order).
app.UseMiddleware<TenantResolutionMiddleware>();

// 7. Authorization
app.UseAuthorization();

// 8. Audit logging (write AuditLog row after response)     — Task 21 completes
app.UseMiddleware<AuditLoggingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();
