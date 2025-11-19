using CFLookup;
using Microsoft.AspNetCore.DataProtection;
#if !DEBUG
using CFLookup.Jobs;
using Hangfire;
using Hangfire.Storage;
using Hangfire.Dashboard.BasicAuthorization;
using Hangfire.Redis.StackExchange;
#endif
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Data.SqlClient;
using Npgsql;
using StackExchange.Redis;
using System.IO.Compression;

public class Program
{
    public static IServiceProvider ServiceProvider { get; set; }

    private static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
        });

        builder.Services.AddResponseCaching();

        builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
        {
            options.Level = CompressionLevel.Fastest;
        });

        builder.Services.Configure<GzipCompressionProviderOptions>(options =>
        {
            options.Level = CompressionLevel.SmallestSize;
        });

        builder.Services.AddHttpClient();

        // Add services to the container.
        builder.Services.AddRazorPages();

        builder.Services.AddControllers();

        var cfApiKey = string.Empty;
        var redisServer = string.Empty;
        var dbConnectionString = string.Empty;

        var hangfireUser = string.Empty;
        var hangfirePassword = string.Empty;
        
        var pgsqlConnString = string.Empty;

        if (OperatingSystem.IsWindows())
        {
            cfApiKey = Environment.GetEnvironmentVariable("CFAPI_Key", EnvironmentVariableTarget.Machine) ??
                Environment.GetEnvironmentVariable("CFAPI_Key", EnvironmentVariableTarget.User) ??
                Environment.GetEnvironmentVariable("CFAPI_Key", EnvironmentVariableTarget.Process) ??
                string.Empty;

            redisServer = Environment.GetEnvironmentVariable("RedisServer", EnvironmentVariableTarget.Machine) ??
                Environment.GetEnvironmentVariable("RedisServer", EnvironmentVariableTarget.User) ??
                Environment.GetEnvironmentVariable("RedisServer", EnvironmentVariableTarget.Process) ??
                "127.0.0.1:6379";

            dbConnectionString = Environment.GetEnvironmentVariable("CFLOOKUP_SQL", EnvironmentVariableTarget.Machine) ??
                Environment.GetEnvironmentVariable("CFLOOKUP_SQL", EnvironmentVariableTarget.User) ??
                Environment.GetEnvironmentVariable("CFLOOKUP_SQL", EnvironmentVariableTarget.Process) ??
                string.Empty;

            hangfireUser = Environment.GetEnvironmentVariable("CFLOOKUP_HangfireUser", EnvironmentVariableTarget.Machine) ??
                Environment.GetEnvironmentVariable("CFLOOKUP_HangfireUser", EnvironmentVariableTarget.User) ??
                Environment.GetEnvironmentVariable("CFLOOKUP_HangfireUser", EnvironmentVariableTarget.Process) ??
                string.Empty;

            hangfirePassword = Environment.GetEnvironmentVariable("CFLOOKUP_HangfirePassword", EnvironmentVariableTarget.Machine) ??
                Environment.GetEnvironmentVariable("CFLOOKUP_HangfirePassword", EnvironmentVariableTarget.User) ??
                Environment.GetEnvironmentVariable("CFLOOKUP_HangfirePassword", EnvironmentVariableTarget.Process) ??
                string.Empty;
            
            pgsqlConnString = Environment.GetEnvironmentVariable("CFLOOKUP_PGSQL", EnvironmentVariableTarget.Machine) ??
                Environment.GetEnvironmentVariable("CFLOOKUP_PGSQL", EnvironmentVariableTarget.User) ??
                Environment.GetEnvironmentVariable("CFLOOKUP_PGSQL", EnvironmentVariableTarget.Process) ??
                string.Empty;
        }
        else
        {
            cfApiKey = Environment.GetEnvironmentVariable("CFAPI_Key") ?? string.Empty;
            redisServer = Environment.GetEnvironmentVariable("RedisServer") ?? "127.0.0.1:6379";
            dbConnectionString = Environment.GetEnvironmentVariable("CFLOOKUP_SQL") ?? string.Empty;
            hangfireUser = Environment.GetEnvironmentVariable("CFLOOKUP_HangfireUser") ?? string.Empty;
            hangfirePassword = Environment.GetEnvironmentVariable("CFLOOKUP_HangfirePassword") ?? string.Empty;
            pgsqlConnString = Environment.GetEnvironmentVariable("CFLOOKUP_PGSQL") ?? string.Empty;
        }

        var redis = ConnectionMultiplexer.Connect(redisServer);

        builder.Services.AddSingleton(redis);
        
        builder.Services.AddDataProtection()
            .PersistKeysToStackExchangeRedis(redis, "CFLookup-DataProtection-Keys");

        builder.Services.AddScoped(x => new SqlConnection(dbConnectionString));
        
        builder.Services.AddScoped(x => new NpgsqlConnection(pgsqlConnString));

        builder.Services.AddScoped<MSSQLDB>();
#if !DEBUG
        builder.Services.AddHangfire(configuration =>
        {
            configuration
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseRedisStorage(redis, new RedisStorageOptions
                {
                    Db = 7,
                    Prefix = "cflookup:",
                    InvisibilityTimeout = TimeSpan.FromHours(6)
                });
        });

        builder.Services.AddHangfireServer(options =>
        {
        });
#endif
        builder.Services.AddScoped(options => new CurseForge.APIClient.ApiClient(cfApiKey, 201, "whatcfprojectisthat@nolifeking85.tv")
        {
            RequestDelay = TimeSpan.FromSeconds(5),
            RequestTimeout = TimeSpan.FromMinutes(10)
        });

        var app = builder.Build();

        ServiceProvider = app.Services;

        app.UseResponseCompression();

        app.UseResponseCaching();

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            _ = app.UseExceptionHandler("/Error");
        }

        app.UseForwardedHeaders();

        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = ctx =>
            {
                ctx.Context.Response.Headers.Append("Cache-Control", $"public, max-age={60 * 60 * 24 * 30}");
            }
        });
#if !DEBUG
        app.UseHangfireDashboard("/hangfire", new DashboardOptions
        {
            Authorization = new[]
            {
                new BasicAuthAuthorizationFilter(new BasicAuthAuthorizationFilterOptions
                {
                    SslRedirect = false,
                    RequireSsl = false,
                    LoginCaseSensitive = true,
                    Users = new[]
                    {
                        new BasicAuthAuthorizationUser
                        {
                            Login = hangfireUser,
                            PasswordClear = hangfirePassword
                        }
                    }
                })
            }
        });
#endif
        app.UseRouting();

        app.UseAuthorization();

        app.MapRazorPages();

        app.MapDefaultControllerRoute();

#if !DEBUG
        RecurringJob.AddOrUpdate("cflookup:GetLatestUpdatedModPerGame", () => GetLatestUpdatedModPerGame.RunAsync(null), "*/5 * * * *");
        RecurringJob.AddOrUpdate("cflookup:SaveMinecraftModStats", () => SaveMinecraftModStats.RunAsync(null), Cron.Hourly());
        RecurringJob.AddOrUpdate("cflookup:CacheMCStatsOvertime", () => CacheMCOverTime.RunAsync(null), "*/30 * * * *");
        
        if (!SharedMethods.CheckIfTaskIsScheduledOrInProgress("StoreCFApiProjects", "RunAsync") &&
            !SharedMethods.CheckIfTaskIsScheduledOrInProgress("StoreCFApiFiles", "RunAsync"))
        {
            BackgroundJob.Schedule(() => StoreCFApiProjects.RunAsync(null, JobCancellationToken.Null), TimeSpan.FromSeconds(10));
        }
#endif

        await app.RunAsync();
    }
}