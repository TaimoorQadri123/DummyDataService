using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DummyDataWorker.Data;
using DummyDataWorker.Models;

namespace DummyDataWorker
{
    // SERVICE 1: Source table mein dummy data daalti hai
    public class InsertWorker : BackgroundService
    {
        private readonly ILogger<InsertWorker> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _config;
        private readonly Random _random = new();

        private static readonly string[] Names =
            { "Ali Khan", "Sara Ahmed", "Usman Raza", "Ayesha Malik", "Hamza Shaikh", "Fatima Noor" };

        private static readonly string[] Statuses =
            { "Pending", "Success", "Failed" };

        public InsertWorker(ILogger<InsertWorker> logger,
                            IServiceScopeFactory scopeFactory,
                            IConfiguration config)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _config = config;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var interval = _config.GetValue<int>("WorkerSettings:InsertIntervalSeconds", 10);
            var batchSize = _config.GetValue<int>("WorkerSettings:InsertBatchSize", 1000);

            _logger.LogInformation("InsertWorker started. Every {i} seconds, {b} rows will go to SourceTransaction.", interval, batchSize);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();

                    var rows = Enumerable.Range(0, batchSize).Select(_ => new SourceTransaction
                    {
                        CustomerName = Names[_random.Next(Names.Length)],
                        Amount = Math.Round((decimal)(_random.NextDouble() * 10000), 2),
                        Status = Statuses[_random.Next(Statuses.Length)],
                        CreatedAt = DateTime.Now
                    }).ToList();

                    db.SourceTransactions.AddRange(rows);
                    await db.SaveChangesAsync(stoppingToken);

                    _logger.LogInformation("{n} rows inserted into SourceTransaction.", rows.Count);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "InsertWorker: error while inserting.");
                }

                await Task.Delay(TimeSpan.FromSeconds(interval), stoppingToken);
            }
        }
    }
}
