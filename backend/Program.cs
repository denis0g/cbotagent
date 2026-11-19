using System.Text;
using backend.Services;
using backend.Tools;
using Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (!File.Exists(envPath))
    envPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", ".env"));
DotNetEnv.Env.Load(envPath);

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();


var dbUrl = builder.Configuration.GetConnectionString("PostgresConnectionUrl");

builder.Services.AddDbContext<AppDatabaseContext>(
    options => options.UseNpgsql(dbUrl)
);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration.GetValue<string>("Jwt:Issuer"),
            ValidateAudience = true,
            ValidAudience = builder.Configuration.GetValue<string>("Jwt:Audience"),
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration.GetValue<string>("Jwt:SecurityKey")!)
            ),
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true
        };
    });

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins("http://localhost:4200", "https://localhost:4200", "http://localhost:58073")
        .AllowAnyHeader()
        .AllowAnyMethod()));

//add the services
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<ConversationsService>();
builder.Services.AddScoped<CbotsService>();
builder.Services.AddScoped<CbotAgentService>();
builder.Services.AddScoped<StrategyRefinerAgent>();
builder.Services.AddScoped<CodeGenerationAgent>();
builder.Services.AddScoped<CompilationAgent>();
builder.Services.AddScoped<CompilationAgentTools>();
builder.Services.AddScoped<GeminiClient>();

var app = builder.Build();

await app.Services.ApplyDatabaseMigrationsAsync();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
