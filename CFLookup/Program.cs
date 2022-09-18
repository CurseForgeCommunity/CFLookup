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

builder.Services.AddSingleton(ConnectionMultiplexer.Connect("127.0.0.1:6379"));

builder.Services.AddScoped(options =>
{
    var cfApiKey = Environment.GetEnvironmentVariable("CFAPI_Key");
    return new CurseForge.APIClient.ApiClient(cfApiKey, 201, "whatcfprojectisthat@nolifeking85.tv");
});

var app = builder.Build();

app.UseResponseCompression();

app.UseResponseCaching();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.Append("Cache-Control", $"public, max-age={(60 * 60 * 24 * 30)}");
    }
});

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();

app.MapDefaultControllerRoute();

app.Run();
