using Microsoft.AspNetCore.ResponseCompression;
using StackExchange.Redis;
using System.IO.Compression;

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
}
else
{
    cfApiKey = Environment.GetEnvironmentVariable("CFAPI_Key") ?? string.Empty;
    redisServer = Environment.GetEnvironmentVariable("RedisServer") ?? "127.0.0.1:6379";
}

builder.Services.AddSingleton(ConnectionMultiplexer.Connect(redisServer));

builder.Services.AddScoped(options =>
{
    return new CurseForge.APIClient.ApiClient(cfApiKey, 201, "whatcfprojectisthat@nolifeking85.tv");
});

var app = builder.Build();

app.UseResponseCompression();

app.UseResponseCaching();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    _ = app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    //app.UseHsts();
}

app.UseForwardedHeaders();

//app.UseHttpsRedirection();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.Append("Cache-Control", $"public, max-age={60 * 60 * 24 * 30}");
    }
});

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();

app.MapDefaultControllerRoute();

app.Run();
