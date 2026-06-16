using System.Text;
using System.Text.Json.Serialization;
using cars_website_api.CarsWebsite.Interfaces;
using cars_website_api.CarsWebsite.Services;
using cars_website_api.CarsWebsite.Domain.Entities;
using CarsWebsite;
using CloudinaryDotNet;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using DriveType = cars_website_api.CarsWebsite.Domain.Entities.DriveType;


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

        var cloudinaryAccount = new Account(
            Environment.GetEnvironmentVariable("CLOUDINARY_CLOUD_NAME") ?? "",
            Environment.GetEnvironmentVariable("CLOUDINARY_API_KEY") ?? "",
            Environment.GetEnvironmentVariable("CLOUDINARY_API_SECRET") ?? ""
        );
        var cloudinary = new Cloudinary(cloudinaryAccount);
        cloudinary.Api.Secure = true;
        builder.Services.AddSingleton(cloudinary);
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
        builder.Services.AddHostedService<BadgeExpiryJob>();

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

            // Add IsMain to advertimages — column was added to entity after the original migration.
            try { db.Database.ExecuteSqlRaw("ALTER TABLE `advertimages` ADD COLUMN `IsMain` tinyint(1) NOT NULL DEFAULT 0"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN advertimages.IsMain skipped: {Message}", ex.Message); }

            // Add VehicleCategoryId to featurecategories for category-specific equipment grouping
            try { db.Database.ExecuteSqlRaw("ALTER TABLE `featurecategories` ADD COLUMN `VehicleCategoryId` int NULL"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN featurecategories.VehicleCategoryId skipped: {Message}", ex.Message); }

            // Add CategoryId to features if it was added after the DB was exported.
            try { db.Database.ExecuteSqlRaw("ALTER TABLE `features` ADD COLUMN `CategoryId` int NOT NULL DEFAULT 0"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN features.CategoryId skipped: {Message}", ex.Message); }

            // Add columns to `users` that were added to the User entity after the original schema.
            // Missing columns cause `FindAsync` to fail with "Unknown column", making /api/User/me return 500.
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
                "ALTER TABLE `users` ADD COLUMN `EmailVerified` tinyint(1) NOT NULL DEFAULT 0",
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

            // Make `adverts.City` and `adverts.Region` nullable — migration had them NOT NULL
            // but the entity has them as nullable, so INSERT fails when no city/region is provided.
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

            // Fix `reviews` table: the initial CREATE TABLE used wrong column names
            // (ReviewerId/ReviewedUserId); the entity uses SellerId/BuyerId/AdvertId.
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

            SeedDataIfEmpty(db, logger);
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

        // Fuel Types
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

        // Gearboxes
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

        // Body Types
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

        // Drive Types
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

        // Colors
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

        // Feature Categories + Features (per vehicle category)
        if (!db.FeatureCategories.Any())
        {
            var catList = db.VehicleCategories.ToList();

            int carCatId     = catList.FirstOrDefault(c => c.Slug == "auta-osobowe")?.Id ?? 0;
            int motoCatId    = catList.FirstOrDefault(c => c.Slug == "motocykle")?.Id ?? 0;
            int trailerCatId = catList.FirstOrDefault(c => c.Slug == "przyczepy")?.Id ?? 0;
            int agriCatId    = catList.FirstOrDefault(c => c.Slug == "rolnicze")?.Id ?? 0;

            var featureCategories = new List<FeatureCategory>
            {
                // ── CARS & VANS (VehicleCategoryId = carCatId) ──
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
                // ── MOTORCYCLES ──
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
                        new Feature { Name = "Podnóżki pasażera" }
                    }
                },
                // ── TRAILERS ──
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
                // ── AGRICULTURAL ──
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

        // Brands
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

            var carMoto = new[] { carCat, motoCat }.Where(c => c != null).Cast<VehicleCategory>().ToList();
            var carVanMoto = new[] { carCat, vanCat, motoCat }.Where(c => c != null).Cast<VehicleCategory>().ToList();

            var brands = new List<Brand>
            {
                // Cars & vans
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
                // Motorcycles only
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
                // Trucks
                new Brand { Name = "DAF",             Slug = "daf",            Categories = truckOnly },
                new Brand { Name = "Iveco",           Slug = "iveco",          Categories = truckOnly },
                new Brand { Name = "MAN",             Slug = "man",            Categories = truckOnly },
                new Brand { Name = "Scania",          Slug = "scania",         Categories = truckOnly },
                new Brand { Name = "Renault Trucks",  Slug = "renault-trucks", Categories = truckOnly },
                // Agricultural
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
        }
    }
}
