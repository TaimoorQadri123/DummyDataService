using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using DummyDataWorker.Data;
using DummyDataWorker.Models;
using Microsoft.EntityFrameworkCore;

namespace DummyDataWorker
{
    // SERVICE 2: Source se naya data uthake Local mein daalti hai,
    // aur ModifyPoint (checkpoint) update karti hai
    public class SyncWorker : BackgroundService
    {
        private readonly ILogger<SyncWorker> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _config;

        public SyncWorker(ILogger<SyncWorker> logger,
                          IServiceScopeFactory scopeFactory,
                          IConfiguration config)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _config = config;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var interval = _config.GetValue<int>("WorkerSettings:SyncIntervalSeconds", 10); 
            var batchSize = _config.GetValue<int>("WorkerSettings:SyncBatchSize", 1000);

            _logger.LogInformation("SyncWorker started. Every {i} seconds, max {b} rows per round.", interval, batchSize);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SyncOnceAsync(batchSize, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "SyncWorker: error during sync.");
                }

                await Task.Delay(TimeSpan.FromSeconds(interval), stoppingToken);
            }
        }

        private async Task SyncOnceAsync(int batchSize, CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();

            // 1) Checkpoint (modify point) padho
            var checkpoint = await db.ModifyPoints.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);

            // Pehli baar: row nahi hai, to TranId = 0 ke saath banao
            if (checkpoint == null)
            {
                checkpoint = new ModifyPoint { TranId = 0, ModifyOn = DateTime.Now };
                db.ModifyPoints.Add(checkpoint);
                await db.SaveChangesAsync(ct);
                _logger.LogInformation("First run: ModifyPoint created with TranId = 0.");
            }

            var lastTranId = checkpoint.TranId;

            // 2) Source se sirf naya data lo (TranId > lastTranId), order ke saath
            var newRows = await db.SourceTransactions
                .AsNoTracking()
                .Where(x => x.TranId > lastTranId)
                .OrderBy(x => x.TranId)
                .Take(batchSize)
                .ToListAsync(ct);

            if (newRows.Count == 0)
            {
                _logger.LogInformation("Nothing new (last TranId = {id}).", lastTranId);
                return;
            }

            // 3) Local mein insert (TranId source wali hi rahegi)
            var localRows = newRows.Select(x => new LocalTransaction
            {
                TranId = x.TranId,
                CustomerName = x.CustomerName,
                Amount = x.Amount,
                Status = x.Status,
                CreatedAt = x.CreatedAt
            });
            db.LocalTransactions.AddRange(localRows);

            // 4) Checkpoint update: fetch hui rows ki sabse bari TranId
            checkpoint.TranId = newRows.Max(x => x.TranId);
            checkpoint.ModifyOn = DateTime.Now;

            // Dono kaam ek hi SaveChanges mein: ya dono honge, ya koi nahi
            await db.SaveChangesAsync(ct);

            _logger.LogInformation("Synced {n} rows (TranId {from} to {to}). Checkpoint updated.",
                newRows.Count, lastTranId + 1, checkpoint.TranId);
        }
    }
}
