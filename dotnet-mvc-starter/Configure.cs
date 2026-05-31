using System.Net;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using api.Services.Storage;
using api.Utils;
using dotenv.net;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using IPNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Swashbuckle.AspNetCore.Filters;
using System.IO.Compression;
using System.Text;
using api.DbContext;
using api.Identity;

namespace api;

internal static class Configure
{
    private static readonly IDictionary<string, string> Env = DotEnv.Read();

    internal static void AddUkrainianLanguageSupport()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Console.OutputEncoding = Encoding.GetEncoding(1251);
        Console.InputEncoding = Encoding.GetEncoding(1251);
    }

    internal static void AddForwardedHeaders(WebApplicationBuilder builder)
    {
        if (builder.Environment.IsDevelopment()) return;

        builder.Services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                               | ForwardedHeaders.XForwardedProto;

            // Reject spoofed headers from outside trusted proxies
            o.KnownNetworks.Clear();
            o.KnownProxies.Clear();

            // Trust Docker bridge network (matches docker-compose subnet 172.20.0.0/16)
            o.KnownNetworks.Add(new IPNetwork(IPAddress.Parse("172.20.0.0"), 16));

            // Extra CIDRs from env: TRUSTED_PROXY_CIDRS="10.0.0.0/8,192.168.0.0/16"
            var raw = TryEnv("TRUSTED_PROXY_CIDRS");
            if (!string.IsNullOrWhiteSpace(raw))
            {
                foreach (var cidr in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var parts = cidr.Split('/');
                    if (parts.Length == 2 && IPAddress.TryParse(parts[0], out var addr) && int.TryParse(parts[1], out var prefix))
                        o.KnownNetworks.Add(new IPNetwork(addr, prefix));
                }
            }

            o.ForwardLimit = 2;
        });
    }

    internal static void ValidateProductionSecrets(WebApplicationBuilder builder)
    {
        if (!builder.Environment.IsProduction()) return;

        var key = TryEnv("JWT_PRIVATE_KEY") ?? "";
        if (key.Length < 32)
            throw new InvalidOperationException(
                "JWT_PRIVATE_KEY must be at least 32 characters in Production.");
        if (key.Contains("please_change_me", StringComparison.OrdinalIgnoreCase)
            || key.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "JWT_PRIVATE_KEY is set to the default placeholder. Replace it before deploying to Production.");

        if (string.IsNullOrWhiteSpace(TryEnv("JWT_AUDIENCE")) || string.IsNullOrWhiteSpace(TryEnv("JWT_ISSUER")))
            throw new InvalidOperationException("JWT_AUDIENCE and JWT_ISSUER are required in Production.");
    }

    internal static void AddCors(WebApplicationBuilder builder)
    {
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("DefaultCors", policy =>
            {
                if (builder.Environment.IsDevelopment())
                {
                    policy.AllowAnyHeader()
                        .AllowAnyMethod()
                        .SetIsOriginAllowed(_ => true)
                        .AllowCredentials();
                    return;
                }

                var rawOrigins = TryEnv("ALLOWED_ORIGINS");
                if (string.IsNullOrWhiteSpace(rawOrigins))
                    throw new InvalidOperationException(
                        "ALLOWED_ORIGINS is required in non-Development environments. Set comma-separated origins in .env.");

                var origins = rawOrigins
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToArray();

                policy.WithOrigins(origins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });
    }

    internal static void AddFileStorage(WebApplicationBuilder builder)
    {
        var provider = (TryEnv("STORAGE_PROVIDER") ?? "local").Trim().ToLowerInvariant();

        if (provider == "s3")
        {
            var endpoint = TryEnv("S3_ENDPOINT");
            var bucket = TryEnv("S3_BUCKET")
                         ?? throw new InvalidOperationException("S3_BUCKET is required when STORAGE_PROVIDER=s3.");
            var accessKey = TryEnv("S3_ACCESS_KEY")
                            ?? throw new InvalidOperationException("S3_ACCESS_KEY is required when STORAGE_PROVIDER=s3.");
            var secretKey = TryEnv("S3_SECRET_KEY")
                            ?? throw new InvalidOperationException("S3_SECRET_KEY is required when STORAGE_PROVIDER=s3.");
            var region = TryEnv("S3_REGION") ?? "us-east-1";
            var forcePathStyle = string.Equals(TryEnv("S3_FORCE_PATH_STYLE"), "true", StringComparison.OrdinalIgnoreCase);

            var s3Config = new AmazonS3Config
            {
                ForcePathStyle = forcePathStyle
            };
            if (!string.IsNullOrWhiteSpace(endpoint))
                s3Config.ServiceURL = endpoint;
            else
                s3Config.RegionEndpoint = RegionEndpoint.GetBySystemName(region);

            var creds = new BasicAWSCredentials(accessKey, secretKey);
            var client = new AmazonS3Client(creds, s3Config);
            builder.Services.AddSingleton<IAmazonS3>(client);
            builder.Services.AddSingleton<IFileStorage>(_ => new S3FileStorage(client, bucket));
        }
        else
        {
            var root = TryEnv("LOCAL_STORAGE_ROOT");
            if (string.IsNullOrWhiteSpace(root))
                root = Path.Combine(Directory.GetCurrentDirectory(), "uploads");

            builder.Services.AddSingleton<IFileStorage>(_ => new LocalFileStorage(root));
        }
    }

    private static string? TryEnv(string key) => Env.TryGetValue(key, out var v) ? v : null;

    internal static void AddLogs(WebApplicationBuilder builder)
    {
        builder.Services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Information);
            builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Debug);
            builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
            builder.Logging.AddFilter("Default", LogLevel.Warning);
        });
        builder.Services.AddHttpLogging(logging =>
        {
            logging.LoggingFields = HttpLoggingFields.RequestPath
                                    | HttpLoggingFields.RequestMethod
                                    | HttpLoggingFields.RequestQuery
                                    | HttpLoggingFields.ResponseStatusCode;
        });
    }

    internal static void ConfigureNewtonJson()
    {
        JsonConvert.DefaultSettings = () =>
        {
            JsonSerializerSettings settings = new()
            {
                MaxDepth = 16
            };
            return settings;
        };
    }

    internal static void ConfigControllers<TContext>(WebApplicationBuilder builder, string connectionStringName)
        where TContext : Microsoft.EntityFrameworkCore.DbContext
    {
        builder.Services.AddControllers()
            .AddNewtonsoftJson(options =>
            {
                options.SerializerSettings.ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new CamelCaseNamingStrategy
                    {
                        ProcessDictionaryKeys = true,
                        OverrideSpecifiedNames = false
                    }
                };


                options.SerializerSettings.Formatting = Newtonsoft.Json.Formatting.Indented;
                options.SerializerSettings.DateFormatString = "yyyy-MM-dd";
                options.SerializerSettings.NullValueHandling = NullValueHandling.Include;
                options.SerializerSettings.MissingMemberHandling = MissingMemberHandling.Ignore;
                options.SerializerSettings.TypeNameHandling = TypeNameHandling.Auto;
                options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
                options.SerializerSettings.MetadataPropertyHandling = MetadataPropertyHandling.Ignore;

                options.SerializerSettings.Converters.Add(new StringEnumConverter(new CamelCaseNamingStrategy()));
                options.SerializerSettings.Converters.Add(new DateOnlyConverter());
            });

            var connectionString =
				builder.Configuration.GetConnectionString(connectionStringName)
				?? builder.Configuration[connectionStringName]
				?? (Env.TryGetValue(connectionStringName, out var v) ? v : null);

			if (string.IsNullOrWhiteSpace(connectionString))
			{
				throw new InvalidOperationException(
					$"Missing required configuration key '{connectionStringName}'. " +
					"Set it as an environment variable or in appsettings.");
			}

			builder.Services.AddDbContext<TContext>(opts =>
				opts.UseSqlite(connectionString)
					.UseLazyLoadingProxies());
    }

    internal static void AddIfDevelopmentSuppressModelStateInvalidFilter(WebApplicationBuilder builder)
    {
        //Uncomment for api models problems
        //https://mirsaeedi.medium.com/asp-net-core-customize-validation-error-message-9022c12d3d7d
        if (builder.Environment.IsDevelopment())
            builder.Services.Configure<ApiBehaviorOptions>(apiBehaviorOptions =>
            {
                apiBehaviorOptions.SuppressModelStateInvalidFilter = true;
            });
    }

    internal static void AddCompression(WebApplicationBuilder builder, CompressionLevel compressionLevel)
    {
        builder.Services.AddResponseCompression(options =>
        {
            options.Providers.Add<GzipCompressionProvider>();
            options.EnableForHttps = true;
        });

        builder.Services.Configure<GzipCompressionProviderOptions>(options => { options.Level = compressionLevel; });
    }

    internal static void AddSwagger(WebApplicationBuilder builder)
    {
        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer abcdef12345\"",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "Bearer",
                BearerFormat = "JWT"
            });
            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("bearer", document)] = []
            });
            options.OperationFilter<SecurityRequirementsOperationFilter>();
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Version = "v1",
                Title = " API",
                Description = "An ASP.NET Core Web API for UActive wep-app"
            });
            //const string xmlFilename = @"swagger_docs.xml";
            //try
            //{
            //    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFilename));
            //}
            //catch (XmlException)
            //{
            //    File.Create(Path.Combine(AppContext.BaseDirectory, xmlFilename));
            //}
            //var xmlFilename = $"{typeof(Program).Assembly.GetName().Name}.xml";
            //options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFilename));
            var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);

            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath);
            }

        });
    }

    internal static void AddAuthenticationAndAuthorisation(WebApplicationBuilder builder)
    {
        builder.Services.AddAuthentication(x =>
        {
            x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            x.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
        }).AddJwtBearer(x =>
        {
            x.TokenValidationParameters = new TokenValidationParameters
            {
                IssuerSigningKey = JwtHandler.GetPrivateKey(),
                ValidIssuer = JwtHandler.GetIssuer(),
                ValidAudience = JwtHandler.GetAudience(),
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.Zero
            };
            x.RequireHttpsMetadata = true;
            x.SaveToken = true;
        });

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(IdentityData.PolicyAdmin, p =>
                p.RequireRole(IdentityData.ClaimAdmin.ToString(), IdentityData.ClaimAdmin.ToString()));
            options.AddPolicy(IdentityData.PolicyModerator, p =>
                p.RequireRole(IdentityData.ClaimModerator.ToString(), IdentityData.ClaimAdmin.ToString()));
            options.AddPolicy(IdentityData.PolicyUser, p =>
                p.RequireRole(IdentityData.ClaimUser.ToString(), IdentityData.ClaimModerator.ToString(), IdentityData.ClaimAdmin.ToString()));
        });
    }

    internal static void IfIsDevelopmentUseSwaggerElseHsts(WebApplication app)
    {
        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "v1");
                options.RoutePrefix = string.Empty;
            });
        }
        else
        {
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }
    }

    internal static void CreateDbIfNotExists(WebApplication app)
    {
	    using var scope = app.Services.CreateScope();
	    var logger = scope.ServiceProvider.GetRequiredService<ILogger<MyDbContext>>();
	    var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();

	    logger.LogInformation("Database check: connecting to {Provider}...", db.Database.ProviderName);

	    if (app.Environment.IsProduction())
	    {
		    db.Database.Migrate();
		    logger.LogInformation("Production: migrations applied (if any pending)");
	    }
	    else
	    {
		    var created = db.Database.EnsureCreated();
		    logger.LogInformation(created
			    ? "Dev: database created and schema applied"
			    : "Dev: database already exists, schema unchanged");
	    }
    }
}