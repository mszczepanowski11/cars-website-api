using System.Text;
using System.Text.Json.Serialization;
using cars_website_api.CarsWebsite.Data;
using cars_website_api.CarsWebsite.Interfaces;
using cars_website_api.CarsWebsite.Services;
using cars_website_api.CarsWebsite.Domain.Entities;
using CarsWebsite;
using CloudinaryDotNet;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using DriveType = cars_website_api.CarsWebsite.Domain.Entities.DriveType;


internal class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("CARIZO API v1.0.2 STARTING");
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

        // JWT_SECRET_KEY env var takes precedence over appsettings (required in production)
        var jwtKey = Environment.GetEnvironmentVariable("JWT_SECRET_KEY") ?? builder.Configuration["Jwt:Key"];
        var jwtIssuer = builder.Configuration["Jwt:Issuer"];
        var jwtAudience = builder.Configuration["Jwt:Audience"];
        var jwtExpiresInMinutes = builder.Configuration["Jwt:ExpiresInMinutes"];
        if (string.IsNullOrEmpty(jwtKey) || string.IsNullOrEmpty(jwtIssuer) ||
            string.IsNullOrEmpty(jwtAudience) || string.IsNullOrEmpty(jwtExpiresInMinutes))
            throw new InvalidOperationException("JWT configuration is incomplete. Ensure Jwt:Key, Jwt:Issuer, Jwt:Audience, and Jwt:ExpiresInMinutes are set.");
        if (!double.TryParse(jwtExpiresInMinutes, out _))
            throw new InvalidOperationException("Jwt:ExpiresInMinutes must be a valid number.");

        // B-02: Validate Imoje payment credentials at startup.
        // Read from environment variables (preferred in production) or appsettings fallback.
        var imojeMerchantId  = Environment.GetEnvironmentVariable("IMOJE_MERCHANT_ID")     ?? builder.Configuration["Imoje:MerchantId"]    ?? "";
        var imojeApiKey      = Environment.GetEnvironmentVariable("IMOJE_API_KEY")          ?? builder.Configuration["Imoje:ApiKey"]        ?? "";
        var imojeWebhookSec  = Environment.GetEnvironmentVariable("IMOJE_WEBHOOK_SECRET")   ?? builder.Configuration["Imoje:WebhookSecret"] ?? "";
        var imojeServiceId   = Environment.GetEnvironmentVariable("IMOJE_SERVICE_ID")       ?? builder.Configuration["Imoje:ServiceId"]     ?? "";
        var missingImoje = new List<string>();
        if (string.IsNullOrWhiteSpace(imojeMerchantId))  missingImoje.Add("IMOJE_MERCHANT_ID / Imoje:MerchantId");
        if (string.IsNullOrWhiteSpace(imojeApiKey))      missingImoje.Add("IMOJE_API_KEY / Imoje:ApiKey");
        if (string.IsNullOrWhiteSpace(imojeWebhookSec))  missingImoje.Add("IMOJE_WEBHOOK_SECRET / Imoje:WebhookSecret");
        if (string.IsNullOrWhiteSpace(imojeServiceId))   missingImoje.Add("IMOJE_SERVICE_ID / Imoje:ServiceId");
        if (missingImoje.Count > 0 && !builder.Environment.IsDevelopment())
            throw new InvalidOperationException(
                $"Imoje payment configuration is missing required values: {string.Join(", ", missingImoje)}. " +
                "Set the corresponding environment variables or appsettings values before starting the application.");
        if (missingImoje.Count > 0)
            Console.WriteLine($"[WARNING] Imoje payment credentials not fully configured (missing: {string.Join(", ", missingImoje)}). Payments will fail at runtime.");

        builder.Services.AddControllers()
            .AddJsonOptions(options => {
                options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
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

        var cloudName   = (Environment.GetEnvironmentVariable("CLOUDINARY_CLOUD_NAME")   ?? "").Trim();
        var cloudApiKey = (Environment.GetEnvironmentVariable("CLOUDINARY_API_KEY")       ?? "").Trim();
        var cloudSecret = (Environment.GetEnvironmentVariable("CLOUDINARY_API_SECRET")    ?? "").Trim();

        // Use placeholder credentials when env vars are missing so the API still starts.
        // Actual uploads will fail at runtime with a clear error rather than crashing the container.
        var effectiveCloud  = string.IsNullOrEmpty(cloudName)   ? "placeholder"   : cloudName;
        var effectiveKey    = string.IsNullOrEmpty(cloudApiKey) ? "placeholder"   : cloudApiKey;
        var effectiveSecret = string.IsNullOrEmpty(cloudSecret) ? "placeholder"   : cloudSecret;

        var cloudinaryAccount = new Account(effectiveCloud, effectiveKey, effectiveSecret);
        var cloudinary = new Cloudinary(cloudinaryAccount);
        cloudinary.Api.Secure = true;
        builder.Services.AddSingleton(cloudinary);
        builder.Services.AddMemoryCache(); // B-27: taxonomy caching
        builder.Services.AddScoped<ITaxonomyService, TaxonomyService>();
        builder.Services.AddScoped<ICategoryService, CategoryService>();
        builder.Services.AddScoped<IFavoriteService, FavoriteService>();
        builder.Services.AddScoped<IMessageService, MessageService>();
        builder.Services.AddScoped<IReportService, ReportService>();
        builder.Services.AddScoped<IAdminService, AdminService>();
        builder.Services.AddScoped<IStatsService, StatsService>();
        builder.Services.AddScoped<IEventService, EventService>();
        builder.Services.AddHttpClient();
        builder.Services.AddHttpClient<IPhotoAnalysisService, PhotoAnalysisService>();
        builder.Services.AddScoped<IEmailService, EmailService>();
        builder.Services.AddScoped<INotificationService, NotificationService>();
        builder.Services.AddScoped<IPaymentService, PaymentService>();
        builder.Services.AddScoped<IInvoiceService, InvoiceService>();
        builder.Services.AddHostedService<MonthlyInvoiceJob>();
        builder.Services.AddHostedService<ExpiryReminderJob>();
        builder.Services.AddHostedService<BadgeExpiryJob>();
        builder.Services.AddHostedService<EventFeaturedExpiryJob>();

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // B-26: Global rate limiter — applies to all endpoints via app.UseRateLimiter()
            // and [EnableRateLimiting("global")] on sensitive controllers.
            options.AddFixedWindowLimiter("global", o =>
            {
                o.PermitLimit = 100;
                o.Window = TimeSpan.FromMinutes(1);
                o.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
                o.QueueLimit = 2;
            });

            // Per-endpoint stricter policies
            options.AddFixedWindowLimiter("auth", o =>
            {
                o.PermitLimit = 10;
                o.Window = TimeSpan.FromMinutes(1);
                o.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
                o.QueueLimit = 0;
            });
            options.AddFixedWindowLimiter("strict", o =>
            {
                o.PermitLimit = 5;
                o.Window = TimeSpan.FromMinutes(5);
                o.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
                o.QueueLimit = 0;
            });
        });

        builder.Services.AddMemoryCache();
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
                    .WithHeaders("Content-Type", "Authorization", "X-Requested-With", "X-CSRF-Token")
                    .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS");
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
            var startLogger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();
            startLogger.LogInformation("[Cloudinary] cloud={Cloud} key={Key}", cloudName.Length > 0 ? cloudName : "(empty)", cloudApiKey.Length > 4 ? cloudApiKey[..4] + "****" : "(empty)");

            // Bootstrap EF Core migration history for databases that were created via
            // EnsureCreated before formal migrations were adopted. On a fresh DB,
            // EnsureCreated builds the schema; then MigrateAsync applies only the new
            // pending migrations (e.g. indexes). On an existing DB without migration
            // history we pre-mark historical migrations as applied so MigrateAsync
            // only runs the new ones.
            db.Database.EnsureCreated();
            try
            {
                var histLogger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();
                db.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory` (
                        `MigrationId` varchar(150) CHARACTER SET utf8mb4 NOT NULL,
                        `ProductVersion` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
                        PRIMARY KEY (`MigrationId`)
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

                var applied = db.Database.GetAppliedMigrations().ToHashSet();
                if (!applied.Any())
                {
                    // DB was created via EnsureCreated — mark all pre-existing migrations
                    // as applied so MigrateAsync only runs genuinely new ones.
                    var allMigrations = db.Database.GetMigrations().ToList();
                    var newMigrations = new HashSet<string>
                    {
                        "20260621120000_AddBrandModelToFeatureCategory",
                        "20260621150000_AddFuelConsumptionToEngineVersion",
                        "20260622100000_AddMissingIndexes2",
                        "20260622120000_AddRefreshTokenRevokedAt",
                        "20260623100000_AddTrimVehicleSubtypePartCategories",
                        "20260623105000_AddVehicleCategoryIdToFeatureCategory",
                        "20260623110000_AddCustomCategoryRequests",
                    };
                    foreach (var m in allMigrations.Where(m => !newMigrations.Contains(m)))
                    {
                        db.Database.ExecuteSqlRaw(
                            $"INSERT IGNORE INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`) VALUES ('{m}', '9.0.0')");
                    }
                    histLogger.LogInformation("[Migrations] Bootstrapped migration history with {Count} pre-existing migrations", allMigrations.Count - newMigrations.Count);
                }

                db.Database.Migrate();
                histLogger.LogInformation("[Migrations] MigrateAsync completed");
            }
            catch (Exception ex)
            {
                var histLogger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();
                histLogger.LogWarning("[Migrations] Migration bootstrap failed (non-fatal): {Msg}", ex.Message);
            }

            // Explicit column guards — idempotent fallback for any migration that
            // may have been silently swallowed (try/catch above) or pre-marked via
            // the bootstrap path without actually running the DDL.
            var logger = scope.ServiceProvider
                .GetRequiredService<ILogger<AppDbContext>>();

            // Explicit column guards — idempotent fallback for any migration that
            // may have been silently swallowed or pre-marked without running the DDL.
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    ALTER TABLE `engineversions`
                    ADD COLUMN IF NOT EXISTS `FuelConsumptionCity`     decimal(5,2) NULL,
                    ADD COLUMN IF NOT EXISTS `FuelConsumptionHighway`  decimal(5,2) NULL,
                    ADD COLUMN IF NOT EXISTS `FuelConsumptionCombined` decimal(5,2) NULL
                ");
                logger.LogInformation("[Schema] FuelConsumption columns ensured on engineversions");
            }
            catch (Exception ex)
            {
                logger.LogWarning("[Schema] Could not ensure FuelConsumption columns: {Msg}", ex.Message);
            }

            try
            {
                db.Database.ExecuteSqlRaw(@"
                    ALTER TABLE `FeatureCategories`
                    ADD COLUMN IF NOT EXISTS `VehicleCategoryId` int NULL,
                    ADD COLUMN IF NOT EXISTS `BrandId` int NULL,
                    ADD COLUMN IF NOT EXISTS `ModelId` int NULL
                ");
                logger.LogInformation("[Schema] FeatureCategories columns ensured (VehicleCategoryId, BrandId, ModelId)");
            }
            catch (Exception ex)
            {
                logger.LogWarning("[Schema] Could not ensure FeatureCategories columns: {Msg}", ex.Message);
            }

            try
            {
                db.Database.ExecuteSqlRaw(@"
                    ALTER TABLE `engineversions`
                    ADD COLUMN IF NOT EXISTS `TrimId`           int NULL,
                    ADD COLUMN IF NOT EXISTS `TorqueNm`         int NULL,
                    ADD COLUMN IF NOT EXISTS `Co2EmissionGkm`   int NULL,
                    ADD COLUMN IF NOT EXISTS `EuroNorm`         varchar(20) NULL,
                    ADD COLUMN IF NOT EXISTS `AvgConsumptionL`  decimal(4,1) NULL,
                    ADD COLUMN IF NOT EXISTS `Acceleration0100` decimal(4,1) NULL,
                    ADD COLUMN IF NOT EXISTS `TopSpeedKmh`      int NULL,
                    ADD COLUMN IF NOT EXISTS `DriveType`        varchar(10) NULL,
                    ADD COLUMN IF NOT EXISTS `GearboxType`      varchar(20) NULL,
                    ADD COLUMN IF NOT EXISTS `Cylinders`        int NULL
                ");
                logger.LogInformation("[Schema] EngineVersions extra columns ensured");
            }
            catch (Exception ex)
            {
                logger.LogWarning("[Schema] Could not ensure EngineVersions extra columns: {Msg}", ex.Message);
            }

            try
            {
                db.Database.ExecuteSqlRaw(@"
                    ALTER TABLE `caradverts`
                    ADD COLUMN IF NOT EXISTS `TrimId`                  int NULL,
                    ADD COLUMN IF NOT EXISTS `VehicleSubtypeId`        int NULL,
                    ADD COLUMN IF NOT EXISTS `PartCategoryId`          int NULL,
                    ADD COLUMN IF NOT EXISTS `PartSubcategoryId`       int NULL,
                    ADD COLUMN IF NOT EXISTS `OemNumber`               varchar(100) NULL,
                    ADD COLUMN IF NOT EXISTS `ManufacturerPartNumber`  varchar(100) NULL,
                    ADD COLUMN IF NOT EXISTS `PartManufacturer`        varchar(100) NULL
                ");
                logger.LogInformation("[Schema] CarAdverts extra columns ensured");
            }
            catch (Exception ex)
            {
                logger.LogWarning("[Schema] Could not ensure CarAdverts extra columns: {Msg}", ex.Message);
            }

            // VehicleSubtype slug
            try {
                db.Database.ExecuteSqlRaw("ALTER TABLE `vehiclesubtypes` ADD COLUMN IF NOT EXISTS `Slug` varchar(100) NULL");
            } catch (Exception ex) { logger.LogWarning("ALTER vehiclesubtypes.Slug skipped: {Message}", ex.Message); }

            // CarAdvert subtype-specific fields
            try {
                db.Database.ExecuteSqlRaw(@"ALTER TABLE `caradverts`
        ADD COLUMN IF NOT EXISTS `OperatingWeightKg` int NULL,
        ADD COLUMN IF NOT EXISTS `WorkingWidthCm` int NULL,
        ADD COLUMN IF NOT EXISTS `MaxDiggingDepthM` decimal(5,2) NULL,
        ADD COLUMN IF NOT EXISTS `BucketCapacityL` int NULL,
        ADD COLUMN IF NOT EXISTS `TankCapacityL` int NULL");
            } catch (Exception ex) { logger.LogWarning("ALTER caradverts subtype fields skipped: {Message}", ex.Message); }

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

                @"CREATE TABLE IF NOT EXISTS `fueltypes` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `Name` longtext NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS `gearboxes` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `Name` longtext NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS `bodytypes` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `Name` longtext NOT NULL,
  PRIMARY KEY (`Id`)
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
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS `customcategoryrequests` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `UserId` varchar(255) NULL,
  `CategoryName` varchar(200) NOT NULL,
  `Description` text NULL,
  `ParametersJson` text NULL,
  `Status` varchar(20) NOT NULL DEFAULT 'Pending',
  `AdminNotes` text NULL,
  `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  `ReviewedAt` datetime(6) NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_customcategoryrequests_Status` (`Status`)
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

            var addAdvertColumnsSql = new[]
            {
                "ALTER TABLE `adverts` ADD COLUMN `IsHidden` tinyint(1) NOT NULL DEFAULT 0",
                "ALTER TABLE `adverts` ADD COLUMN `IsActive` tinyint(1) NOT NULL DEFAULT 1",
                "ALTER TABLE `adverts` ADD COLUMN `ExpiresAt` datetime(6) NULL",
                "ALTER TABLE `adverts` ADD COLUMN `SoldAt` datetime(6) NULL",
            };
            foreach (var sql in addAdvertColumnsSql)
            {
                try { db.Database.ExecuteSqlRaw(sql); }
                catch (Exception ex) { logger.LogDebug("ADD COLUMN adverts skipped: {Message}", ex.Message); }
            }

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

            // Ensure advertimages table exists
            try
            {
                db.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS `advertimages` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `AdvertId` int NOT NULL,
  `Url` longtext NOT NULL,
  `IsMain` tinyint(1) NOT NULL DEFAULT 0,
  `Order` int NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`),
  KEY `IX_AdvertImages_AdvertId` (`AdvertId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
            }
            catch (Exception ex) { logger.LogWarning("CREATE TABLE advertimages skipped: {Message}", ex.Message); }

            try { db.Database.ExecuteSqlRaw("ALTER TABLE `advertimages` ADD COLUMN `IsMain` tinyint(1) NOT NULL DEFAULT 0"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN advertimages.IsMain skipped: {Message}", ex.Message); }

            try
            {
                db.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS `trims` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `GenerationId` int NOT NULL,
  `Name` varchar(100) NOT NULL,
  `Description` varchar(500) NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_trims_GenerationId` (`GenerationId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
            }
            catch (Exception ex) { logger.LogWarning("CREATE TABLE trims skipped: {Message}", ex.Message); }

            try
            {
                db.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS `vehiclesubtypes` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `VehicleCategoryId` int NOT NULL,
  `Name` varchar(100) NOT NULL,
  `NamePl` varchar(100) NULL,
  `SortOrder` int NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`),
  KEY `IX_vehiclesubtypes_VehicleCategoryId` (`VehicleCategoryId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
            }
            catch (Exception ex) { logger.LogWarning("CREATE TABLE vehiclesubtypes skipped: {Message}", ex.Message); }

            try
            {
                db.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS `partcategories` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `Name` varchar(100) NOT NULL,
  `NamePl` varchar(100) NULL,
  `SortOrder` int NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
            }
            catch (Exception ex) { logger.LogWarning("CREATE TABLE partcategories skipped: {Message}", ex.Message); }

            try
            {
                db.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS `partsubcategories` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `PartCategoryId` int NOT NULL,
  `Name` varchar(100) NOT NULL,
  `NamePl` varchar(100) NULL,
  `SortOrder` int NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`),
  KEY `IX_partsubcategories_PartCategoryId` (`PartCategoryId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
            }
            catch (Exception ex) { logger.LogWarning("CREATE TABLE partsubcategories skipped: {Message}", ex.Message); }

            try
            {
                db.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS `customcategoryrequests` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `UserId` int NOT NULL,
  `CategoryName` varchar(255) NOT NULL,
  `Description` longtext NULL,
  `Status` varchar(50) NOT NULL DEFAULT 'Pending',
  `CreatedAt` datetime(6) NOT NULL,
  `ReviewedAt` datetime(6) NULL,
  `ReviewNote` longtext NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_customcategoryrequests_UserId` (`UserId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
            }
            catch (Exception ex) { logger.LogWarning("CREATE TABLE customcategoryrequests skipped: {Message}", ex.Message); }

            try { db.Database.ExecuteSqlRaw("ALTER TABLE `advertviews` ADD COLUMN `UserId` int NULL"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN advertviews.UserId skipped: {Message}", ex.Message); }

            try { db.Database.ExecuteSqlRaw("ALTER TABLE `userfollows` ADD COLUMN `FollowedAt` datetime(6) NOT NULL DEFAULT '2000-01-01 00:00:00'"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN userfollows.FollowedAt skipped: {Message}", ex.Message); }

            try { db.Database.ExecuteSqlRaw("ALTER TABLE `users` ADD COLUMN `GoogleId` varchar(255) NULL"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN users.GoogleId skipped: {Message}", ex.Message); }

            try { db.Database.ExecuteSqlRaw("ALTER TABLE `users` ADD COLUMN `FacebookId` varchar(255) NULL"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN users.FacebookId skipped: {Message}", ex.Message); }

            try { db.Database.ExecuteSqlRaw("ALTER TABLE `users` ADD COLUMN `BusinessType` int NULL"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN users.BusinessType skipped: {Message}", ex.Message); }

            try { db.Database.ExecuteSqlRaw("ALTER TABLE `users` ADD COLUMN `EmailVerificationToken` varchar(64) NULL"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN users.EmailVerificationToken skipped: {Message}", ex.Message); }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE `users` ADD COLUMN `EmailVerificationTokenExpires` datetime(6) NULL"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN users.EmailVerificationTokenExpires skipped: {Message}", ex.Message); }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE `users` ADD COLUMN `PasswordResetToken` varchar(64) NULL"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN users.PasswordResetToken skipped: {Message}", ex.Message); }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE `users` ADD COLUMN `PasswordResetTokenExpires` datetime(6) NULL"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN users.PasswordResetTokenExpires skipped: {Message}", ex.Message); }

            try { db.Database.ExecuteSqlRaw("ALTER TABLE `featurecategories` ADD COLUMN `VehicleCategoryId` int NULL"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN featurecategories.VehicleCategoryId skipped: {Message}", ex.Message); }

            try { db.Database.ExecuteSqlRaw("ALTER TABLE `events` ADD COLUMN `FeaturedUntil` datetime(6) NULL"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN events.FeaturedUntil skipped: {Message}", ex.Message); }

            try { db.Database.ExecuteSqlRaw("ALTER TABLE `payments` ADD COLUMN `EventId` int NULL"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN payments.EventId skipped: {Message}", ex.Message); }

            try { db.Database.ExecuteSqlRaw("ALTER TABLE `payments` ADD COLUMN `DurationDays` int NULL"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN payments.DurationDays skipped: {Message}", ex.Message); }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE `payments` ADD COLUMN `InvoiceId` int NULL"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN payments.InvoiceId skipped: {Message}", ex.Message); }

            try { db.Database.ExecuteSqlRaw("ALTER TABLE `features` ADD COLUMN `CategoryId` int NOT NULL DEFAULT 0"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN features.CategoryId skipped: {Message}", ex.Message); }

            var addUserColumnsSql = new[]
            {
                "ALTER TABLE `users` ADD COLUMN `AccountType` int NOT NULL DEFAULT 0",
                "ALTER TABLE `users` ADD COLUMN `CompanyName` longtext NULL",
                "ALTER TABLE `users` ADD COLUMN `Nip` varchar(50) NULL",
                "ALTER TABLE `users` ADD COLUMN `IsAdmin` tinyint(1) NOT NULL DEFAULT 0",
                "ALTER TABLE `users` ADD COLUMN `IsBlocked` tinyint(1) NOT NULL DEFAULT 0",
                "ALTER TABLE `users` ADD COLUMN `BlockedAt` datetime(6) NULL",
                "ALTER TABLE `users` ADD COLUMN `BlockedReason` longtext NULL",
                "ALTER TABLE `users` ADD COLUMN `AvatarUrl` longtext NULL",
                "ALTER TABLE `users` ADD COLUMN `EmailVerified` tinyint(1) NOT NULL DEFAULT 1",
                "ALTER TABLE `users` ADD COLUMN `LastLoginAt` datetime(6) NULL",
                "ALTER TABLE `users` ADD COLUMN `CreatedAt` datetime(6) NOT NULL DEFAULT '2000-01-01 00:00:00'",
                "ALTER TABLE `users` ADD COLUMN `City` longtext NULL",
                "ALTER TABLE `users` ADD COLUMN `Region` longtext NULL",
                "ALTER TABLE `users` ADD COLUMN `Street` longtext NULL",
                "ALTER TABLE `users` ADD COLUMN `PostalCode` longtext NULL",
                "ALTER TABLE `users` ADD COLUMN `Country` longtext NULL",
                "ALTER TABLE `users` ADD COLUMN `About` longtext NULL",
                "ALTER TABLE `users` ADD COLUMN `EmailNotifications` tinyint(1) NOT NULL DEFAULT 1",
                "ALTER TABLE `users` ADD COLUMN `PriceChangeAlerts` tinyint(1) NOT NULL DEFAULT 1",
                "ALTER TABLE `users` ADD COLUMN `NewMessageAlerts` tinyint(1) NOT NULL DEFAULT 1",
                "ALTER TABLE `users` ADD COLUMN `NewsletterSubscribed` tinyint(1) NOT NULL DEFAULT 0",
            };
            foreach (var sql in addUserColumnsSql)
            {
                try { db.Database.ExecuteSqlRaw(sql); }
                catch (Exception ex) { logger.LogDebug("ADD COLUMN users skipped: {Message}", ex.Message); }
            }

            try { db.Database.ExecuteSqlRaw("UPDATE `users` SET `EmailVerified` = 1 WHERE `EmailVerificationToken` IS NULL AND `EmailVerified` = 0"); }
            catch (Exception ex) { logger.LogDebug("UPDATE users.EmailVerified skipped: {Message}", ex.Message); }

            var modifyAdvertNullableSql = new[]
            {
                "ALTER TABLE `adverts` MODIFY COLUMN `City` longtext NULL",
                "ALTER TABLE `adverts` MODIFY COLUMN `Region` longtext NULL",
            };
            foreach (var sql in modifyAdvertNullableSql)
            {
                try { db.Database.ExecuteSqlRaw(sql); }
                catch (Exception ex) { logger.LogDebug("MODIFY COLUMN adverts skipped: {Message}", ex.Message); }
            }

            var addReviewColumnsSql = new[]
            {
                "ALTER TABLE `reviews` ADD COLUMN `SellerId` int NOT NULL DEFAULT 0",
                "ALTER TABLE `reviews` ADD COLUMN `BuyerId` int NOT NULL DEFAULT 0",
                "ALTER TABLE `reviews` ADD COLUMN `AdvertId` int NOT NULL DEFAULT 0",
                "ALTER TABLE `reviews` ADD COLUMN `IsVerifiedPurchase` tinyint(1) NOT NULL DEFAULT 0",
            };
            foreach (var sql in addReviewColumnsSql)
            {
                try { db.Database.ExecuteSqlRaw(sql); }
                catch (Exception ex) { logger.LogDebug("ADD COLUMN reviews skipped: {Message}", ex.Message); }
            }

            // RefreshToken columns — plain ADD COLUMN, try/catch handles duplicate silently
            try { db.Database.ExecuteSqlRaw("ALTER TABLE `users` ADD COLUMN `RefreshToken` varchar(128) NULL"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN users.RefreshToken skipped: {Message}", ex.Message); }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE `users` ADD COLUMN `RefreshTokenExpiry` datetime(6) NULL"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN users.RefreshTokenExpiry skipped: {Message}", ex.Message); }

            // Billing columns for payments
            try { db.Database.ExecuteSqlRaw("ALTER TABLE `payments` ADD COLUMN `BillingName` varchar(200) NULL"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN payments.BillingName skipped: {Message}", ex.Message); }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE `payments` ADD COLUMN `BillingNip` varchar(20) NULL"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN payments.BillingNip skipped: {Message}", ex.Message); }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE `payments` ADD COLUMN `BillingStreet` varchar(200) NULL"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN payments.BillingStreet skipped: {Message}", ex.Message); }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE `payments` ADD COLUMN `BillingPostalCode` varchar(20) NULL"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN payments.BillingPostalCode skipped: {Message}", ex.Message); }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE `payments` ADD COLUMN `BillingCity` varchar(100) NULL"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN payments.BillingCity skipped: {Message}", ex.Message); }

            try { db.Database.ExecuteSqlRaw("ALTER TABLE `newslettersubscribers` ADD COLUMN `IsConfirmed` tinyint(1) NOT NULL DEFAULT 0"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN newslettersubscribers.IsConfirmed skipped: {Message}", ex.Message); }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE `newslettersubscribers` ADD COLUMN `ConfirmationToken` varchar(64) NULL"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN newslettersubscribers.ConfirmationToken skipped: {Message}", ex.Message); }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE `newslettersubscribers` ADD COLUMN `ConfirmationTokenExpires` datetime(6) NULL"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN newslettersubscribers.ConfirmationTokenExpires skipped: {Message}", ex.Message); }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE `newslettersubscribers` ADD COLUMN `ConfirmedAt` datetime(6) NULL"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN newslettersubscribers.ConfirmedAt skipped: {Message}", ex.Message); }

            // Fix brands seeded with numeric names
            try
            {
                var firstBrand = db.Brands.OrderBy(b => b.Id).FirstOrDefault();
                if (firstBrand != null && firstBrand.Name.All(char.IsDigit))
                {
                    logger.LogWarning("Detected numeric brand names — clearing brand tables for re-seed");
                    db.Database.ExecuteSqlRaw("SET FOREIGN_KEY_CHECKS=0");
                    try
                    {
                        try { db.Database.ExecuteSqlRaw("DELETE FROM `brandvehiclecategories`"); } catch { }
                        try { db.Database.ExecuteSqlRaw("DELETE FROM `generations`"); } catch { }
                        try { db.Database.ExecuteSqlRaw("DELETE FROM `models`"); } catch { }
                        try { db.Database.ExecuteSqlRaw("DELETE FROM `brands`"); } catch { }
                        db.Database.ExecuteSqlRaw("UPDATE `caradverts` SET `BrandId` = NULL, `ModelId` = NULL WHERE 1=1");
                    }
                    finally
                    {
                        db.Database.ExecuteSqlRaw("SET FOREIGN_KEY_CHECKS=1");
                    }
                    logger.LogInformation("Brand tables cleared — seeder will re-populate on next call");
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning("Brand name fix skipped: {Message}", ex.Message);
            }

            // Idempotent: add missing equipment features that weren't in original seeder
            try
            {
                var safetyByCar = db.FeatureCategories
                    .Include(fc => fc.Features)
                    .FirstOrDefault(fc => fc.Name == "Bezpieczeństwo" && fc.VehicleCategoryId == db.VehicleCategories.Where(vc => vc.Slug == "auta-osobowe").Select(vc => vc.Id).FirstOrDefault());
                if (safetyByCar != null)
                {
                    var existingNames = safetyByCar.Features.Select(f => f.Name).ToHashSet();
                    var newSafety = new[] {
                        "BAS (Brake Assist System)", "EBD (elektroniczny podział sił hamowania)", "HBA (Hydraulic Brake Assist)",
                        "HDC (asystent zjazdu ze wzniesienia)", "Aktywny system unikania kolizji (FCW)",
                        "Asystent martwego pola (BSM)", "Ostrzeganie o ruchu poprzecznym (RCTA)",
                        "System nocnego widzenia", "Detekcja pieszych i rowerzystów",
                        "Rozpoznawanie znaków drogowych (TSR)", "System utrzymania pasa ruchu (LDW)",
                        "Monitoring ciśnienia w oponach (TPMS)", "Elektroniczna blokada dyferencjału"
                    }.Where(n => !existingNames.Contains(n));
                    foreach (var n in newSafety)
                        safetyByCar.Features.Add(new Feature { Name = n, CategoryId = safetyByCar.Id });
                    db.SaveChanges();
                }

                var comfortByCar = db.FeatureCategories
                    .Include(fc => fc.Features)
                    .FirstOrDefault(fc => fc.Name == "Komfort" && fc.VehicleCategoryId == db.VehicleCategories.Where(vc => vc.Slug == "auta-osobowe").Select(vc => vc.Id).FirstOrDefault());
                if (comfortByCar != null)
                {
                    var existingNames = comfortByCar.Features.Select(f => f.Name).ToHashSet();
                    var newComfort = new[] {
                        "Czterostrefowa klimatyzacja", "Podgrzewana szyba przednia", "Podgrzewana szyba tylna",
                        "Fotele masujące przednie", "Fotele masujące tylne", "Wentylowane fotele tylne",
                        "Pamięć ustawień fotela kierowcy i pasażera", "Ambientowe oświetlenie wnętrza",
                        "Elektryczna regulacja kierownicy", "Pamięć ustawień kierownicy",
                        "Szyberdach elektryczny", "Dach panoramiczny z roletą", "Ładowarka indukcyjna Qi",
                        "Automatyczne przyciemnianie lusterek", "Lusterko wsteczne z auto-ściemnianiem",
                        "Elektrycznie składane lusterka", "Ogrzewanie postojowe (Webasto/Eberspächer)",
                        "Elektryczna regulacja zagłówków tylnych", "Fotel relaksacyjny pasażera"
                    }.Where(n => !existingNames.Contains(n));
                    foreach (var n in newComfort)
                        comfortByCar.Features.Add(new Feature { Name = n, CategoryId = comfortByCar.Id });
                    db.SaveChanges();
                }

                var multiByCar = db.FeatureCategories
                    .Include(fc => fc.Features)
                    .FirstOrDefault(fc => fc.Name == "Multimedia" && fc.VehicleCategoryId == db.VehicleCategories.Where(vc => vc.Slug == "auta-osobowe").Select(vc => vc.Id).FirstOrDefault());
                if (multiByCar != null)
                {
                    var existingNames = multiByCar.Features.Select(f => f.Name).ToHashSet();
                    var newMulti = new[] {
                        "System audio Harman Kardon", "System audio Bose", "System audio Burmester",
                        "System audio Bang & Olufsen", "System audio Meridian", "System audio JBL",
                        "System audio Sony", "System audio Naim", "System audio Dynaudio",
                        "Panel dotykowy tylny", "Pilot od tyłu", "System audio 3D / surround",
                        "Kamera cofania HD", "Kamera 360° HD", "Widok z drona (Bird's Eye View)",
                        "Wyświetlacz przezierny (HUD)", "Cyfrowy kokpit (Digital Cockpit)",
                        "Cyfrowe lusterko wsteczne", "Streaming muzyki (Spotify/Apple Music)"
                    }.Where(n => !existingNames.Contains(n));
                    foreach (var n in newMulti)
                        multiByCar.Features.Add(new Feature { Name = n, CategoryId = multiByCar.Id });
                    db.SaveChanges();
                }

                var lightByCar = db.FeatureCategories
                    .Include(fc => fc.Features)
                    .FirstOrDefault(fc => fc.Name == "Oświetlenie" && fc.VehicleCategoryId == db.VehicleCategories.Where(vc => vc.Slug == "auta-osobowe").Select(vc => vc.Id).FirstOrDefault());
                if (lightByCar != null)
                {
                    var existingNames = lightByCar.Features.Select(f => f.Name).ToHashSet();
                    var newLight = new[] {
                        "Laser LED (BMW Laserlight / Audi Laser)", "HD Matrix LED", "Digital Matrix LED",
                        "Adaptacyjne światła przednie (AFS)", "Dynamiczne kierunkowskazy LED",
                        "Sekwencyjne kierunkowskazy LED", "Oświetlenie wejściowe LED (Welcome Light)",
                        "Diody tylne Full LED", "Tylne światła dynamiczne", "Podświetlenie progów LED"
                    }.Where(n => !existingNames.Contains(n));
                    foreach (var n in newLight)
                        lightByCar.Features.Add(new Feature { Name = n, CategoryId = lightByCar.Id });
                    db.SaveChanges();
                }

                var assistByCar = db.FeatureCategories
                    .Include(fc => fc.Features)
                    .FirstOrDefault(fc => fc.Name == "Systemy wspomagania" && fc.VehicleCategoryId == db.VehicleCategories.Where(vc => vc.Slug == "auta-osobowe").Select(vc => vc.Id).FirstOrDefault());
                if (assistByCar != null)
                {
                    var existingNames = assistByCar.Features.Select(f => f.Name).ToHashSet();
                    var newAssist = new[] {
                        "Aktywny asystent jazdy na autostradzie", "Asystent korytarza ratunkowego",
                        "Automatyczna zmiana pasa ruchu (LCA)", "Adaptacyjne zawieszenie pneumatyczne",
                        "Aktywny stabilizator przechyłów", "Skrętna tylna oś", "Tryby jazdy (Eco/Comfort/Sport/Off-Road)",
                        "Launch Control", "Asystent manewrowania z przyczepą", "Automatyczny parking z kluczyka (Remote Park)",
                        "Asystent drogowy (Traffic Assist)", "Predykcyjne zarządzanie energią (hybryda/EV)"
                    }.Where(n => !existingNames.Contains(n));
                    foreach (var n in newAssist)
                        assistByCar.Features.Add(new Feature { Name = n, CategoryId = assistByCar.Id });
                    db.SaveChanges();
                }

                var motoByCat = db.VehicleCategories.FirstOrDefault(vc => vc.Slug == "motocykle")?.Id;
                if (motoByCat.HasValue)
                {
                    var motoSafety = db.FeatureCategories.Include(fc => fc.Features)
                        .FirstOrDefault(fc => fc.Name == "Bezpieczeństwo" && fc.VehicleCategoryId == motoByCat.Value);
                    if (motoSafety != null)
                    {
                        var existingNames = motoSafety.Features.Select(f => f.Name).ToHashSet();
                        var newMotoSafety = new[] {
                            "Cornering ABS", "Cornering kontrola trakcji", "IMU (jednostka inercyjna)",
                            "DTC (Dynamic Traction Control)", "Rear Wheel Lift Mitigation",
                            "Slide Control", "Launch Control"
                        }.Where(n => !existingNames.Contains(n));
                        foreach (var n in newMotoSafety)
                            motoSafety.Features.Add(new Feature { Name = n, CategoryId = motoSafety.Id });
                        db.SaveChanges();
                    }

                    var motoComfort = db.FeatureCategories.Include(fc => fc.Features)
                        .FirstOrDefault(fc => fc.Name == "Komfort" && fc.VehicleCategoryId == motoByCat.Value);
                    if (motoComfort != null)
                    {
                        var existingNames = motoComfort.Features.Select(f => f.Name).ToHashSet();
                        var newMotoComfort = new[] {
                            "Tryby jazdy", "Regulowane zawieszenie elektroniczne (ESA)", "Aktywne zawieszenie",
                            "Adaptacyjny reflektor LED", "Ogrzewanie siodełka", "Gniazdo USB",
                            "Bezkluczykowy zapłon (keyless)"
                        }.Where(n => !existingNames.Contains(n));
                        foreach (var n in newMotoComfort)
                            motoComfort.Features.Add(new Feature { Name = n, CategoryId = motoComfort.Id });
                        db.SaveChanges();
                    }
                }
                logger.LogInformation("[Equipment] Expanded equipment features seeded successfully");
            }
            catch (Exception ex)
            {
                logger.LogWarning("[Equipment] Equipment expansion skipped: {Msg}", ex.Message);
            }

            SeedDataIfEmpty(db, logger);

            // Startup config diagnostics
            var imojeMid    = Environment.GetEnvironmentVariable("IMOJE_MERCHANT_ID") ?? "";
            var imojeKey    = Environment.GetEnvironmentVariable("IMOJE_API_KEY") ?? "";
            var imojeSecret = Environment.GetEnvironmentVariable("IMOJE_WEBHOOK_SECRET") ?? "";
            var internalSec = Environment.GetEnvironmentVariable("INTERNAL_SERVICE_SECRET") ?? "";
            logger.LogInformation(
                "[Config] IMOJE_MERCHANT_ID={HasMid} IMOJE_API_KEY={HasKey}(pfx={Pfx}) IMOJE_WEBHOOK_SECRET={HasWs} INTERNAL_SERVICE_SECRET={HasIs}",
                string.IsNullOrEmpty(imojeMid) ? "EMPTY" : "SET",
                string.IsNullOrEmpty(imojeKey) ? "EMPTY" : "SET",
                imojeKey.Length >= 6 ? imojeKey[..6] + "..." : "(short)",
                string.IsNullOrEmpty(imojeSecret) ? "EMPTY←WEBHOOKS BĘDĄ ODRZUCANE" : "SET",
                string.IsNullOrEmpty(internalSec) ? "EMPTY←WEBHOOKS BĘDĄ ODRZUCANE" : "SET");
        }

        app.UseExceptionHandler(exApp =>
        {
            exApp.Run(async context =>
            {
                var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
                var exLogger = app.Services.GetRequiredService<ILogger<Program>>();
                if (feature?.Error != null)
                    exLogger.LogError(feature.Error, "[GlobalExceptionHandler] Unhandled exception at {Path}", context.Request.Path);
                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new { message = "Internal server error" });
            });
        });

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                             | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
        });
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            await next();
        });
        if (!app.Environment.IsDevelopment())
            app.UseHsts();
        app.UseStaticFiles();
        app.UseHttpsRedirection();
        app.UseCors("AllowNuxt");
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.Run();
    }

    private static void SeedDataIfEmpty(AppDbContext db, ILogger logger)
    {
        // Vehicle Categories
        if (!db.VehicleCategories.Any())
        {
            db.VehicleCategories.AddRange(
                new VehicleCategory { Slug = "auta-osobowe",  Name = "Auta osobowe",  Description = "Sedany, coupe, SUV-y i więcej",          IconName = "mdi-car",                    SortOrder = 1 },
                new VehicleCategory { Slug = "dostawcze",     Name = "Dostawcze",     Description = "Busy, vany, samochody dostawcze",          IconName = "mdi-truck-delivery",         SortOrder = 2 },
                new VehicleCategory { Slug = "ciezarowe",     Name = "Ciężarowe",     Description = "Ciężarówki, TIR-y, naczepy i więcej",      IconName = "mdi-truck",                  SortOrder = 3 },
                new VehicleCategory { Slug = "maszyny",       Name = "Maszyny",       Description = "Maszyny budowlane, rolnicze i przemysłowe", IconName = "mdi-excavator",              SortOrder = 4 },
                new VehicleCategory { Slug = "czesci",        Name = "Części",        Description = "Części samochodowe, akcesoria i tuning",    IconName = "mdi-cog",                    SortOrder = 5 },
                new VehicleCategory { Slug = "motocykle",     Name = "Motocykle",     Description = "Motocykle, skutery, quady i więcej",        IconName = "mdi-motorbike",              SortOrder = 6 },
                new VehicleCategory { Slug = "przyczepy",     Name = "Przyczepy",     Description = "Przyczepy, lawety, naczepy i więcej",       IconName = "mdi-rv-truck",               SortOrder = 7 },
                new VehicleCategory { Slug = "rolnicze",      Name = "Rolnicze",      Description = "Maszyny i pojazdy rolnicze",                IconName = "mdi-tractor",                SortOrder = 8 },
                new VehicleCategory { Slug = "budowlane",     Name = "Budowlane",     Description = "Sprzęt budowlany i narzędzia",              IconName = "mdi-hard-hat",               SortOrder = 9 },
                new VehicleCategory { Slug = "inne",          Name = "Inne",          Description = "Pozostałe pojazdy i przedmioty",            IconName = "mdi-dots-horizontal-circle", SortOrder = 10 }
            );
            db.SaveChanges();
            logger.LogInformation("Seeded vehicle categories");
        }

        if (!db.FuelTypes.Any())
        {
            db.FuelTypes.AddRange(
                new FuelType { Name = "Benzyna" },
                new FuelType { Name = "Diesel" },
                new FuelType { Name = "LPG" },
                new FuelType { Name = "CNG" },
                new FuelType { Name = "Hybryda" },
                new FuelType { Name = "Hybryda mild" },
                new FuelType { Name = "Hybryda plug-in" },
                new FuelType { Name = "Elektryczny" },
                new FuelType { Name = "Wodór" },
                new FuelType { Name = "Benzyna + LPG" }
            );
            db.SaveChanges();
            logger.LogInformation("Seeded fuel types");
        }

        if (!db.Gearboxes.Any())
        {
            db.Gearboxes.AddRange(
                new Gearbox { Name = "Manualna" },
                new Gearbox { Name = "Automatyczna" },
                new Gearbox { Name = "Automatyczna (DSG/DCT)" },
                new Gearbox { Name = "Półautomatyczna" },
                new Gearbox { Name = "CVT" }
            );
            db.SaveChanges();
            logger.LogInformation("Seeded gearboxes");
        }

        if (!db.BodyTypes.Any())
        {
            db.BodyTypes.AddRange(
                new BodyType { Name = "Sedan" },
                new BodyType { Name = "Hatchback" },
                new BodyType { Name = "Kombi" },
                new BodyType { Name = "SUV" },
                new BodyType { Name = "Crossover" },
                new BodyType { Name = "Coupe" },
                new BodyType { Name = "Kabriolet" },
                new BodyType { Name = "Minivan / Van" },
                new BodyType { Name = "Pickup" },
                new BodyType { Name = "Liftback" },
                new BodyType { Name = "Roadster" },
                new BodyType { Name = "MPV" }
            );
            db.SaveChanges();
            logger.LogInformation("Seeded body types");
        }

        if (!db.DriveTypes.Any())
        {
            db.DriveTypes.AddRange(
                new DriveType { Name = "Przedni (FWD)" },
                new DriveType { Name = "Tylny (RWD)" },
                new DriveType { Name = "4x4 stały (AWD)" },
                new DriveType { Name = "4x4 dołączany (4WD)" }
            );
            db.SaveChanges();
            logger.LogInformation("Seeded drive types");
        }

        if (!db.CarColors.Any())
        {
            db.CarColors.AddRange(
                new CarColor { Name = "Biały",       HexCode = "#FFFFFF" },
                new CarColor { Name = "Czarny",      HexCode = "#111111" },
                new CarColor { Name = "Srebrny",      HexCode = "#C0C0C0" },
                new CarColor { Name = "Szary",        HexCode = "#808080" },
                new CarColor { Name = "Czerwony",     HexCode = "#CC0000" },
                new CarColor { Name = "Niebieski",    HexCode = "#0055CC" },
                new CarColor { Name = "Granatowy",    HexCode = "#1a237e" },
                new CarColor { Name = "Zielony",      HexCode = "#2E7D32" },
                new CarColor { Name = "Brązowy",      HexCode = "#5D4037" },
                new CarColor { Name = "Beżowy",       HexCode = "#D7CCC8" },
                new CarColor { Name = "Żółty",        HexCode = "#F9A825" },
                new CarColor { Name = "Pomarańczowy", HexCode = "#E65100" },
                new CarColor { Name = "Fioletowy",    HexCode = "#6A1B9A" },
                new CarColor { Name = "Złoty",        HexCode = "#FFD700" },
                new CarColor { Name = "Bordowy",      HexCode = "#800000" },
                new CarColor { Name = "Turkusowy",    HexCode = "#006064" }
            );
            db.SaveChanges();
            logger.LogInformation("Seeded car colors");
        }

        if (!db.FeatureCategories.Any())
        {
            var catList = db.VehicleCategories.ToList();

            int carCatId     = catList.FirstOrDefault(c => c.Slug == "auta-osobowe")?.Id ?? 0;
            int motoCatId    = catList.FirstOrDefault(c => c.Slug == "motocykle")?.Id ?? 0;
            int trailerCatId = catList.FirstOrDefault(c => c.Slug == "przyczepy")?.Id ?? 0;
            int agriCatId    = catList.FirstOrDefault(c => c.Slug == "rolnicze")?.Id ?? 0;

            var featureCategories = new List<FeatureCategory>
            {
                new FeatureCategory
                {
                    Name = "Bezpieczeństwo", VehicleCategoryId = carCatId == 0 ? null : carCatId,
                    Features = new List<Feature> {
                        new Feature { Name = "ABS" }, new Feature { Name = "ESP" }, new Feature { Name = "ASR / kontrola trakcji" },
                        new Feature { Name = "Airbag kierowcy" }, new Feature { Name = "Airbag pasażera" }, new Feature { Name = "Kurtyny powietrzne" },
                        new Feature { Name = "Boczne poduszki powietrzne" }, new Feature { Name = "Isofix" },
                        new Feature { Name = "Czujniki parkowania przednie" }, new Feature { Name = "Czujniki parkowania tylne" },
                        new Feature { Name = "Alarm" }, new Feature { Name = "Immobilizer" }
                    }
                },
                new FeatureCategory
                {
                    Name = "Komfort", VehicleCategoryId = carCatId == 0 ? null : carCatId,
                    Features = new List<Feature> {
                        new Feature { Name = "Klimatyzacja manualna" }, new Feature { Name = "Klimatyzacja automatyczna" },
                        new Feature { Name = "Dwustrefowa klimatyzacja" }, new Feature { Name = "Trzystrefowa klimatyzacja" },
                        new Feature { Name = "Podgrzewane fotele przednie" }, new Feature { Name = "Podgrzewane fotele tylne" },
                        new Feature { Name = "Wentylowane fotele" }, new Feature { Name = "Elektryczne fotele" },
                        new Feature { Name = "Pamięć ustawień fotela" }, new Feature { Name = "Podgrzewana kierownica" },
                        new Feature { Name = "Elektryczna regulacja lusterek" }, new Feature { Name = "Podgrzewane lusterka" },
                        new Feature { Name = "Elektryczna szyba przednia" }, new Feature { Name = "Elektryczna szyba tylna" },
                        new Feature { Name = "Keyless Entry" }, new Feature { Name = "Start/Stop" }
                    }
                },
                new FeatureCategory
                {
                    Name = "Multimedia", VehicleCategoryId = carCatId == 0 ? null : carCatId,
                    Features = new List<Feature> {
                        new Feature { Name = "Bluetooth" }, new Feature { Name = "Android Auto" }, new Feature { Name = "Apple CarPlay" },
                        new Feature { Name = "GPS / Nawigacja" }, new Feature { Name = "USB" }, new Feature { Name = "Ładowarka indukcyjna Qi" },
                        new Feature { Name = "System audio premium" }, new Feature { Name = "Radio fabryczne" },
                        new Feature { Name = "Ekran dotykowy" }, new Feature { Name = "Head-up display (HUD)" },
                        new Feature { Name = "Asystent głosowy" }, new Feature { Name = "Wi-Fi hotspot" }
                    }
                },
                new FeatureCategory
                {
                    Name = "Oświetlenie", VehicleCategoryId = carCatId == 0 ? null : carCatId,
                    Features = new List<Feature> {
                        new Feature { Name = "Halogeny" }, new Feature { Name = "Xenon" }, new Feature { Name = "Bi-Xenon" },
                        new Feature { Name = "Full LED" }, new Feature { Name = "Matrix LED" }, new Feature { Name = "Światła adaptacyjne" },
                        new Feature { Name = "Światła do jazdy dziennej (DRL)" }, new Feature { Name = "Podświetlenie wnętrza" }
                    }
                },
                new FeatureCategory
                {
                    Name = "Systemy wspomagania", VehicleCategoryId = carCatId == 0 ? null : carCatId,
                    Features = new List<Feature> {
                        new Feature { Name = "Tempomat" }, new Feature { Name = "Aktywny tempomat (ACC)" },
                        new Feature { Name = "Asystent pasa ruchu (LKA)" }, new Feature { Name = "Asystent martwego pola (BSM)" },
                        new Feature { Name = "Asystent parkowania" }, new Feature { Name = "Automatyczne parkowanie" },
                        new Feature { Name = "Kamera cofania" }, new Feature { Name = "Kamera 360°" },
                        new Feature { Name = "Hamowanie awaryjne (AEB)" }, new Feature { Name = "Rozpoznawanie znaków (TSR)" },
                        new Feature { Name = "Asystent zmęczenia kierowcy" }, new Feature { Name = "Asystent zjazdu ze wzniesienia (HDC)" },
                        new Feature { Name = "Asystent ruszania pod górkę (HSA)" }
                    }
                },
                new FeatureCategory
                {
                    Name = "Nadwozie i wyposażenie zewnętrzne", VehicleCategoryId = carCatId == 0 ? null : carCatId,
                    Features = new List<Feature> {
                        new Feature { Name = "Dach panoramiczny" }, new Feature { Name = "Szklany dach (moonroof)" },
                        new Feature { Name = "Relingi dachowe" }, new Feature { Name = "Hak holowniczy" },
                        new Feature { Name = "Przyciemniane szyby" }, new Feature { Name = "Felgi aluminiowe" },
                        new Feature { Name = "Opony zimowe (komplet)" }, new Feature { Name = "Koło zapasowe pełnowymiarowe" },
                        new Feature { Name = "Boczne progi" }, new Feature { Name = "Elektrycznie otwierana klapa bagażnika" }
                    }
                },
                new FeatureCategory
                {
                    Name = "Bezpieczeństwo", VehicleCategoryId = motoCatId == 0 ? null : motoCatId,
                    Features = new List<Feature> {
                        new Feature { Name = "ABS" }, new Feature { Name = "Kontrola trakcji (TCS)" },
                        new Feature { Name = "Asystent ruszania pod górkę (HSA)" }, new Feature { Name = "Hamowanie kombinowane (CBS)" }
                    }
                },
                new FeatureCategory
                {
                    Name = "Komfort", VehicleCategoryId = motoCatId == 0 ? null : motoCatId,
                    Features = new List<Feature> {
                        new Feature { Name = "Quickshifter" }, new Feature { Name = "Podgrzewane manetki" },
                        new Feature { Name = "Tempomat" }, new Feature { Name = "Elektrycznie regulowana szyba" },
                        new Feature { Name = "Elektryczna regulacja zawieszenia" }, new Feature { Name = "Podgrzewane siodełko" }
                    }
                },
                new FeatureCategory
                {
                    Name = "Bagaż i akcesoria", VehicleCategoryId = motoCatId == 0 ? null : motoCatId,
                    Features = new List<Feature> {
                        new Feature { Name = "Kufry boczne (oryginalne)" }, new Feature { Name = "Centralny kufer (oryginalne)" },
                        new Feature { Name = "Tankbag" }, new Feature { Name = "Owiewki boczne" },
                        new Feature { Name = "Osłona silnika" }, new Feature { Name = "Uchwyty pasażera" },
                        new Feature { Name = "Podnożki pasażera" }
                    }
                },
                new FeatureCategory
                {
                    Name = "Wyposażenie techniczne", VehicleCategoryId = trailerCatId == 0 ? null : trailerCatId,
                    Features = new List<Feature> {
                        new Feature { Name = "Hamulec najazdowy" }, new Feature { Name = "Koło podporowe" },
                        new Feature { Name = "Podpory tylne" }, new Feature { Name = "Burtownica aluminiowa" },
                        new Feature { Name = "Plandeka" }, new Feature { Name = "Rampa załadowcza" },
                        new Feature { Name = "Oświetlenie LED" }, new Feature { Name = "Blokada kuli" }
                    }
                },
                new FeatureCategory
                {
                    Name = "Kabina i komfort", VehicleCategoryId = agriCatId == 0 ? null : agriCatId,
                    Features = new List<Feature> {
                        new Feature { Name = "Klimatyzacja kabiny" }, new Feature { Name = "Zawieszenie kabiny" },
                        new Feature { Name = "Radio / Bluetooth" }, new Feature { Name = "Fotel z zawieszeniem pneumatycznym" }
                    }
                },
                new FeatureCategory
                {
                    Name = "Technologia i systemy", VehicleCategoryId = agriCatId == 0 ? null : agriCatId,
                    Features = new List<Feature> {
                        new Feature { Name = "GPS / Autosteering" }, new Feature { Name = "Kamera robocza" },
                        new Feature { Name = "System telematyczny" }, new Feature { Name = "4WD" },
                        new Feature { Name = "Przedni WOM" }, new Feature { Name = "Tylny WOM" },
                        new Feature { Name = "Blokada mechanizmu różnicowego" }
                    }
                },
            };

            db.FeatureCategories.AddRange(featureCategories);
            db.SaveChanges();
            logger.LogInformation("Seeded feature categories and features");
        }

        // Ensure feature categories exist for vehicle types that may have been skipped
        {
            var allVCats = db.VehicleCategories.ToList();
            int vanId   = allVCats.FirstOrDefault(c => c.Slug == "dostawcze")?.Id   ?? 0;
            int truckId = allVCats.FirstOrDefault(c => c.Slug == "ciezarowe")?.Id   ?? 0;
            int buildId = allVCats.FirstOrDefault(c => c.Slug == "budowlane")?.Id   ?? 0;

            if (vanId > 0 && !db.FeatureCategories.Any(fc => fc.VehicleCategoryId == vanId))
            {
                db.FeatureCategories.Add(new FeatureCategory
                {
                    Name = "Wyposażenie", VehicleCategoryId = vanId,
                    Features = new List<Feature> {
                        new Feature { Name = "Klimatyzacja" }, new Feature { Name = "Zabudowa chłodnicza" },
                        new Feature { Name = "Brygadówka" }, new Feature { Name = "Hak holowniczy" },
                        new Feature { Name = "GPS / Lokalizator" }, new Feature { Name = "Przegroda ładunkowa" },
                        new Feature { Name = "Regały ładunkowe" }, new Feature { Name = "Kamera cofania" },
                        new Feature { Name = "Podgrzewane fotele" }, new Feature { Name = "Ogrzewanie postojowe" }
                    }
                });
                db.SaveChanges();
                logger.LogInformation("Seeded feature categories for dostawcze");
            }

            if (truckId > 0 && !db.FeatureCategories.Any(fc => fc.VehicleCategoryId == truckId))
            {
                db.FeatureCategories.Add(new FeatureCategory
                {
                    Name = "Wyposażenie", VehicleCategoryId = truckId,
                    Features = new List<Feature> {
                        new Feature { Name = "Tachograf cyfrowy" }, new Feature { Name = "Retarder" },
                        new Feature { Name = "Lodówka / Chłodziarka" }, new Feature { Name = "Spojlery aerodynamiczne" },
                        new Feature { Name = "Dodatkowe zbiorniki paliwa" }, new Feature { Name = "Skrzynia chłodnicza" },
                        new Feature { Name = "Ogrzewanie postojowe" }, new Feature { Name = "Klimatyzacja kabiny" },
                        new Feature { Name = "GPS / System telematyczny" }, new Feature { Name = "Podnośnik kabiny" }
                    }
                });
                db.SaveChanges();
                logger.LogInformation("Seeded feature categories for ciezarowe");
            }

            if (buildId > 0 && !db.FeatureCategories.Any(fc => fc.VehicleCategoryId == buildId))
            {
                db.FeatureCategories.Add(new FeatureCategory
                {
                    Name = "Wyposażenie", VehicleCategoryId = buildId,
                    Features = new List<Feature> {
                        new Feature { Name = "Łyżka koparkowa" }, new Feature { Name = "Młot hydrauliczny" },
                        new Feature { Name = "Szybkozłącze" }, new Feature { Name = "Klimatyzacja kabiny" },
                        new Feature { Name = "Kamera cofania" }, new Feature { Name = "Hydraulika dodatkowa" },
                        new Feature { Name = "Łyżka podsiębierna" }, new Feature { Name = "System monitorowania obciążenia" },
                        new Feature { Name = "Centralny układ smarowania" }, new Feature { Name = "Zawieszenie kabiny" }
                    }
                });
                db.SaveChanges();
                logger.LogInformation("Seeded feature categories for budowlane");
            }
        }


        // Deduplicate brands if same slug was inserted multiple times
        try
        {
            var duplicateSlugs = db.Brands
                .GroupBy(b => b.Slug)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicateSlugs.Any())
            {
                logger.LogWarning("Found {Count} duplicate brand slugs — deduplicating", duplicateSlugs.Count);
                foreach (var slug in duplicateSlugs)
                {
                    var dupes = db.Brands.Where(b => b.Slug == slug).OrderBy(b => b.Id).ToList();
                    var keepId = dupes.First().Id;
                    var deleteIds = string.Join(",", dupes.Skip(1).Select(b => b.Id));
                    db.Database.ExecuteSqlRaw($"DELETE FROM `brandvehiclecategories` WHERE `BrandsId` IN ({deleteIds})");
                    db.Database.ExecuteSqlRaw($"UPDATE `caradverts` SET `BrandId` = {keepId} WHERE `BrandId` IN ({deleteIds})");
                    db.Database.ExecuteSqlRaw($"DELETE FROM `brands` WHERE `Id` IN ({deleteIds})");
                }
                logger.LogInformation("Brand deduplication complete");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("Brand deduplication skipped: {Message}", ex.Message);
        }

        if (!db.Brands.Any())
        {
            var catList = db.VehicleCategories.ToList();
            var carCat    = catList.FirstOrDefault(c => c.Slug == "auta-osobowe");
            var vanCat    = catList.FirstOrDefault(c => c.Slug == "dostawcze");
            var truckCat  = catList.FirstOrDefault(c => c.Slug == "ciezarowe");
            var motoCat   = catList.FirstOrDefault(c => c.Slug == "motocykle");
            var agriCat   = catList.FirstOrDefault(c => c.Slug == "rolnicze");

            var carVanTruck = new[] { carCat, vanCat, truckCat }.Where(c => c != null).Cast<VehicleCategory>().ToList();
            var carVan = new[] { carCat, vanCat }.Where(c => c != null).Cast<VehicleCategory>().ToList();
            var carOnly = new[] { carCat }.Where(c => c != null).Cast<VehicleCategory>().ToList();
            var motoOnly = new[] { motoCat }.Where(c => c != null).Cast<VehicleCategory>().ToList();
            var truckOnly = new[] { truckCat, vanCat }.Where(c => c != null).Cast<VehicleCategory>().ToList();
            var agriOnly = new[] { agriCat }.Where(c => c != null).Cast<VehicleCategory>().ToList();

            var brands = new List<Brand>
            {
                new Brand { Name = "Abarth",         Slug = "abarth",         Categories = carOnly },
                new Brand { Name = "Alfa Romeo",      Slug = "alfa-romeo",     Categories = carOnly },
                new Brand { Name = "Audi",            Slug = "audi",           Categories = carVan },
                new Brand { Name = "BMW",             Slug = "bmw",            Categories = new[] { carCat, vanCat, motoCat }.Where(c => c != null).Cast<VehicleCategory>().ToList() },
                new Brand { Name = "Chevrolet",       Slug = "chevrolet",      Categories = carOnly },
                new Brand { Name = "Chrysler",        Slug = "chrysler",       Categories = carOnly },
                new Brand { Name = "Citroën",         Slug = "citroen",        Categories = carVan },
                new Brand { Name = "Dacia",           Slug = "dacia",          Categories = carVan },
                new Brand { Name = "Dodge",           Slug = "dodge",          Categories = carOnly },
                new Brand { Name = "Ferrari",         Slug = "ferrari",        Categories = carOnly },
                new Brand { Name = "Fiat",            Slug = "fiat",           Categories = carVan },
                new Brand { Name = "Ford",            Slug = "ford",           Categories = carVan },
                new Brand { Name = "Genesis",         Slug = "genesis",        Categories = carOnly },
                new Brand { Name = "Honda",           Slug = "honda",          Categories = new[] { carCat, motoCat }.Where(c => c != null).Cast<VehicleCategory>().ToList() },
                new Brand { Name = "Hyundai",         Slug = "hyundai",        Categories = carVan },
                new Brand { Name = "Jaguar",          Slug = "jaguar",         Categories = carOnly },
                new Brand { Name = "Jeep",            Slug = "jeep",           Categories = carOnly },
                new Brand { Name = "Kia",             Slug = "kia",            Categories = carVan },
                new Brand { Name = "Lamborghini",     Slug = "lamborghini",    Categories = carOnly },
                new Brand { Name = "Land Rover",      Slug = "land-rover",     Categories = carOnly },
                new Brand { Name = "Lexus",           Slug = "lexus",          Categories = carOnly },
                new Brand { Name = "Maserati",        Slug = "maserati",       Categories = carOnly },
                new Brand { Name = "Mazda",           Slug = "mazda",          Categories = carVan },
                new Brand { Name = "Mercedes-Benz",   Slug = "mercedes-benz",  Categories = carVanTruck },
                new Brand { Name = "MG",              Slug = "mg",             Categories = carOnly },
                new Brand { Name = "Mini",            Slug = "mini",           Categories = carOnly },
                new Brand { Name = "Mitsubishi",      Slug = "mitsubishi",     Categories = carVan },
                new Brand { Name = "Nissan",          Slug = "nissan",         Categories = carVan },
                new Brand { Name = "Opel",            Slug = "opel",           Categories = carVan },
                new Brand { Name = "Peugeot",         Slug = "peugeot",        Categories = carVan },
                new Brand { Name = "Porsche",         Slug = "porsche",        Categories = carOnly },
                new Brand { Name = "Renault",         Slug = "renault",        Categories = carVan },
                new Brand { Name = "Seat",            Slug = "seat",           Categories = carVan },
                new Brand { Name = "Skoda",           Slug = "skoda",          Categories = carVan },
                new Brand { Name = "Subaru",          Slug = "subaru",         Categories = carOnly },
                new Brand { Name = "Suzuki",          Slug = "suzuki",         Categories = new[] { carCat, motoCat }.Where(c => c != null).Cast<VehicleCategory>().ToList() },
                new Brand { Name = "Tesla",           Slug = "tesla",          Categories = carVan },
                new Brand { Name = "Toyota",          Slug = "toyota",         Categories = carVan },
                new Brand { Name = "Volkswagen",      Slug = "volkswagen",     Categories = carVan },
                new Brand { Name = "Volvo",           Slug = "volvo",          Categories = carVanTruck },
                new Brand { Name = "BYD",             Slug = "byd",            Categories = carVan },
                new Brand { Name = "Aprilia",         Slug = "aprilia",        Categories = motoOnly },
                new Brand { Name = "Ducati",          Slug = "ducati",         Categories = motoOnly },
                new Brand { Name = "Harley-Davidson", Slug = "harley-davidson", Categories = motoOnly },
                new Brand { Name = "Kawasaki",        Slug = "kawasaki",       Categories = motoOnly },
                new Brand { Name = "KTM",             Slug = "ktm",            Categories = motoOnly },
                new Brand { Name = "MV Agusta",       Slug = "mv-agusta",      Categories = motoOnly },
                new Brand { Name = "Royal Enfield",   Slug = "royal-enfield",  Categories = motoOnly },
                new Brand { Name = "Triumph",         Slug = "triumph",        Categories = motoOnly },
                new Brand { Name = "Yamaha",          Slug = "yamaha",         Categories = motoOnly },
                new Brand { Name = "Indian",          Slug = "indian",         Categories = motoOnly },
                new Brand { Name = "Husqvarna",       Slug = "husqvarna",      Categories = motoOnly },
                new Brand { Name = "DAF",             Slug = "daf",            Categories = truckOnly },
                new Brand { Name = "Iveco",           Slug = "iveco",          Categories = truckOnly },
                new Brand { Name = "MAN",             Slug = "man",            Categories = truckOnly },
                new Brand { Name = "Scania",          Slug = "scania",         Categories = truckOnly },
                new Brand { Name = "Renault Trucks",  Slug = "renault-trucks", Categories = truckOnly },
                new Brand { Name = "Case IH",         Slug = "case-ih",        Categories = agriOnly },
                new Brand { Name = "Claas",           Slug = "claas",          Categories = agriOnly },
                new Brand { Name = "Fendt",           Slug = "fendt",          Categories = agriOnly },
                new Brand { Name = "John Deere",      Slug = "john-deere",     Categories = agriOnly },
                new Brand { Name = "Kubota",          Slug = "kubota",         Categories = agriOnly },
                new Brand { Name = "Massey Ferguson", Slug = "massey-ferguson", Categories = agriOnly },
                new Brand { Name = "New Holland",     Slug = "new-holland",    Categories = agriOnly },
                new Brand { Name = "Zetor",           Slug = "zetor",          Categories = agriOnly },
            };

            db.Brands.AddRange(brands);
            db.SaveChanges();
            logger.LogInformation("Seeded {Count} brands", brands.Count);
        } // end if (!db.Brands.Any())

        // Brands for budowlane and przyczepy (may be missing from initial seed)
        {
            var allVCats2 = db.VehicleCategories.ToList();
            var buildCat2  = allVCats2.FirstOrDefault(c => c.Slug == "budowlane");
            var trailerCat2 = allVCats2.FirstOrDefault(c => c.Slug == "przyczepy");
            var machineCat = allVCats2.FirstOrDefault(c => c.Slug == "maszyny");

            var existingBrandSlugs = db.Brands.Select(b => b.Slug).ToHashSet();

            var newBrands = new List<Brand>();
            if (buildCat2 != null)
            {
                var buildOnly = new List<VehicleCategory> { buildCat2 };
                var buildMachine = machineCat != null
                    ? new List<VehicleCategory> { buildCat2, machineCat }
                    : buildOnly;
                foreach (var (n, s, cats) in new (string, string, List<VehicleCategory>)[] {
                    ("Caterpillar", "caterpillar", buildMachine),
                    ("JCB", "jcb", buildMachine),
                    ("Komatsu", "komatsu", buildMachine),
                    ("Liebherr", "liebherr", buildMachine),
                    ("Bobcat", "bobcat", buildOnly),
                    ("Takeuchi", "takeuchi", buildOnly),
                    ("Wacker Neuson", "wacker-neuson", buildOnly),
                    ("Doosan", "doosan", buildMachine),
                    ("Hitachi Construction", "hitachi-construction", buildMachine),
                    ("Terex", "terex", buildMachine),
                })
                {
                    if (!existingBrandSlugs.Contains(s))
                        newBrands.Add(new Brand { Name = n, Slug = s, Categories = cats });
                }
            }

            if (trailerCat2 != null)
            {
                var trailerOnly = new List<VehicleCategory> { trailerCat2 };
                foreach (var (n, s) in new (string, string)[] {
                    ("Humbaur", "humbaur"),
                    ("Niewiadów", "niewiadow"),
                    ("Schmitz Cargobull", "schmitz-cargobull"),
                    ("Krone", "krone-trailer"),
                    ("Wielton", "wielton"),
                    ("Fliegl", "fliegl"),
                    ("Kogel", "kogel"),
                    ("Schwarzmüller", "schwarzmuller"),
                    ("Meiller", "meiller"),
                    ("Nooteboom", "nooteboom"),
                })
                {
                    if (!existingBrandSlugs.Contains(s))
                        newBrands.Add(new Brand { Name = n, Slug = s, Categories = trailerOnly });
                }
            }

            if (newBrands.Count > 0)
            {
                db.Brands.AddRange(newBrands);
                db.SaveChanges();
                logger.LogInformation("Seeded {Count} additional brands for budowlane/przyczepy/maszyny", newBrands.Count);
            }
        }

        // Expanded feature categories for dostawcze (additional categories)
        {
            var allVCats4 = db.VehicleCategories.ToList();
            int vanId4 = allVCats4.FirstOrDefault(c => c.Slug == "dostawcze")?.Id ?? 0;
            if (vanId4 > 0)
            {
                var existingVanCatNames = db.FeatureCategories
                    .Where(fc => fc.VehicleCategoryId == vanId4)
                    .Select(fc => fc.Name).ToHashSet();

                if (!existingVanCatNames.Contains("Bezpieczeństwo"))
                    db.FeatureCategories.Add(new FeatureCategory {
                        Name = "Bezpieczeństwo", VehicleCategoryId = vanId4,
                        Features = new List<Feature> {
                            new Feature { Name = "ABS" }, new Feature { Name = "ESP" },
                            new Feature { Name = "Poduszka powietrzna kierowcy" }, new Feature { Name = "Kamera cofania" },
                            new Feature { Name = "Czujniki parkowania" }, new Feature { Name = "Alarm" },
                            new Feature { Name = "Immobilizer" }, new Feature { Name = "Hamowanie awaryjne (AEB)" }
                        }
                    });

                if (!existingVanCatNames.Contains("Komfort kabiny"))
                    db.FeatureCategories.Add(new FeatureCategory {
                        Name = "Komfort kabiny", VehicleCategoryId = vanId4,
                        Features = new List<Feature> {
                            new Feature { Name = "Klimatyzacja" }, new Feature { Name = "Klimatyzacja automatyczna" },
                            new Feature { Name = "Podgrzewane fotele" }, new Feature { Name = "Elektrycznie regulowane lusterka" },
                            new Feature { Name = "Tempomat" }, new Feature { Name = "Ogrzewanie postojowe" },
                            new Feature { Name = "Radio / Bluetooth" }, new Feature { Name = "Nawigacja GPS" },
                            new Feature { Name = "Ekran dotykowy" }, new Feature { Name = "USB" }
                        }
                    });

                if (!existingVanCatNames.Contains("Zabudowa i ładunek"))
                    db.FeatureCategories.Add(new FeatureCategory {
                        Name = "Zabudowa i ładunek", VehicleCategoryId = vanId4,
                        Features = new List<Feature> {
                            new Feature { Name = "Przegroda ładunkowa" }, new Feature { Name = "Regały ładunkowe" },
                            new Feature { Name = "Zabudowa chłodnicza" }, new Feature { Name = "Zabudowa izoterma" },
                            new Feature { Name = "Platforma/skrzynia" }, new Feature { Name = "Winda załadowcza" },
                            new Feature { Name = "Hak holowniczy" }, new Feature { Name = "Drzwi boczne przesuwne" },
                            new Feature { Name = "Podłoga antypoślizgowa" }, new Feature { Name = "Mocowania cargo" }
                        }
                    });

                if (!existingVanCatNames.Contains("Flota i telematyka"))
                    db.FeatureCategories.Add(new FeatureCategory {
                        Name = "Flota i telematyka", VehicleCategoryId = vanId4,
                        Features = new List<Feature> {
                            new Feature { Name = "GPS / Lokalizator" }, new Feature { Name = "System telematyczny" },
                            new Feature { Name = "Tachograf cyfrowy" }, new Feature { Name = "Kamera rejestrująca" },
                            new Feature { Name = "Automatyczne raportowanie trasy" }
                        }
                    });

                db.SaveChanges();
            }
        }

        // Expanded feature categories for ciężarowe (additional categories)
        {
            var allVCats5 = db.VehicleCategories.ToList();
            int truckId5 = allVCats5.FirstOrDefault(c => c.Slug == "ciezarowe")?.Id ?? 0;
            if (truckId5 > 0)
            {
                var existingTruckCatNames = db.FeatureCategories
                    .Where(fc => fc.VehicleCategoryId == truckId5)
                    .Select(fc => fc.Name).ToHashSet();

                if (!existingTruckCatNames.Contains("Kabina i komfort"))
                    db.FeatureCategories.Add(new FeatureCategory {
                        Name = "Kabina i komfort", VehicleCategoryId = truckId5,
                        Features = new List<Feature> {
                            new Feature { Name = "Klimatyzacja kabiny" }, new Feature { Name = "Leżanka / łóżko w kabinie" },
                            new Feature { Name = "Lodówka / Chłodziarka" }, new Feature { Name = "Podgrzewane fotele" },
                            new Feature { Name = "Ogrzewanie postojowe" }, new Feature { Name = "Fotel z zawieszeniem pneumatycznym" },
                            new Feature { Name = "Radio / Bluetooth" }, new Feature { Name = "Nawigacja GPS" }
                        }
                    });

                if (!existingTruckCatNames.Contains("Bezpieczeństwo i systemy"))
                    db.FeatureCategories.Add(new FeatureCategory {
                        Name = "Bezpieczeństwo i systemy", VehicleCategoryId = truckId5,
                        Features = new List<Feature> {
                            new Feature { Name = "ABS" }, new Feature { Name = "ESP" },
                            new Feature { Name = "Tachograf cyfrowy" }, new Feature { Name = "Retarder" },
                            new Feature { Name = "Kamera cofania" }, new Feature { Name = "Hamowanie awaryjne (AEB)" },
                            new Feature { Name = "Asystent pasa ruchu (LKA)" }, new Feature { Name = "Asystent parkowania" },
                            new Feature { Name = "System telematyczny" }, new Feature { Name = "ADR (transport niebezp.)" }
                        }
                    });

                if (!existingTruckCatNames.Contains("Silnik i napęd"))
                    db.FeatureCategories.Add(new FeatureCategory {
                        Name = "Silnik i napęd", VehicleCategoryId = truckId5,
                        Features = new List<Feature> {
                            new Feature { Name = "Norma emisji EURO 6" }, new Feature { Name = "Norma emisji EURO 5" },
                            new Feature { Name = "Pomocniczy układ hamulcowy" }, new Feature { Name = "Blokada dyferencjału" },
                            new Feature { Name = "Skrzynia automatyczna" }, new Feature { Name = "PTO (odbiór mocy)" },
                            new Feature { Name = "Opony super-single" }, new Feature { Name = "Dodatkowe zbiorniki paliwa" }
                        }
                    });

                if (!existingTruckCatNames.Contains("Zabudowa i osprzęt"))
                    db.FeatureCategories.Add(new FeatureCategory {
                        Name = "Zabudowa i osprzęt", VehicleCategoryId = truckId5,
                        Features = new List<Feature> {
                            new Feature { Name = "Plandeka" }, new Feature { Name = "Skrzynia chłodnicza" },
                            new Feature { Name = "Wywrotka" }, new Feature { Name = "Platforma / flatbed" },
                            new Feature { Name = "Dźwig HDS" }, new Feature { Name = "Naczepa" },
                            new Feature { Name = "Spojlery aerodynamiczne" }, new Feature { Name = "Podnośnik kabiny" },
                            new Feature { Name = "Zbiornik AdBlue" }
                        }
                    });

                db.SaveChanges();
            }
        }

        // Expanded feature categories for rolnicze (additional categories)
        {
            var allVCats6 = db.VehicleCategories.ToList();
            int agriId6 = allVCats6.FirstOrDefault(c => c.Slug == "rolnicze")?.Id ?? 0;
            if (agriId6 > 0)
            {
                var existingAgriCatNames = db.FeatureCategories
                    .Where(fc => fc.VehicleCategoryId == agriId6)
                    .Select(fc => fc.Name).ToHashSet();

                if (!existingAgriCatNames.Contains("Układy hydrauliczne i WOM"))
                    db.FeatureCategories.Add(new FeatureCategory {
                        Name = "Układy hydrauliczne i WOM", VehicleCategoryId = agriId6,
                        Features = new List<Feature> {
                            new Feature { Name = "Tylny WOM" }, new Feature { Name = "Przedni WOM" },
                            new Feature { Name = "Hydraulika tylna" }, new Feature { Name = "Hydraulika przednia" },
                            new Feature { Name = "Hydraulika dodatkowa (wyjścia)" }, new Feature { Name = "Automatyczne zaczepienie TUZ" },
                            new Feature { Name = "Blokada mechanizmu różnicowego" }, new Feature { Name = "4WD" }
                        }
                    });

                if (!existingAgriCatNames.Contains("Bezpieczeństwo i ochrona"))
                    db.FeatureCategories.Add(new FeatureCategory {
                        Name = "Bezpieczeństwo i ochrona", VehicleCategoryId = agriId6,
                        Features = new List<Feature> {
                            new Feature { Name = "Belka ochronna ROPS" }, new Feature { Name = "Kabina bezpieczeństwa (FOPS)" },
                            new Feature { Name = "Światła robocze LED" }, new Feature { Name = "Sygnalizacja świetlna drogowa" },
                            new Feature { Name = "Hamulce hydrauliczne" }
                        }
                    });

                db.SaveChanges();
            }
        }

        // Expanded feature categories for motocykle (additional categories)
        {
            var allVCats7 = db.VehicleCategories.ToList();
            int motoId7 = allVCats7.FirstOrDefault(c => c.Slug == "motocykle")?.Id ?? 0;
            if (motoId7 > 0)
            {
                var existingMotoCatNames = db.FeatureCategories
                    .Where(fc => fc.VehicleCategoryId == motoId7)
                    .Select(fc => fc.Name).ToHashSet();

                if (!existingMotoCatNames.Contains("Elektronika i tryby jazdy"))
                    db.FeatureCategories.Add(new FeatureCategory {
                        Name = "Elektronika i tryby jazdy", VehicleCategoryId = motoId7,
                        Features = new List<Feature> {
                            new Feature { Name = "Tryby jazdy (Rain/Sport/Track)" }, new Feature { Name = "Kontrola wheelie" },
                            new Feature { Name = "Launch control" }, new Feature { Name = "Cornering ABS" },
                            new Feature { Name = "Cornering TCS" }, new Feature { Name = "IMU (6-axis)" },
                            new Feature { Name = "Wyświetlacz TFT / kolorowy" }, new Feature { Name = "Bluetooth / łączność" }
                        }
                    });

                if (!existingMotoCatNames.Contains("Zawieszenie i hamulce"))
                    db.FeatureCategories.Add(new FeatureCategory {
                        Name = "Zawieszenie i hamulce", VehicleCategoryId = motoId7,
                        Features = new List<Feature> {
                            new Feature { Name = "Zawieszenie regulowane" }, new Feature { Name = "Zawieszenie elektroniczne (EESA)" },
                            new Feature { Name = "Monoszok tylny" }, new Feature { Name = "Hamulce Brembo" },
                            new Feature { Name = "Tarcze pływające" }, new Feature { Name = "Radialne zaciski hamulcowe" }
                        }
                    });

                db.SaveChanges();
            }
        }

        // Expanded feature categories for przyczepy (additional categories)
        {
            var allVCats8 = db.VehicleCategories.ToList();
            int trailerIdX = allVCats8.FirstOrDefault(c => c.Slug == "przyczepy")?.Id ?? 0;
            if (trailerIdX > 0)
            {
                var existingTrailerCatNames = db.FeatureCategories
                    .Where(fc => fc.VehicleCategoryId == trailerIdX)
                    .Select(fc => fc.Name).ToHashSet();

                if (!existingTrailerCatNames.Contains("Wyposażenie techniczne"))
                    db.FeatureCategories.Add(new FeatureCategory {
                        Name = "Wyposażenie techniczne", VehicleCategoryId = trailerIdX,
                        Features = new List<Feature> {
                            new Feature { Name = "Hamulec najazdowy" }, new Feature { Name = "Koło podporowe" },
                            new Feature { Name = "Podpory tylne" }, new Feature { Name = "Burtownica aluminiowa" },
                            new Feature { Name = "Plandeka" }, new Feature { Name = "Rampa załadowcza" },
                            new Feature { Name = "Oświetlenie LED" }, new Feature { Name = "Blokada kuli" }
                        }
                    });

                if (!existingTrailerCatNames.Contains("Typ zabudowy"))
                    db.FeatureCategories.Add(new FeatureCategory {
                        Name = "Typ zabudowy", VehicleCategoryId = trailerIdX,
                        Features = new List<Feature> {
                            new Feature { Name = "Skrzynia otwarta" }, new Feature { Name = "Plandeka boczna" },
                            new Feature { Name = "Zabudowa chłodnicza" }, new Feature { Name = "Zabudowa izoterma" },
                            new Feature { Name = "Platforma niskopodłogowa" }, new Feature { Name = "Wywrotka" },
                            new Feature { Name = "Silos" }, new Feature { Name = "Cysterna" }
                        }
                    });

                if (!existingTrailerCatNames.Contains("Osie i podwozie"))
                    db.FeatureCategories.Add(new FeatureCategory {
                        Name = "Osie i podwozie", VehicleCategoryId = trailerIdX,
                        Features = new List<Feature> {
                            new Feature { Name = "Oś podnoszona" }, new Feature { Name = "Zawieszenie pneumatyczne" },
                            new Feature { Name = "Zawieszenie mechaniczne" }, new Feature { Name = "EBS (elektroniczny układ hamulcowy)" },
                            new Feature { Name = "Poszerzenie burtnic" }, new Feature { Name = "Koła aluminiowe" }
                        }
                    });

                db.SaveChanges();
            }
        }

        // Feature categories for maszyny, czesci, inne (equipment step hidden or minimal)
        {
            var allVCats3 = db.VehicleCategories.ToList();
            var maszId = allVCats3.FirstOrDefault(c => c.Slug == "maszyny")?.Id ?? 0;
            var czesciId = allVCats3.FirstOrDefault(c => c.Slug == "czesci")?.Id ?? 0;

            if (maszId > 0 && !db.FeatureCategories.Any(fc => fc.VehicleCategoryId == maszId))
            {
                db.FeatureCategories.Add(new FeatureCategory
                {
                    Name = "Wyposażenie", VehicleCategoryId = maszId,
                    Features = new List<Feature> {
                        new Feature { Name = "Klimatyzacja kabiny" }, new Feature { Name = "Ogrzewanie kabiny" },
                        new Feature { Name = "Kamera cofania" }, new Feature { Name = "Centralny układ smarowania" },
                        new Feature { Name = "System monitorowania" }, new Feature { Name = "Hydraulika dodatkowa" },
                        new Feature { Name = "Szybkozłącze" }, new Feature { Name = "GPS" },
                        new Feature { Name = "Zawieszenie kabiny" }, new Feature { Name = "Radio / Bluetooth" }
                    }
                });
                db.SaveChanges();
                logger.LogInformation("Seeded feature categories for maszyny");
            }

            if (czesciId > 0 && !db.FeatureCategories.Any(fc => fc.VehicleCategoryId == czesciId))
            {
                db.FeatureCategories.Add(new FeatureCategory
                {
                    Name = "Stan i typ", VehicleCategoryId = czesciId,
                    Features = new List<Feature> {
                        new Feature { Name = "Nowa" }, new Feature { Name = "Używana" },
                        new Feature { Name = "Regenerowana" }, new Feature { Name = "Oryginał (OEM)" },
                        new Feature { Name = "Zamiennik" }, new Feature { Name = "Certyfikowana" },
                        new Feature { Name = "Gwarancja" }, new Feature { Name = "Z faktury" }
                    }
                });
                db.SaveChanges();
                logger.LogInformation("Seeded feature categories for czesci");
            }
        }

        // VehicleSubtype seeds
        if (!db.VehicleSubtypes.Any())
        {
            var allVCatsForSubtypes = db.VehicleCategories.ToList();

            int CatId(string slug) => allVCatsForSubtypes.FirstOrDefault(c => c.Slug == slug)?.Id ?? 0;

            var subtypeDefs = new List<(string catSlug, string name, string slug)>
            {
                // auta-osobowe
                ("auta-osobowe", "Sedan",      "sedan"),
                ("auta-osobowe", "Kombi",      "kombi"),
                ("auta-osobowe", "Hatchback",  "hatchback"),
                ("auta-osobowe", "SUV",        "suv"),
                ("auta-osobowe", "Coupe",      "coupe"),
                ("auta-osobowe", "Kabriolet",  "kabriolet"),
                ("auta-osobowe", "Minivan",    "minivan"),
                ("auta-osobowe", "Pickup",     "pickup"),

                // dostawcze
                ("dostawcze", "Furgon",     "furgon"),
                ("dostawcze", "Brygadówka", "brygadowka"),
                ("dostawcze", "Chłodnia",   "chlodnia"),
                ("dostawcze", "Izoterma",   "izoterma"),
                ("dostawcze", "Platforma",  "platforma"),
                ("dostawcze", "Kontener",   "kontener"),

                // ciezarowe
                ("ciezarowe", "Ciągnik siodłowy", "ciagnik-siodlowy"),
                ("ciezarowe", "Wywrotka",         "wywrotka"),
                ("ciezarowe", "Chłodnia",         "chlodnia-ciezarowa"),
                ("ciezarowe", "Firanka",          "firanka"),
                ("ciezarowe", "Platforma",        "platforma-ciezarowa"),
                ("ciezarowe", "Kontener",         "kontener-ciezarowy"),
                ("ciezarowe", "Beczka/Cysterna",  "cysterna"),
                ("ciezarowe", "Hakowiec",         "hakowiec"),
                ("ciezarowe", "Śmieciarka",       "smieciarka"),

                // przyczepy
                ("przyczepy", "Naczepa firanka",      "naczepa-firanka"),
                ("przyczepy", "Naczepa chłodnia",     "naczepa-chlodnia"),
                ("przyczepy", "Naczepa platforma",    "naczepa-platforma"),
                ("przyczepy", "Laweta",               "laweta"),
                ("przyczepy", "Przyczepa towarowa",   "przyczepa-towarowa"),
                ("przyczepy", "Przyczepa rolnicza",   "przyczepa-rolnicza"),
                ("przyczepy", "Przyczepa kempingowa", "przyczepa-kempingowa"),

                // rolnicze
                ("rolnicze", "Ciągnik",           "ciagnik"),
                ("rolnicze", "Kombajn",           "kombajn"),
                ("rolnicze", "Opryskiwacz",       "opryskiwacz"),
                ("rolnicze", "Pług",              "plug"),
                ("rolnicze", "Glebogryzarka",     "glebogryzarka"),
                ("rolnicze", "Prasa",             "prasa"),
                ("rolnicze", "Siewnik",           "siewnik"),
                ("rolnicze", "Ładowarka rolnicza","ladowarka-rolnicza"),

                // budowlane
                ("budowlane", "Koparka",      "koparka"),
                ("budowlane", "Minikopiarka", "minikopiarka"),
                ("budowlane", "Ładowarka",    "ladowarka"),
                ("budowlane", "Spycharka",    "spycharka"),
                ("budowlane", "Walec",        "walec"),
                ("budowlane", "Żuraw",        "zuraw"),
                ("budowlane", "Rusztowanie",  "rusztowanie"),
                ("budowlane", "Wibrator",     "wibrator"),

                // maszyny
                ("maszyny", "Agregat prądotwórczy", "agregat"),
                ("maszyny", "Kompresor",            "kompresor"),
                ("maszyny", "Wózek widłowy",        "wozek-widlowy"),
                ("maszyny", "Podnośnik",            "podnośnik"),
                ("maszyny", "Myjnia",               "myjnia"),

                // motocykle
                ("motocykle", "Motocykl sportowy", "sport"),
                ("motocykle", "Naked",             "naked"),
                ("motocykle", "Turystyczny",       "turystyczny"),
                ("motocykle", "Enduro/Cross",      "enduro"),
                ("motocykle", "Skuter",            "skuter"),
                ("motocykle", "Chopper",           "chopper"),
                ("motocykle", "Quad",              "quad"),
            };

            var subtypes = new List<VehicleSubtype>();
            int order = 1;
            string lastCat = "";
            foreach (var (catSlug, name, slug) in subtypeDefs)
            {
                int catId = CatId(catSlug);
                if (catId == 0) continue;
                if (catSlug != lastCat) { order = 1; lastCat = catSlug; }
                subtypes.Add(new VehicleSubtype { VehicleCategoryId = catId, Name = name, Slug = slug, SortOrder = order++ });
            }

            if (subtypes.Count > 0)
            {
                db.VehicleSubtypes.AddRange(subtypes);
                db.SaveChanges();
                logger.LogInformation("Seeded {Count} vehicle subtypes", subtypes.Count);
            }
        }

        // PartCategory + PartSubcategory seeds
        if (!db.PartCategories.Any())
        {
            var partCategoriesData = new List<(string Name, int SortOrder, string[] Subcategories)>
            {
                ("Silnik i napęd", 1, new[] { "Blok silnika", "Głowica", "Tłoki i pierścienie", "Wały korbowe", "Rozrząd", "Turbosprężarka", "Intercooler" }),
                ("Układ chłodzenia", 2, new[] { "Chłodnica", "Pompa wody", "Termostat", "Wentylator", "Zbiornik wyrównawczy", "Korek chłodnicy" }),
                ("Układ paliwowy", 3, new[] { "Pompa paliwa", "Wtryskiwacze", "Filtr paliwa", "Zbiornik paliwa", "Przepustnica", "Kolektor ssący" }),
                ("Układ wydechowy", 4, new[] { "Katalizator", "Filtr DPF/FAP", "Tłumik", "Rura wydechowa", "Czujnik lambda", "EGR" }),
                ("Układ hamulcowy", 5, new[] { "Tarcze hamulcowe", "Klocki hamulcowe", "Zaciski", "Pompa hamulcowa", "Przewody hamulcowe", "ABS" }),
                ("Układ kierowniczy", 6, new[] { "Maglownica", "Pompa wspomagania", "Drążki kierownicze", "Kolumna kierownicy", "Końcówki drążków" }),
                ("Zawieszenie", 7, new[] { "Amortyzatory", "Sprężyny", "Wahacze", "Łączniki stabilizatora", "Tuleje", "Łożyska kół" }),
                ("Skrzynia biegów", 8, new[] { "Skrzynia manualna", "Skrzynia automatyczna", "Sprzęgło", "Koło dwumasowe", "Wałek napędowy" }),
                ("Karoseria i nadwozie", 9, new[] { "Drzwi", "Maski", "Błotniki", "Zderzaki", "Szyby", "Lusterka", "Progi" }),
                ("Oświetlenie", 10, new[] { "Reflektory", "Lampy tylne", "Kierunkowskazy", "Żarówki", "Moduły LED", "Przetwory ksenonowe" }),
                ("Elektryka i elektronika", 11, new[] { "Alternator", "Rozrusznik", "Akumulator", "Sterowniki ECU", "Czujniki", "Wiązki elektryczne" }),
                ("Wnętrze", 12, new[] { "Fotele", "Tapicerka", "Deski rozdzielcze", "Dywaniki", "Kierownica", "Pasy bezpieczeństwa" }),
                ("Klimatyzacja", 13, new[] { "Sprężarka", "Skraplacz", "Parownik", "Filtr kabinowy", "Wentylator", "Zawór rozprężny" }),
                ("Koła i opony", 14, new[] { "Opony letnie", "Opony zimowe", "Felgi stalowe", "Felgi aluminiowe", "Śruby i nakrętki", "Czujniki TPMS" }),
                ("Akcesoria i tuning", 15, new[] { "Spoilery", "Dysze wydechowe", "Folie i oklejanie", "Systemy audio", "Haki holownicze", "Bagażniki dachowe" }),
            };

            foreach (var (catName, sortOrder, subcats) in partCategoriesData)
            {
                var cat = new PartCategory
                {
                    Name = catName,
                    SortOrder = sortOrder,
                    Subcategories = subcats.Select((s, idx) => new PartSubcategory
                    {
                        Name = s,
                        SortOrder = idx + 1
                    }).ToList()
                };
                db.PartCategories.Add(cat);
            }
            db.SaveChanges();
            logger.LogInformation("Seeded {Count} part categories with subcategories", partCategoriesData.Count);
        }

        ModelSeeder.SeedModelsGenerationsEngines(db, logger);
        VehicleDataSeeder.SeedVehicleData(db, logger);
        TrimSeeder.SeedTrims(db, logger);
    }
}
