using FashionSaaS.TryOn.Application.Gemini;
using FashionSaaS.TryOn.Application.HuggingFace;
using FashionSaaS.TryOn.Infrastructure;
using FashionSaaS.TryOn.Infrastructure.BackgroundJobs;
using FashionSaaS.TryOn.Infrastructure.HuggingFace;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.Extensions.Options;
using Refit;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddTryOnInfrastructure(builder.Configuration);
builder.Services.AddTryOnAuthentication(builder.Configuration);
builder.Services.AddAuthorization();

builder.Services.AddOptions<GeminiSettings>()
    .Bind(builder.Configuration.GetSection(GeminiSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<HuggingFaceSettings>()
    .Bind(builder.Configuration.GetSection(HuggingFaceSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddRefitClient<IGeminiTextClient>()
    .ConfigureHttpClient((sp, client) =>
    {
        GeminiSettings settings = sp.GetRequiredService<IOptions<GeminiSettings>>().Value;
        client.BaseAddress = new Uri(settings.BaseUrl);
    });

builder.Services.AddHttpClient<IHuggingFaceTryOnClient, HuggingFaceTryOnClient>((sp, client) =>
{
    HuggingFaceSettings settings = sp.GetRequiredService<IOptions<HuggingFaceSettings>>().Value;
    client.BaseAddress = new Uri(settings.SpaceUrl);
    // Free-tier CPU rendering can genuinely take minutes; the default 100s HttpClient timeout
    // would abort a slow-but-successful poll response.
    client.Timeout = TimeSpan.FromMinutes(2);
});

builder.Services.AddHostedService<TryOnPollingWorker>();

builder.Services.AddHttpClient(); // plain named client for the garment-image GET (TryOnService's IHttpClientFactory.CreateClient())

builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssembly(typeof(FashionSaaS.TryOn.Application.TryOn.TryOnRequestFormValidator).Assembly);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "FashionSaaS.TryOn API", Version = "v1" });
});

WebApplication app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

await app.RunAsync();

// Required for WebApplicationFactory<Program> in the cross-service JWT acceptance test (Group D)
// to locate the entry point.
public partial class Program
{
    private Program()
    {
    }
}
