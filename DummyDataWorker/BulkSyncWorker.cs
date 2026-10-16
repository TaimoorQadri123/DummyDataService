using DummyDataWorker.Data;
using DummyDataWorker.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DummyDataWorker
{
    internal class BulkSyncWorker : BackgroundService
    {
        private readonly ILogger<BulkSyncWorker> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _config;

        public BulkSyncWorker(ILogger<BulkSyncWorker> logger,
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
            var batchSize = _config.GetValue<int>("WorkerSettings:SyncBatchSize", 5000);

            _logger.LogInformation("BulkSyncWorker started.Every {i} seconds,max {b} rows per round.", interval, batchSize);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SyncOnceAsync(batchSize,stoppingToken);

                }
                catch(Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "BulkSyncWorker: error during sync.");
                }

               await Task.Delay(TimeSpan.FromSeconds(interval), stoppingToken);
            }

        }

        private async Task SyncOnceAsync(int batchSize, CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();

            var checkpoint = await db.ModifyPoints.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);

            if (checkpoint == null)
            {
                checkpoint = new ModifyPoint { TranId = 0, ModifyOn = DateTime.Now };
                db.ModifyPoints.Add(checkpoint);
                await db.SaveChangesAsync(ct);
                _logger.LogInformation("No checkpoint found. Created a new checkpoint with TranId 0.");
            }

            var lastTranId = checkpoint.TranId;

            var newRows = await db.SourceTransactions
                .AsNoTracking()
                .Where(x => x.TranId > lastTranId)
                .OrderBy(x => x.TranId)
                .Take(batchSize)
                .ToListAsync(ct);

            if (newRows.Count == 0)
            {
                _logger.LogInformation("Nothing new to sync. Last TranId: {LastTranId}", lastTranId);
                return;
            }

            var table = ToDataTable(newRows);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var connection = (SqlConnection)db.Database.GetDbConnection();
            var sqlTx = (SqlTransaction)tx.GetDbTransaction();


            using (var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, sqlTx))
            {
                bulk.DestinationTableName = "DestTransaction";
                bulk.BatchSize = 1000;
                bulk.BulkCopyTimeout = 60;
                bulk.ColumnMappings.Add("TranId", "TransactionId");
                bulk.ColumnMappings.Add("CustomerName", "CustName");
                bulk.ColumnMappings.Add("Amount", "TranAmount");
                bulk.ColumnMappings.Add("Status", "TranStatus");
                bulk.ColumnMappings.Add("CreatedAt", "SourceCreatedOn");

                await bulk.WriteToServerAsync(table, ct);
            }

            checkpoint.TranId = newRows.Max(x => x.TranId);
            checkpoint.ModifyOn = DateTime.Now;
            await db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);

            _logger.LogInformation("Bulk copied {n} rows (TranId {from} to {to}). Checkpoint updated.",
                 newRows.Count, lastTranId + 1, checkpoint.TranId);


        }
        private static DataTable ToDataTable(List<SourceTransaction> rows)
        {
            var table = new DataTable();
            table.Columns.Add("TranId", typeof(int));
            table.Columns.Add("CustomerName", typeof(string));
            table.Columns.Add("Amount", typeof(decimal));
            table.Columns.Add("Status", typeof(string));
            table.Columns.Add("CreatedAt", typeof(DateTime));

            foreach (var r in rows)
            {
                table.Rows.Add(r.TranId, r.CustomerName, r.Amount, r.Status, r.CreatedAt);
            }

            return table;
        }
    }
}
