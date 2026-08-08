using System.Text.Json.Serialization;
using Azure.Messaging.ServiceBus;
using FashionSaaS.API.BackgroundJobs;
using FashionSaaS.API.Extensions;
using FashionSaaS.API.Handlers;
using FashionSaaS.API.Hubs;
using FashionSaaS.API.Logging;
using FashionSaaS.API.Middleware;
using FashionSaaS.Application.Configuration;
using FashionSaaS.Infrastructure;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Serilog;
using Serilog.Events;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// ── Serilog ──────────────────────────────────────────────────────────────────
// CA1305 suppressed: Serilog's WriteTo.Console()/File() sink configuration overloads used
// here don't expose a caller-supplied IFormatProvider parameter — the analyzer's match is
// against Serilog's own internal formatting, not something this call site can address.
#pragma warning disable CA1305
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .Destructure.With(new SensitiveDataDestructuringPolicy())
    .WriteTo.Console()
    .WriteTo.File("logs/fashionsaas-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();
#pragma warning restore CA1305

builder.Host.UseSerilog();

// ── Services ─────────────────────────────────────────────────────────────────

// Infrastructure: DbContext, repos, UoW, security services, email, audit, hosted jobs
builder.Services.AddInfrastructure(builder.Configuration);

// Application services (AuthService, TenantService, UserService, etc.)
builder.Services.AddApplicationServices();

// JWT bearer auth (HS256)
builder.Services.AddJwtAuthentication(builder.Configuration);

// SignalR (real-time notifications hub) — in-framework, no new server NuGet package
builder.Services.AddSignalR();

// Try-on results consumer: reads the try-on microservice's TryOnResultEvent off Service Bus and
// turns it into a Notification + live push. Registered here rather than in AddInfrastructure
// because it depends on IHubContext<NotificationsHub>, which only exists once SignalR is added.
builder.Services.AddOptions<ServiceBusSettings>()
    .Bind(builder.Configuration.GetSection(ServiceBusSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton(sp =>
    new ServiceBusClient(sp.GetRequiredService<IOptions<ServiceBusSettings>>().Value.ConnectionString));

builder.Services.AddHostedService<TryOnResultConsumer>();

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

// Register all FluentValidation validators in the Application assembly (current + future).
// CONVENTIONS §8: validators run at the API boundary before the controller action.
builder.Services.AddValidatorsFromAssembly(typeof(FashionSaaS.Application.Categories.CategoryService).Assembly);

// Global exception handler (CONVENTIONS §3)
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// Health checks — consumed by the Dockerfile's HEALTHCHECK directive (Phase 8, D6).
builder.Services.AddHealthChecks();

// Controllers + Swagger
// JsonStringEnumConverter: all enum-typed request/response DTO properties serialize/bind as their
// member name (e.g. "Pending"), not the underlying int. Without this, any DTO exposing a raw enum
// property (as opposed to OrderDto's explicit lowercase-string mapping) round-trips as a bare number,
// silently breaking any client that assumes named values.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
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

// CORS — allowed origins from config (CONVENTIONS §2: Options pattern), fall back to local Angular dev server
builder.Services.AddCors(options =>
{
    options.AddPolicy("FashionSaaSCors", policy =>
    {
        CorsSettings? corsSettings = builder.Configuration
            .GetSection(CorsSettings.SectionName)
            .Get<CorsSettings>();
        var allowed = corsSettings?.AllowedOrigins is { Length: > 0 }
            ? corsSettings.AllowedOrigins
            : ["http://localhost:4200"];

        policy.WithOrigins(allowed)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

WebApplication app = builder.Build();

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
app.MapHub<NotificationsHub>("/hubs/notifications");
app.MapHealthChecks("/health");

await app.RunAsync();
