namespace AUSBarcodeValidator.Models;

public enum DetectionStatus
{
    DETECTED,
    MATCH,
    NOT_MATCH,
    NOT_DETECTED,
    ERROR,
    REFERENCE
}

public sealed class BarcodeDetectionResult
{
    public string FileName { get; set; } = string.Empty;
    public int PageNumber { get; set; }
    public string? Barcode { get; set; }
    public string? BarcodeFormat { get; set; }
    public DetectionStatus DetectionStatus { get; set; } = DetectionStatus.NOT_DETECTED;
    public long ProcessingTimeMs { get; set; }
    public int AttemptNumber { get; set; } = 1;
    public string? ErrorMessage { get; set; }
}

public sealed class PdfValidationResult
{
    public int SrNo { get; set; }
    public string FolderName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int TotalPdfsInFolder { get; set; }
    public int ExpectedPages { get; set; }
    public int TotalPagesFound { get; set; }
    public string ReferenceBarcode { get; set; } = "-";
    public int MatchedPagesCount { get; set; }
    public int MismatchPagesCount { get; set; }
    public string MismatchPages { get; set; } = "-";
    public string MismatchDetails { get; set; } = "-";
    public int NotDetectedPagesCount { get; set; }
    public string NotDetectedPages { get; set; } = "-";
    public int MissingPagesCount { get; set; }
    public string FinalStatus { get; set; } = "VALID";
    public List<BarcodeDetectionResult> PageDetails { get; set; } = new();
    public double ProcessingSeconds { get; set; }
    public DateTime ProcessedTime { get; set; } = DateTime.Now;
}

public sealed class ValidationSummary
{
    public int TotalPdfs { get; set; }
    public int ProcessedPdfs { get; set; }
    public int ValidCount { get; set; }
    public int MismatchCount { get; set; }
    public int NotDetectedCount { get; set; }
    public int InvalidCount { get; set; }
    public int ErrorCount { get; set; }
    public int TotalPages { get; set; }
    public int MatchedPages { get; set; }
    public int NotMatchedPages { get; set; }
    public int NotDetectedPages { get; set; }
    public double TotalProcessingSeconds { get; set; }
    public double AveragePdfSeconds { get; set; }
    public double AveragePageSeconds { get; set; }
    public double PagesPerSecond { get; set; }
    public double PdfsPerMinute { get; set; }
    public double DetectionSuccessRate { get; set; }
}

public sealed class ValidationSettings
{
    public int Dpi { get; set; } = 250;
    public int RetryDpi { get; set; } = 300;
    public int MaxRetries { get; set; } = 4;
    public int MaxParallelWorkers { get; set; } = Math.Clamp(Environment.ProcessorCount, 2, 8);
    public string ExpectedFormat { get; set; } = "CODE_128";
    public float ContrastAlpha { get; set; } = 1.5f;
    public bool CheckOnlyOddPages { get; set; } = true;
    public string ExpectedBarcodesPerPage { get; set; } = "One";
}

public sealed class ValidationJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Status { get; set; } = "Initializing...";
    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public string? CurrentFile { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ReportCsvPath { get; set; }
    public string? ReportExcelPath { get; set; }
    public bool CancelRequested { get; set; }
    public bool PauseRequested { get; set; }
    public List<PdfValidationResult> Results { get; } = new();
    public ValidationSummary Summary { get; set; } = new();
}
