using System.Text;
using FlowerShop.Domain.Entities;
using FlowerShop.Infrastructure.Authentication;
using FlowerShop.Infrastructure.Persistence;
using FlowerShop.Infrastructure.Storage;
using FlowerShop.Api.Modules.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("FlowerShop")
    ?? Environment.GetEnvironmentVariable("FLOWERSHOP_CONNECTION_STRING")
    ?? throw new InvalidOperationException("Chưa cấu hình connection string FlowerShop.");
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (Encoding.UTF8.GetByteCount(jwt.SigningKey) < 32)
    throw new InvalidOperationException("Jwt:SigningKey phải có ít nhất 32 byte và được cấp qua cấu hình bí mật.");

builder.Services.AddDbContext<FlowerShopDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<IPasswordService, PasswordService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<AccountStore>();
builder.Services.AddScoped<BusinessCommands>();
builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" };
        return Task.CompletedTask;
    });
    options.AddOperationTransformer((operation, context, _) =>
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<Microsoft.AspNetCore.Authorization.IAuthorizeData>().Any() && !metadata.OfType<Microsoft.AspNetCore.Authorization.IAllowAnonymous>().Any())
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = [] });
        }
        return Task.CompletedTask;
    });
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = jwt.Issuer,
        ValidateAudience = true, ValidAudience = jwt.Audience,
        ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
        ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30)
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var subject = context.Principal?.FindFirst("sub")?.Value;
            var session = context.Principal?.FindFirst("sid")?.Value;
            var role = context.Principal?.FindFirst("active_role")?.Value;
            if (!Guid.TryParse(subject, out var userId) || !Guid.TryParse(session, out var sessionId) || role is null)
            { context.Fail("Phiên đăng nhập không hợp lệ."); return; }
            var db = context.HttpContext.RequestServices.GetRequiredService<FlowerShopDbContext>();
            var valid = await db.Sessions.AnyAsync(s => s.Id == sessionId && s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > DateTimeOffset.UtcNow)
                && await db.Users.AnyAsync(u => u.Id == userId && u.IsActive && u.Roles.Any(r => r.Role == role));
            if (!valid) context.Fail("Tài khoản, quyền hoặc phiên đã bị thu hồi.");
        }
    };
});
builder.Services.AddAuthorization();

var app = builder.Build();
if (args.Contains("--bootstrap", StringComparer.Ordinal))
{
    var username = Environment.GetEnvironmentVariable("FLOWERSHOP_BOOTSTRAP_USERNAME") ?? "";
    var password = Environment.GetEnvironmentVariable("FLOWERSHOP_BOOTSTRAP_PASSWORD") ?? "";
    var fullName = Environment.GetEnvironmentVariable("FLOWERSHOP_BOOTSTRAP_FULLNAME") ?? "";
    await DatabaseInitializer.InitializeFirstAccountAsync(app.Services, username, password, fullName);
    Console.WriteLine("Đã tạo tài khoản quản trị đầu tiên.");
    return;
}
app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.AddHttpAuthentication("Bearer", scheme => scheme.Token = ""));
}
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", async (FlowerShopDbContext db) => await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ok" }) : Results.StatusCode(503));
await app.RunAsync();

public partial class Program;
