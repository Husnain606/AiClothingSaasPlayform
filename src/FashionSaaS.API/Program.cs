using FashionSaaS.Application.Interfaces;
using FashionSaaS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Stub CurrentTenantService for design time (temporary, will be replaced in Task 10)
builder.Services.AddScoped<ICurrentTenantService>(_ => new StubCurrentTenantService());

// Add DbContext
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection") ?? "Server=.;Database=FashionSaaS;Trusted_Connection=true;TrustServerCertificate=true;"));

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

// Temporary stub for design time — will be replaced in Task 10
class StubCurrentTenantService : ICurrentTenantService
{
    public Guid? TenantId => null;
    public string? TenantSlug => null;
    public bool IsResolved => false;
    public void SetTenant(Guid tenantId, string slug) { }
}
