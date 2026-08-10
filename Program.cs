var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<app_dev_assignment.Services.IBlobService, app_dev_assignment.Services.BlobService>();
builder.Services.AddSingleton<app_dev_assignment.Services.IHistoryService, app_dev_assignment.Services.HistoryService>();
builder.Services.AddHttpClient<app_dev_assignment.Services.IVisionService, app_dev_assignment.Services.VisionService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
