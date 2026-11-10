using Bookstore.Authentication;
using Bookstore.Application;
using Bookstore.Infrastructure;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews(options =>
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddBookstoreAccounts(builder.Configuration);
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<ICatalogRepository, SqliteCatalogRepository>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<IBookstoreReports, SqliteBookstoreReports>();
builder.Services.AddScoped<ReceivingService>();
builder.Services.AddScoped<IReceiptRepository, SqliteReceiptRepository>();
var databasePath = builder.Configuration["Bookstore:Database"] ?? "bookstore.db";
builder.Services.AddSingleton(new SqliteDatabase(Path.Combine(builder.Environment.ContentRootPath, databasePath)));
builder.Services.AddScoped<IOrderRepository, SqliteOrderRepository>();
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();
app.Services.GetRequiredService<SqliteDatabase>().Initialize();
app.SeedBookstoreAccounts();
app.UseExceptionHandler("/Home/Error");
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultControllerRoute();

// Keep URLs from the initial demo available for existing links and bookmarks.
app.MapControllerRoute("legacy-login", "Home/Login", new { controller = "Account", action = "Login" });
app.MapControllerRoute("legacy-register", "Home/Register", new { controller = "Account", action = "Register" });
app.MapControllerRoute("legacy-logout", "Home/Logout", new { controller = "Account", action = "Logout" });
app.MapControllerRoute("legacy-denied", "Home/Denied", new { controller = "Account", action = "Denied" });
app.MapControllerRoute("legacy-orders", "Home/MyOrders", new { controller = "Orders", action = "Index" });
app.MapControllerRoute("legacy-admin", "Home/Admin", new { controller = "AdminOrders", action = "Index" });
app.MapControllerRoute("legacy-confirm", "Home/Confirm", new { controller = "AdminOrders", action = "Confirm" });
app.Run();


