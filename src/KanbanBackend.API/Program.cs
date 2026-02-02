using FluentValidation;
using HotChocolate.Authorization;
using HotChocolate.Data;
using HotChocolate.Validation;
using KanbanBackend.API.Data;
using KanbanBackend.API.GraphQL;
using KanbanBackend.API.GraphQL.Mutations;
using KanbanBackend.API.GraphQL.Queries;
using KanbanBackend.API.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Disable legacy claim mapping (SOAP-style)
System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

// Serilog Configuration
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console()); // Structured Logging to Console

// Add services to the container.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
if (allowedOrigins == null || allowedOrigins.Length == 0)
    allowedOrigins = new[] { "http://localhost:3000" };

builder.Services.AddHttpContextAccessor();
builder.Services.AddValidatorsFromAssemblyContaining<Program>(); // Register all validators

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigins", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .WithMethods("POST") // GraphQL only needs POST
              .WithHeaders("Content-Type", "Authorization")
              .AllowCredentials() // Required for Cookies
              .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
    });
});

builder.Services.AddMemoryCache();
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrEmpty(connectionString))
    {
        throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
    }

    options.UseSqlite(connectionString);
});

builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
{
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
})
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
    options.DefaultScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    var secret = builder.Configuration["Auth:JwtSecret"] ?? throw new InvalidOperationException("JWT Secret is missing from configuration. Please set 'Auth:JwtSecret'.");
    var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(secret!));

    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true, 
        ValidateIssuerSigningKey = true,
        ValidIssuer = "kanban-backend",
        ValidAudience = "kanban-client",
        IssuerSigningKey = key,
        ClockSkew = TimeSpan.Zero
    };
    options.MapInboundClaims = false; // Disable default claim mapping
});

builder.Services.AddAuthorization();
builder.Services.AddScoped<KanbanBackend.API.Services.AuthService>();
builder.Services.AddHostedService<KanbanBackend.API.Services.TokenCleanupService>(); // Daily cleanup

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>();

builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>()
    .AddMutationType<Mutation>()
    .AddTypeExtension<KanbanBackend.API.GraphQL.Mutations.AuthMutations>()
    .AddProjections()
    .AddFiltering()
    .AddSorting()
    .AddErrorFilter<GraphQLErrorFilter>()
    .AddMaxExecutionDepthRule(8) 
    .AddAuthorization()
    .AddInMemorySubscriptions(); // Enable Subscriptions & Event Sender

var app = builder.Build();

// Ensure database is created (for demo purposes)
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    context.Database.Migrate();
}

app.UseSerilogRequestLogging(); // Log HTTP requests
app.UseCors("AllowSpecificOrigins");

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");

app.MapGraphQL();

app.Run();

public partial class Program { }
