using api.Utils;
using dotenv.net;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

    internal static void CreateRootDirectoryIfNotExists()
    {
        var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), StaticDetails.UserProfileImagePath);
        if (!Directory.Exists(uploadsPath))
        {
            Directory.CreateDirectory(uploadsPath);
            Console.WriteLine($"Created directory: {uploadsPath}");
        }
    }

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
            logging.LoggingFields = HttpLoggingFields.Request | HttpLoggingFields.ResponseHeaders;
            logging.MediaTypeOptions.AddText("application/javascript");
            logging.RequestBodyLogLimit = 4096;
            logging.ResponseBodyLogLimit = 4096;
            logging.CombineLogs = true;
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
        // Add services to the container.
        builder.Services.AddControllers(
                //options => { options.Filters.Add<SanitizeInputFilter>();}
            )
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

			builder.Services.AddNpgsql<TContext>(connectionString);
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
	    var created = db.Database.EnsureCreated();
	    logger.LogInformation(created
		    ? "Database created and schema applied"
		    : "Database already exists, schema unchanged");
    }
}