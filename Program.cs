using BlockChain_FP_ITStep.Data;
using BlockChain_FP_ITStep.Hubs;
using BlockChain_FP_ITStep.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();



// DB context Factory
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseSqlite("Data Source=blockchain.db"));


// DI's
//builder.Services.AddScoped<BlockChainService>();

builder.Services.AddSingleton<BlockChainService>();   // Wallets мб всетаки надо в БД писать,  пока что сделаем Синглтон чтобы коллеккция Валлетс сохранялась между запросами в UI.

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

// signalR mining Hub
app.MapHub<MiningHub>("/miningHub");


//  DB scope
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    //db.Database.EnsureDeleted();
    db.Database.EnsureCreated();
}



app.Run();
