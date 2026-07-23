using MailKit.Net.Smtp;
using MailKit.Security;
using System.Text;
using System.Text.Json.Serialization;
using cars_website_api.CarsWebsite.Data;
using cars_website_api.CarsWebsite.Interfaces;
using cars_website_api.CarsWebsite.Services;
using cars_website_api.CarsWebsite.Domain.Entities;
using CarsWebsite;
using CloudinaryDotNet;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.MySql;
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

        // Prevent oversized requests (JSON API: 8 MB; multipart/form uploads handled separately)
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Limits.MaxRequestBodySize = 8 * 1024 * 1024; // 8 MB
        });
        builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(opts =>
        {
            opts.MultipartBodyLengthLimit = 26 * 1024 * 1024; // 26 MB for image/PDF uploads
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

        // Explicit MySqlConnector-level pool bounds (defaults are 0/100) so pool exhaustion under
        // load is a deliberate, tunable ceiling rather than an implicit default nobody set.
        if (!connectionString.Contains("Pooling=", StringComparison.OrdinalIgnoreCase))
        {
            var minPoolSize = Environment.GetEnvironmentVariable("DB_MIN_POOL_SIZE") ?? "5";
            var maxPoolSize = Environment.GetEnvironmentVariable("DB_MAX_POOL_SIZE") ?? "100";
            connectionString += $"Pooling=true;MinimumPoolSize={minPoolSize};MaximumPoolSize={maxPoolSize};";
        }

        // Migrations use MySQL user variables (SET @guard_... / PREPARE) for idempotent DDL guards;
        // MySqlConnector treats @x in command text as an undefined parameter and throws
        // "Parameter '@guard_exists' must be defined" unless this flag is set. Without it every
        // migration since 20260621 silently no-ops (Database.Migrate is wrapped in a non-fatal catch).
        if (!connectionString.Contains("AllowUserVariables=", StringComparison.OrdinalIgnoreCase))
        {
            connectionString += "AllowUserVariables=true;";
        }

        // SMTP_ env vars (single underscore) take precedence over appsettings Smtp:* section
        // ASP.NET Core normally uses double-underscore (Smtp__Host), but we map explicitly
        // so operators can use the more natural SMTP_HOST convention.
        var smtpHost = Environment.GetEnvironmentVariable("SMTP_HOST");
        var smtpPort = Environment.GetEnvironmentVariable("SMTP_PORT");
        var smtpUser = Environment.GetEnvironmentVariable("SMTP_USER");
        var smtpPass = Environment.GetEnvironmentVariable("SMTP_PASS") ?? Environment.GetEnvironmentVariable("SMTP_PASSWORD");
        var smtpFrom = Environment.GetEnvironmentVariable("SMTP_FROM");
        if (!string.IsNullOrEmpty(smtpHost)) builder.Configuration["Smtp:Host"] = smtpHost;
        if (!string.IsNullOrEmpty(smtpPort)) builder.Configuration["Smtp:Port"] = smtpPort;
        if (!string.IsNullOrEmpty(smtpUser)) builder.Configuration["Smtp:User"] = smtpUser;
        if (!string.IsNullOrEmpty(smtpPass)) builder.Configuration["Smtp:Password"] = smtpPass;
        if (!string.IsNullOrEmpty(smtpFrom)) builder.Configuration["Smtp:From"] = smtpFrom;
        // Log SMTP config at startup (password masked) to detect misconfiguration early
        Console.WriteLine($"[SMTP] host={builder.Configuration["Smtp:Host"] ?? "(not set)"} port={builder.Configuration["Smtp:Port"] ?? "587"} user={builder.Configuration["Smtp:User"] ?? "(not set)"} from={builder.Configuration["Smtp:From"] ?? "(not set)"} pass={(string.IsNullOrEmpty(smtpPass) ? "(NOT SET)" : "***")}");


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
        var imojeApiKey      = Environment.GetEnvironmentVariable("IMOJE_API_KEY")          ?? builder.Configuration["Imoje:ApiKey"]        ?? builder.Configuration["Imoje:ServiceKey"] ?? "";
        var imojeWebhookSec  = Environment.GetEnvironmentVariable("IMOJE_WEBHOOK_SECRET")   ?? builder.Configuration["Imoje:WebhookSecret"] ?? "";
        var imojeServiceId   = Environment.GetEnvironmentVariable("IMOJE_SERVICE_ID")       ?? builder.Configuration["Imoje:ServiceId"]     ?? "";
        var missingImoje = new List<string>();
        if (string.IsNullOrWhiteSpace(imojeMerchantId))  missingImoje.Add("IMOJE_MERCHANT_ID / Imoje:MerchantId");
        if (string.IsNullOrWhiteSpace(imojeApiKey))      missingImoje.Add("IMOJE_API_KEY / Imoje:ApiKey");
        if (string.IsNullOrWhiteSpace(imojeWebhookSec))  missingImoje.Add("IMOJE_WEBHOOK_SECRET / Imoje:WebhookSecret");
        if (string.IsNullOrWhiteSpace(imojeServiceId))   missingImoje.Add("IMOJE_SERVICE_ID / Imoje:ServiceId");
        if (missingImoje.Count > 0)
            Console.WriteLine($"[WARNING] Imoje payment credentials not fully configured (missing: {string.Join(", ", missingImoje)}). Payments will fail at runtime.");

        builder.Services.AddControllers()
            .AddJsonOptions(options => {
                options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            });
        // Standardizes every error response ASP.NET Core generates itself (the [ApiController]
        // attribute's automatic model-validation 400s, and the 500 written by UseExceptionHandler
        // below) onto the RFC 7807 application/problem+json shape. `message` is added alongside
        // the standard fields (not instead of them) because the frontend's proxy layer already
        // reads `.message` off every error body - dropping it would silently blank out every
        // error toast site-wide the moment this shipped.
        builder.Services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = ctx =>
            {
                ctx.ProblemDetails.Extensions["message"] = ctx.ProblemDetails.Detail ?? ctx.ProblemDetails.Title;
            };
        });
        builder.Services.AddEndpointsApiExplorer();
        // Pooled: AppDbContext has no per-request state beyond DbContextOptions (verified - its
        // only constructor takes DbContextOptions<AppDbContext>), so reusing instances across
        // requests is safe and avoids re-allocating/re-initializing a DbContext on every request.
        var dbContextPoolSize = int.TryParse(Environment.GetEnvironmentVariable("DB_CONTEXT_POOL_SIZE"), out var poolSize) ? poolSize : 128;
        builder.Services.AddDbContextPool<AppDbContext>(options => options
            .UseMySql(connectionString, new MySqlServerVersion(new Version(9, 4, 0)), mySqlOptions =>
                mySqlOptions.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null))
            .ConfigureWarnings(w =>
                w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)),
            poolSize: dbContextPoolSize);
        
        builder.Services.AddScoped<UserService>();
        builder.Services.AddScoped<IUserService, UserService>();
        builder.Services.AddScoped<AuthService>();
        builder.Services.AddScoped<IAuthService, AuthService>();
        builder.Services.AddScoped<IFollowService, FollowService>();
        builder.Services.AddScoped<IReviewService, ReviewService>();
        builder.Services.AddScoped<IAdvertService, AdvertService>();
        builder.Services.AddScoped<IAdvertImageService, AdvertImageService>();
        builder.Services.AddScoped<ITransactionService, TransactionService>();
        builder.Services.AddScoped<ISavedSearchService, SavedSearchService>();
        builder.Services.AddScoped<IPartnerService, PartnerService>();
        builder.Services.AddScoped<IPartnerImportService, PartnerImportService>();
        builder.Services.AddScoped<IPartnerSignupService, PartnerSignupService>();
        builder.Services.AddHttpClient<IPartnerFeedFetchService, PartnerFeedFetchService>()
            .ConfigurePrimaryHttpMessageHandler(() => SsrfGuard.CreateHandler());

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

        // Meilisearch (see docs/search-engine-evaluation.md): an accelerator for advert free-text
        // search, never a hard dependency - unset MEILISEARCH_HOST/Meilisearch:Host means the
        // registered client is null and IAdvertSearchIndexService.IsEnabled is false, so every
        // caller skips straight to the existing MySQL FULLTEXT path with no network attempt at all.
        var meilisearchHost = (Environment.GetEnvironmentVariable("MEILISEARCH_HOST") ?? builder.Configuration["Meilisearch:Host"] ?? "").Trim();
        var meilisearchApiKey = (Environment.GetEnvironmentVariable("MEILISEARCH_API_KEY") ?? builder.Configuration["Meilisearch:ApiKey"] ?? "").Trim();
        builder.Services.AddSingleton<Meilisearch.MeilisearchClient?>(_ =>
            string.IsNullOrEmpty(meilisearchHost) ? null : new Meilisearch.MeilisearchClient(meilisearchHost, meilisearchApiKey));
        builder.Services.AddScoped<IAdvertSearchIndexService, MeilisearchAdvertIndexService>();

        builder.Services.AddMemoryCache(); // B-27: taxonomy caching
        builder.Services.AddSingleton<ITaxonomyCacheVersion, TaxonomyCacheVersion>();
        builder.Services.AddScoped<ITaxonomyService, TaxonomyService>();
        builder.Services.AddScoped<IHierarchyValidationService, HierarchyValidationService>();
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
        builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();
        builder.Services.AddScoped<IPaymentService, PaymentService>();
        builder.Services.AddScoped<IInvoiceService, InvoiceService>();
        builder.Services.AddHttpClient<IKSeFService, KSeFService>();
        builder.Services.AddHttpClient<ICepikService, CepikService>();
        builder.Services.AddScoped<IFinancingService, FinancingService>();

        // Background jobs run on Hangfire's own recurring-job schedule (registered after
        // app.Build() below) instead of AddHostedService's polling-loop pattern - its MySQL-backed
        // queue gives them a persistent, resumable-after-restart schedule and a dashboard for
        // free, and guarantees only one server instance picks up a given scheduled occurrence
        // (what AdvisoryLock used to do by hand for each job individually).
        builder.Services.AddScoped<SubscriptionExpiryJob>();
        builder.Services.AddScoped<MonthlyInvoiceJob>();
        builder.Services.AddScoped<ExpiryReminderJob>();
        builder.Services.AddScoped<BadgeExpiryJob>();
        builder.Services.AddScoped<EventFeaturedExpiryJob>();
        builder.Services.AddScoped<DeletedUserPurgeJob>();
        builder.Services.AddScoped<SavedSearchAlertJob>();
        builder.Services.AddScoped<PartnerFeedSyncJob>();
        builder.Services.AddScoped<ITranslationProvider, HttpTranslationProvider>();
        builder.Services.AddScoped<DirectoryTranslationJob>();
        builder.Services.AddScoped<DirectoryGeocodeJob>();

        builder.Services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseStorage(new MySqlStorage(connectionString, new MySqlStorageOptions
            {
                TablesPrefix = "Hangfire_",
                PrepareSchemaIfNecessary = true,
                QueuePollInterval = TimeSpan.FromSeconds(15),
            })));
        builder.Services.AddHangfireServer();

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
            // AI endpoints that call paid external APIs — limit per user to cap cost.
            options.AddFixedWindowLimiter("ai", o =>
            {
                o.PermitLimit = 10;
                o.Window = TimeSpan.FromHours(1);
                o.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
                o.QueueLimit = 0;
            });
        });

        builder.Services.AddMemoryCache();
        builder.Services.AddResponseCompression(opts =>
        {
            opts.EnableForHttps = true;
            opts.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
            opts.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
        });
        builder.Services.AddResponseCaching();
        builder.Services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("db");
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

            // Email transport diagnostic: prints whether the app actually sees the Resend key
            // and the SMTP From, so we can tell config-load issues from code issues at a glance.
            var startCfg = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var resendKeyDiag = startCfg["Resend:ApiKey"]
                ?? Environment.GetEnvironmentVariable("RESEND_API_KEY")
                ?? "";
            startLogger.LogInformation(
                "[Email] transport={Transport} resendKeyLen={Len} resendKeyPrefix={Prefix} smtpFrom={From}",
                string.IsNullOrEmpty(resendKeyDiag) ? "SMTP (fallback)" : "Resend HTTP",
                resendKeyDiag.Length,
                resendKeyDiag.Length >= 3 ? resendKeyDiag[..3] : "(empty)",
                startCfg["Smtp:From"] ?? "(empty)");

            // Dump exact OS env var names containing "esend" (quoted, so trailing spaces show)
            // to expose a misnamed Railway variable that the config provider can't map.
            try
            {
                var esendVars = Environment.GetEnvironmentVariables()
                    .Cast<System.Collections.DictionaryEntry>()
                    .Where(e => (e.Key?.ToString() ?? "").ToLowerInvariant().Contains("esend"))
                    .Select(e => $"'{e.Key}'(valLen={((e.Value?.ToString()) ?? "").Length})")
                    .ToList();
                startLogger.LogInformation("[Email] env vars matching 'esend': {Vars}",
                    esendVars.Count > 0 ? string.Join(", ", esendVars) : "(none found)");
            }
            catch (Exception ex) { startLogger.LogDebug("[Email] esend env dump failed: {Msg}", ex.Message); }

            // Bootstrap EF Core migration history for databases that were created via
            // EnsureCreated before formal migrations were adopted. On a fresh DB,
            // EnsureCreated builds the schema; then MigrateAsync applies only the new
            // pending migrations (e.g. indexes). On an existing DB without migration
            // history we pre-mark historical migrations as applied so MigrateAsync
            // only runs the new ones.
            //
            // Wrapped in a MySQL advisory lock: with more than one instance starting
            // concurrently (rolling deploy), each running EnsureCreated/Migrate/the raw ALTER
            // TABLE guards below against the same schema would race. Only the instance that wins
            // the lock actually runs DDL; the rest wait up to 30s for it to finish before
            // proceeding to serve traffic, so nobody starts up assuming a schema change that's
            // still mid-flight on a sibling instance.
            AdvisoryLock.TryRunExclusive(db, "carizo:startup_migrate", () =>
            {
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
                                "INSERT IGNORE INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`) VALUES ({0}, '9.0.0')", m);
                        }
                        histLogger.LogInformation("[Migrations] Bootstrapped migration history with {Count} pre-existing migrations", allMigrations.Count - newMigrations.Count);
                    }

                    db.Database.Migrate();
                    histLogger.LogInformation("[Migrations] MigrateAsync completed");
                }
                catch (Exception ex)
                {
                    var histLogger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();
                    // Full exception (not just Message) - the inner MySqlException carries the actual
                    // failing statement/parameter and this swallowed line is the only diagnostic trace.
                    histLogger.LogError(ex, "[Migrations] Migration bootstrap failed (non-fatal)");
                }
            });

            // Explicit column guards — idempotent fallback for any migration that
            // may have been silently swallowed (try/catch above) or pre-marked via
            // the bootstrap path without actually running the DDL.
            var logger = scope.ServiceProvider
                .GetRequiredService<ILogger<AppDbContext>>();

            // Short command timeout for the guard/seed section below: these statements are
            // either trivial idempotent DDL (expected to fail fast with "already exists") or
            // small idempotent seed batches. If a stray lock (e.g. leftover from a prior crash)
            // blocks one of them, we want it to fail fast and get caught rather than hang the
            // whole startup for minutes via retry-on-failure backoff.
            db.Database.SetCommandTimeout(8);
            logger.LogWarning("[STARTUP-TRACE] Schema guards starting (commandTimeout=8s)");

            // Explicit column guards — idempotent fallback for migrations that may have been
            // silently swallowed or pre-marked without running the DDL.
            // IMPORTANT: Each statement is plain ADD COLUMN (no IF NOT EXISTS) in its own
            // try/catch. MySQL throws "Duplicate column name" when the column already exists —
            // that error is caught and logged at Debug level to avoid log spam.
            // Using IF NOT EXISTS requires MySQL 8.0.29+; plain ADD COLUMN works everywhere.

            foreach (var colDef in new[] {
                "`FuelConsumptionCity` decimal(5,2) NULL",
                "`FuelConsumptionHighway` decimal(5,2) NULL",
                "`FuelConsumptionCombined` decimal(5,2) NULL" })
            { try { db.Database.ExecuteSqlRaw($"ALTER TABLE `engineversions` ADD COLUMN {colDef}"); } catch (Exception ex) { logger.LogDebug("[Schema] engineversions.{Col}: {Msg}", colDef, ex.Message); } }

            foreach (var colDef in new[] {
                "`VehicleCategoryId` int NULL",
                "`BrandId` int NULL",
                "`ModelId` int NULL" })
            { try { db.Database.ExecuteSqlRaw($"ALTER TABLE `featurecategories` ADD COLUMN {colDef}"); } catch (Exception ex) { logger.LogDebug("[Schema] featurecategories.{Col}: {Msg}", colDef, ex.Message); } }

            foreach (var colDef in new[] {
                "`TrimId` int NULL",
                "`TorqueNm` int NULL",
                "`Co2EmissionGkm` int NULL",
                "`EuroNorm` varchar(20) NULL",
                "`AvgConsumptionL` decimal(4,1) NULL",
                "`Acceleration0100` decimal(4,1) NULL",
                "`TopSpeedKmh` int NULL",
                "`DriveType` varchar(10) NULL",
                "`GearboxType` varchar(20) NULL",
                "`Cylinders` int NULL",
                "`EngineCode` varchar(30) NULL" })
            { try { db.Database.ExecuteSqlRaw($"ALTER TABLE `engineversions` ADD COLUMN {colDef}"); } catch (Exception ex) { logger.LogDebug("[Schema] engineversions.{Col}: {Msg}", colDef, ex.Message); } }

            // Physical dimensions (audit §5/§6): belong on Generation, not CarAdvert, since every
            // advert of the same generation shares the same body dimensions.
            foreach (var colDef in new[] {
                "`LengthMm` int NULL",
                "`WidthMm` int NULL",
                "`HeightMm` int NULL",
                "`WheelbaseMm` int NULL",
                "`TrunkCapacityL` int NULL",
                "`DefaultSeatsCount` int NULL",
                "`DefaultDoorsCount` int NULL" })
            { try { db.Database.ExecuteSqlRaw($"ALTER TABLE `generations` ADD COLUMN {colDef}"); } catch (Exception ex) { logger.LogDebug("[Schema] generations.{Col}: {Msg}", colDef, ex.Message); } }

            foreach (var colDef in new[] {
                "`TrimId` int NULL",
                "`VehicleSubtypeId` int NULL",
                "`PartCategoryId` int NULL",
                "`PartSubcategoryId` int NULL",
                "`OemNumber` varchar(100) NULL",
                "`ManufacturerPartNumber` varchar(100) NULL",
                "`PartManufacturer` varchar(100) NULL",
                "`FeaturedUntil` datetime(6) NULL",
                "`OperatingWeightKg` int NULL",
                "`WorkingWidthCm` int NULL",
                "`MaxDiggingDepthM` decimal(5,2) NULL",
                "`BucketCapacityL` int NULL",
                "`TankCapacityL` int NULL",
                // Premium advert fields (migrations AddPremiumAdvertFields / AddPdfBrochureUrl).
                // Added here too because the migration history bootstrap can mark these as
                // already-applied, so MigrateAsync skips them and the columns never get created.
                "`RegistrationPlate` varchar(20) NULL",
                "`HasVatInvoice` tinyint(1) NOT NULL DEFAULT 0",
                "`IsLeasingPossible` tinyint(1) NOT NULL DEFAULT 0",
                "`IsCreditPossible` tinyint(1) NOT NULL DEFAULT 0",
                "`IsExchangePossible` tinyint(1) NOT NULL DEFAULT 0",
                "`GearCount` int NULL",
                "`MetallicPaint` tinyint(1) NOT NULL DEFAULT 0",
                "`ColorFinish` varchar(20) NULL",
                "`MaxTrailerWeight` int NULL",
                "`IsFirstOwner` tinyint(1) NOT NULL DEFAULT 0",
                "`IsServicedAtASO` tinyint(1) NOT NULL DEFAULT 0",
                "`IsGaraged` tinyint(1) NOT NULL DEFAULT 0",
                "`KeyCount` int NULL",
                "`InsuranceUntil` datetime(6) NULL",
                "`YoutubeUrl` varchar(500) NULL",
                "`PdfBrochureUrl` varchar(1000) NULL",
                // Parts catalog fields (migration AddPartCatalogFields) — same bootstrap risk.
                "`Side` varchar(20) NULL",
                "`Quantity` int NULL",
                // Faza 8 of the category/attribute restructure: homologation stays a real column
                // (applies to nearly every advert) - documents/videos go in the new AdvertDocuments
                // table below instead, replacing the single YoutubeUrl/PdfBrochureUrl columns above
                // (kept for one more release as deprecated/backward-compatible, per the plan).
                "`HasHomologation` tinyint(1) NOT NULL DEFAULT 0",
                "`HomologationType` varchar(100) NULL",
                // Partner API import tagging (migrations AddPartnerApi / AddPartnerSignupAndFeedSync).
                // The EF model selects these on every CarAdvert query, so their absence broke
                // search/list/favorites sitewide ("Unknown column 'c.ExternalId'") - same
                // bootstrap risk as the premium fields above.
                "`PartnerId` int NULL",
                "`ExternalId` varchar(200) NULL" })
            { try { db.Database.ExecuteSqlRaw($"ALTER TABLE `caradverts` ADD COLUMN {colDef}"); } catch (Exception ex) { logger.LogDebug("[Schema] caradverts.{Col}: {Msg}", colDef, ex.Message); } }

            // customcategoryrequests result columns (migration AddCustomCategoryRequestResults) — same bootstrap risk.
            foreach (var colDef in new[] {
                "`ResultingVehicleCategoryId` int NULL",
                "`ResultingVehicleSubtypeId` int NULL" })
            { try { db.Database.ExecuteSqlRaw($"ALTER TABLE `customcategoryrequests` ADD COLUMN {colDef}"); } catch (Exception ex) { logger.LogDebug("[Schema] customcategoryrequests.{Col}: {Msg}", colDef, ex.Message); } }

            // brands metadata columns (Faza 1 of the category/attribute restructure) — backs the
            // "Samochody amerykańskie/japońskie/chińskie" and "Samochody luksusowe" filters.
            foreach (var colDef in new[] {
                "`OriginCountry` varchar(50) NULL",
                "`IsLuxury` tinyint(1) NOT NULL DEFAULT 0" })
            { try { db.Database.ExecuteSqlRaw($"ALTER TABLE `brands` ADD COLUMN {colDef}"); } catch (Exception ex) { logger.LogDebug("[Schema] brands.{Col}: {Msg}", colDef, ex.Message); } }

            // Partner API + "Dla firm" signup + transactions/saved searches tables (migrations
            // AddPartnerApi / AddTransactionsAndSavedSearches / AddPartnerSignupAndFeedSync).
            // Same bootstrap risk as the tables below: these migrations sat behind a broken
            // pending chain, so production ran without them while the endpoints already existed
            // (POST /api/partner-signup 500'd with "Table 'railway.partnersignuprequests'
            // doesn't exist"). Definitions mirror the migration SQL exactly, except partners
            // includes the feed-sync columns up front (fresh create) with per-column ALTER
            // guards right after (table created earlier by the migration, without them).
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS `partners` (
                        `Id` int NOT NULL AUTO_INCREMENT,
                        `CompanyName` varchar(200) NOT NULL,
                        `ContactEmail` varchar(200) NOT NULL,
                        `ApiKeyHash` varchar(200) NOT NULL,
                        `LinkedUserId` int NOT NULL,
                        `IsActive` tinyint(1) NOT NULL DEFAULT 1,
                        `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                        `LastImportAt` datetime(6) NULL,
                        `FeedUrl` varchar(500) NULL,
                        `FeedFormat` int NULL,
                        `AutoSyncEnabled` tinyint(1) NOT NULL DEFAULT 1,
                        PRIMARY KEY (`Id`),
                        KEY `IX_partners_LinkedUserId` (`LinkedUserId`),
                        CONSTRAINT `FK_partners_users_LinkedUserId` FOREIGN KEY (`LinkedUserId`) REFERENCES `users` (`Id`) ON DELETE RESTRICT
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                ");
            }
            catch (Exception ex) { logger.LogWarning("[Schema] partners table: {Msg}", ex.Message); }

            foreach (var colDef in new[] {
                "`FeedUrl` varchar(500) NULL",
                "`FeedFormat` int NULL",
                "`AutoSyncEnabled` tinyint(1) NOT NULL DEFAULT 1" })
            { try { db.Database.ExecuteSqlRaw($"ALTER TABLE `partners` ADD COLUMN {colDef}"); } catch (Exception ex) { logger.LogDebug("[Schema] partners.{Col}: {Msg}", colDef, ex.Message); } }

            try
            {
                db.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS `partnerimportlogs` (
                        `Id` int NOT NULL AUTO_INCREMENT,
                        `PartnerId` int NOT NULL,
                        `Format` int NOT NULL,
                        `StartedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                        `CompletedAt` datetime(6) NULL,
                        `ItemsTotal` int NOT NULL DEFAULT 0,
                        `ItemsCreated` int NOT NULL DEFAULT 0,
                        `ItemsUpdated` int NOT NULL DEFAULT 0,
                        `ItemsFailed` int NOT NULL DEFAULT 0,
                        `ErrorSummary` text NULL,
                        PRIMARY KEY (`Id`),
                        KEY `IX_partnerimportlogs_PartnerId` (`PartnerId`),
                        CONSTRAINT `FK_partnerimportlogs_partners_PartnerId` FOREIGN KEY (`PartnerId`) REFERENCES `partners` (`Id`) ON DELETE CASCADE
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                ");
            }
            catch (Exception ex) { logger.LogWarning("[Schema] partnerimportlogs table: {Msg}", ex.Message); }

            try
            {
                db.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS `partnersignuprequests` (
                        `Id` int NOT NULL AUTO_INCREMENT,
                        `CompanyName` varchar(200) NOT NULL,
                        `Email` varchar(200) NOT NULL,
                        `Phone` varchar(30) NOT NULL,
                        `WebsiteUrl` varchar(300) NULL,
                        `FeedUrl` varchar(500) NULL,
                        `FeedFormat` int NULL,
                        `DetectedItemCount` int NULL,
                        `Status` int NOT NULL DEFAULT 0,
                        `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                        `ReviewedAt` datetime(6) NULL,
                        `ReviewedByAdminId` int NULL,
                        `RejectionReason` varchar(500) NULL,
                        `PartnerId` int NULL,
                        PRIMARY KEY (`Id`),
                        KEY `IX_partnersignuprequests_Status` (`Status`),
                        KEY `IX_partnersignuprequests_Email` (`Email`),
                        KEY `IX_partnersignuprequests_PartnerId` (`PartnerId`),
                        CONSTRAINT `FK_partnersignuprequests_partners_PartnerId` FOREIGN KEY (`PartnerId`) REFERENCES `partners` (`Id`) ON DELETE SET NULL
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                ");
            }
            catch (Exception ex) { logger.LogWarning("[Schema] partnersignuprequests table: {Msg}", ex.Message); }

            try
            {
                db.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS `transactions` (
                        `Id` int NOT NULL AUTO_INCREMENT,
                        `Type` int NOT NULL,
                        `Status` int NOT NULL,
                        `AdvertId` int NOT NULL,
                        `BuyerId` int NOT NULL,
                        `SellerId` int NOT NULL,
                        `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                        `ScheduledAt` datetime(6) NULL,
                        `CompletedAt` datetime(6) NULL,
                        `Notes` text NULL,
                        PRIMARY KEY (`Id`),
                        KEY `IX_transactions_AdvertId` (`AdvertId`),
                        KEY `IX_transactions_BuyerId` (`BuyerId`),
                        KEY `IX_transactions_SellerId` (`SellerId`),
                        CONSTRAINT `FK_transactions_caradverts_AdvertId` FOREIGN KEY (`AdvertId`) REFERENCES `caradverts` (`Id`) ON DELETE RESTRICT,
                        CONSTRAINT `FK_transactions_users_BuyerId` FOREIGN KEY (`BuyerId`) REFERENCES `users` (`Id`) ON DELETE RESTRICT,
                        CONSTRAINT `FK_transactions_users_SellerId` FOREIGN KEY (`SellerId`) REFERENCES `users` (`Id`) ON DELETE RESTRICT
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                ");
            }
            catch (Exception ex) { logger.LogWarning("[Schema] transactions table: {Msg}", ex.Message); }

            try
            {
                db.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS `savedsearches` (
                        `Id` int NOT NULL AUTO_INCREMENT,
                        `UserId` int NOT NULL,
                        `Name` varchar(200) NOT NULL,
                        `CriteriaJson` longtext NOT NULL,
                        `NotifyOnNew` tinyint(1) NOT NULL DEFAULT 1,
                        `NewResultsCount` int NOT NULL DEFAULT 0,
                        `LastCheckedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                        `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                        PRIMARY KEY (`Id`),
                        KEY `IX_savedsearches_UserId` (`UserId`),
                        CONSTRAINT `FK_savedsearches_users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE CASCADE
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                ");
            }
            catch (Exception ex) { logger.LogWarning("[Schema] savedsearches table: {Msg}", ex.Message); }

            // Business Directory (blueprint section 17) - public catalogue of automotive/transport
            // companies with a global Carizo ID. Same belt-and-braces guard as the tables above.
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS `directorycompanies` (
                        `Id` int NOT NULL AUTO_INCREMENT,
                        `PublicId` varchar(64) NOT NULL,
                        `Slug` varchar(220) NOT NULL,
                        `Name` varchar(200) NOT NULL,
                        `NameNormalized` varchar(200) NOT NULL,
                        `Category` varchar(60) NOT NULL,
                        `CountryCode` varchar(2) NULL,
                        `City` varchar(120) NULL,
                        `Address` varchar(250) NULL,
                        `PostalCode` varchar(20) NULL,
                        `Phone` varchar(40) NULL,
                        `Email` varchar(200) NULL,
                        `EmailType` varchar(20) NULL,
                        `Website` varchar(300) NULL,
                        `ProfileUrl` varchar(300) NULL,
                        `Language` varchar(5) NULL,
                        `Latitude` double NULL,
                        `Longitude` double NULL,
                        `Status` varchar(20) NOT NULL DEFAULT 'unverified',
                        `Source` varchar(60) NULL,
                        `PartnerId` int NULL,
                        `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                        `UpdatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                        PRIMARY KEY (`Id`),
                        UNIQUE KEY `IX_directorycompanies_PublicId` (`PublicId`),
                        UNIQUE KEY `IX_directorycompanies_Slug` (`Slug`),
                        KEY `IX_directorycompanies_Category_CountryCode` (`Category`, `CountryCode`),
                        KEY `IX_directorycompanies_NameNormalized` (`NameNormalized`),
                        KEY `IX_directorycompanies_Status` (`Status`),
                        KEY `IX_directorycompanies_PartnerId` (`PartnerId`),
                        CONSTRAINT `FK_directorycompanies_partners_PartnerId` FOREIGN KEY (`PartnerId`) REFERENCES `partners` (`Id`) ON DELETE SET NULL
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                ");
            }
            catch (Exception ex) { logger.LogWarning("[Schema] directorycompanies table: {Msg}", ex.Message); }

            // Directory i18n columns (multi-language foundation) - same bootstrap risk as above.
            foreach (var colDef in new[] {
                "`Description` varchar(2000) NULL",
                "`I18n` longtext NULL" })
            { try { db.Database.ExecuteSqlRaw($"ALTER TABLE `directorycompanies` ADD COLUMN {colDef}"); } catch (Exception ex) { logger.LogDebug("[Schema] directorycompanies.{Col}: {Msg}", colDef, ex.Message); } }

            // Vehicle-specific attribute scoping (Brand/Model/Generation/Trim) - the "inteligentny
            // formularz". Same belt-and-braces column guard for pre-existing DBs.
            foreach (var colDef in new[] {
                "`BrandId` int NULL",
                "`ModelId` int NULL",
                "`GenerationId` int NULL",
                "`TrimId` int NULL" })
            { try { db.Database.ExecuteSqlRaw($"ALTER TABLE `attributedefinitions` ADD COLUMN {colDef}"); } catch (Exception ex) { logger.LogDebug("[Schema] attributedefinitions.{Col}: {Msg}", colDef, ex.Message); } }

            // Global reference-data core (Faza 0 of going worldwide). Belt-and-braces CREATE TABLE
            // IF NOT EXISTS on pre-existing production DBs (EnsureCreated only builds these on a
            // genuinely fresh DB). Order matters: parent tables (continents/currencies/languages/
            // timezones) before countries, countries before regions, regions before cities.
            foreach (var sql in new[] {
                @"CREATE TABLE IF NOT EXISTS `continents` (
                    `Id` int NOT NULL AUTO_INCREMENT, `Code` varchar(2) NOT NULL, `Name` varchar(80) NOT NULL,
                    PRIMARY KEY (`Id`), UNIQUE KEY `IX_continents_Code` (`Code`)
                  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",
                @"CREATE TABLE IF NOT EXISTS `currencies` (
                    `Id` int NOT NULL AUTO_INCREMENT, `Iso` varchar(3) NOT NULL, `Symbol` varchar(8) NOT NULL,
                    `Name` varchar(60) NOT NULL, `Decimals` tinyint unsigned NOT NULL DEFAULT 2,
                    `SymbolPosition` varchar(4) NOT NULL DEFAULT 'pre', `IsActive` tinyint(1) NOT NULL DEFAULT 1,
                    PRIMARY KEY (`Id`), UNIQUE KEY `IX_currencies_Iso` (`Iso`)
                  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",
                @"CREATE TABLE IF NOT EXISTS `languages` (
                    `Id` int NOT NULL AUTO_INCREMENT, `Iso1` varchar(2) NOT NULL, `Endonym` varchar(60) NOT NULL,
                    `EnglishName` varchar(60) NOT NULL, `IsRtl` tinyint(1) NOT NULL DEFAULT 0,
                    `IsActive` tinyint(1) NOT NULL DEFAULT 1,
                    PRIMARY KEY (`Id`), UNIQUE KEY `IX_languages_Iso1` (`Iso1`)
                  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",
                @"CREATE TABLE IF NOT EXISTS `timezones` (
                    `Id` int NOT NULL AUTO_INCREMENT, `IanaName` varchar(60) NOT NULL,
                    `UtcOffsetMinutes` int NOT NULL DEFAULT 0, `DisplayName` varchar(80) NOT NULL DEFAULT '',
                    PRIMARY KEY (`Id`), UNIQUE KEY `IX_timezones_IanaName` (`IanaName`)
                  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",
                @"CREATE TABLE IF NOT EXISTS `countries` (
                    `Id` int NOT NULL AUTO_INCREMENT, `Iso2` varchar(2) NOT NULL, `Iso3` varchar(3) NOT NULL,
                    `Name` varchar(80) NOT NULL, `NativeName` varchar(80) NOT NULL DEFAULT '',
                    `ContinentId` int NULL, `DefaultCurrencyId` int NULL, `DefaultLanguageId` int NULL,
                    `DefaultTimeZoneId` int NULL, `PhonePrefix` varchar(8) NULL,
                    `MeasurementSystem` varchar(8) NOT NULL DEFAULT 'metric', `DrivingSide` varchar(1) NOT NULL DEFAULT 'R',
                    `PostalCodeRegex` varchar(24) NULL, `IsActive` tinyint(1) NOT NULL DEFAULT 1,
                    PRIMARY KEY (`Id`), UNIQUE KEY `IX_countries_Iso2` (`Iso2`),
                    KEY `IX_countries_ContinentId` (`ContinentId`)
                  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",
                @"CREATE TABLE IF NOT EXISTS `regions` (
                    `Id` int NOT NULL AUTO_INCREMENT, `CountryId` int NOT NULL, `Code` varchar(10) NULL,
                    `Name` varchar(120) NOT NULL, `Type` varchar(30) NOT NULL DEFAULT 'region',
                    PRIMARY KEY (`Id`), KEY `IX_regions_CountryId_Code` (`CountryId`, `Code`),
                    CONSTRAINT `FK_regions_countries` FOREIGN KEY (`CountryId`) REFERENCES `countries` (`Id`) ON DELETE CASCADE
                  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",
                @"CREATE TABLE IF NOT EXISTS `cities` (
                    `Id` bigint NOT NULL AUTO_INCREMENT, `CountryId` int NOT NULL, `RegionId` int NULL,
                    `Name` varchar(160) NOT NULL, `AsciiName` varchar(160) NOT NULL DEFAULT '',
                    `Latitude` double NULL, `Longitude` double NULL, `Population` int NOT NULL DEFAULT 0,
                    `GeonameId` bigint NULL,
                    PRIMARY KEY (`Id`), KEY `IX_cities_CountryId_RegionId` (`CountryId`, `RegionId`),
                    KEY `IX_cities_CountryId_AsciiName` (`CountryId`, `AsciiName`),
                    CONSTRAINT `FK_cities_countries` FOREIGN KEY (`CountryId`) REFERENCES `countries` (`Id`) ON DELETE CASCADE
                  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",
                @"CREATE TABLE IF NOT EXISTS `exchangerates` (
                    `Id` bigint NOT NULL AUTO_INCREMENT, `CurrencyId` int NOT NULL,
                    `RateToEur` decimal(18,8) NOT NULL, `AsOf` datetime(6) NOT NULL,
                    PRIMARY KEY (`Id`), UNIQUE KEY `IX_exchangerates_CurrencyId_AsOf` (`CurrencyId`, `AsOf`),
                    CONSTRAINT `FK_exchangerates_currencies` FOREIGN KEY (`CurrencyId`) REFERENCES `currencies` (`Id`) ON DELETE CASCADE
                  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;" })
            { try { db.Database.ExecuteSqlRaw(sql); } catch (Exception ex) { logger.LogWarning("[Schema] geo core table: {Msg}", ex.Message); } }

            // Global-location + currency columns on Adverts (Etap 3). Belt-and-braces on existing DBs.
            foreach (var colDef in new[] {
                "`CountryId` int NULL", "`RegionId` int NULL", "`CityId` bigint NULL",
                "`PostalCode` varchar(16) NULL", "`AddressLine` varchar(250) NULL",
                "`Latitude` double NULL", "`Longitude` double NULL",
                "`CurrencyId` int NULL", "`PriceEur` decimal(15,2) NULL", "`PriceEurAsOf` datetime(6) NULL",
                "`SourceLanguageId` int NULL", "`TimeZoneId` int NULL" })
            { try { db.Database.ExecuteSqlRaw($"ALTER TABLE `adverts` ADD COLUMN {colDef}"); } catch (Exception ex) { logger.LogDebug("[Schema] Adverts.{Col}: {Msg}", colDef, ex.Message); } }

            // FULLTEXT index backing AdvertService.SearchCarAdvertsAsync's MATCH...AGAINST text search.
            // Migration 20260622100000_AddPerformanceIndexesAndFullText only creates it via a raw
            // migrationBuilder.Sql() call - EnsureCreated() (used above to bootstrap a pre-existing DB's
            // schema) has no way to reflect that, and the migration-history bootstrap right after marks
            // this migration "applied" without ever running it. Net effect: on any DB that went through
            // that bootstrap path, the index never gets created, and every search with a text term 500s
            // with "Can't find FULLTEXT index matching the column list". Belt-and-braces every startup.
            try
            {
                var ftIndexExists = db.Database.SqlQuery<int>(
                    $"SELECT COUNT(1) FROM INFORMATION_SCHEMA.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'adverts' AND INDEX_NAME = 'FT_Adverts_TitleDescription'")
                    .ToList().FirstOrDefault();
                if (ftIndexExists == 0)
                    db.Database.ExecuteSqlRaw("CREATE FULLTEXT INDEX `FT_Adverts_TitleDescription` ON `adverts` (`Title`, `Description`)");
            }
            catch (Exception ex) { logger.LogWarning("[Schema] Adverts fulltext index: {Msg}", ex.Message); }

            // Company branches/locations, phones, opening hours, contact languages (Etap 4 of the
            // globalization roadmap). New tables only - no `dotnet ef migrations add` here on purpose:
            // this repo's model snapshot has drifted so far from the actual schema (years of raw-SQL
            // guards like this one, never reflected back into a migration) that scaffolding a real
            // migration for this change produced 266 DROP statements against unrelated tables. Belt-
            // and-braces CREATE TABLE IF NOT EXISTS, consistent with every other new-table addition
            // in this file, is the only safe way to add schema here until that drift is cleaned up.
            foreach (var sql in new[] {
                @"CREATE TABLE IF NOT EXISTS `companybranches` (
                    `Id` int NOT NULL AUTO_INCREMENT, `DirectoryCompanyId` int NOT NULL,
                    `Name` varchar(120) NULL, `IsPrimary` tinyint(1) NOT NULL DEFAULT 0,
                    `CountryId` int NULL, `RegionId` int NULL, `CityId` bigint NULL,
                    `PostalCode` varchar(20) NULL, `AddressLine` varchar(250) NULL,
                    `Latitude` double NULL, `Longitude` double NULL, `TimeZoneId` int NULL,
                    PRIMARY KEY (`Id`), KEY `IX_companybranches_DirectoryCompanyId` (`DirectoryCompanyId`),
                    CONSTRAINT `FK_companybranches_directorycompanies` FOREIGN KEY (`DirectoryCompanyId`) REFERENCES `directorycompanies` (`Id`) ON DELETE CASCADE,
                    CONSTRAINT `FK_companybranches_countries` FOREIGN KEY (`CountryId`) REFERENCES `countries` (`Id`) ON DELETE SET NULL,
                    CONSTRAINT `FK_companybranches_regions` FOREIGN KEY (`RegionId`) REFERENCES `regions` (`Id`) ON DELETE SET NULL,
                    CONSTRAINT `FK_companybranches_cities` FOREIGN KEY (`CityId`) REFERENCES `cities` (`Id`) ON DELETE SET NULL,
                    CONSTRAINT `FK_companybranches_timezones` FOREIGN KEY (`TimeZoneId`) REFERENCES `timezones` (`Id`) ON DELETE SET NULL
                  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",
                @"CREATE TABLE IF NOT EXISTS `companyphones` (
                    `Id` int NOT NULL AUTO_INCREMENT, `CompanyBranchId` int NOT NULL,
                    `Number` varchar(40) NOT NULL, `Label` varchar(40) NULL,
                    PRIMARY KEY (`Id`), KEY `IX_companyphones_CompanyBranchId` (`CompanyBranchId`),
                    CONSTRAINT `FK_companyphones_companybranches` FOREIGN KEY (`CompanyBranchId`) REFERENCES `companybranches` (`Id`) ON DELETE CASCADE
                  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",
                @"CREATE TABLE IF NOT EXISTS `companyopeninghours` (
                    `Id` int NOT NULL AUTO_INCREMENT, `CompanyBranchId` int NOT NULL,
                    `DayOfWeek` int NOT NULL, `IsClosed` tinyint(1) NOT NULL DEFAULT 0,
                    `OpenTime` time(6) NULL, `CloseTime` time(6) NULL,
                    PRIMARY KEY (`Id`), KEY `IX_companyopeninghours_CompanyBranchId` (`CompanyBranchId`),
                    CONSTRAINT `FK_companyopeninghours_companybranches` FOREIGN KEY (`CompanyBranchId`) REFERENCES `companybranches` (`Id`) ON DELETE CASCADE
                  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",
                @"CREATE TABLE IF NOT EXISTS `companylanguages` (
                    `Id` int NOT NULL AUTO_INCREMENT, `DirectoryCompanyId` int NOT NULL, `LanguageId` int NOT NULL,
                    PRIMARY KEY (`Id`), UNIQUE KEY `IX_companylanguages_DirectoryCompanyId_LanguageId` (`DirectoryCompanyId`, `LanguageId`),
                    CONSTRAINT `FK_companylanguages_directorycompanies` FOREIGN KEY (`DirectoryCompanyId`) REFERENCES `directorycompanies` (`Id`) ON DELETE CASCADE,
                    CONSTRAINT `FK_companylanguages_languages` FOREIGN KEY (`LanguageId`) REFERENCES `languages` (`Id`) ON DELETE CASCADE
                  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;" })
            { try { db.Database.ExecuteSqlRaw(sql); } catch (Exception ex) { logger.LogWarning("[Schema] company branch tables: {Msg}", ex.Message); } }

            // Remove the "Koła i opony" parts category on existing DBs - Opony/Felgi are now their own
            // top-level categories with dedicated forms, so they no longer belong under parts. FK on
            // caradverts.PartCategoryId/PartSubcategoryId is SetNull, so any advert that referenced these
            // simply loses the (now irrelevant) parts tag. Subcategories deleted first to be FK-agnostic.
            foreach (var sql in new[] {
                "DELETE FROM `partsubcategories` WHERE `PartCategoryId` IN (SELECT `Id` FROM `partcategories` WHERE `Name` = 'Koła i opony')",
                "DELETE FROM `partcategories` WHERE `Name` = 'Koła i opony'",
                // Felgi shipped with mdi-alloy-wheel, which does not exist in @mdi/font 7.4.47 -> empty
                // icon box. Repoint existing rows to an icon that renders (a rim-like double ring).
                "UPDATE `vehiclecategories` SET `IconName` = 'mdi-circle-double' WHERE `Slug` = 'felgi' AND `IconName` = 'mdi-alloy-wheel'" })
            { try { db.Database.ExecuteSqlRaw(sql); } catch (Exception ex) { logger.LogDebug("[Schema] parts/icon cleanup: {Msg}", ex.Message); } }

            // These 3 tables were first created (via the CREATE TABLE IF NOT EXISTS guards right
            // below) with PascalCase names, shadowing the lowercase name EF's generated queries
            // actually look for on this DB (same class of bug documented in the rename block
            // above) — every GET /api/Advert/{id} failed with "Table 'partcompatibilities'
            // doesn't exist" as a result. Rename first (preserves any rows already written) so
            // the CREATE TABLE IF NOT EXISTS calls below only fire on a genuinely fresh DB.
            foreach (var sql in new[] {
                "RENAME TABLE `PartCompatibilities` TO `partcompatibilities`",
                "RENAME TABLE `BrandAllowedFuelTypes` TO `brandallowedfueltypes`",
                "RENAME TABLE `ModelNamePlausibilityRules` TO `modelnameplausibilityrules`",
            })
            {
                try { db.Database.ExecuteSqlRaw(sql); }
                catch (Exception ex) { logger.LogDebug("RENAME TABLE skipped: {Message}", ex.Message); }
            }

            // New tables added by migrations that may have been marked applied without running
            // (same bootstrap risk documented above) — CREATE TABLE IF NOT EXISTS is safe to
            // re-run every startup regardless of migration history state.
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS `partcompatibilities` (
                        `Id` int NOT NULL AUTO_INCREMENT,
                        `CarAdvertId` int NOT NULL,
                        `BrandId` int NOT NULL,
                        `ModelId` int NULL,
                        `GenerationId` int NULL,
                        PRIMARY KEY (`Id`),
                        KEY `IX_PartCompatibilities_CarAdvertId` (`CarAdvertId`),
                        KEY `IX_PartCompatibilities_BrandId` (`BrandId`),
                        KEY `IX_PartCompatibilities_ModelId` (`ModelId`),
                        KEY `IX_PartCompatibilities_GenerationId` (`GenerationId`)
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                ");
            }
            catch (Exception ex) { logger.LogDebug("[Schema] partcompatibilities table: {Msg}", ex.Message); }

            try
            {
                db.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS `brandallowedfueltypes` (
                        `Id` int NOT NULL AUTO_INCREMENT,
                        `BrandId` int NOT NULL,
                        `FuelTypeId` int NOT NULL,
                        PRIMARY KEY (`Id`),
                        KEY `IX_BrandAllowedFuelTypes_BrandId` (`BrandId`),
                        KEY `IX_BrandAllowedFuelTypes_FuelTypeId` (`FuelTypeId`)
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                ");
            }
            catch (Exception ex) { logger.LogDebug("[Schema] brandallowedfueltypes table: {Msg}", ex.Message); }

            try
            {
                db.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS `modelnameplausibilityrules` (
                        `Id` int NOT NULL AUTO_INCREMENT,
                        `NamePattern` varchar(100) NOT NULL,
                        `MinPowerHP` int NOT NULL,
                        `Description` varchar(500) NULL,
                        PRIMARY KEY (`Id`)
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                ");
            }
            catch (Exception ex) { logger.LogDebug("[Schema] modelnameplausibilityrules table: {Msg}", ex.Message); }

            // FeatureCategories.VehicleCategoryId NOT NULL (migration MakeFeatureCategoryVehicleCategoryRequired)
            // — the background-task guard added for this already self-heals independently of
            // migration status (see the "Fix confirmed cross-category leak" block below), but the
            // table CREATEs above are placed here, synchronously, so the columns/tables they need
            // exist before any request can be served — unlike the FeatureCategory backfill, which
            // needs the full seed data present first and so must stay in the background task.

            // Conversation pin/archive (migration AddConversationPinArchive) — same bootstrap risk.
            foreach (var colDef in new[] {
                "`IsPinned` tinyint(1) NOT NULL DEFAULT 0",
                "`IsArchived` tinyint(1) NOT NULL DEFAULT 0" })
            { try { db.Database.ExecuteSqlRaw($"ALTER TABLE `conversations` ADD COLUMN {colDef}"); } catch (Exception ex) { logger.LogDebug("[Schema] conversations.{Col}: {Msg}", colDef, ex.Message); } }

            try { db.Database.ExecuteSqlRaw("ALTER TABLE `vehiclesubtypes` ADD COLUMN `Slug` varchar(100) NULL"); }
            catch (Exception ex) { logger.LogDebug("[Schema] vehiclesubtypes.Slug: {Msg}", ex.Message); }

            // refreshtokens.RevokedAt — required for every RefreshToken INSERT (EF Core always sends all columns)
            try { db.Database.ExecuteSqlRaw("ALTER TABLE `refreshtokens` ADD COLUMN `RevokedAt` datetime(6) NULL"); }
            catch (Exception ex) { logger.LogDebug("[Schema] refreshtokens.RevokedAt: {Msg}", ex.Message); }

            // users — columns added to User.cs without a corresponding migration
            foreach (var colDef in new[] {
                // notification preferences
                "`EmailNotifications`             tinyint(1)   NOT NULL DEFAULT 1",
                "`PriceChangeAlerts`              tinyint(1)   NOT NULL DEFAULT 1",
                "`NewMessageAlerts`               tinyint(1)   NOT NULL DEFAULT 1",
                "`NewsletterSubscribed`           tinyint(1)   NOT NULL DEFAULT 0",
                // email verification & password reset tokens (never had a migration)
                "`EmailVerificationToken`         longtext     NULL",
                "`EmailVerificationTokenExpires`  datetime(6)  NULL",
                "`PasswordResetToken`             longtext     NULL",
                "`PasswordResetTokenExpires`      datetime(6)  NULL",
                // subscription fields (covered by AddSubscriptionToUsers migration but guard
                // is needed for DBs where that migration was bootstrapped without running)
                "`SubscriptionTier`              int          NOT NULL DEFAULT 0",
                "`SubscriptionExpiresAt`         datetime(6)  NULL",
                "`SubscriptionStartedAt`         datetime(6)  NULL",
                "`StartProgramActivatedAt`       datetime(6)  NULL",
                "`FeaturedQuotaUsed`             int          NOT NULL DEFAULT 0",
                "`FeaturedQuotaResetAt`          datetime(6)  NULL",
                "`IsVerifiedDealer`              tinyint(1)   NOT NULL DEFAULT 0",
                "`FreePromoBoostUsedAt`          datetime(6)  NULL" })
            { try { db.Database.ExecuteSqlRaw($"ALTER TABLE `users` ADD COLUMN {colDef}"); } catch (Exception ex) { logger.LogDebug("[Schema] users.{Col}: {Msg}", colDef, ex.Message); } }

            // events — columns added to Event.cs without a corresponding migration
            foreach (var colDef in new[] {
                "`IsFeatured`       tinyint(1)   NOT NULL DEFAULT 0",
                "`OrganizerName`    longtext     NULL",
                "`OrganizerEmail`   longtext     NULL",
                "`OrganizerPhone`   longtext     NULL",
                "`TicketsUrl`       longtext     NULL",
                "`WebsiteUrl`       longtext     NULL",
                "`UpdatedAt`        datetime(6)  NULL",
                "`FeaturedUntil`    datetime(6)  NULL" })
            { try { db.Database.ExecuteSqlRaw($"ALTER TABLE `events` ADD COLUMN {colDef}"); } catch (Exception ex) { logger.LogDebug("[Schema] events.{Col}: {Msg}", colDef, ex.Message); } }

            // AdvertViews.IpAddress — renamed from IpHash; try rename first, then plain ADD as fallback
            try { db.Database.ExecuteSqlRaw("ALTER TABLE `advertviews` CHANGE COLUMN `IpHash` `IpAddress` longtext NULL"); }
            catch (Exception ex) { logger.LogDebug("RENAME advertviews.IpHash→IpAddress skipped: {Message}", ex.Message); }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE `advertviews` ADD COLUMN `IpAddress` longtext NULL"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN advertviews.IpAddress skipped: {Message}", ex.Message); }

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
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS `refreshtokens` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `Token` varchar(128) NOT NULL,
  `UserId` int NOT NULL,
  `ExpiresAt` datetime(6) NOT NULL,
  `IsRevoked` tinyint(1) NOT NULL DEFAULT 0,
  `RevokedAt` datetime(6) NULL,
  `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_refreshtokens_Token` (`Token`),
  KEY `IX_refreshtokens_UserId` (`UserId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS `financinginquiries` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `AdvertId` int NOT NULL,
  `UserId` int NULL,
  `Name` varchar(200) NOT NULL,
  `Phone` varchar(30) NOT NULL,
  `Email` varchar(200) NULL,
  `Type` varchar(20) NOT NULL DEFAULT 'leasing',
  `Price` decimal(18,2) NULL,
  `DownPaymentPct` int NULL,
  `Months` int NULL,
  `Status` varchar(20) NOT NULL DEFAULT 'new',
  `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  PRIMARY KEY (`Id`),
  KEY `IX_financinginquiries_AdvertId` (`AdvertId`),
  KEY `IX_financinginquiries_UserId` (`UserId`),
  KEY `IX_financinginquiries_CreatedAt` (`CreatedAt`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS `consentrecords` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `UserId` int NOT NULL,
  `ConsentType` varchar(100) NOT NULL,
  `PolicyVersion` varchar(50) NOT NULL,
  `GrantedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  `IpAddress` varchar(64) NULL,
  `UserAgent` varchar(500) NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_consentrecords_UserId` (`UserId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS `datadeletionrequests` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `FacebookUserId` varchar(100) NOT NULL,
  `UserId` int NULL,
  `ConfirmationCode` varchar(64) NOT NULL,
  `RequestedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  `CompletedAt` datetime(6) NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_datadeletionrequests_ConfirmationCode` (`ConfirmationCode`),
  KEY `IX_datadeletionrequests_FacebookUserId` (`FacebookUserId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                // Faza 2 of the category/attribute restructure (crispy-riding-mochi.md) - generic
                // per-category field system replacing the old "dump extraFields into description
                // text" pattern.
                @"CREATE TABLE IF NOT EXISTS `attributedefinitions` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `VehicleCategoryId` int NOT NULL,
  `VehicleSubtypeId` int NULL,
  `BrandId` int NULL,
  `ModelId` int NULL,
  `GenerationId` int NULL,
  `TrimId` int NULL,
  `Key` varchar(100) NOT NULL,
  `LabelPl` varchar(200) NOT NULL,
  `DataType` int NOT NULL,
  `Unit` varchar(30) NULL,
  `ValidationJson` longtext NULL,
  `OptionsJson` longtext NULL,
  `IsRequired` tinyint(1) NOT NULL DEFAULT 0,
  `IsFilterable` tinyint(1) NOT NULL DEFAULT 0,
  `IsSearchable` tinyint(1) NOT NULL DEFAULT 0,
  `IsActive` tinyint(1) NOT NULL DEFAULT 1,
  `SortOrder` int NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`),
  KEY `IX_attributedefinitions_VehicleCategoryId` (`VehicleCategoryId`),
  KEY `IX_attributedefinitions_VehicleSubtypeId` (`VehicleSubtypeId`),
  KEY `IX_attributedefinitions_scope` (`VehicleCategoryId`, `BrandId`, `ModelId`, `GenerationId`, `TrimId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

                @"CREATE TABLE IF NOT EXISTS `advertattributevalues` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `AdvertId` int NOT NULL,
  `AttributeDefinitionId` int NOT NULL,
  `ValueText` longtext NULL,
  `ValueNumber` decimal(18,4) NULL,
  `ValueBool` tinyint(1) NULL,
  `ValueDate` datetime(6) NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_advertattributevalues_AdvertId_AttributeDefinitionId` (`AdvertId`, `AttributeDefinitionId`),
  KEY `IX_advertattributevalues_AttrDef_ValueNumber` (`AttributeDefinitionId`, `ValueNumber`),
  KEY `IX_advertattributevalues_AttrDef_ValueText` (`AttributeDefinitionId`, `ValueText`(191)),
  KEY `IX_advertattributevalues_AttrDef_ValueBool` (`AttributeDefinitionId`, `ValueBool`),
  KEY `IX_advertattributevalues_AttrDef_ValueDate` (`AttributeDefinitionId`, `ValueDate`)
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
                    logger.LogDebug("CREATE TABLE skipped: {Message}", ex.Message);
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
                // Some brands (esp. newly-added motorcycle brands) have no seeded Models yet -
                // the add-advert form falls back to letting the user skip Model entirely rather
                // than block on missing taxonomy data, so the column must accept NULL.
                "ALTER TABLE `caradverts` MODIFY COLUMN `ModelId` int NULL",
                // Faza 6 of the category/attribute restructure: non-vehicle categories (Usługi
                // motoryzacyjne) and free-text-brand machinery categories (rolnicze/budowlane/
                // maszyny) never populate BrandId/FuelTypeId, which previously hit a NOT NULL FK
                // violation (500) on every such submission.
                "ALTER TABLE `caradverts` MODIFY COLUMN `BrandId` int NULL",
                "ALTER TABLE `caradverts` MODIFY COLUMN `FuelTypeId` int NULL",
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

            // Faza 8: AdvertDocuments — replaces the single YoutubeUrl/PdfBrochureUrl columns with
            // proper multiple-documents-per-advert support. AdvertId FKs to the base Advert table
            // (not CarAdvert specifically), same TPT-shared-Id relationship as AdvertAttributeValue.
            // No explicit FK constraint, index only — matches the advertimages table above.
            try
            {
                db.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS `advertdocuments` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `AdvertId` int NOT NULL,
  `Url` varchar(1000) NOT NULL,
  `Type` int NOT NULL,
  `Label` varchar(200) NULL,
  `SortOrder` int NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`),
  KEY `IX_AdvertDocuments_AdvertId` (`AdvertId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
            }
            catch (Exception ex) { logger.LogWarning("CREATE TABLE advertdocuments skipped: {Message}", ex.Message); }

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
                "ALTER TABLE `reviews` ADD COLUMN `Comment` longtext NULL",
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

            // CreatedByAdminId (migration AddCreatedByAdminIdToUsers) — same bootstrap risk as
            // every other column above: the migration history bootstrap can mark this migration
            // as already-applied on an existing DB, so MigrateAsync alone cannot be trusted here.
            try { db.Database.ExecuteSqlRaw("ALTER TABLE `users` ADD COLUMN `CreatedByAdminId` int NULL"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN users.CreatedByAdminId skipped: {Message}", ex.Message); }

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

            // KSeF columns for invoices
            try { db.Database.ExecuteSqlRaw("ALTER TABLE `invoices` ADD COLUMN `KSeFReferenceNumber` varchar(200) NULL"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN invoices.KSeFReferenceNumber skipped: {Message}", ex.Message); }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE `invoices` ADD COLUMN `IsKSeFSent` tinyint(1) NOT NULL DEFAULT 0"); }
            catch (Exception ex) { logger.LogDebug("ADD COLUMN invoices.IsKSeFSent skipped: {Message}", ex.Message); }

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
                var numericBrandCount = db.Brands.Count(b => b.Name != null && b.Name != "" && EF.Functions.Like(b.Name, "%") && b.Name.Length < 10);
                var sampleNames = db.Brands.OrderBy(b => b.Id).Take(3).Select(b => b.Name).ToList();
                var hasNumericNames = sampleNames.Any(n => !string.IsNullOrEmpty(n) && n.All(char.IsDigit));
                if (hasNumericNames)
                {
                    logger.LogWarning("Detected numeric brand names (samples: {Samples}) — clearing brand tables for re-seed", string.Join(", ", sampleNames));
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
                    logger.LogInformation("Brand tables cleared — seeder will re-populate immediately in this startup");
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

            // FK constraint guards — MySQL 8.0 does not support ADD CONSTRAINT IF NOT EXISTS,
            // so each constraint is added individually with try/catch (duplicate = skip).
            // This covers the FKs that migration 20260623200000 could not add safely.
            try { db.Database.ExecuteSqlRaw("ALTER TABLE `advertviews` ADD CONSTRAINT `FK_advertviews_caradverts_AdvertId` FOREIGN KEY (`AdvertId`) REFERENCES `caradverts`(`Id`) ON DELETE CASCADE"); }
            catch (Exception ex) { logger.LogDebug("FK advertviews.AdvertId skipped: {Message}", ex.Message); }

            try { db.Database.ExecuteSqlRaw("ALTER TABLE `caradverts` ADD CONSTRAINT `FK_caradverts_trims_TrimId` FOREIGN KEY (`TrimId`) REFERENCES `trims`(`Id`) ON DELETE SET NULL"); }
            catch (Exception ex) { logger.LogDebug("FK caradverts.TrimId skipped: {Message}", ex.Message); }

            try { db.Database.ExecuteSqlRaw("ALTER TABLE `caradverts` ADD CONSTRAINT `FK_caradverts_vehiclesubtypes_VehicleSubtypeId` FOREIGN KEY (`VehicleSubtypeId`) REFERENCES `vehiclesubtypes`(`Id`) ON DELETE SET NULL"); }
            catch (Exception ex) { logger.LogDebug("FK caradverts.VehicleSubtypeId skipped: {Message}", ex.Message); }

            try { db.Database.ExecuteSqlRaw("ALTER TABLE `caradverts` ADD CONSTRAINT `FK_caradverts_partcategories_PartCategoryId` FOREIGN KEY (`PartCategoryId`) REFERENCES `partcategories`(`Id`) ON DELETE SET NULL"); }
            catch (Exception ex) { logger.LogDebug("FK caradverts.PartCategoryId skipped: {Message}", ex.Message); }

            try { db.Database.ExecuteSqlRaw("ALTER TABLE `caradverts` ADD CONSTRAINT `FK_caradverts_partsubcategories_PartSubcategoryId` FOREIGN KEY (`PartSubcategoryId`) REFERENCES `partsubcategories`(`Id`) ON DELETE SET NULL"); }
            catch (Exception ex) { logger.LogDebug("FK caradverts.PartSubcategoryId skipped: {Message}", ex.Message); }

            logger.LogWarning("[STARTUP-TRACE] Reached FK constraint guards");

            // Also guard FKs from migrations 20260623100000 and 20260623105000 which used
            // the unsupported ADD CONSTRAINT IF NOT EXISTS syntax on MySQL 8.0.
            try { db.Database.ExecuteSqlRaw("ALTER TABLE `engineversions` ADD CONSTRAINT `FK_engineversions_trims_TrimId` FOREIGN KEY (`TrimId`) REFERENCES `trims`(`Id`) ON DELETE SET NULL"); }
            catch (Exception ex) { logger.LogDebug("FK engineversions.TrimId skipped: {Message}", ex.Message); }

            try { db.Database.ExecuteSqlRaw("ALTER TABLE `FeatureCategories` ADD CONSTRAINT `FK_FeatureCategories_VehicleCategories_VehicleCategoryId` FOREIGN KEY (`VehicleCategoryId`) REFERENCES `VehicleCategories`(`Id`) ON DELETE SET NULL"); }
            catch (Exception ex) { logger.LogDebug("FK FeatureCategories.VehicleCategoryId skipped: {Message}", ex.Message); }

            try { db.Database.ExecuteSqlRaw("ALTER TABLE `FeatureCategories` ADD CONSTRAINT `FK_FeatureCategories_Brands_BrandId` FOREIGN KEY (`BrandId`) REFERENCES `Brands`(`Id`) ON DELETE SET NULL"); }
            catch (Exception ex) { logger.LogDebug("FK FeatureCategories.BrandId skipped: {Message}", ex.Message); }

            try { db.Database.ExecuteSqlRaw("ALTER TABLE `FeatureCategories` ADD CONSTRAINT `FK_FeatureCategories_Models_ModelId` FOREIGN KEY (`ModelId`) REFERENCES `Models`(`Id`) ON DELETE SET NULL"); }
            catch (Exception ex) { logger.LogDebug("FK FeatureCategories.ModelId skipped: {Message}", ex.Message); }

            try { db.Database.ExecuteSqlRaw("ALTER TABLE `customcategoryrequests` ADD CONSTRAINT `FK_customcategoryrequests_VehicleCategories_ResultingVehicleCategoryId` FOREIGN KEY (`ResultingVehicleCategoryId`) REFERENCES `VehicleCategories`(`Id`) ON DELETE SET NULL"); }
            catch (Exception ex) { logger.LogDebug("FK customcategoryrequests.ResultingVehicleCategoryId skipped: {Message}", ex.Message); }

            try { db.Database.ExecuteSqlRaw("ALTER TABLE `customcategoryrequests` ADD CONSTRAINT `FK_customcategoryrequests_VehicleSubtypes_ResultingVehicleSubtypeId` FOREIGN KEY (`ResultingVehicleSubtypeId`) REFERENCES `VehicleSubtypes`(`Id`) ON DELETE SET NULL"); }
            catch (Exception ex) { logger.LogDebug("FK customcategoryrequests.ResultingVehicleSubtypeId skipped: {Message}", ex.Message); }

            try { db.Database.ExecuteSqlRaw("ALTER TABLE `PartCompatibilities` ADD CONSTRAINT `FK_PartCompatibilities_CarAdverts_CarAdvertId` FOREIGN KEY (`CarAdvertId`) REFERENCES `caradverts`(`Id`) ON DELETE CASCADE"); }
            catch (Exception ex) { logger.LogDebug("FK PartCompatibilities.CarAdvertId skipped: {Message}", ex.Message); }

            try { db.Database.ExecuteSqlRaw("ALTER TABLE `PartCompatibilities` ADD CONSTRAINT `FK_PartCompatibilities_Brands_BrandId` FOREIGN KEY (`BrandId`) REFERENCES `Brands`(`Id`) ON DELETE RESTRICT"); }
            catch (Exception ex) { logger.LogDebug("FK PartCompatibilities.BrandId skipped: {Message}", ex.Message); }

            try { db.Database.ExecuteSqlRaw("ALTER TABLE `PartCompatibilities` ADD CONSTRAINT `FK_PartCompatibilities_Models_ModelId` FOREIGN KEY (`ModelId`) REFERENCES `Models`(`Id`) ON DELETE RESTRICT"); }
            catch (Exception ex) { logger.LogDebug("FK PartCompatibilities.ModelId skipped: {Message}", ex.Message); }

            try { db.Database.ExecuteSqlRaw("ALTER TABLE `PartCompatibilities` ADD CONSTRAINT `FK_PartCompatibilities_Generations_GenerationId` FOREIGN KEY (`GenerationId`) REFERENCES `Generations`(`Id`) ON DELETE RESTRICT"); }
            catch (Exception ex) { logger.LogDebug("FK PartCompatibilities.GenerationId skipped: {Message}", ex.Message); }

            try { db.Database.ExecuteSqlRaw("ALTER TABLE `BrandAllowedFuelTypes` ADD CONSTRAINT `FK_BrandAllowedFuelTypes_Brands_BrandId` FOREIGN KEY (`BrandId`) REFERENCES `Brands`(`Id`) ON DELETE CASCADE"); }
            catch (Exception ex) { logger.LogDebug("FK BrandAllowedFuelTypes.BrandId skipped: {Message}", ex.Message); }

            try { db.Database.ExecuteSqlRaw("ALTER TABLE `BrandAllowedFuelTypes` ADD CONSTRAINT `FK_BrandAllowedFuelTypes_FuelTypes_FuelTypeId` FOREIGN KEY (`FuelTypeId`) REFERENCES `FuelTypes`(`Id`) ON DELETE RESTRICT"); }
            catch (Exception ex) { logger.LogDebug("FK BrandAllowedFuelTypes.FuelTypeId skipped: {Message}", ex.Message); }

            db.Database.SetCommandTimeout(30);
            logger.LogWarning("[STARTUP-TRACE] FK constraint guards complete; calling MergeDuplicateBrands");

            // Everything below is idempotent, self-healing data seeding/cleanup — never schema-
            // critical for serving requests — but its cumulative runtime has grown with every
            // brand batch added to ComprehensiveSeeder. Running it synchronously here blocked
            // app.Run()/the health check from ever coming up once that runtime exceeded Railway's
            // deploy timeout, causing a full outage (every request 502'd, not just seed-dependent
            // ones). Moved to a background task with its own DI scope so Kestrel starts accepting
            // connections immediately; seeding still runs to completion, just without blocking
            // startup, matching how this section is already designed to be safe to (re)run at
            // any time.
            _ = Task.Run(() =>
            {
                using var bgScope = app.Services.CreateScope();
                var bgDb = bgScope.ServiceProvider.GetRequiredService<AppDbContext>();
                var bgLogger = bgScope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();
                bgDb.Database.SetCommandTimeout(30);

                try
                {
                    MergeDuplicateBrands(bgDb, bgLogger);
                    bgLogger.LogWarning("[STARTUP-TRACE] MergeDuplicateBrands returned normally");
                }
                catch (Exception ex)
                {
                    bgLogger.LogError(ex, "[Cleanup] MergeDuplicateBrands failed: {Msg}", ex.Message);
                }

                // One-time self-heal for PartCompatibilities rows orphaned by earlier startups —
                // before the fix above, MergeDuplicateBrands deleted a duplicate Brand without
                // repointing PartCompatibilities.BrandId (that table has no FK enforcing this,
                // see the raw CREATE TABLE guard), leaving a dangling BrandId. AutoMapper's
                // unguarded `src.Brand.Name` then threw a NullReferenceException on every
                // GET /api/Advert/{id} for the affected adverts — 500 for those specific IDs only.
                try
                {
                    var orphanedPartCompat = bgDb.PartCompatibilities
                        .Where(pc => !bgDb.Brands.Any(b => b.Id == pc.BrandId))
                        .ToList();
                    if (orphanedPartCompat.Count > 0)
                    {
                        bgDb.PartCompatibilities.RemoveRange(orphanedPartCompat);
                        bgDb.SaveChanges();
                        bgLogger.LogWarning(
                            "[Cleanup] Removed {Count} PartCompatibilities rows with a dangling BrandId (advertIds: {AdvertIds})",
                            orphanedPartCompat.Count, string.Join(",", orphanedPartCompat.Select(pc => pc.CarAdvertId).Distinct()));
                    }
                }
                catch (Exception ex)
                {
                    bgLogger.LogError(ex, "[Cleanup] Orphaned PartCompatibilities cleanup failed: {Msg}", ex.Message);
                }

                bgLogger.LogWarning("[STARTUP-TRACE] Calling SeedDataIfEmpty");
                try
                {
                    SeedDataIfEmpty(bgDb, bgLogger);
                    bgLogger.LogWarning("[STARTUP-TRACE] SeedDataIfEmpty returned normally");
                }
                catch (Exception ex)
                {
                    bgLogger.LogError(ex, "[Seeder] SeedDataIfEmpty failed — app will start without complete seed data: {Msg}", ex.Message);
                }

                // Category expansion (10 -> 17): SeedDataIfEmpty above only seeds the original 10
                // VehicleCategory rows on a genuinely empty DB, so these 7 new categories need
                // their own idempotent guard, checked by slug so it's a no-op on every restart
                // once seeded. Each gets a starter VehicleSubtype set and 1-2 FeatureCategories —
                // deliberately not an attempt to match auta-osobowe's depth immediately; further
                // depth is ongoing backlog via the admin panel (see PRZEBUDOWA plan, Phase 8/10).
                try
                {
                    var newCategorySpecs = new[]
                    {
                        new {
                            Slug = "lodzie-i-jachty", Name = "Łodzie i jachty",
                            Description = "Łodzie motorowe, żaglówki, jachty i pontony", IconName = "mdi-sail-boat", SortOrder = 11,
                            Subtypes = new (string Name, string Slug)[] {
                                ("Łódź motorowa", "lodz-motorowa"), ("Jacht żaglowy", "jacht-zaglowy"),
                                ("Jacht motorowy", "jacht-motorowy"), ("Ponton", "ponton"),
                                ("Łódź wiosłowa / kajak", "lodz-wioslowa-kajak"), ("Houseboat", "houseboat"),
                                ("Łódź rybacka", "lodz-rybacka"),
                            },
                            FeatureCats = new (string Name, string[] Features)[] {
                                ("Nawigacja i elektronika", new[] { "GPS / plotter", "Sonar / echosonda", "Autopilot", "Radio VHF", "Radar" }),
                                ("Wyposażenie pokładowe", new[] { "Kotwica", "Liny cumownicze", "Drabinka kąpielowa", "Prysznic pokładowy", "Markiza / bimini", "Kambuz" }),
                            },
                        },
                        new {
                            Slug = "kampery", Name = "Kampery",
                            Description = "Kampery, autobusy kempingowe i pojazdy rekreacyjne", IconName = "mdi-caravan", SortOrder = 12,
                            Subtypes = new (string Name, string Slug)[] {
                                ("Kamper zabudowany", "kamper-zabudowany"), ("Kamper na podwoziu VAN", "kamper-van"),
                                ("Autobus kempingowy", "autobus-kempingowy"), ("Kamper pickup (camper shell)", "kamper-pickup"),
                            },
                            FeatureCats = new (string Name, string[] Features)[] {
                                ("Wyposażenie mieszkalne", new[] { "Kuchnia", "Lodówka", "Toaleta", "Prysznic", "Ogrzewanie postojowe", "Klimatyzacja postojowa", "Markiza" }),
                                ("Instalacje", new[] { "Panel słoneczny", "Generator prądu", "Zbiornik wody czystej", "Zbiornik wody szarej", "Instalacja gazowa", "Falownik 230V" }),
                            },
                        },
                        new {
                            Slug = "quady-atv", Name = "Quady i ATV",
                            Description = "Quady sportowe, użytkowe i pojazdy SSV/UTV", IconName = "mdi-atv", SortOrder = 13,
                            Subtypes = new (string Name, string Slug)[] {
                                ("Quad sportowy", "quad-sportowy"), ("Quad użytkowy", "quad-uzytkowy"),
                                ("Quad dziecięcy", "quad-dzieciecy"), ("SSV / UTV (side-by-side)", "ssv-utv"),
                            },
                            FeatureCats = new (string Name, string[] Features)[] {
                                ("Napęd i zawieszenie", new[] { "Napęd 4x4", "Reduktor", "Wspomaganie kierownicy (EPS)", "Zawieszenie niezależne" }),
                                ("Wyposażenie", new[] { "Wyciągarka", "Bagażnik przedni/tylny", "Hak holowniczy", "Oświetlenie LED", "Skrzynia ładunkowa" }),
                            },
                        },
                        new {
                            Slug = "skutery-wodne", Name = "Skutery wodne",
                            Description = "Skutery wodne jedno- i wieloosobowe", IconName = "mdi-ski-water", SortOrder = 14,
                            Subtypes = new (string Name, string Slug)[] {
                                ("Skuter jednoosobowy", "skuter-jednoosobowy"), ("Skuter wieloosobowy", "skuter-wieloosobowy"),
                                ("Skuter wyścigowy", "skuter-wyscigowy"),
                            },
                            FeatureCats = new (string Name, string[] Features)[] {
                                ("Wyposażenie", new[] { "System zabezpieczający (kill switch)", "Hak holowniczy", "Schowek wodoszczelny", "Uchwyt do holowania", "Drabinka" }),
                            },
                        },
                        new {
                            Slug = "autobusy", Name = "Autobusy",
                            Description = "Autobusy miejskie, turystyczne i minibusy", IconName = "mdi-bus", SortOrder = 15,
                            Subtypes = new (string Name, string Slug)[] {
                                ("Autobus miejski", "autobus-miejski"), ("Autobus turystyczny", "autobus-turystyczny"),
                                ("Minibus", "minibus"), ("Autobus szkolny", "autobus-szkolny"),
                            },
                            FeatureCats = new (string Name, string[] Features)[] {
                                ("Wyposażenie pasażerskie", new[] { "Klimatyzacja", "Monitoring", "System informacji pasażerskiej", "WiFi", "Gniazda USB przy siedzeniach", "Toaleta" }),
                                ("Bezpieczeństwo", new[] { "ABS", "ESP", "System unikania kolizji", "Kamery cofania", "Pasy bezpieczeństwa na wszystkich miejscach" }),
                            },
                        },
                        new {
                            Slug = "naczepy", Name = "Naczepy",
                            Description = "Naczepy ciągnięte przez ciągniki siodłowe", IconName = "mdi-truck-trailer", SortOrder = 16,
                            Subtypes = new (string Name, string Slug)[] {
                                ("Naczepa firanka", "naczepa-firanka"), ("Naczepa chłodnia", "naczepa-chlodnia"),
                                ("Naczepa wywrotka", "naczepa-wywrotka"), ("Naczepa niskopodwoziowa", "naczepa-niskopodwoziowa"),
                                ("Naczepa cysterna", "naczepa-cysterna"), ("Naczepa kontenerowa", "naczepa-kontenerowa"),
                            },
                            FeatureCats = new (string Name, string[] Features)[] {
                                ("Wyposażenie", new[] { "ABS", "System podnoszenia osi", "Plandeka", "Klapy boczne", "Ogumienie bliźniacze", "Zawieszenie pneumatyczne" }),
                            },
                        },
                        new {
                            Slug = "wozki-widlowe", Name = "Wózki widłowe",
                            Description = "Wózki widłowe spalinowe, elektryczne i magazynowe", IconName = "mdi-forklift", SortOrder = 17,
                            Subtypes = new (string Name, string Slug)[] {
                                ("Wózek widłowy spalinowy", "wozek-spalinowy"), ("Wózek widłowy elektryczny", "wozek-elektryczny"),
                                ("Wózek widłowy gazowy (LPG)", "wozek-gazowy"), ("Wózek boczny", "wozek-boczny"),
                                ("Wózek magazynowy (paleciak)", "wozek-magazynowy"),
                            },
                            FeatureCats = new (string Name, string[] Features)[] {
                                ("Wyposażenie", new[] { "Maszt trójstopniowy (triplex)", "Przesuw boczny", "Kabina zamknięta", "Ogumienie pełne", "Widły teleskopowe" }),
                            },
                        },
                    };

                    var existingSlugs = bgDb.VehicleCategories.Select(c => c.Slug).ToHashSet();
                    foreach (var spec in newCategorySpecs)
                    {
                        if (existingSlugs.Contains(spec.Slug)) continue;

                        var vcat = new VehicleCategory {
                            Slug = spec.Slug, Name = spec.Name, Description = spec.Description,
                            IconName = spec.IconName, SortOrder = spec.SortOrder,
                        };
                        bgDb.VehicleCategories.Add(vcat);
                        bgDb.SaveChanges(); // need vcat.Id for the children below

                        var order = 0;
                        foreach (var (name, slug) in spec.Subtypes)
                            bgDb.VehicleSubtypes.Add(new VehicleSubtype { VehicleCategoryId = vcat.Id, Name = name, Slug = slug, SortOrder = order++ });

                        foreach (var (fcName, features) in spec.FeatureCats)
                        {
                            bgDb.FeatureCategories.Add(new FeatureCategory {
                                Name = fcName,
                                VehicleCategoryId = vcat.Id,
                                Features = features.Select(f => new Feature { Name = f }).ToList(),
                            });
                        }

                        bgDb.SaveChanges();
                        bgLogger.LogWarning("[STARTUP-TRACE] Seeded new vehicle category '{Slug}' ({SubCount} subtypes, {FcCount} feature categories)",
                            spec.Slug, spec.Subtypes.Length, spec.FeatureCats.Length);
                    }
                }
                catch (Exception ex)
                {
                    bgLogger.LogError(ex, "[STARTUP-TRACE] New category expansion seeding failed: {Msg}", ex.Message);
                }

                // newCategorySpecs above only inserts a row once (guarded by existingSlugs), so a
                // corrected IconName in the spec never reaches rows already seeded by an earlier
                // deploy. mdi-quadbike / mdi-jet-ski never existed in @mdi/font and rendered as
                // blank tiles; force-sync the corrected names on every startup instead.
                try
                {
                    var iconFixes = new Dictionary<string, string> {
                        ["quady-atv"] = "mdi-atv",
                        ["skutery-wodne"] = "mdi-ski-water",
                    };
                    foreach (var (slug, icon) in iconFixes)
                    {
                        var cat = bgDb.VehicleCategories.FirstOrDefault(c => c.Slug == slug);
                        if (cat != null && cat.IconName != icon)
                        {
                            cat.IconName = icon;
                            bgLogger.LogWarning("[STARTUP-TRACE] Fixed IconName for '{Slug}' -> '{Icon}'", slug, icon);
                        }
                    }
                    bgDb.SaveChanges();
                }
                catch (Exception ex)
                {
                    bgLogger.LogError(ex, "[STARTUP-TRACE] Category icon backfill failed: {Msg}", ex.Message);
                }

                // Real Brand<->VehicleCategory data for the 7 new categories (Phase 8 seeded the
                // categories/subtypes/features but left the brand field as free text everywhere,
                // which showed a misleading "N dostępnych marek" hint pulled from the *global*
                // brand count instead of any real per-category data). This is a starter set of
                // well-known manufacturers per category, not an exhaustive catalog - full brand/
                // model population remains ongoing backlog. Runs every startup; idempotent by slug.
                try
                {
                    var newCatBrands = bgDb.VehicleCategories
                        .Where(c => new[] { "lodzie-i-jachty", "kampery", "quady-atv", "skutery-wodne", "autobusy", "naczepy", "wozki-widlowe" }.Contains(c.Slug))
                        .ToDictionary(c => c.Slug, c => c);

                    // Attach the new category to brands that already exist (avoids duplicating
                    // well-known manufacturers who legitimately span multiple categories).
                    var attachExisting = new Dictionary<string, string[]> {
                        ["kampery"] = new[] { "fiat", "mercedes-benz" },
                        ["quady-atv"] = new[] { "yamaha", "honda", "suzuki" },
                        ["skutery-wodne"] = new[] { "yamaha", "kawasaki" },
                        ["autobusy"] = new[] { "mercedes-benz", "man", "scania", "iveco", "volvo" },
                        ["naczepy"] = new[] { "krone-trailer", "wielton", "fliegl", "kogel", "schwarzmuller", "schmitz-cargobull", "nooteboom", "meiller" },
                        ["wozki-widlowe"] = new[] { "toyota", "komatsu" },
                        ["lodzie-i-jachty"] = new[] { "yamaha" },
                    };
                    foreach (var (catSlug, brandSlugs) in attachExisting)
                    {
                        if (!newCatBrands.TryGetValue(catSlug, out var cat)) continue;
                        foreach (var bSlug in brandSlugs)
                        {
                            var brand = bgDb.Brands.Include(b => b.Categories).FirstOrDefault(b => b.Slug == bSlug);
                            if (brand != null && !brand.Categories.Any(c => c.Id == cat.Id))
                                brand.Categories.Add(cat);
                        }
                    }
                    bgDb.SaveChanges();

                    // New brands specific to these categories.
                    var existingBrandSlugs = bgDb.Brands.Select(b => b.Slug).ToHashSet();
                    var newBrandsToAdd = new List<Brand>();
                    void AddIfMissing(string catSlug, string name, string slug)
                    {
                        if (!newCatBrands.TryGetValue(catSlug, out var cat)) return;
                        if (existingBrandSlugs.Contains(slug)) return;
                        newBrandsToAdd.Add(new Brand { Name = name, Slug = slug, Categories = new List<VehicleCategory> { cat } });
                    }
                    foreach (var (n, s) in new (string, string)[] {
                        ("Bavaria Yachts", "bavaria-yachts"), ("Jeanneau", "jeanneau"), ("Beneteau", "beneteau"),
                        ("Quicksilver", "quicksilver-boats"), ("Bayliner", "bayliner"), ("Sea Ray", "sea-ray"),
                        ("Galeon", "galeon"), ("Ranieri", "ranieri"),
                    }) AddIfMissing("lodzie-i-jachty", n, s);
                    foreach (var (n, s) in new (string, string)[] {
                        ("Adria", "adria"), ("Dethleffs", "dethleffs"), ("Hymer", "hymer"), ("Knaus", "knaus"),
                        ("Carthago", "carthago"), ("Rapido", "rapido"), ("Chausson", "chausson"), ("Benimar", "benimar"),
                    }) AddIfMissing("kampery", n, s);
                    foreach (var (n, s) in new (string, string)[] {
                        ("Polaris", "polaris"), ("CFMOTO", "cfmoto"), ("Can-Am", "can-am"), ("Kymco", "kymco"), ("Segway", "segway-powersports"),
                    }) AddIfMissing("quady-atv", n, s);
                    AddIfMissing("skutery-wodne", "Sea-Doo", "sea-doo");
                    foreach (var (n, s) in new (string, string)[] {
                        ("Solaris Bus & Coach", "solaris-bus"), ("Setra", "setra"), ("Neoplan", "neoplan"), ("Irisbus", "irisbus"),
                    }) AddIfMissing("autobusy", n, s);
                    foreach (var (n, s) in new (string, string)[] {
                        ("Linde", "linde"), ("Jungheinrich", "jungheinrich"), ("Still", "still"), ("Hyster", "hyster"), ("Crown", "crown-equipment"),
                    }) AddIfMissing("wozki-widlowe", n, s);

                    if (newBrandsToAdd.Count > 0)
                    {
                        bgDb.Brands.AddRange(newBrandsToAdd);
                        bgDb.SaveChanges();
                    }
                    bgLogger.LogWarning("[STARTUP-TRACE] Brand<->category seeding for new categories done ({Count} new brands)", newBrandsToAdd.Count);
                }
                catch (Exception ex)
                {
                    bgLogger.LogError(ex, "[STARTUP-TRACE] New-category brand seeding failed: {Msg}", ex.Message);
                }

                // The 7 new categories launched with only 1-2 equipment groups each (5-13 items),
                // noticeably thinner than established categories like auta-osobowe (6 groups,
                // ~35-40 items) or rolnicze/dostawcze (4 groups, ~20-25 items). Adds a second
                // group where there was only one, and backfills missing items into existing
                // groups by name. Idempotent, runs every startup.
                try
                {
                    var equipCatBySlug = bgDb.VehicleCategories
                        .Where(c => new[] { "lodzie-i-jachty", "kampery", "quady-atv", "skutery-wodne", "autobusy", "naczepy", "wozki-widlowe" }.Contains(c.Slug))
                        .ToDictionary(c => c.Slug, c => c.Id);

                    var equipmentExpansions = new Dictionary<string, (string Group, string[] Features)[]> {
                        ["lodzie-i-jachty"] = new[] {
                            ("Wyposażenie pokładowe", new[] { "Zapasowy silnik zaburtowy", "Oświetlenie podwodne LED", "Chłodziarka pokładowa / lodówka", "System audio zewnętrzny", "Toaleta / WC chemiczne" }),
                            ("Komfort i zasilanie", new[] { "Klimatyzacja kabiny", "Generator / agregat prądotwórczy", "Instalacja elektryczna 12V/230V" }),
                        },
                        ["kampery"] = new[] {
                            ("Multimedia i bezpieczeństwo", new[] { "Markiza elektryczna", "System nawigacji kamperowej", "Telewizor", "System alarmowy", "Kamera cofania", "Bagażnik rowerowy zewnętrzny", "Tempomat", "Hak holowniczy" }),
                        },
                        ["quady-atv"] = new[] {
                            ("Wyposażenie", new[] { "Ogrzewane manetki", "Wyświetlacz cyfrowy (kokpit)", "Kufer boczny / kufry", "Osłona silnika (skid plate)", "Alarm / immobilizer" }),
                        },
                        ["skutery-wodne"] = new[] {
                            ("Wyposażenie", new[] { "Bluetooth / zestaw audio", "Oświetlenie LED", "Wyświetlacz cyfrowy", "Uchwyt narciarski", "Immobilizer / alarm", "Pokrowiec transportowy" }),
                        },
                        ["autobusy"] = new[] {
                            ("Komfort kierowcy i dostępność", new[] { "Winda dla niepełnosprawnych", "Ogrzewanie postojowe", "Fotel pneumatyczny kierowcy", "Tachograf cyfrowy", "Klimatyzacja niezależna kierowcy", "Rampa najazdowa dla wózków" }),
                        },
                        ["naczepy"] = new[] {
                            ("Systemy i homologacje", new[] { "Winda załadowcza", "Klapa tylna", "System TPMS (kontrola ciśnienia)", "Homologacja ADR", "Oświetlenie LED", "Instalacja chłodnicza (agregat)" }),
                        },
                        ["wozki-widlowe"] = new[] {
                            ("Komfort i bezpieczeństwo operatora", new[] { "Kamera cofania", "Zawieszona / amortyzowana kabina", "Klimatyzacja kabiny", "Wskaźnik obciążenia", "Udźwig regulowany", "Ogumienie poliuretanowe", "Sygnalizacja LED (blue spot)" }),
                        },
                    };

                    int addedGroups = 0, addedFeatures = 0;
                    foreach (var (catSlug, groups) in equipmentExpansions)
                    {
                        if (!equipCatBySlug.TryGetValue(catSlug, out var catId)) continue;
                        foreach (var (groupName, featureNames) in groups)
                        {
                            var fc = bgDb.FeatureCategories
                                .Include(f => f.Features)
                                .FirstOrDefault(f => f.VehicleCategoryId == catId && f.Name == groupName);
                            if (fc == null)
                            {
                                bgDb.FeatureCategories.Add(new FeatureCategory {
                                    Name = groupName, VehicleCategoryId = catId,
                                    Features = featureNames.Select(f => new Feature { Name = f }).ToList(),
                                });
                                addedGroups++;
                                addedFeatures += featureNames.Length;
                            }
                            else
                            {
                                var existingNames = fc.Features.Select(f => f.Name).ToHashSet();
                                foreach (var fname in featureNames)
                                {
                                    if (!existingNames.Contains(fname))
                                    {
                                        fc.Features.Add(new Feature { Name = fname });
                                        addedFeatures++;
                                    }
                                }
                            }
                        }
                    }
                    bgDb.SaveChanges();
                    bgLogger.LogWarning("[STARTUP-TRACE] Expanded equipment for new categories: {Groups} new groups, {Features} features added", addedGroups, addedFeatures);
                }
                catch (Exception ex)
                {
                    bgLogger.LogError(ex, "[STARTUP-TRACE] New-category equipment expansion failed: {Msg}", ex.Message);
                }

                // przyczepy vs naczepy brand/subtype overlap cleanup. Krone/Schmitz Cargobull/
                // Kögel/Schwarzmüller/Nooteboom make heavy semi-trailers exclusively, not
                // car-towable trailers — they were only ever seeded under przyczepy by an earlier
                // pass, before naczepy existed. Wielton/Fliegl genuinely span both segments and
                // stay linked to both.
                try
                {
                    var przyczepyCat = bgDb.VehicleCategories.FirstOrDefault(c => c.Slug == "przyczepy");
                    if (przyczepyCat != null)
                    {
                        var semiOnlySlugs = new[] { "krone-trailer", "schmitz-cargobull", "kogel", "schwarzmuller", "nooteboom" };
                        foreach (var slug in semiOnlySlugs)
                        {
                            var brand = bgDb.Brands.Include(b => b.Categories).FirstOrDefault(b => b.Slug == slug);
                            var link = brand?.Categories.FirstOrDefault(c => c.Id == przyczepyCat.Id);
                            if (link != null) brand!.Categories.Remove(link);
                        }
                        bgDb.SaveChanges();

                        var existingBrandSlugs3 = bgDb.Brands.Select(b => b.Slug).ToHashSet();
                        var newTrailerBrands = new List<Brand>();
                        foreach (var (n, s) in new (string, string)[] {
                            ("Böckmann", "boeckmann"), ("Brenderup", "brenderup"), ("Saris", "saris"),
                        })
                        {
                            if (!existingBrandSlugs3.Contains(s))
                                newTrailerBrands.Add(new Brand { Name = n, Slug = s, Categories = new List<VehicleCategory> { przyczepyCat } });
                        }
                        if (newTrailerBrands.Count > 0)
                        {
                            bgDb.Brands.AddRange(newTrailerBrands);
                            bgDb.SaveChanges();
                        }
                        bgLogger.LogWarning("[STARTUP-TRACE] przyczepy/naczepy brand cleanup done, {Count} new light-trailer brands", newTrailerBrands.Count);
                    }
                }
                catch (Exception ex)
                {
                    bgLogger.LogError(ex, "[STARTUP-TRACE] przyczepy/naczepy brand cleanup failed: {Msg}", ex.Message);
                }

                // Faza 6 of the category/attribute restructure (crispy-riding-mochi.md): 4 new
                // categories - Opony, Felgi, Akcesoria, Usługi motoryzacyjne. Same idempotent-by-
                // slug pattern as the category expansion above. Opony/Felgi/Akcesoria still reuse
                // CarAdvert (a tire/wheel/accessory has a real Brand); Usługi motoryzacyjne
                // genuinely has neither brand nor fuel type - safe now that BrandId/FuelTypeId are
                // nullable (see the "Fix 500 on brand-less/fuel-type-less advert submissions" PR).
                try
                {
                    var faza6Specs = new[]
                    {
                        new {
                            Slug = "opony", Name = "Opony",
                            Description = "Opony osobowe, dostawcze, ciężarowe i motocyklowe", IconName = "mdi-tire", SortOrder = 18,
                            Subtypes = new (string Name, string Slug)[] {
                                ("Osobowe", "opony-osobowe"), ("Dostawcze / SUV", "opony-dostawcze-suv"),
                                ("Ciężarowe", "opony-ciezarowe"), ("Motocyklowe", "opony-motocyklowe"),
                                ("Rolnicze / przemysłowe", "opony-rolnicze"),
                            },
                        },
                        new {
                            Slug = "felgi", Name = "Felgi",
                            Description = "Felgi stalowe i aluminiowe do wszystkich typów pojazdów", IconName = "mdi-circle-double", SortOrder = 19,
                            Subtypes = new (string Name, string Slug)[] {
                                ("Osobowe", "felgi-osobowe"), ("Dostawcze / SUV", "felgi-dostawcze-suv"),
                                ("Ciężarowe", "felgi-ciezarowe"), ("Motocyklowe", "felgi-motocyklowe"),
                            },
                        },
                        new {
                            Slug = "akcesoria", Name = "Akcesoria",
                            Description = "Akcesoria samochodowe i wyposażenie dodatkowe", IconName = "mdi-package-variant-closed", SortOrder = 20,
                            Subtypes = Array.Empty<(string, string)>(),
                        },
                        new {
                            Slug = "uslugi-motoryzacyjne", Name = "Usługi motoryzacyjne",
                            Description = "Warsztaty, mechanika, wulkanizacja, detailing i inne usługi", IconName = "mdi-car-wrench", SortOrder = 21,
                            Subtypes = Array.Empty<(string, string)>(),
                        },
                    };

                    var existingSlugs6 = bgDb.VehicleCategories.Select(c => c.Slug).ToHashSet();
                    foreach (var spec in faza6Specs)
                    {
                        if (existingSlugs6.Contains(spec.Slug)) continue;

                        var vcat = new VehicleCategory {
                            Slug = spec.Slug, Name = spec.Name, Description = spec.Description,
                            IconName = spec.IconName, SortOrder = spec.SortOrder,
                        };
                        bgDb.VehicleCategories.Add(vcat);
                        bgDb.SaveChanges(); // need vcat.Id for subtypes below

                        var order = 0;
                        foreach (var (name, slug) in spec.Subtypes)
                            bgDb.VehicleSubtypes.Add(new VehicleSubtype { VehicleCategoryId = vcat.Id, Name = name, Slug = slug, SortOrder = order++ });
                        bgDb.SaveChanges();

                        bgLogger.LogWarning("[STARTUP-TRACE] Seeded Faza 6 vehicle category '{Slug}' ({SubCount} subtypes)", spec.Slug, spec.Subtypes.Length);
                    }
                }
                catch (Exception ex)
                {
                    bgLogger.LogError(ex, "[STARTUP-TRACE] Faza 6 category seeding failed: {Msg}", ex.Message);
                }

                // Faza 6: tire/wheel manufacturer Brand<->Category data, reusing the existing
                // Brand->Model hierarchy exactly like every other category (Michelin/Continental/
                // etc. as Brands linked to "opony"/"felgi" via the existing many-to-many, with a
                // few representative product lines as Models) - zero new schema per the plan. Not
                // an exhaustive catalog, same starter-set convention as the Phase 8 brand seeding
                // above. Runs every startup; idempotent by slug.
                try
                {
                    string Slugify(string s) => s.ToLowerInvariant()
                        .Replace(" ", "-").Replace("/", "-").Replace("*", "").Replace("ą", "a")
                        .Replace("ę", "e").Replace("ł", "l").Replace("ó", "o").Replace("ś", "s")
                        .Replace("ż", "z").Replace("ź", "z").Replace("ń", "n").Replace("ć", "c");

                    var tireWheelCats = bgDb.VehicleCategories
                        .Where(c => new[] { "opony", "felgi" }.Contains(c.Slug))
                        .ToDictionary(c => c.Slug, c => c);

                    var existingBrandSlugs6 = bgDb.Brands.Select(b => b.Slug).ToHashSet();
                    var newBrands6 = new List<Brand>();
                    var brandModels6 = new Dictionary<string, string[]>();
                    void AddTireWheelBrand(string catSlug, string name, string slug, string[] models)
                    {
                        if (!tireWheelCats.TryGetValue(catSlug, out var cat)) return;
                        if (existingBrandSlugs6.Contains(slug)) return;
                        newBrands6.Add(new Brand { Name = name, Slug = slug, Categories = new List<VehicleCategory> { cat } });
                        brandModels6[slug] = models;
                    }

                    foreach (var (n, s, models) in new (string, string, string[])[] {
                        ("Michelin", "michelin-tyres", new[] { "Pilot Sport 4", "CrossClimate 2", "Primacy 4", "Agilis 3", "Alpin 6" }),
                        ("Continental", "continental-tyres", new[] { "PremiumContact 6", "WinterContact TS 870", "EcoContact 6", "VanContact 4Season" }),
                        ("Bridgestone", "bridgestone-tyres", new[] { "Turanza T005", "Blizzak LM005", "Potenza Sport", "Duravis" }),
                        ("Pirelli", "pirelli-tyres", new[] { "P Zero", "Cinturato P7", "Winter Sottozero 3", "Scorpion" }),
                        ("Goodyear", "goodyear-tyres", new[] { "EfficientGrip Performance 2", "UltraGrip 9+", "Vector 4Seasons" }),
                        ("Dunlop", "dunlop-tyres", new[] { "Sport Maxx RT2", "Winter Sport 5", "SP Sport" }),
                        ("Nokian", "nokian-tyres", new[] { "Hakkapeliitta R5", "Powerproof", "WR Snowproof" }),
                        ("Hankook", "hankook-tyres", new[] { "Ventus Prime4", "Winter i*cept", "Kinergy 4S2" }),
                        ("Yokohama", "yokohama-tyres", new[] { "BluEarth-GT", "Advan Sport", "Geolandar" }),
                        ("Falken", "falken-tyres", new[] { "Azenis FK510", "Eurowinter HS01" }),
                        ("Kumho", "kumho-tyres", new[] { "Ecsta PS71", "WinterCraft" }),
                        ("Semperit", "semperit-tyres", new[] { "Speed-Life 3", "Master-Grip 2" }),
                    }) AddTireWheelBrand("opony", n, s, models);

                    foreach (var (n, s, models) in new (string, string, string[])[] {
                        ("BBS", "bbs-wheels", new[] { "CH-R", "SR", "LM" }),
                        ("OZ Racing", "oz-racing", new[] { "Ultraleggera", "Superturismo", "Botticelli" }),
                        ("Enkei", "enkei-wheels", new[] { "RPF1", "PF01", "Performance Line" }),
                        ("Borbet", "borbet-wheels", new[] { "BY", "CW", "A" }),
                        ("ATS", "ats-wheels", new[] { "Radial+", "Emotion" }),
                        ("Alutec", "alutec-wheels", new[] { "Grip", "Ikenu" }),
                        ("Dezent", "dezent-wheels", new[] { "TE", "RE" }),
                        ("Ronal", "ronal-wheels", new[] { "R59", "R60" }),
                    }) AddTireWheelBrand("felgi", n, s, models);

                    if (newBrands6.Count > 0)
                    {
                        bgDb.Brands.AddRange(newBrands6);
                        bgDb.SaveChanges(); // need brand.Id for Models below

                        foreach (var brand in newBrands6)
                        {
                            if (!brandModels6.TryGetValue(brand.Slug, out var models)) continue;
                            foreach (var modelName in models)
                                bgDb.Models.Add(new Model { Brand = brand, Name = modelName, Slug = $"{brand.Slug}-{Slugify(modelName)}" });
                        }
                        bgDb.SaveChanges();
                    }
                    bgLogger.LogWarning("[STARTUP-TRACE] Tire/wheel brand seeding done ({Count} new brands)", newBrands6.Count);
                }
                catch (Exception ex)
                {
                    bgLogger.LogError(ex, "[STARTUP-TRACE] Tire/wheel brand seeding failed: {Msg}", ex.Message);
                }

                // Re-run the attribute-definition seeder AFTER the Faza 6 category block: the first
                // pass (inside SeedDataIfEmpty above) runs before these categories exist on a fresh
                // or partially-seeded DB, so every opony/felgi/akcesoria/usługi spec lands in
                // skippedNoCategory and the add-advert form shows no size/parameter fields for them
                // until the NEXT restart. The seeder is an idempotent upsert, so the second pass is
                // a no-op when the first one already covered everything.
                try
                {
                    AttributeDefinitionMigrationSeeder.Seed(bgDb, bgLogger);
                    bgLogger.LogWarning("[STARTUP-TRACE] Post-category attribute seeding pass done");
                }
                catch (Exception ex)
                {
                    bgLogger.LogError(ex, "[STARTUP-TRACE] Post-category attribute seeding pass failed: {Msg}", ex.Message);
                }

                // przyczepy also accumulated ~12 "Naczepa X" subtypes from earlier seeding passes
                // that predate the naczepy category — several are exact duplicates of an existing
                // non-"Naczepa" row (e.g. both "Naczepa wywrotka" and "Przyczepa wywrotka" exist),
                // and the rest just don't need the prefix (every other category's subtypes are
                // bare type words, e.g. ciezarowe's "Firanka"). Re-points any advert referencing a
                // duplicate to the surviving row before deleting it, so nothing loses its subtype.
                try
                {
                    var duplicateSubtypePairs = new (string DupSlug, string KeepSlug)[] {
                        ("naczepa-platforma", "przyczepa-platforma"),
                        ("naczepa-wywrotka", "przyczepa-wywrotka"),
                        ("naczepa-cysterna", "przyczepa-cysterna"),
                        ("naczepa-niskopodwoziowa", "przyczepa-niskopodwoziowa"),
                        ("naczepa-silos", "silos"),
                        ("naczepa-dluzica", "dluzica"),
                        ("naczepa-kurtynowa", "naczepa-firanka"),
                    };
                    foreach (var (dupSlug, keepSlug) in duplicateSubtypePairs)
                    {
                        var dup = bgDb.VehicleSubtypes.FirstOrDefault(s => s.Slug == dupSlug);
                        var keep = bgDb.VehicleSubtypes.FirstOrDefault(s => s.Slug == keepSlug);
                        if (dup == null || keep == null || dup.Id == keep.Id) continue;
                        var affectedAdverts = bgDb.CarAdverts.Where(a => a.VehicleSubtypeId == dup.Id).ToList();
                        foreach (var ad in affectedAdverts) ad.VehicleSubtypeId = keep.Id;
                        bgDb.VehicleSubtypes.Remove(dup);
                    }
                    bgDb.SaveChanges();

                    var subtypeRenames = new Dictionary<string, string> {
                        ["naczepa-firanka"] = "Firanka / plandeka",
                        ["naczepa-chlodnia"] = "Chłodnia",
                        ["naczepa-izoterma"] = "Izoterma",
                        ["naczepa-kontener"] = "Kontener",
                        ["naczepa-autotransporter"] = "Autotransporter",
                    };
                    foreach (var (slug, newName) in subtypeRenames)
                    {
                        var st = bgDb.VehicleSubtypes.FirstOrDefault(s => s.Slug == slug);
                        if (st != null && st.Name != newName) st.Name = newName;
                    }
                    bgDb.SaveChanges();
                    bgLogger.LogWarning("[STARTUP-TRACE] Deduped przyczepy/naczepy subtype overlap");
                }
                catch (Exception ex)
                {
                    bgLogger.LogError(ex, "[STARTUP-TRACE] przyczepy subtype cleanup failed: {Msg}", ex.Message);
                }

                // Fix confirmed cross-category leak, then harden the schema so it can't recur.
                // History: 6 FeatureCategory rows named "Specjalne - <type>" (created via the
                // admin panel, not seeded by any code here) had a vehicle-type name but
                // VehicleCategoryId = NULL, meaning they showed up on EVERY category's equipment
                // step instead of just their own — e.g. motorcycle-only equipment showing on a car
                // listing. Confirmed via the AUDIT-FEATURES log added in #57.
                //
                // Step 1: name-pattern self-heal for the known "Specjalne - ..." rows.
                // Step 2: any OTHER row still unscoped (not caught by the name pattern) is parked
                //         under "inne" as a visible, admin-reviewable fallback instead of left NULL.
                // Step 3: once nothing is NULL, VehicleCategoryId is made NOT NULL at the DB level,
                //         so this bug class becomes structurally impossible instead of relying on
                //         self-heal code catching every future case.
                // All three steps run as raw SQL (not through the FeatureCategory entity, whose
                // VehicleCategoryId is now a non-nullable C# int) so EF never has to materialize a
                // row that is still NULL in the DB while these fixes are in flight.
                try
                {
                    var slugMatches = new (string Match, string Slug)[]
                    {
                        ("Ciężarówki", "ciezarowe"),
                        ("Dostawcze", "dostawcze"),
                        ("budowlane", "budowlane"),
                        ("rolnicze", "rolnicze"),
                        ("Motocykle", "motocykle"),
                        ("Przyczepy", "przyczepy"),
                    };
                    foreach (var (match, slug) in slugMatches)
                    {
                        var affected = bgDb.Database.ExecuteSqlRaw(
                            "UPDATE `featurecategories` fc JOIN `vehiclecategories` vc ON vc.`Slug` = {0} " +
                            "SET fc.`VehicleCategoryId` = vc.`Id` " +
                            "WHERE fc.`VehicleCategoryId` IS NULL AND fc.`Name` LIKE 'Specjalne%' AND fc.`Name` LIKE CONCAT('%', {1}, '%')",
                            slug, match);
                        if (affected > 0)
                            bgLogger.LogWarning("[STARTUP-TRACE] Fixed {Count} FeatureCategory row(s) matching '{Match}' scope: ANY -> {Slug}", affected, match, slug);
                    }
                }
                catch (Exception ex)
                {
                    bgLogger.LogError(ex, "[STARTUP-TRACE] FeatureCategory scope fix failed: {Msg}", ex.Message);
                }

                try
                {
                    var fallbackFixed = bgDb.Database.ExecuteSqlRaw(
                        "UPDATE `featurecategories` fc JOIN `vehiclecategories` vc ON vc.`Slug` = 'inne' " +
                        "SET fc.`VehicleCategoryId` = vc.`Id` " +
                        "WHERE fc.`VehicleCategoryId` IS NULL");
                    if (fallbackFixed > 0)
                        bgLogger.LogWarning("[STARTUP-TRACE] {Count} FeatureCategory row(s) had no vehicle-category scope and no name match — parked under 'inne' for admin review", fallbackFixed);
                }
                catch (Exception ex)
                {
                    bgLogger.LogError(ex, "[STARTUP-TRACE] FeatureCategory null-scope fallback failed: {Msg}", ex.Message);
                }

                try
                {
                    bgDb.Database.ExecuteSqlRaw("ALTER TABLE `featurecategories` MODIFY COLUMN `VehicleCategoryId` int NOT NULL");
                    bgLogger.LogWarning("[STARTUP-TRACE] FeatureCategories.VehicleCategoryId is now NOT NULL");
                }
                catch (Exception ex)
                {
                    bgLogger.LogError(ex, "[STARTUP-TRACE] Could not make FeatureCategories.VehicleCategoryId NOT NULL yet (some row may still be unscoped): {Msg}", ex.Message);
                }

                // Repair fallout from the fallback above, confirmed via the AUDIT-FEATURES log
                // dump below: exactly 8 rows (the lowest ids in the table, 1-8 - the very first
                // FeatureCategory rows ever created, predating the VehicleCategoryId column added
                // 2026-06-23) sit under 'inne' - Bezpieczeństwo, Komfort, Multimedia i łączność,
                // Oświetlenie, Systemy wspomagania, Dodatki i akcesoria, Tapicerka i wnętrze,
                // Zewnętrzne - wiping the entire auta-osobowe equipment step. Every other vehicle
                // type (motocykle/przyczepy/rolnicze/...) already has its own groups correctly
                // scoped, so 'inne' only ever holds this one leaked car-equipment set.
                // A prior attempt at this fix matched by exact Feature-name set against the
                // original seed lists above, but these 8 rows have since been edited via the
                // admin panel (while still misscoped) and now carry many more features than the
                // original seed, so that match found nothing - match by name within the 'inne'
                // bucket instead, which is unambiguous since no other 'inne' row shares these names.
                try
                {
                    var inneCatId = bgDb.VehicleCategories.Where(vc => vc.Slug == "inne").Select(vc => vc.Id).FirstOrDefault();
                    var carCatId2 = bgDb.VehicleCategories.Where(vc => vc.Slug == "auta-osobowe").Select(vc => vc.Id).FirstOrDefault();
                    if (inneCatId > 0 && carCatId2 > 0)
                    {
                        var carGroupNames = new[] {
                            "Bezpieczeństwo", "Komfort", "Multimedia i łączność", "Oświetlenie",
                            "Systemy wspomagania", "Dodatki i akcesoria", "Tapicerka i wnętrze", "Zewnętrzne"
                        };
                        var toFix = bgDb.FeatureCategories
                            .Where(fc => fc.VehicleCategoryId == inneCatId && carGroupNames.Contains(fc.Name))
                            .ToList();
                        if (toFix.Count > 0)
                        {
                            foreach (var fc in toFix) fc.VehicleCategoryId = carCatId2;
                            bgDb.SaveChanges();
                            bgLogger.LogWarning("[STARTUP-TRACE] Repaired {Count} FeatureCategory row(s) mis-parked under 'inne' back to auta-osobowe: {Names}",
                                toFix.Count, string.Join(", ", toFix.Select(fc => fc.Name)));
                        }
                    }
                }
                catch (Exception ex)
                {
                    bgLogger.LogError(ex, "[STARTUP-TRACE] FeatureCategory 'inne' mis-park repair failed: {Msg}", ex.Message);
                }

                // One-off cleanup: several adverts have raw scraped boilerplate pasted into their
                // free-text description - a "Wyposazenie: - <model> do <price> zł..." bullet block
                // (actually related-search links copied from the source listing site, not real
                // equipment - that's handled separately by the structured featureIds checkboxes)
                // followed by a "Zrodlo: <url>" attribution line. Both headers are spelled without
                // Polish diacritics ("Wyposazenie"/"Zrodlo" rather than "Wyposażenie"/"Źródło"), a
                // signature specific enough to this scraped junk that it's safe to truncate on -
                // genuine user-written Polish text doesn't drop diacritics from exactly these words.
                try
                {
                    var junkMarkers = new[] { "Zrodlo:", "Źródło:" };
                    var headerMarkers = new[] { "Wyposazenie:", "Wyposażenie:" };
                    var affectedAdverts = bgDb.CarAdverts
                        // Plain OR'd .Contains() calls rather than junkMarkers.Any(...) - the
                        // latter (a local array queried via nested lambda) isn't guaranteed to
                        // translate cleanly to SQL across EF Core versions.
                        .Where(a => a.Description.Contains("Zrodlo:") || a.Description.Contains("Źródło:"))
                        .ToList();

                    int cleaned = 0;
                    foreach (var ad in affectedAdverts)
                    {
                        var desc = ad.Description;
                        // Prefer truncating at the equipment-junk header if present (it's part of
                        // the same scraped block), otherwise fall back to the source-link marker.
                        var cutIdx = -1;
                        foreach (var h in headerMarkers)
                        {
                            var i = desc.IndexOf(h, StringComparison.Ordinal);
                            if (i > 0 && (cutIdx == -1 || i < cutIdx)) cutIdx = i;
                        }
                        if (cutIdx == -1)
                        {
                            foreach (var m in junkMarkers)
                            {
                                var i = desc.IndexOf(m, StringComparison.Ordinal);
                                if (i > 0 && (cutIdx == -1 || i < cutIdx)) cutIdx = i;
                            }
                        }
                        if (cutIdx <= 0) continue;

                        var trimmed = desc[..cutIdx].TrimEnd();
                        if (trimmed.Length > 0 && trimmed != desc)
                        {
                            ad.Description = trimmed;
                            cleaned++;
                        }
                    }

                    if (cleaned > 0)
                    {
                        bgDb.SaveChanges();
                        bgLogger.LogWarning("[STARTUP-TRACE] Cleaned scraped 'Wyposazenie:/Zrodlo:' boilerplate out of {Count} advert description(s)", cleaned);
                    }
                }
                catch (Exception ex)
                {
                    bgLogger.LogError(ex, "[STARTUP-TRACE] Advert description cleanup failed: {Msg}", ex.Message);
                }

                // Follow-up repair: the cleanup above could leave a dangling, incomplete final
                // sentence (e.g. "...Auto jest" with nothing after) when the real text ran
                // directly into the removed junk header with no sentence break in between.
                // There's no description-history table, so the exact missing words can't be
                // recovered - the best available fix is to drop the incomplete trailing
                // fragment so the description ends on a full sentence instead of mid-word.
                // Scoped only to the same scraped batch (identified by its distinctive
                // "Cena: X zł" / "Przebieg: X km" template lines - real user-written text
                // doesn't restate price/mileage that way, since both already have their own
                // form fields) so ordinary descriptions that simply lack a trailing period
                // aren't touched.
                try
                {
                    var scrapedBatch = bgDb.CarAdverts
                        .Where(a => a.Description.Contains("Cena:") && a.Description.Contains("Przebieg:"))
                        .ToList();

                    var enders = new[] { '.', '!', '?' };
                    int trimmedCount = 0;
                    foreach (var ad in scrapedBatch)
                    {
                        var desc = ad.Description.TrimEnd();
                        if (desc.Length == 0 || enders.Contains(desc[^1])) continue;

                        var lastEnderIdx = desc.LastIndexOfAny(enders);
                        if (lastEnderIdx <= 0) continue;

                        var cut = desc[..(lastEnderIdx + 1)].TrimEnd();
                        if (cut.Length > 0 && cut != ad.Description)
                        {
                            ad.Description = cut;
                            trimmedCount++;
                        }
                    }

                    if (trimmedCount > 0)
                    {
                        bgDb.SaveChanges();
                        bgLogger.LogWarning("[STARTUP-TRACE] Trimmed {Count} advert description(s) back to the last complete sentence (dangling fragment left by the scraped-boilerplate cleanup above)", trimmedCount);
                    }
                }
                catch (Exception ex)
                {
                    bgLogger.LogError(ex, "[STARTUP-TRACE] Dangling-sentence description trim failed: {Msg}", ex.Message);
                }

                // One-off repair: add-advert.vue's description builder had a bug where the
                // color-picker extra field ("Kolor nadwozia"/"Kolor") serialized the raw
                // CarColor id instead of its resolved name (e.g. "Kolor nadwozia: 19"), because
                // its generic tech-data loop only special-cased radio/select fields, not
                // color-picker. Fixed in the frontend going forward; repair already-saved
                // descriptions here using each advert's own (correctly-set) ColorId/CarColor.
                try
                {
                    var colorLineRegex = new System.Text.RegularExpressions.Regex(@"^(Kolor(?: nadwozia)?): (\d+)$", System.Text.RegularExpressions.RegexOptions.Multiline);
                    var affected = bgDb.CarAdverts
                        .Include(a => a.CarColor)
                        .Where(a => a.Description.Contains("Kolor nadwozia: ") || a.Description.Contains("Kolor: "))
                        .ToList();

                    int fixedColorLines = 0;
                    foreach (var ad in affected)
                    {
                        if (ad.CarColor == null) continue;
                        var replaced = colorLineRegex.Replace(ad.Description, m =>
                            m.Groups[2].Value == ad.ColorId.ToString()
                                ? $"{m.Groups[1].Value}: {ad.CarColor.Name}"
                                : m.Value);
                        if (replaced != ad.Description)
                        {
                            ad.Description = replaced;
                            fixedColorLines++;
                        }
                    }

                    if (fixedColorLines > 0)
                    {
                        bgDb.SaveChanges();
                        bgLogger.LogWarning("[STARTUP-TRACE] Fixed raw color-id leak in {Count} advert description(s)", fixedColorLines);
                    }
                }
                catch (Exception ex)
                {
                    bgLogger.LogError(ex, "[STARTUP-TRACE] Color-id description repair failed: {Msg}", ex.Message);
                }

                // Backfill CarAdvert.VehicleCategoryId where it's NULL (nullable column - reported
                // as "doesn't show up when filtering by category" even though the direct advert
                // link still works, since a category filter's WHERE VehicleCategoryId = X excludes
                // NULL rows entirely).
                //
                // Confirmed live via a prior run's AUDIT-CATEGORY dump: 224 adverts were still
                // null, and EVERY one was skipped by the single-category-brand rule below because
                // mainstream brands (Opel, Ford, BMW, Audi, Renault, VW, ...) are themselves
                // associated with multiple categories (they sell both passenger cars and vans) -
                // the brand-only rule alone left almost the entire marketplace uncategorized.
                // Manually reviewed that dump's actual titles: it's overwhelmingly regular
                // passenger cars (Q5, Superb, Golf, X1, A3, Astra, Fiesta, Meriva, Zafira, ...)
                // with a handful of genuine commercial vans (Opel Vivaro/Combo, Ford Transit,
                // Renault Trafic, ...). So: still fix unambiguous single-category brands first,
                // then for the rest fall back to the advert's own Model name (a far more specific
                // signal than the ambiguous brand) against a known van/commercial-model list, and
                // default anything left to auta-osobowe - a targeted call grounded in the actual
                // data, not a blind guess.
                try
                {
                    var uncategorized = bgDb.CarAdverts
                        .Include(a => a.Brand).ThenInclude(b => b.Categories)
                        .Include(a => a.Model)
                        .Where(a => a.VehicleCategoryId == null)
                        .ToList();

                    var carCatId = bgDb.VehicleCategories.Where(vc => vc.Slug == "auta-osobowe").Select(vc => vc.Id).FirstOrDefault();
                    var vanCatId = bgDb.VehicleCategories.Where(vc => vc.Slug == "dostawcze").Select(vc => vc.Id).FirstOrDefault();
                    var vanModelNames = new[] {
                        "vivaro", "combo", "transit", "trafic", "master", "kangoo", "berlingo", "partner",
                        "doblo", "caddy", "transporter", "crafter", "ducato", "boxer", "jumper", "movano",
                        "nv200", "nv300", "nv400", "sprinter", "expert", "jumpy", "scudo", "talento"
                    };

                    int fixedUnambiguous = 0, fixedByModel = 0;
                    var stillAmbiguous = new List<string>();
                    foreach (var ad in uncategorized)
                    {
                        if (ad.Brand == null)
                        {
                            stillAmbiguous.Add($"id={ad.Id} \"{ad.Title}\" (dangling BrandId={ad.BrandId})");
                            continue;
                        }
                        var cats = ad.Brand.Categories;
                        if (cats.Count == 1)
                        {
                            ad.VehicleCategoryId = cats.First().Id;
                            fixedUnambiguous++;
                            continue;
                        }

                        var modelName = (ad.Model?.Name ?? "").ToLowerInvariant();
                        if (vanModelNames.Any(v => modelName.Contains(v)) && vanCatId > 0)
                        {
                            ad.VehicleCategoryId = vanCatId;
                            fixedByModel++;
                        }
                        else if (carCatId > 0)
                        {
                            ad.VehicleCategoryId = carCatId;
                            fixedByModel++;
                        }
                        else
                        {
                            stillAmbiguous.Add($"id={ad.Id} \"{ad.Title}\" ({ad.Brand.Name} {ad.Model?.Name ?? "brak modelu"})");
                        }
                    }

                    if (fixedUnambiguous + fixedByModel > 0)
                    {
                        bgDb.SaveChanges();
                        bgLogger.LogWarning("[STARTUP-TRACE] Backfilled VehicleCategoryId for {Total} advert(s) with a null category ({Unambig} via single-category brand, {ByModel} via model-name fallback)",
                            fixedUnambiguous + fixedByModel, fixedUnambiguous, fixedByModel);
                    }
                    if (stillAmbiguous.Count > 0)
                    {
                        bgLogger.LogWarning("[STARTUP-TRACE] AUDIT-CATEGORY: {Count} advert(s) still have a null category and need manual review: {List}",
                            stillAmbiguous.Count, string.Join(" | ", stillAmbiguous));
                    }
                }
                catch (Exception ex)
                {
                    bgLogger.LogError(ex, "[STARTUP-TRACE] Advert category backfill failed: {Msg}", ex.Message);
                }

                // Starter engine-plausibility rules: a small, high-confidence allowlist for a
                // handful of petrol/hybrid-only exotic brands (fail-open — any other brand has no
                // restriction). Idempotent: only inserts a (brand, fuel type) pair that isn't
                // already present. Deliberately NOT an attempt at exhaustive coverage — see
                // BrandAllowedFuelType's doc comment.
                try
                {
                    var exoticBrandIds = bgDb.Brands
                        .Where(b => new[] { "ferrari", "lamborghini", "bugatti", "mclaren" }.Contains(b.Slug))
                        .Select(b => b.Id)
                        .ToList();
                    var petrolHybridFuelIds = bgDb.FuelTypes
                        .Where(f => new[] { "Benzyna", "Hybryda", "Hybryda mild", "Hybryda plug-in" }.Contains(f.Name))
                        .Select(f => f.Id)
                        .ToList();
                    if (exoticBrandIds.Count > 0 && petrolHybridFuelIds.Count > 0)
                    {
                        var existing = bgDb.BrandAllowedFuelTypes
                            .Where(x => exoticBrandIds.Contains(x.BrandId))
                            .Select(x => new { x.BrandId, x.FuelTypeId })
                            .ToList();
                        var toAdd = new List<BrandAllowedFuelType>();
                        foreach (var brandId in exoticBrandIds)
                            foreach (var fuelId in petrolHybridFuelIds)
                                if (!existing.Any(x => x.BrandId == brandId && x.FuelTypeId == fuelId))
                                    toAdd.Add(new BrandAllowedFuelType { BrandId = brandId, FuelTypeId = fuelId });
                        if (toAdd.Count > 0)
                        {
                            bgDb.BrandAllowedFuelTypes.AddRange(toAdd);
                            bgDb.SaveChanges();
                            bgLogger.LogWarning("[STARTUP-TRACE] Seeded {Count} starter BrandAllowedFuelType rows", toAdd.Count);
                        }
                    }
                }
                catch (Exception ex)
                {
                    bgLogger.LogError(ex, "[STARTUP-TRACE] Starter engine-plausibility seed failed: {Msg}", ex.Message);
                }

                // Audit: dump every FeatureCategory's scope (VehicleCategoryId/BrandId/ModelId) and
                // feature count, so equipment leaking into the wrong vehicle category (e.g. car
                // features showing on a motorcycle listing) can be spotted from the scope values
                // directly instead of clicking through every category in the form by hand.
                // VehicleCategoryId is required (see the migration hardening this above) — only
                // BrandId/ModelId can still be a deliberate "applies to every brand/model in the
                // category" wildcard (see GetFeatureCategoriesByContextAsync).
                try
                {
                    var vcatNames = bgDb.VehicleCategories.ToDictionary(c => c.Id, c => c.Slug);
                    var fcDump = bgDb.FeatureCategories.Include(fc => fc.Features)
                        .AsEnumerable()
                        .OrderBy(fc => fc.Name)
                        .Select(fc =>
                            $"{fc.Name} [vcat={vcatNames.GetValueOrDefault(fc.VehicleCategoryId, "?")}, " +
                            $"brand={(fc.BrandId?.ToString() ?? "ANY")}, model={(fc.ModelId?.ToString() ?? "ANY")}, features={fc.Features.Count}] (id={fc.Id})")
                        .ToList();
                    bgLogger.LogWarning("[STARTUP-TRACE] AUDIT-FEATURES: {Count} feature categories: {List}",
                        fcDump.Count, string.Join(" | ", fcDump));

                    var dupNames = bgDb.FeatureCategories.AsEnumerable()
                        .GroupBy(fc => fc.Name)
                        .Where(g => g.Count() > 1)
                        .Select(g => $"{g.Key} (x{g.Count()}, ids={string.Join(",", g.Select(fc => fc.Id))})")
                        .ToList();
                    if (dupNames.Any())
                        bgLogger.LogWarning("[STARTUP-TRACE] AUDIT-FEATURES: {Count} feature-category names appear more than once (possible scope conflict): {List}",
                            dupNames.Count, string.Join(" | ", dupNames));
                }
                catch (Exception ex)
                {
                    bgLogger.LogError(ex, "[STARTUP-TRACE] AUDIT-FEATURES failed: {Msg}", ex.Message);
                }
            });

            // Startup config diagnostics — read via the same IConfiguration section
            // BuildImojeFormData actually uses (Imoje__* env vars via ASP.NET Core's standard
            // double-underscore convention), not the old flat IMOJE_* names, which this used to
            // check and reported "EMPTY" even when the real config was correctly set.
            //
            // Fallback order and empty-string handling MUST match BuildImojeFormData/VerifySignature
            // in PaymentService.cs exactly (ServiceKey checked first, blank values treated as unset)
            // — a mismatched order here previously reported "EMPTY" for a genuinely-set ServiceKey
            // whenever a blank (but present) Imoje__ApiKey env var existed on the host, since `??`
            // only falls through on null, not on "".
            static string FirstNonEmptyCfg(params string?[] values) =>
                values.FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? "";
            var imojeSection = app.Configuration.GetSection("Imoje");
            var imojeMid    = imojeSection["MerchantId"] ?? "";
            var imojeKey    = FirstNonEmptyCfg(imojeSection["ServiceKey"], imojeSection["ApiKey"]);
            var imojeSecret = imojeSection["WebhookSecret"] ?? "";
            var internalSec = app.Configuration["InternalServiceSecret"] ?? Environment.GetEnvironmentVariable("INTERNAL_SERVICE_SECRET") ?? "";
            logger.LogInformation(
                "[Config] Imoje:MerchantId={HasMid} Imoje:ApiKey/ServiceKey={HasKey}(pfx={Pfx}) Imoje:WebhookSecret={HasWs} InternalServiceSecret={HasIs}",
                string.IsNullOrEmpty(imojeMid) ? "EMPTY" : "SET",
                string.IsNullOrEmpty(imojeKey) ? "EMPTY" : "SET",
                imojeKey.Length >= 6 ? imojeKey[..6] + "..." : "(short)",
                string.IsNullOrEmpty(imojeSecret) ? "EMPTY←WEBHOOKS BĘDĄ ODRZUCANE" : "SET",
                string.IsNullOrEmpty(internalSec) ? "EMPTY←WEBHOOKS BĘDĄ ODRZUCANE" : "SET");

            // KSeFService.SendInvoiceAsync silently no-ops (LogDebug, invisible in Railway's log
            // viewer) whenever this token is missing, so every monthly invoice generation would
            // quietly skip KSeF submission with no visible trace. Surface it at startup instead.
            var ksefToken = Environment.GetEnvironmentVariable("KSEF_TOKEN") ?? app.Configuration["KSeF:Token"] ?? "";
            logger.LogInformation(
                "[Config] KSeF:Token={HasToken} — {Note}",
                string.IsNullOrEmpty(ksefToken) ? "EMPTY" : "SET",
                string.IsNullOrEmpty(ksefToken) ? "brak tokena = faktury NIE są wysyłane do KSeF (IsKSeFSent zostanie false)" : "faktury z NIP nabywcy będą wysyłane do KSeF");
        }

        app.UseExceptionHandler(exApp =>
        {
            exApp.Run(async context =>
            {
                var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
                var exLogger = app.Services.GetRequiredService<ILogger<Program>>();
                if (feature?.Error != null)
                {
                    // Railway's compact log view only renders the message template text, not the
                    // structured exception argument passed to LogError - so the actual exception
                    // type/message/inner-exception was invisible without digging further into the
                    // UI. Bake it into the message itself so it's visible at a glance.
                    var ex = feature.Error;
                    var inner = ex.InnerException != null ? $" | inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}" : "";
                    exLogger.LogError(ex, "[GlobalExceptionHandler] Unhandled exception at {Path} -- {ExType}: {ExMessage}{Inner} -- {StackTop}",
                        context.Request.Path, ex.GetType().Name, ex.Message, inner,
                        ex.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains("cars_website_api") || l.Contains("CarsWebsite"))?.Trim() ?? ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim() ?? "");
                }
                context.Response.StatusCode = 500;
                var problemDetailsService = context.RequestServices.GetService<IProblemDetailsService>();
                if (problemDetailsService != null)
                {
                    await problemDetailsService.WriteAsync(new()
                    {
                        HttpContext = context,
                        ProblemDetails = new Microsoft.AspNetCore.Mvc.ProblemDetails
                        {
                            Status = 500,
                            Title = "Internal server error",
                            Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                        },
                    });
                }
                else
                {
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(new { message = "Internal server error" });
                }
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

        // This API has no routes at any of these paths — they're the well-known scanner
        // signature for exposed config/credential files and debug endpoints (config.js,
        // .aws/config, phpinfo.php, /_debugbar, wp-login.php, ...). Reject them here, before
        // static files, CORS, rate limiting or auth do any work, so scan traffic is as cheap
        // as possible to turn away and doesn't add noise to the normal request pipeline.
        var scannerProbePathPrefixes = new[]
        {
            "/config.js", "/aws.config.js", "/aws-config.js", "/aws.json", "/aws-credentials",
            "/.aws/", "/.env", "/debugbar", "/_debugbar", "/debug", "/info.php", "/phpinfo.php",
            "/test.php", "/wp-admin", "/wp-login.php", "/.git/", "/.svn/", "/xmlrpc.php",
        };
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? "";
            if (scannerProbePathPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                context.RequestServices.GetRequiredService<ILogger<Program>>()
                    .LogDebug("[ScannerProbe] Rejected probe request for {Path} from {IP}", path, context.Connection.RemoteIpAddress);
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            await next();
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
        app.UseResponseCompression();
        app.UseResponseCaching();
        app.UseStaticFiles();
        app.UseHttpsRedirection();
        app.UseCors("AllowNuxt");
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapHealthChecks("/health").AllowAnonymous();

        // Hangfire dashboard has no relationship to this API's own JWT-bearer auth (it's a
        // browser-navigated page, not an endpoint a Bearer token gets attached to) - gate it with
        // its own HTTP Basic Auth credentials instead. Fails closed: if the env vars are unset,
        // nobody gets in rather than leaving the dashboard open to anyone who finds the URL.
        app.UseHangfireDashboard("/hangfire", new DashboardOptions
        {
            Authorization = new[] { new HangfireDashboardAuthFilter(app.Configuration) },
        });

        var recurringJobs = app.Services.GetRequiredService<Hangfire.IRecurringJobManager>();
        recurringJobs.AddOrUpdate<BadgeExpiryJob>("badge-expiry", job => job.RunAsync(CancellationToken.None), Cron.Hourly());
        recurringJobs.AddOrUpdate<EventFeaturedExpiryJob>("event-featured-expiry", job => job.RunAsync(CancellationToken.None), Cron.Hourly());
        recurringJobs.AddOrUpdate<ExpiryReminderJob>("expiry-reminder", job => job.RunAsync(CancellationToken.None), Cron.Daily(8));
        recurringJobs.AddOrUpdate<SubscriptionExpiryJob>("subscription-expiry", job => job.RunAsync(CancellationToken.None), "0 */6 * * *");
        recurringJobs.AddOrUpdate<MonthlyInvoiceJob>("monthly-invoice", job => job.RunAsync(CancellationToken.None), Cron.Monthly(1, 2));
        recurringJobs.AddOrUpdate<DeletedUserPurgeJob>("deleted-user-purge", job => job.RunAsync(CancellationToken.None), Cron.Daily(3));
        recurringJobs.AddOrUpdate<SavedSearchAlertJob>("saved-search-alerts", job => job.RunAsync(CancellationToken.None), "0 */2 * * *");
        recurringJobs.AddOrUpdate<PartnerFeedSyncJob>("partner-feed-sync", job => job.RunAsync(CancellationToken.None), "0 */6 * * *");
        // Directory enrichment - both no-op unless configured (TRANSLATION_API_KEY / DIRECTORY_GEOCODE=1).
        recurringJobs.AddOrUpdate<DirectoryTranslationJob>("directory-translation", job => job.RunAsync(CancellationToken.None), "0 */4 * * *");
        recurringJobs.AddOrUpdate<DirectoryGeocodeJob>("directory-geocode", job => job.RunAsync(CancellationToken.None), "30 */3 * * *");

        // Email transport test — runs in background after startup so it appears in Railway logs
        _ = Task.Run(async () =>
        {
            await Task.Delay(3000); // wait for app to fully start
            var cfg = app.Services.GetRequiredService<IConfiguration>();
            var logger = app.Services.GetRequiredService<ILogger<Program>>();

            // Resend is preferred — if configured, SMTP is not used at all.
            var resendKey = (
                cfg["Resend:ApiKey"] ?? cfg["RESEND_API_KEY"]
                ?? Environment.GetEnvironmentVariable("Resend__ApiKey")
                ?? Environment.GetEnvironmentVariable("RESEND_API_KEY")
                ?? Environment.GetEnvironmentVariable("RESEND_APIKEY")
                ?? ""
            ).Trim();
            if (!string.IsNullOrEmpty(resendKey))
            {
                logger.LogInformation("[EMAIL-TEST] Resend API skonfigurowany — transport HTTP/443 aktywny ✓");
                return;
            }

            var rawHost = (cfg["Smtp:Host"] ?? "").Trim();
            if (string.IsNullOrEmpty(rawHost)) { logger.LogWarning("[SMTP-TEST] Brak RESEND_API_KEY i brak Smtp:Host — wysyłka e-mail wyłączona."); return; }
            var host = rawHost.Contains("://") ? rawHost.Split("://", 2)[1].TrimEnd('/') : rawHost;
            if (!host.StartsWith("[") && host.Contains(':')) host = host.Split(':')[0];
            var port = int.TryParse(cfg["Smtp:Port"], out var sp) ? sp : 587;
            var user = cfg["Smtp:User"] ?? "";
            var pass = cfg["Smtp:Password"] ?? "";
            logger.LogInformation("[SMTP-TEST] Testuję połączenie: {Host}:{Port} user={User} pass={Pass}",
                host, port, string.IsNullOrEmpty(user) ? "(brak)" : user, string.IsNullOrEmpty(pass) ? "(NIE USTAWIONE!)" : "***");
            try
            {
                using var client = new SmtpClient();
                client.Timeout = 10000; // 10 s — Railway blocks SMTP so fail fast
                await client.ConnectAsync(host, port, SecureSocketOptions.Auto);
                logger.LogInformation("[SMTP-TEST] Połączenie OK. Serwer: {ServerCaps}", client.Capabilities);
                if (!string.IsNullOrEmpty(user))
                {
                    await client.AuthenticateAsync(user, pass);
                    logger.LogInformation("[SMTP-TEST] Uwierzytelnienie OK dla {User}", user);
                }
                await client.DisconnectAsync(true);
                logger.LogInformation("[SMTP-TEST] SMTP działa poprawnie ✓");
            }
            catch (Exception ex)
            {
                logger.LogError("[SMTP-TEST] BŁĄD: {Type}: {Message}", ex.GetType().Name, ex.Message);
            }
        });

        app.Run();
    }

    // Merges duplicate Brand rows sharing the same Name (e.g. two "Krone" rows) into the
    // oldest one. Duplicates otherwise (a) show the brand twice in the add-listing dropdown
    // and (b) crash every seeder's startup ToDictionary(b => b.Name, ...) call, which blocks
    // the entire seeding chain — including unrelated fixes — from running at all.
    private static void MergeDuplicateBrands(AppDbContext db, ILogger logger)
    {
        logger.LogWarning("[STARTUP-TRACE] MergeDuplicateBrands entered");
        var duplicateGroups = db.Brands.Include(b => b.Categories).AsEnumerable()
            .GroupBy(b => b.Name)
            .Where(g => g.Count() > 1)
            .ToList();
        if (duplicateGroups.Count == 0)
        {
            logger.LogWarning("[STARTUP-TRACE] MergeDuplicateBrands: no duplicate brand names found, nothing to merge");
            return;
        }

        foreach (var group in duplicateGroups)
        {
            var ordered = group.OrderBy(b => b.Id).ToList();
            var canonical = ordered[0];
            var duplicates = ordered.Skip(1).ToList();

            foreach (var dup in duplicates)
            {
                // Go through EF's own tracked entities/table metadata instead of raw SQL table
                // names — this codebase's migration history has drifted from the live schema
                // (see the many ALTER TABLE guards above), so a hardcoded table name here would
                // just be guessing at casing that may not match what's actually deployed.
                foreach (var m in db.Models.Where(m => m.BrandId == dup.Id)) m.BrandId = canonical.Id;
                foreach (var a in db.CarAdverts.Where(a => a.BrandId == dup.Id)) a.BrandId = canonical.Id;
                foreach (var fc in db.FeatureCategories.Where(fc => fc.BrandId == dup.Id)) fc.BrandId = canonical.Id;
                // PartCompatibilities/BrandAllowedFuelTypes were added via raw CREATE TABLE IF NOT
                // EXISTS guards (Program.cs migration-bootstrap fallback) with no FK constraint, so
                // deleting `dup` below wouldn't fail even if these were left dangling — repoint them
                // explicitly instead of relying on referential integrity that isn't actually enforced.
                foreach (var pc in db.PartCompatibilities.Where(pc => pc.BrandId == dup.Id)) pc.BrandId = canonical.Id;
                foreach (var baf in db.BrandAllowedFuelTypes.Where(baf => baf.BrandId == dup.Id))
                {
                    if (db.BrandAllowedFuelTypes.Any(x => x.BrandId == canonical.Id && x.FuelTypeId == baf.FuelTypeId))
                        db.BrandAllowedFuelTypes.Remove(baf);
                    else
                        baf.BrandId = canonical.Id;
                }

                foreach (var cat in dup.Categories)
                    if (!canonical.Categories.Any(c => c.Id == cat.Id))
                        canonical.Categories.Add(cat);

                db.Brands.Remove(dup);
                logger.LogInformation(
                    "[Cleanup] Merged duplicate Brand '{Name}' (id={DupId}) into canonical id={CanonicalId}",
                    dup.Name, dup.Id, canonical.Id);
            }
        }

        db.SaveChanges();
    }

    private static void SeedDataIfEmpty(AppDbContext db, ILogger logger)
    {
        logger.LogWarning("[STARTUP-TRACE] SeedDataIfEmpty entered");
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
                    Name = "Bezpieczeństwo", VehicleCategoryId = carCatId,
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
                    Name = "Komfort", VehicleCategoryId = carCatId,
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
                    Name = "Multimedia", VehicleCategoryId = carCatId,
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
                    Name = "Oświetlenie", VehicleCategoryId = carCatId,
                    Features = new List<Feature> {
                        new Feature { Name = "Halogeny" }, new Feature { Name = "Xenon" }, new Feature { Name = "Bi-Xenon" },
                        new Feature { Name = "Full LED" }, new Feature { Name = "Matrix LED" }, new Feature { Name = "Światła adaptacyjne" },
                        new Feature { Name = "Światła do jazdy dziennej (DRL)" }, new Feature { Name = "Podświetlenie wnętrza" }
                    }
                },
                new FeatureCategory
                {
                    Name = "Systemy wspomagania", VehicleCategoryId = carCatId,
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
                    Name = "Nadwozie i wyposażenie zewnętrzne", VehicleCategoryId = carCatId,
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
                    Name = "Bezpieczeństwo", VehicleCategoryId = motoCatId,
                    Features = new List<Feature> {
                        new Feature { Name = "ABS" }, new Feature { Name = "Kontrola trakcji (TCS)" },
                        new Feature { Name = "Asystent ruszania pod górkę (HSA)" }, new Feature { Name = "Hamowanie kombinowane (CBS)" }
                    }
                },
                new FeatureCategory
                {
                    Name = "Komfort", VehicleCategoryId = motoCatId,
                    Features = new List<Feature> {
                        new Feature { Name = "Quickshifter" }, new Feature { Name = "Podgrzewane manetki" },
                        new Feature { Name = "Tempomat" }, new Feature { Name = "Elektrycznie regulowana szyba" },
                        new Feature { Name = "Elektryczna regulacja zawieszenia" }, new Feature { Name = "Podgrzewane siodełko" }
                    }
                },
                new FeatureCategory
                {
                    Name = "Bagaż i akcesoria", VehicleCategoryId = motoCatId,
                    Features = new List<Feature> {
                        new Feature { Name = "Kufry boczne (oryginalne)" }, new Feature { Name = "Centralny kufer (oryginalne)" },
                        new Feature { Name = "Tankbag" }, new Feature { Name = "Owiewki boczne" },
                        new Feature { Name = "Osłona silnika" }, new Feature { Name = "Uchwyty pasażera" },
                        new Feature { Name = "Podnożki pasażera" }
                    }
                },
                new FeatureCategory
                {
                    Name = "Wyposażenie techniczne", VehicleCategoryId = trailerCatId,
                    Features = new List<Feature> {
                        new Feature { Name = "Hamulec najazdowy" }, new Feature { Name = "Koło podporowe" },
                        new Feature { Name = "Podpory tylne" }, new Feature { Name = "Burtownica aluminiowa" },
                        new Feature { Name = "Plandeka" }, new Feature { Name = "Rampa załadowcza" },
                        new Feature { Name = "Oświetlenie LED" }, new Feature { Name = "Blokada kuli" }
                    }
                },
                new FeatureCategory
                {
                    Name = "Kabina i komfort", VehicleCategoryId = agriCatId,
                    Features = new List<Feature> {
                        new Feature { Name = "Klimatyzacja kabiny" }, new Feature { Name = "Zawieszenie kabiny" },
                        new Feature { Name = "Radio / Bluetooth" }, new Feature { Name = "Fotel z zawieszeniem pneumatycznym" }
                    }
                },
                new FeatureCategory
                {
                    Name = "Technologia i systemy", VehicleCategoryId = agriCatId,
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

        // Deduplicate models (same Brand + same Name) - the audit found 863 excess rows here
        // (54% of the table), caused by the heavy-equipment seeders re-inserting the same
        // brand+model repeatedly without checking for an existing row first (e.g. "Iveco Stralis"
        // existed as 14 identical rows). Re-point every child FK to the surviving (lowest-Id) row
        // before deleting the rest, so no CarAdvert/Generation/etc. is left dangling mid-repair.
        try
        {
            var modelKeys = db.Models.Select(m => new { m.Id, m.BrandId, m.Name }).ToList();
            var duplicateModelGroups = modelKeys
                .GroupBy(m => (m.BrandId, Name: (m.Name ?? "").Trim().ToLowerInvariant()))
                .Where(g => g.Count() > 1)
                .ToList();

            if (duplicateModelGroups.Any())
            {
                logger.LogWarning("Found {Count} duplicate model groups — deduplicating", duplicateModelGroups.Count);
                foreach (var group in duplicateModelGroups)
                {
                    var ids = group.Select(m => m.Id).OrderBy(id => id).ToList();
                    var keepId = ids.First();
                    var deleteIds = string.Join(",", ids.Skip(1));
                    db.Database.ExecuteSqlRaw($"UPDATE `generations` SET `ModelId` = {keepId} WHERE `ModelId` IN ({deleteIds})");
                    db.Database.ExecuteSqlRaw($"UPDATE `caradverts` SET `ModelId` = {keepId} WHERE `ModelId` IN ({deleteIds})");
                    db.Database.ExecuteSqlRaw($"UPDATE `featurecategories` SET `ModelId` = {keepId} WHERE `ModelId` IN ({deleteIds})");
                    db.Database.ExecuteSqlRaw($"UPDATE `partcompatibilities` SET `ModelId` = {keepId} WHERE `ModelId` IN ({deleteIds})");
                    db.Database.ExecuteSqlRaw($"UPDATE `attributedefinitions` SET `ModelId` = {keepId} WHERE `ModelId` IN ({deleteIds})");
                    db.Database.ExecuteSqlRaw($"DELETE FROM `models` WHERE `Id` IN ({deleteIds})");
                }
                logger.LogInformation("Model deduplication complete");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("Model deduplication skipped: {Message}", ex.Message);
        }

        // Deduplicate generations (same Model + same Name) - not part of the original audit
        // (Generations were clean at the time), but re-verifying before adding the unique
        // constraint below turned up 889 excess rows freshly accumulated from repeated local
        // restarts, confirming the same non-idempotent-seeder root cause reaches this table too.
        // Runs before the EngineVersion dedup below so engines under two now-merged duplicate
        // generations end up sharing one GenerationId and can be deduplicated against each other.
        try
        {
            var generationKeys = db.Generations.Select(g => new { g.Id, g.ModelId, g.Name }).ToList();
            var duplicateGenerationGroups = generationKeys
                .GroupBy(g => (g.ModelId, Name: (g.Name ?? "").Trim().ToLowerInvariant()))
                .Where(g => g.Count() > 1)
                .ToList();

            if (duplicateGenerationGroups.Any())
            {
                logger.LogWarning("Found {Count} duplicate generation groups — deduplicating", duplicateGenerationGroups.Count);
                foreach (var group in duplicateGenerationGroups)
                {
                    var ids = group.Select(g => g.Id).OrderBy(id => id).ToList();
                    var keepId = ids.First();
                    var deleteIds = string.Join(",", ids.Skip(1));
                    db.Database.ExecuteSqlRaw($"UPDATE `engineversions` SET `GenerationId` = {keepId} WHERE `GenerationId` IN ({deleteIds})");
                    db.Database.ExecuteSqlRaw($"UPDATE `trims` SET `GenerationId` = {keepId} WHERE `GenerationId` IN ({deleteIds})");
                    db.Database.ExecuteSqlRaw($"UPDATE `caradverts` SET `GenerationId` = {keepId} WHERE `GenerationId` IN ({deleteIds})");
                    db.Database.ExecuteSqlRaw($"UPDATE `attributedefinitions` SET `GenerationId` = {keepId} WHERE `GenerationId` IN ({deleteIds})");
                    db.Database.ExecuteSqlRaw($"DELETE FROM `generations` WHERE `Id` IN ({deleteIds})");
                }
                logger.LogInformation("Generation deduplication complete");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("Generation deduplication skipped: {Message}", ex.Message);
        }

        // Deduplicate engine versions (same Generation + same EngineName) - the audit found 576
        // excess rows here (12% of the table). Root cause: a two-pass seeding process where the
        // later "enrichment" pass (torque/CO2/euro norm/etc.) inserted a brand new row instead of
        // updating the existing one, leaving two near-identical rows per engine - one bare, one
        // enriched. Merge the enrichment fields onto the surviving row (in case the enriched data
        // landed on whichever row doesn't happen to survive) before deleting the duplicates.
        try
        {
            var allEngines = db.EngineVersions.ToList();
            var duplicateEngineGroups = allEngines
                .GroupBy(e => (e.GenerationId, Name: (e.EngineName ?? "").Trim().ToLowerInvariant()))
                .Where(g => g.Count() > 1)
                .ToList();

            if (duplicateEngineGroups.Any())
            {
                logger.LogWarning("Found {Count} duplicate engine-version groups — deduplicating", duplicateEngineGroups.Count);
                foreach (var group in duplicateEngineGroups)
                {
                    var rows = group.OrderBy(e => e.Id).ToList();
                    var canonical = rows.First();
                    foreach (var dup in rows.Skip(1))
                    {
                        canonical.TorqueNm ??= dup.TorqueNm;
                        canonical.Co2EmissionGkm ??= dup.Co2EmissionGkm;
                        canonical.EuroNorm ??= dup.EuroNorm;
                        canonical.FuelConsumptionCity ??= dup.FuelConsumptionCity;
                        canonical.FuelConsumptionHighway ??= dup.FuelConsumptionHighway;
                        canonical.FuelConsumptionCombined ??= dup.FuelConsumptionCombined;
                        canonical.AvgConsumptionL ??= dup.AvgConsumptionL;
                        canonical.Acceleration0100 ??= dup.Acceleration0100;
                        canonical.TopSpeedKmh ??= dup.TopSpeedKmh;
                        canonical.Cylinders ??= dup.Cylinders;
                        canonical.DriveType ??= dup.DriveType;
                        canonical.GearboxType ??= dup.GearboxType;
                        canonical.TrimId ??= dup.TrimId;
                    }
                    var deleteIds = string.Join(",", rows.Skip(1).Select(e => e.Id));
                    db.Database.ExecuteSqlRaw($"UPDATE `caradverts` SET `EngineVersionId` = {canonical.Id} WHERE `EngineVersionId` IN ({deleteIds})");
                    db.Database.ExecuteSqlRaw($"DELETE FROM `engineversions` WHERE `Id` IN ({deleteIds})");
                }
                db.SaveChanges();
                logger.LogInformation("EngineVersion deduplication complete");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("EngineVersion deduplication skipped: {Message}", ex.Message);
        }

        // Uniqueness constraints for the vehicle taxonomy chain (audit §2) - must run after the
        // dedup blocks above, since a duplicate-free table is a precondition for the constraint
        // to even be addable. Guarded/idempotent like every other schema change in this file,
        // since this codebase does not run `dotnet ef database update` in production.
        // Several of these Name columns were never given a [MaxLength] on their entity, so EF/
        // Pomelo mapped them to `longtext` instead of `varchar` - MySQL refuses to index a
        // BLOB/TEXT column without an explicit key-length prefix, hence the "(100)" on those.
        // Harmless for these tables (short lookup labels, never anywhere near 100 characters).
        foreach (var (table, columns, constraintName) in new[] {
            ("brands", "(`Name`)", "UQ_brands_Name"),
            ("models", "(`BrandId`, `Name`)", "UQ_models_BrandId_Name"),
            ("generations", "(`ModelId`, `Name`)", "UQ_generations_ModelId_Name"),
            ("engineversions", "(`GenerationId`, `EngineName`(100))", "UQ_engineversions_GenerationId_EngineName"),
            ("trims", "(`GenerationId`, `Name`(100))", "UQ_trims_GenerationId_Name"),
            ("fueltypes", "(`Name`(100))", "UQ_fueltypes_Name"),
            ("gearboxes", "(`Name`(100))", "UQ_gearboxes_Name"),
            ("drivetypes", "(`Name`(100))", "UQ_drivetypes_Name"),
            ("bodytypes", "(`Name`(100))", "UQ_bodytypes_Name"),
            ("carcolors", "(`Name`(100))", "UQ_carcolors_Name"),
            ("vehiclesubtypes", "(`VehicleCategoryId`, `Name`)", "UQ_vehiclesubtypes_CategoryId_Name"),
        })
        {
            try { db.Database.ExecuteSqlRaw($"ALTER TABLE `{table}` ADD CONSTRAINT `{constraintName}` UNIQUE {columns}"); }
            catch (Exception ex) { logger.LogDebug("[Schema] {Table} unique constraint skipped: {Message}", table, ex.Message); }
        }

        // Missing FK indexes from the architecture audit. Declared in AppDbContext's Fluent API
        // too, but (like everything else in this file) not relied on to actually create anything
        // on an already-existing database - EnsureCreated() only applies schema to a brand new
        // one, and this repo doesn't run real migrations in production.
        foreach (var (table, column, indexName) in new[] {
            ("caradverts", "GenerationId", "IX_caradverts_GenerationId"),
            ("caradverts", "EngineVersionId", "IX_caradverts_EngineVersionId"),
            ("caradverts", "DriveTypeId", "IX_caradverts_DriveTypeId"),
            ("caradverts", "ColorId", "IX_caradverts_ColorId"),
            ("caradverts", "TrimId", "IX_caradverts_TrimId"),
            ("adverts", "CountryId", "IX_adverts_CountryId"),
            ("adverts", "RegionId", "IX_adverts_RegionId"),
            ("adverts", "CityId", "IX_adverts_CityId"),
            ("adverts", "CurrencyId", "IX_adverts_CurrencyId"),
        })
        {
            try { db.Database.ExecuteSqlRaw($"CREATE INDEX `{indexName}` ON `{table}` (`{column}`)"); }
            catch (Exception ex) { logger.LogDebug("[Schema] {Table}.{Column} index skipped: {Message}", table, column, ex.Message); }
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

        // Additional VehicleSubtype seeds — per-slug idempotent, safe when subtypes already exist
        {
            var allVCatsForSubtypes2 = db.VehicleCategories.ToList();
            int CatId2(string slug) => allVCatsForSubtypes2.FirstOrDefault(c => c.Slug == slug)?.Id ?? 0;

            var existingSubtypeSlugs = db.VehicleSubtypes.Select(s => s.Slug).ToHashSet();

            var newSubtypeDefs = new List<(string catSlug, string name, string slug)>
            {
                // przyczepy
                ("przyczepy", "Naczepa kurtynowa",        "naczepa-kurtynowa"),
                ("przyczepy", "Naczepa wywrotka",          "naczepa-wywrotka"),
                ("przyczepy", "Naczepa cysterna",          "naczepa-cysterna"),
                ("przyczepy", "Naczepa izoterma",          "naczepa-izoterma"),
                ("przyczepy", "Naczepa silos",             "naczepa-silos"),
                ("przyczepy", "Naczepa kontener",          "naczepa-kontener"),
                ("przyczepy", "Naczepa niskopodwoziowa",   "naczepa-niskopodwoziowa"),
                ("przyczepy", "Naczepa autotransporter",   "naczepa-autotransporter"),
                ("przyczepy", "Naczepa dłużyca",           "naczepa-dluzica"),
                ("przyczepy", "Przyczepa laweta",          "przyczepa-laweta-aut"),
                ("przyczepy", "Przyczepa laweta moto",     "przyczepa-laweta-moto"),
                ("przyczepy", "Przyczepa platforma",       "przyczepa-platforma"),
                ("przyczepy", "Przyczepa niskopodwoziowa", "przyczepa-niskopodwoziowa"),
                ("przyczepy", "Przyczepa wywrotka",        "przyczepa-wywrotka"),
                ("przyczepy", "Przyczepa cysterna",        "przyczepa-cysterna"),
                ("przyczepy", "Przyczepa gastronomiczna",  "przyczepa-gastronomiczna"),
                ("przyczepy", "Przyczepa do koni",         "przyczepa-konie"),
                ("przyczepy", "Przyczepa do maszyn",       "przyczepa-maszyny"),
                ("przyczepy", "Dłużyca",                   "dluzica"),
                ("przyczepy", "Silos",                     "silos"),
                ("przyczepy", "Kontener morski",           "kontener-morski"),

                // budowlane
                ("budowlane", "Koparko-ładowarka",  "koparko-ladowarka"),
                ("budowlane", "Równiarka",           "rowniarka"),
                ("budowlane", "Wozidło",             "wozidlo"),
                ("budowlane", "Kruszarka",           "kruszarka"),
                ("budowlane", "Przesiewacz",         "przesiewacz"),
                ("budowlane", "Dźwig samojezdny",    "dzwig"),
                ("budowlane", "Podnośnik koszowy",   "podnosnik-koszowy"),
                ("budowlane", "Betoniarka",          "betoniarka"),
                ("budowlane", "Pompa do betonu",     "pompa-betonu"),
                ("budowlane", "Zagęszczarka",        "zagestczarka"),

                // rolnicze
                ("rolnicze", "Brona",                  "brona"),
                ("rolnicze", "Kosiarka",               "kosiarka"),
                ("rolnicze", "Rozrzutnik",             "rozrzutnik"),
                ("rolnicze", "Wóz paszowy",            "woz-paszowy"),
                ("rolnicze", "Agregat uprawowy",       "agregat-uprawowy"),
                ("rolnicze", "Ładowarka teleskopowa",  "ladowarka-teleskopowa"),
                ("rolnicze", "Sadzarka",               "sadzarka"),
                ("rolnicze", "Żniwiarka",              "zniwiarka"),

                // maszyny
                ("maszyny", "Wózek elektryczny", "wozek-elektryczny"),
                ("maszyny", "Reach truck",        "reach-truck"),
                ("maszyny", "Układnica",          "ukladnica"),
                ("maszyny", "Taśmociąg",          "tasmociag"),
                ("maszyny", "Suwnica",            "suwnica"),
                ("maszyny", "Wciągnik",           "wciagnik"),

                // motocykle
                ("motocykle", "Maxi skuter",           "maxi-skuter"),
                ("motocykle", "Motocykl elektryczny",  "motocykl-elektryczny"),
                ("motocykle", "Klasyczny",             "klasyczny"),
                ("motocykle", "Adventure",             "adventure"),
                ("motocykle", "Custom",                "custom"),
                ("motocykle", "Trójkołowiec",          "trojkolowiec"),
            };

            var newSubtypes2 = new List<VehicleSubtype>();
            var catOrderCounters = new Dictionary<int, int>();

            foreach (var (catSlug, name, slug) in newSubtypeDefs)
            {
                if (existingSubtypeSlugs.Contains(slug)) continue;
                int catId = CatId2(catSlug);
                if (catId == 0) continue;
                if (!catOrderCounters.ContainsKey(catId))
                    catOrderCounters[catId] = db.VehicleSubtypes.Where(s => s.VehicleCategoryId == catId).Select(s => (int?)s.SortOrder).Max() ?? 0 + 1;
                newSubtypes2.Add(new VehicleSubtype { VehicleCategoryId = catId, Name = name, Slug = slug, SortOrder = catOrderCounters[catId]++ });
                existingSubtypeSlugs.Add(slug);
            }

            if (newSubtypes2.Count > 0)
            {
                db.VehicleSubtypes.AddRange(newSubtypes2);
                db.SaveChanges();
                logger.LogInformation("Seeded {Count} additional vehicle subtypes", newSubtypes2.Count);
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
                // "Koła i opony" świadomie usunięte z części - Opony i Felgi to teraz osobne kategorie
                // najwyższego poziomu z własnymi formularzami (AttributeDefinition), nie podkategorie części.
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

        logger.LogWarning("[STARTUP-TRACE] Calling ModelSeeder.SeedModelsGenerationsEngines");
        ModelSeeder.SeedModelsGenerationsEngines(db, logger);
        logger.LogWarning("[STARTUP-TRACE] Calling VehicleDataSeeder.SeedVehicleData");
        VehicleDataSeeder.SeedVehicleData(db, logger);
        logger.LogWarning("[STARTUP-TRACE] Calling TrimSeeder.SeedTrims");
        TrimSeeder.SeedTrims(db, logger);
        logger.LogWarning("[STARTUP-TRACE] Calling VehicleDataSeeder.SeedTrimData");
        VehicleDataSeeder.SeedTrimData(db, logger);
        logger.LogWarning("[STARTUP-TRACE] Calling VehicleDataSeeder.SeedMotorcycleData");
        VehicleDataSeeder.SeedMotorcycleData(db, logger);
        logger.LogWarning("[STARTUP-TRACE] Calling ComprehensiveSeeder.SeedComprehensiveData");
        ComprehensiveSeeder.SeedComprehensiveData(db, logger);
        logger.LogWarning("[STARTUP-TRACE] Calling ExternalTaxonomySeeder.Seed");
        ExternalTaxonomySeeder.Seed(db, logger);
        logger.LogWarning("[STARTUP-TRACE] Calling BrandMetadataSeeder.Seed");
        BrandMetadataSeeder.Seed(db, logger);
        logger.LogWarning("[STARTUP-TRACE] Calling AttributeDefinitionMigrationSeeder.Seed");
        AttributeDefinitionMigrationSeeder.Seed(db, logger);
        logger.LogWarning("[STARTUP-TRACE] Calling AdvertDocumentBackfillSeeder.Seed");
        AdvertDocumentBackfillSeeder.Seed(db, logger);
        logger.LogWarning("[STARTUP-TRACE] Calling DirectoryBackfillSeeder.Seed");
        DirectoryBackfillSeeder.Seed(db, logger);
        logger.LogWarning("[STARTUP-TRACE] Calling VehicleEquipmentSeeder.Seed");
        VehicleEquipmentSeeder.Seed(db, logger);
        logger.LogWarning("[STARTUP-TRACE] Calling GeoSeeder.Seed");
        GeoSeeder.Seed(db, logger);

        // Backfill existing adverts onto the new global reference columns (Etap 3). Runs after
        // GeoSeeder so currencies/countries/languages/rates exist. All idempotent (only fills NULLs).
        logger.LogWarning("[STARTUP-TRACE] Backfilling Adverts global columns");
        foreach (var sql in new[] {
            "UPDATE `adverts` SET `CurrencyId` = (SELECT `Id` FROM `currencies` WHERE `Iso` = COALESCE(`Currency`, 'PLN') LIMIT 1) WHERE `CurrencyId` IS NULL",
            "UPDATE `adverts` SET `CountryId` = (SELECT `Id` FROM `countries` WHERE `Iso2` = 'PL' LIMIT 1) WHERE `CountryId` IS NULL",
            "UPDATE `adverts` SET `SourceLanguageId` = (SELECT `Id` FROM `languages` WHERE `Iso1` = 'pl' LIMIT 1) WHERE `SourceLanguageId` IS NULL",
            @"UPDATE `adverts` a JOIN `exchangerates` e ON e.`CurrencyId` = a.`CurrencyId`
              SET a.`PriceEur` = ROUND(a.`Price` * e.`RateToEur`, 2), a.`PriceEurAsOf` = e.`AsOf`
              WHERE a.`PriceEur` IS NULL AND a.`CurrencyId` IS NOT NULL" })
        { try { db.Database.ExecuteSqlRaw(sql); } catch (Exception ex) { logger.LogDebug("[Backfill] Adverts: {Msg}", ex.Message); } }

        logger.LogWarning("[STARTUP-TRACE] SeedDataIfEmpty: all seeders completed");
    }
}

public class DatabaseHealthCheck : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    private readonly IServiceScopeFactory _scopeFactory;

    public DatabaseHealthCheck(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("Database unreachable", ex);
        }
    }
}
