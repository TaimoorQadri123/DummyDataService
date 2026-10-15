using DummyDataWorker;
using DummyDataWorker.Data;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<WorkerDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// builder.Services.AddHostedService<Worker>();   
//builder.Services.AddHostedService<InsertWorker>();
builder.Services.AddHostedService<InsertWorker>();   // Service 1
builder.Services.AddHostedService<SyncWorker>();     // Service 2

var host = builder.Build();
host.Run();





