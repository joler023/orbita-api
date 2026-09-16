using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.IdentityModel.Tokens;
using Orbita.Api.ErrorHandling;
using Orbita.Api.Realtime;
using Orbita.Application;
using Orbita.Application.Crm;
using Orbita.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Enums (e.g. MemberRole on ORB-A07's invitation requests) serialize as their names
// ("Admin") instead of raw integers — readable in requests/responses and in Scalar's
// generated docs.
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddOrbitaApplication();
builder.Services.AddOrbitaInfrastructure();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// The access token travels as an httpOnly cookie, not an Authorization header, so it
// works for both fetch calls and (once Track B builds it) the SignalR WebSocket
// handshake, which cannot set custom headers — see ADR-004/ADR-011. JwtBearer still
// does the actual validation; only where it reads the token from changes.
//
// Configuration is injected into the options via .Configure<IConfiguration>(...)
// rather than captured from builder.Configuration directly, for the same reason
// AddOrbitaInfrastructure resolves its connection string lazily through DI: it is
// guaranteed to run only once the host is fully built, after
// WebApplicationFactory-based tests have layered on their configuration overrides.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IConfiguration>((options, configuration) =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = configuration["Jwt:Issuer"] ?? "orbita",
            ValidateAudience = true,
            ValidAudience = configuration["Jwt:Audience"] ?? "orbita-api",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(
                    configuration["Jwt:SigningKey"]
                        ?? throw new InvalidOperationException("Missing 'Jwt:SigningKey' configuration value."))),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Cookies.TryGetValue("access_token", out var accessToken))
                {
                    context.Token = accessToken;
                }

                // WebSockets cannot attach the cookie on every frame; the JS client
                // also sends the access token as ?access_token= during negotiate.
                if (string.IsNullOrEmpty(context.Token)
                    && context.HttpContext.Request.Path.StartsWithSegments("/hubs")
                    && context.Request.Query.TryGetValue("access_token", out var queryToken))
                {
                    context.Token = queryToken;
                }

                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddSignalR();
builder.Services.AddScoped<ICrmRealtimePublisher, SignalRCrmRealtimePublisher>();

builder.Services.AddCors();
builder.Services.AddOptions<CorsOptions>()
    .Configure<IConfiguration>((options, configuration) => options.AddPolicy("Frontend", policy =>
    {
        var corsOrigin = configuration["Cors:AllowedOrigin"];
        if (corsOrigin is not null)
        {
            policy.WithOrigins(corsOrigin).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }
    }));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseExceptionHandler();

app.UseHttpsRedirection();

app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<CrmHub>("/hubs/crm");

app.Run();

// Exposed so Orbita.IntegrationTests can boot this app via WebApplicationFactory<Program>.
public partial class Program;
