using System.Globalization;
using System.IO.Compression;
using api.DbContext;
using api.Middleware;
using api.Repository;
using api.Services;
using api.Services.IServices;
using dotenv.net;
using FluentValidation;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.IdentityModel.Logging;
using Serilog;
using Serilog.Events;

namespace api;

public static class Program
{
	public static void Main(string[] args)
	{
		CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
		CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

		IdentityModelEventSource.ShowPII = false;

		Configure.AddUkrainianLanguageSupport();

		DotEnv.Load(new DotEnvOptions(false, trimValues: true));

		// Serilog: structured logging to console + file
		Log.Logger = new LoggerConfiguration()
			.MinimumLevel.Information()
			.MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
			.MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
			.Enrich.FromLogContext()
			.WriteTo.Console(outputTemplate:
				"[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
			.WriteTo.File("logs/api-.log",
				rollingInterval: RollingInterval.Day,
				retainedFileCountLimit: 30,
				outputTemplate:
				"{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
			.CreateLogger();

		var builder = WebApplication.CreateBuilder(args);

		Configure.ValidateProductionSecrets(builder);

		builder.Host.UseSerilog();

		builder.Configuration.AddJsonFile("appsettings.json", true, true);

		builder.Services.AddValidatorsFromAssemblies(AppDomain.CurrentDomain.GetAssemblies());

		Configure.AddIfDevelopmentSuppressModelStateInvalidFilter(builder);

		Configure.ConfigControllers<MyDbContext>(builder, "CONNECTION_STRING");
		Configure.ConfigureNewtonJson();
		
		Configure.AddCors(builder);

		Configure.AddCompression(builder, CompressionLevel.Optimal);

		Configure.AddSwagger(builder);

		Configure.AddAuthenticationAndAuthorisation(builder);
		
		builder.Services.Configure<FormOptions>(options => { options.MultipartBodyLengthLimit = 10000000; });
		builder.Services.AddHttpClient();
		builder.Services.AddHealthChecks();
		Configure.AddForwardedHeaders(builder);
		Configure.AddFileStorage(builder);
		builder.Services.AddScoped<IAuthService, AuthService>();
		builder.Services.AddScoped<IUserService, UserService>();

		builder.Services.AddAutoMapper(cfg =>
		{
			/* configuration */
		}, AppDomain.CurrentDomain.GetAssemblies());

		builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

		var app = builder.Build();

		Configure.CreateDbIfNotExists(app);

		app.UseMiddleware<GlobalExceptionHandler>();
		app.UseSerilogRequestLogging();

		Configure.IfIsDevelopmentUseSwaggerElseHsts(app);

		if (app.Environment.IsDevelopment()) IdentityModelEventSource.ShowPII = true;

		// MUST run before any middleware that inspects scheme or RemoteIpAddress
		if (!app.Environment.IsDevelopment())
			app.UseForwardedHeaders();

		app.UseHttpsRedirection();

		if (!app.Environment.IsDevelopment())
			app.UseMiddleware<SecurityHeadersMiddleware>();

		app.UseRouting();
		app.UseCors("DefaultCors");

		app.UseAuthentication();
		app.UseAuthorization();

		app.MapControllers();
		app.MapHealthChecks("/health/live");

		app.Run();
	}
}