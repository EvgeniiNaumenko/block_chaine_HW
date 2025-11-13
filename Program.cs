using BlockChain_FP_ITStep.Data;
using BlockChain_FP_ITStep.Hubs;
using BlockChain_FP_ITStep.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// --------------------
// DB context Factory (SQLite)
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
{
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"));
});

// --------------------
// DI's
builder.Services.AddSingleton<BlockChainService>();

// SignalR
builder.Services.AddSignalR();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=BlockChain}/{action=Index}/{id?}")
    .WithStaticAssets();

// SignalR mining Hub
app.MapHub<MiningHub>("/miningHub");

// --------------------
// DB scope: создаем файл SQLite, если не существует
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.EnsureDeleted(); // раскомментируй только если нужно удалить старую базу
    db.Database.EnsureCreated();
}

app.Run();
