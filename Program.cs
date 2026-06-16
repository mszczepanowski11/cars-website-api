using System.Text;
using System.Text.Json.Serialization;
using cars_website_api.CarsWebsite.Interfaces;
using cars_website_api.CarsWebsite.Services;
using CarsWebsite;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;


internal class Program
{
    public static void Main(string[] args)
    {
        var webRootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        Directory.CreateDirectory(webRootPath);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            WebRootPath = webRootPath
        });

        // Prefer Railway-injected MySQL env vars (always correct) over manually set connection string
        var mysqlHost = Environment.GetEnvironmentVariable("MYSQLHOST");
        var mysqlPass = Environment.GetEnvironmentVariable("MYSQLPASSWORD");
        string? connectionString = null;
        if (!string.IsNullOrEmpty(mysqlHost) && !string.IsNullOrEmpty(mysqlPass))
        {
            var port = Environment.GetEnvironmentVariable("MYSQLPORT") ?? "3306";
            var db   = Environment.GetEnvironmentVariable("MYSQLDATABASE") ?? Environment.GetEnvironmentVariable("MYSQL_DATABASE") ?? "railway";
            var user = Environment.GetEnvironmentVariable("MYSQLUSER") ?? "root";
            connectionString = $"Server={mysqlHost};Port={port};Database={db};User={user};Password={mysqlPass};";
        }
        else
        {
            connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
        }

        if (string.IsNullOrEmpty(connectionString))
            throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

        var jwtKey = builder.Configuration["Jwt:Key"];
        var jwtIssuer = builder.Configuration["Jwt:Issuer"];
        var jwtAudience = builder.Configuration["Jwt:Audience"];
        var jwtExpiresInMinutes = builder.Configuration["Jwt:ExpiresInMinutes"];
        if (string.IsNullOrEmpty(jwtKey) || string.IsNullOrEmpty(jwtIssuer) ||
            string.IsNullOrEmpty(jwtAudience) || string.IsNullOrEmpty(jwtExpiresInMinutes))
            throw new InvalidOperationException("JWT configuration is incomplete. Ensure Jwt:Key, Jwt:Issuer, Jwt:Audience, and Jwt:ExpiresInMinutes are set.");
        if (!double.TryParse(jwtExpiresInMinutes, out _))
            throw new InvalidOperationException("Jwt:ExpiresInMinutes must be a valid number.");

        builder.Services.AddControllers()
            .AddJsonOptions(options => {
                options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            });
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddDbContext<AppDbContext>(options => options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 21))));
        
        builder.Services.AddScoped<UserService>();
        builder.Services.AddScoped<IUserService, UserService>();
        builder.Services.AddScoped<AuthService>();
        builder.Services.AddScoped<IAuthService, AuthService>();
        builder.Services.AddScoped<IFollowService, FollowService>();
        builder.Services.AddScoped<IReviewService, ReviewService>();
        builder.Services.AddScoped<IAdvertService, AdvertService>();
        builder.Services.AddScoped<IAdvertImageService, AdvertImageService>();
        builder.Services.AddScoped<ITaxonomyService, TaxonomyService>();
        builder.Services.AddScoped<ICategoryService, CategoryService>();
        builder.Services.AddScoped<IFavoriteService, FavoriteService>();
        builder.Services.AddScoped<IMessageService, MessageService>();
        builder.Services.AddScoped<IReportService, ReportService>();
        builder.Services.AddScoped<IAdminService, AdminService>();
        builder.Services.AddScoped<IEventService, EventService>();
        builder.Services.AddHttpClient();
        builder.Services.AddScoped<IPaymentService, PaymentService>();
        builder.Services.AddScoped<IInvoiceService, InvoiceService>();
        builder.Services.AddHostedService<MonthlyInvoiceJob>();
        builder.Services.AddHostedService<ExpiryReminderJob>();

        builder.Services.AddAutoMapper(typeof(AdvertMappingProfile));

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtIssuer,
                    ValidAudience = jwtAudience,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtKey))
                };
            });

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("AdminOnly", policy =>
                policy.RequireClaim("isAdmin", "true"));
        });

        var allowedOrigins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? [];

        builder.Services.AddCors(options => {
            options.AddPolicy("AllowNuxt", policy => {
                policy.WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });
        
        builder.Services.AddSwaggerGen(c =>
        {
            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Bearer "
            });

            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    System.Array.Empty<string>()
                }
            });
        });

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();

            // EnsureCreated() is a no-op when the database already exists,
            // even if individual tables are missing (e.g. after a partial data import).
            // Explicitly create any tables that may be absent so the API never
            // crashes with "Table doesn't exist" on first use.
            var missingTableSql = new[]
            {
                @"CREATE TABLE IF NOT EXISTS `AppNotifications` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `UserId` int NOT NULL,
  `Type` varchar(255) NOT NULL,
  `Title` longtext NOT NULL,
  `Content` longtext NOT NULL,
  `IsRead` tinyint(1) NOT NULL DEFAULT 0,
  `CreatedAt` datetime(6) NOT NULL,
  `AdvertId` int NULL,
  `PaymentId` int NULL,
  `InvoiceId` int NULL,
  `EmailSent` tinyint(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`),
  KEY `IX_AppNotifications_UserId` (`UserId`),
  CONSTRAINT `FK_AppNotifications_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS `UserNotificationSettings` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `UserId` int NOT NULL,
  `Category` varchar(255) NOT NULL,
  `EmailEnabled` tinyint(1) NOT NULL DEFAULT 1,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_UserNotificationSettings_UserId_Category` (`UserId`, `Category`),
  CONSTRAINT `FK_UserNotificationSettings_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS `EventAttendees` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `EventId` int NOT NULL,
  `UserId` int NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_EventAttendees_EventId_UserId` (`EventId`, `UserId`),
  KEY `IX_EventAttendees_UserId` (`UserId`),
  CONSTRAINT `FK_EventAttendees_Events_EventId` FOREIGN KEY (`EventId`) REFERENCES `Events` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_EventAttendees_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS `EventFavourites` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `EventId` int NOT NULL,
  `UserId` int NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_EventFavourites_EventId_UserId` (`EventId`, `UserId`),
  KEY `IX_EventFavourites_UserId` (`UserId`),
  CONSTRAINT `FK_EventFavourites_Events_EventId` FOREIGN KEY (`EventId`) REFERENCES `Events` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_EventFavourites_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS `BrandVehicleCategories` (
  `BrandsId` int NOT NULL,
  `CategoriesId` int NOT NULL,
  PRIMARY KEY (`BrandsId`, `CategoriesId`),
  KEY `IX_BrandVehicleCategories_CategoriesId` (`CategoriesId`),
  CONSTRAINT `FK_BrandVehicleCategories_Brands_BrandsId` FOREIGN KEY (`BrandsId`) REFERENCES `Brands` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_BrandVehicleCategories_VehicleCategories_CategoriesId` FOREIGN KEY (`CategoriesId`) REFERENCES `VehicleCategories` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4"
            };

            foreach (var sql in missingTableSql)
                db.Database.ExecuteSqlRaw(sql);
        }

        app.UseSwagger();
        app.UseSwaggerUI();
        
        app.UseStaticFiles(); 
        app.UseHttpsRedirection();
        app.UseCors("AllowNuxt");
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();      
        app.Run();
    }
}
