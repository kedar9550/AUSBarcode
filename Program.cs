using AUSBarcodeValidator.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);


// Add MVC Services
builder.Services.AddControllersWithViews();

// Add Barcode Validation & Persistent Database Services
builder.Services.AddSingleton<ValidationDbService>();
builder.Services.AddSingleton<PdfService>();
builder.Services.AddSingleton<BarcodeDetectionService>();
builder.Services.AddSingleton<ValidationService>();
builder.Services.AddSingleton<ReportService>();
builder.Services.AddSingleton<ValidationJobService>();
builder.Services.AddHttpContextAccessor();

// Configure large upload limits (up to 1 GB)
builder.Services.Configure<FormOptions>(options =>
{
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartBodyLengthLimit = 1073741824L; // 1 GB
    options.MultipartHeadersLengthLimit = int.MaxValue;
});

builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = 1073741824L; // 1 GB
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
