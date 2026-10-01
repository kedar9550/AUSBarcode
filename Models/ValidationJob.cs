namespace AUSBarcodeValidator.Models;

public sealed class ValidationJob
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public string Status { get; set; } = "Waiting";
    public string CurrentFile { get; set; } = "";
    public bool CancelRequested { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ReportPath { get; set; }
    public List<ValidationResultModel> Results { get; } = new();
}
