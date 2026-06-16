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
        builder.Services.AddScoped<IEmailService, EmailService>();
        builder.Services.AddScoped<INotificationService, NotificationService>();
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

            var logger = scope.ServiceProvider
                .GetRequiredService<ILogger<AppDbContext>>();

            // Rename PascalCase tables to lowercase if they were created by a
            // previous deployment before we standardised on lowercase names.
            var renameSql = new[]
            {
                "RENAME TABLE `AppNotifications` TO `appnotifications`",
                "RENAME TABLE `UserNotificationSettings` TO `usernotificationsettings`",
                "RENAME TABLE `EventAttendees` TO `eventattendees`",
                "RENAME TABLE `EventFavourites` TO `eventfavourites`",
                "RENAME TABLE `BrandVehicleCategories` TO `brandvehiclecategories`",
            };
            foreach (var sql in renameSql)
            {
                try { db.Database.ExecuteSqlRaw(sql); }
                catch (Exception ex) { logger.LogDebug("RENAME TABLE skipped: {Message}", ex.Message); }
            }

            // Create any tables that are still missing (no FKs to avoid case issues).
            var missingTableSql = new[]
            {
                @"CREATE TABLE IF NOT EXISTS `appnotifications` (
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
  KEY `IX_AppNotifications_UserId` (`UserId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS `usernotificationsettings` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `UserId` int NOT NULL,
  `Category` varchar(255) NOT NULL,
  `EmailEnabled` tinyint(1) NOT NULL DEFAULT 1,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_UserNotificationSettings_UserId_Category` (`UserId`, `Category`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS `eventattendees` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `EventId` int NOT NULL,
  `UserId` int NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_EventAttendees_EventId_UserId` (`EventId`, `UserId`),
  KEY `IX_EventAttendees_UserId` (`UserId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS `eventfavourites` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `EventId` int NOT NULL,
  `UserId` int NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_EventFavourites_EventId_UserId` (`EventId`, `UserId`),
  KEY `IX_EventFavourites_UserId` (`UserId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS `brandvehiclecategories` (
  `BrandsId` int NOT NULL,
  `CategoriesId` int NOT NULL,
  PRIMARY KEY (`BrandsId`, `CategoriesId`),
  KEY `IX_BrandVehicleCategories_CategoriesId` (`CategoriesId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS `featurecategories` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `Name` longtext NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS `drivetypes` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `Name` longtext NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS `carcolors` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `Name` longtext NOT NULL,
  `HexCode` longtext NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS `advertviews` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `AdvertId` int NOT NULL,
  `ViewedAt` datetime(6) NOT NULL,
  `IpAddress` longtext NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_AdvertViews_AdvertId` (`AdvertId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS `userfollows` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `FollowerId` int NOT NULL,
  `FollowedId` int NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_UserFollows_FollowerId_FollowedId` (`FollowerId`, `FollowedId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS `reviews` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `ReviewerId` int NOT NULL,
  `ReviewedUserId` int NOT NULL,
  `Rating` int NOT NULL,
  `Comment` longtext NULL,
  `CreatedAt` datetime(6) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4"
            };

            foreach (var sql in missingTableSql)
            {
                try
                {
                    db.Database.ExecuteSqlRaw(sql);
                }
                catch (Exception ex)
                {
                    logger.LogWarning("Could not create table: {Message}", ex.Message);
                }
            }

            // Make formerly NOT NULL columns nullable to match current entity definitions.
            // Wrap each in try/catch: if already nullable or column doesn't exist, skip.
            var modifyColumnSql = new[]
            {
                "ALTER TABLE `caradverts` MODIFY COLUMN `GearboxId` int NULL",
                "ALTER TABLE `caradverts` MODIFY COLUMN `BodyTypeId` int NULL",
                "ALTER TABLE `caradverts` MODIFY COLUMN `PowerHP` int NULL",
                "ALTER TABLE `caradverts` MODIFY COLUMN `PowerKW` int NULL",
                "ALTER TABLE `caradverts` MODIFY COLUMN `EngineSize` int NULL",
            };
            foreach (var sql in modifyColumnSql)
            {
                try { db.Database.ExecuteSqlRaw(sql); }
                catch (Exception ex) { logger.LogDebug("MODIFY COLUMN skipped: {Message}", ex.Message); }
            }

            // Add columns to `adverts` that were added to the entity after the last migration.
            // Each statement is wrapped in try/catch; MySQL raises an error if the column
            // already exists so failures here are expected and safe to ignore.
            var addAdvertColumnsSql = new[]
            {
                "ALTER TABLE `adverts` ADD COLUMN `IsHidden` tinyint(1) NOT NULL DEFAULT 0",
                "ALTER TABLE `adverts` ADD COLUMN `IsActive` tinyint(1) NOT NULL DEFAULT 1",
                "ALTER TABLE `adverts` ADD COLUMN `ExpiresAt` datetime(6) NULL",
            };
            foreach (var sql in addAdvertColumnsSql)
            {
                try { db.Database.ExecuteSqlRaw(sql); }
                catch (Exception ex) { logger.LogDebug("ADD COLUMN adverts skipped: {Message}", ex.Message); }
            }

            // Add columns to `caradverts` that were added to the entity after the last migration.
            var addCarAdvertColumnsSql = new[]
            {
                "ALTER TABLE `caradverts` ADD COLUMN `VehicleCategoryId` int NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `DriveTypeId` int NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `ColorId` int NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `DoorCount` int NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `SeatsCount` int NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `Vin` varchar(17) NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `Slug` varchar(255) NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `Condition` varchar(50) NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `IsNegotiable` tinyint(1) NOT NULL DEFAULT 0",
                "ALTER TABLE `caradverts` ADD COLUMN `SellerType` varchar(50) NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `FirstRegistrationDate` datetime(6) NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `RegistrationCountry` varchar(100) NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `OwnersCount` int NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `IsImported` tinyint(1) NOT NULL DEFAULT 0",
                "ALTER TABLE `caradverts` ADD COLUMN `ImportCountry` varchar(100) NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `NextInspection` datetime(6) NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `HasServiceBook` tinyint(1) NOT NULL DEFAULT 0",
                "ALTER TABLE `caradverts` ADD COLUMN `HasFullServiceHistory` tinyint(1) NOT NULL DEFAULT 0",
                "ALTER TABLE `caradverts` ADD COLUMN `HasDamage` tinyint(1) NOT NULL DEFAULT 0",
                "ALTER TABLE `caradverts` ADD COLUMN `DamageDescription` longtext NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `HasWarranty` tinyint(1) NOT NULL DEFAULT 0",
                "ALTER TABLE `caradverts` ADD COLUMN `WarrantyUntil` datetime(6) NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `Torque` int NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `Acceleration` decimal(18,2) NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `FuelConsumptionCity` decimal(18,2) NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `FuelConsumptionHighway` decimal(18,2) NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `FuelConsumptionCombined` decimal(18,2) NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `Co2Emission` int NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `EuroNorm` varchar(50) NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `CurbWeight` int NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `GrossWeight` int NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `Badge` varchar(50) NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `BadgeExpiresAt` datetime(6) NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `AxleCount` int NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `Payload` int NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `CargoLength` decimal(18,2) NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `CargoHeight` decimal(18,2) NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `Volume` decimal(18,2) NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `HasRetarder` tinyint(1) NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `HasTachograph` tinyint(1) NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `BodySubtype` varchar(255) NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `CatalogNumber` varchar(255) NULL",
                "ALTER TABLE `caradverts` ADD COLUMN `Compatibility` longtext NULL",
            };
            foreach (var sql in addCarAdvertColumnsSql)
            {
                try { db.Database.ExecuteSqlRaw(sql); }
                catch (Exception ex) { logger.LogDebug("ADD COLUMN caradverts skipped: {Message}", ex.Message); }
            }

            // Add CategoryId to features if it was added after the DB was exported.
            try { db.Database.ExecuteSqlRaw("ALTER TABLE `features` ADD COLUMN `CategoryId` int NOT NULL DEFAULT 0"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN features.CategoryId skipped: {Message}", ex.Message); }
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
