using AUSBarcodeValidator.Models;
using Microsoft.Data.Sqlite;

namespace AUSBarcodeValidator.Services;

public sealed class ValidationDbService
{
    private readonly string _dbPath;
    private readonly string _connectionString;

    public ValidationDbService(IWebHostEnvironment env)
    {
        var dir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, "barcode_validator.db");
        _connectionString = $"Data Source={_dbPath};";
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        const string schema = @"
        CREATE TABLE IF NOT EXISTS ValidationBatches (
            BatchId TEXT PRIMARY KEY,
            FolderName TEXT NOT NULL,
            TotalFiles INTEGER NOT NULL,
            ProcessedFiles INTEGER NOT NULL,
            ExpectedPages INTEGER NOT NULL,
            ValidCount INTEGER NOT NULL DEFAULT 0,
            InvalidCount INTEGER NOT NULL DEFAULT 0,
            MismatchCount INTEGER NOT NULL DEFAULT 0,
            MissingCount INTEGER NOT NULL DEFAULT 0,
            Status TEXT NOT NULL,
            StartedAt TEXT NOT NULL,
            CompletedAt TEXT,
            ReportPath TEXT
        );

        CREATE TABLE IF NOT EXISTS ValidationResults (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            BatchId TEXT NOT NULL,
            SrNo INTEGER NOT NULL,
            FolderName TEXT NOT NULL,
            FileName TEXT NOT NULL,
            TotalPdfsInFolder INTEGER NOT NULL,
            ExpectedPages INTEGER NOT NULL,
            TotalPagesFound INTEGER NOT NULL,
            ReferenceBarcode TEXT,
            MismatchPages TEXT,
            MismatchDetails TEXT,
            MissingPagesCount INTEGER NOT NULL,
            FinalStatus TEXT NOT NULL,
            ProcessedTime TEXT NOT NULL,
            FOREIGN KEY (BatchId) REFERENCES ValidationBatches(BatchId) ON DELETE CASCADE
        );

        CREATE INDEX IF NOT EXISTS IX_ValidationResults_BatchId ON ValidationResults (BatchId);
        ";

        using var cmd = new SqliteCommand(schema, conn);
        cmd.ExecuteNonQuery();
    }

    public void InsertBatch(Guid batchId, string folderName, int totalFiles, int expectedPages, DateTime startedAt)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        const string sql = @"
        INSERT INTO ValidationBatches (BatchId, FolderName, TotalFiles, ProcessedFiles, ExpectedPages, Status, StartedAt)
        VALUES (@BatchId, @FolderName, @TotalFiles, 0, @ExpectedPages, 'Starting', @StartedAt);";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@BatchId", batchId.ToString("N"));
        cmd.Parameters.AddWithValue("@FolderName", folderName);
        cmd.Parameters.AddWithValue("@TotalFiles", totalFiles);
        cmd.Parameters.AddWithValue("@ExpectedPages", expectedPages);
        cmd.Parameters.AddWithValue("@StartedAt", startedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.ExecuteNonQuery();
    }

    public void SaveResult(Guid batchId, PdfValidationResult r)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        const string sql = @"
        INSERT INTO ValidationResults 
        (BatchId, SrNo, FolderName, FileName, TotalPdfsInFolder, ExpectedPages, TotalPagesFound, ReferenceBarcode, MismatchPages, MismatchDetails, MissingPagesCount, FinalStatus, ProcessedTime)
        VALUES 
        (@BatchId, @SrNo, @FolderName, @FileName, @TotalPdfsInFolder, @ExpectedPages, @TotalPagesFound, @ReferenceBarcode, @MismatchPages, @MismatchDetails, @MissingPagesCount, @FinalStatus, @ProcessedTime);
        
        UPDATE ValidationBatches 
        SET ProcessedFiles = ProcessedFiles + 1 
        WHERE BatchId = @BatchId;";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@BatchId", batchId.ToString("N"));
        cmd.Parameters.AddWithValue("@SrNo", r.SrNo);
        cmd.Parameters.AddWithValue("@FolderName", r.FolderName);
        cmd.Parameters.AddWithValue("@FileName", r.FileName);
        cmd.Parameters.AddWithValue("@TotalPdfsInFolder", r.TotalPdfsInFolder);
        cmd.Parameters.AddWithValue("@ExpectedPages", r.ExpectedPages);
        cmd.Parameters.AddWithValue("@TotalPagesFound", r.TotalPagesFound);
        cmd.Parameters.AddWithValue("@ReferenceBarcode", r.ReferenceBarcode ?? "-");
        cmd.Parameters.AddWithValue("@MismatchPages", r.MismatchPages ?? "-");
        cmd.Parameters.AddWithValue("@MismatchDetails", r.MismatchDetails ?? "-");
        cmd.Parameters.AddWithValue("@MissingPagesCount", r.MissingPagesCount);
        cmd.Parameters.AddWithValue("@FinalStatus", r.FinalStatus);
        cmd.Parameters.AddWithValue("@ProcessedTime", r.ProcessedTime.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.ExecuteNonQuery();
    }

    public void CompleteBatch(
        Guid batchId,
        string status,
        DateTime completedAt,
        string? reportPath,
        int validCount,
        int invalidCount,
        int mismatchCount,
        int missingCount)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        const string sql = @"
        UPDATE ValidationBatches
        SET Status = @Status,
            CompletedAt = @CompletedAt,
            ReportPath = @ReportPath,
            ValidCount = @ValidCount,
            InvalidCount = @InvalidCount,
            MismatchCount = @MismatchCount,
            MissingCount = @MissingCount
        WHERE BatchId = @BatchId;";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@BatchId", batchId.ToString("N"));
        cmd.Parameters.AddWithValue("@Status", status);
        cmd.Parameters.AddWithValue("@CompletedAt", completedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@ReportPath", (object?)reportPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ValidCount", validCount);
        cmd.Parameters.AddWithValue("@InvalidCount", invalidCount);
        cmd.Parameters.AddWithValue("@MismatchCount", mismatchCount);
        cmd.Parameters.AddWithValue("@MissingCount", missingCount);
        cmd.ExecuteNonQuery();
    }

    public List<Dictionary<string, object>> GetHistory(int limit = 50)
    {
        var list = new List<Dictionary<string, object>>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        const string sql = @"
        SELECT BatchId, FolderName, TotalFiles, ProcessedFiles, ExpectedPages, ValidCount, InvalidCount, MismatchCount, MissingCount, Status, StartedAt, CompletedAt, ReportPath
        FROM ValidationBatches
        ORDER BY StartedAt DESC
        LIMIT @Limit;";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Limit", limit);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var dict = new Dictionary<string, object>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                dict[reader.GetName(i)] = reader.IsDBNull(i) ? "" : reader.GetValue(i);
            }
            list.Add(dict);
        }
        return list;
    }
}
