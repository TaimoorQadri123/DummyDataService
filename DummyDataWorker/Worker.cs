using DummyDataWorker.Data;
using DummyDataWorker.Models;

namespace DummyDataWorker
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _config;
        private readonly Random _random = new();

        private static readonly string[] Names =
            { "Ali Khan", "Sara Ahmed", "Usman Raza", "Ayesha Malik", "Hamza Shaikh", "Fatima Noor" };

        private static readonly string[] Statuses =
            { "Pending", "Success", "Failed" };

        public Worker(ILogger<Worker> logger,
                      IServiceScopeFactory scopeFactory,
                      IConfiguration config)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _config = config;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var interval = _config.GetValue<int>("WorkerSettings:IntervalSeconds", 10);
            var batchSize = _config.GetValue<int>("WorkerSettings:BatchSize", 5);

            _logger.LogInformation("Worker started. Every  seconds,  rows will be inserted.", interval, batchSize);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // har round ke liye naya scope (neeche samjhaya hai)
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();

                    var rows = Enumerable.Range(0, batchSize).Select(_ => new WorkerDummyData
                    {
                        CustomerName = Names[_random.Next(Names.Length)],
                        Amount = Math.Round((decimal)(_random.NextDouble() * 10000), 2),
                        Status = Statuses[_random.Next(Statuses.Length)],
                        CreatedAt = DateTime.Now
                    }).ToList();

                    db.WorkerDummyData.AddRange(rows);
                    await db.SaveChangesAsync(stoppingToken);

                    _logger.LogInformation("{n} rows inserted at: {time}", rows.Count, DateTimeOffset.Now);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // error aaye to worker band na ho, sirf log ho aur agla round chale
                    _logger.LogError(ex, "Error occurred while inserting rows.");
                }

                await Task.Delay(TimeSpan.FromSeconds(interval), stoppingToken);
            }
        }
    }
}