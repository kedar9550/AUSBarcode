using AUSBarcodeValidator.Models;
using AUSBarcodeValidator.Services;
using Microsoft.AspNetCore.Mvc;

namespace AUSBarcodeValidator.Controllers;

public sealed class HomeController : Controller
{
    private readonly ValidationService _validator;
    private readonly ValidationJobService _jobs;
    private readonly ValidationDbService _db;
    private readonly ReportService _reports;
    private readonly IWebHostEnvironment _env;

    public HomeController(
        ValidationService validator,
        ValidationJobService jobs,
        ValidationDbService db,
        ReportService reports,
        IWebHostEnvironment env)
    {
        _validator = validator;
        _jobs = jobs;
        _db = db;
        _reports = reports;
        _env = env;
    }

    [HttpGet]
    public IActionResult Index() => View();

    [HttpPost]
    [RequestFormLimits(MultipartBodyLengthLimit = 1073741824)] // 1 GB limit
    [RequestSizeLimit(1073741824)]
    public async Task<IActionResult> Start(
        IFormFileCollection? files,
        string? folderPath,
        int expectedPages,
        int dpi = 250,
        int retryDpi = 300,
        int maxWorkers = 4,
        string expectedFormat = "CODE_128",
        bool checkOnlyOddPages = true)
    {
        if (expectedPages <= 0) return BadRequest("Enter valid expected page count!");

        var job = _jobs.Create();
        string targetFolder;
        string folderDisplayName;

        var settings = new ValidationSettings
        {
            Dpi = Math.Clamp(dpi, 150, 350),
            RetryDpi = Math.Clamp(retryDpi, 200, 400),
            MaxParallelWorkers = Math.Clamp(maxWorkers, 1, 16),
            ExpectedFormat = expectedFormat,
            CheckOnlyOddPages = checkOnlyOddPages
        };

        // Mode 1: Local / Server folder path supplied directly
        if (!string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath.Trim()))
        {
            targetFolder = folderPath.Trim();
            folderDisplayName = Path.GetFileName(targetFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var pdfs = Directory.EnumerateFiles(targetFolder, "*.pdf", SearchOption.TopDirectoryOnly).ToList();
            if (pdfs.Count == 0) return BadRequest("No PDF files found in the specified folder!");
            job.TotalFiles = pdfs.Count;
        }
        // Mode 2: Files uploaded through browser
        else if (files != null && files.Count > 0)
        {
            var validFiles = files.Where(f => Path.GetExtension(f.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase)).ToList();
            if (validFiles.Count == 0) return BadRequest("No PDF files found!");

            var uploadRoot = Path.Combine(_env.ContentRootPath, "App_Data", "Jobs", job.Id.ToString("N"));
            Directory.CreateDirectory(uploadRoot);

            foreach (var file in validFiles)
            {
                var safeName = Path.GetFileName(file.FileName);
                var destPath = Path.Combine(uploadRoot, safeName);
                await using var stream = System.IO.File.Create(destPath);
                await file.CopyToAsync(stream);
            }

            targetFolder = uploadRoot;
            folderDisplayName = "Uploaded Folder";
            job.TotalFiles = validFiles.Count;
        }
        else
        {
            return BadRequest("Select folder first!");
        }

        job.StartedAt = DateTime.Now;
        job.Status = $"Starting validation... (Total: {job.TotalFiles} PDFs)";

        try
        {
            _db.InsertBatch(job.Id, folderDisplayName, job.TotalFiles, expectedPages, job.StartedAt.Value);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DB Error: {ex.Message}");
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _validator.ProcessBatchAsync(
                    targetFolder,
                    expectedPages,
                    job,
                    settings,
                    _jobs.Token(job.Id),
                    folderDisplayName,
                    _db);

                var (csvPath, excelPath) = _reports.GenerateReports(job.Id, job.Results, job.Summary);
                job.ReportCsvPath = csvPath;
                job.ReportExcelPath = excelPath;
                job.Status = job.CancelRequested ? "Process Cancelled ❌" : "Completed";

                _db.CompleteBatch(
                    job.Id,
                    job.Status,
                    DateTime.Now,
                    job.ReportCsvPath,
                    job.Summary.ValidCount,
                    job.Summary.InvalidCount,
                    job.Summary.MismatchCount,
                    job.Summary.InvalidCount);

                // Clean up uploaded PDFs once reports are created to keep repo size small
                try
                {
                    if (!string.IsNullOrEmpty(job.ReportCsvPath) && Directory.Exists(targetFolder))
                    {
                        var pdfFiles = Directory.GetFiles(targetFolder, "*.pdf", SearchOption.TopDirectoryOnly);
                        foreach (var f in pdfFiles)
                        {
                            try
                            {
                                System.IO.File.Delete(f);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Failed to delete file {f}: {ex.Message}");
                            }
                        }

                        // Remove the job folder if it's empty
                        if (!Directory.EnumerateFileSystemEntries(targetFolder).Any())
                        {
                            Directory.Delete(targetFolder);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Cleanup error for job {job.Id}: {ex.Message}");
                }
            }
            catch (OperationCanceledException)
            {
                job.Status = "Process Cancelled ❌";
                _db.CompleteBatch(job.Id, job.Status, DateTime.Now, null, 0, 0, 0, 0);
            }
            catch (Exception ex)
            {
                job.Status = "Error: " + ex.Message;
                _db.CompleteBatch(job.Id, job.Status, DateTime.Now, null, 0, 0, 0, 0);
            }
            finally
            {
                job.CompletedAt = DateTime.Now;
            }
        });

        return Json(new { jobId = job.Id, totalFiles = job.TotalFiles });
    }

    [HttpGet]
    public IActionResult Status(Guid id)
    {
        var job = _jobs.Get(id);
        if (job is null) return NotFound();

        var endTime = job.CompletedAt ?? DateTime.Now;
        var elapsed = job.StartedAt.HasValue ? endTime - job.StartedAt.Value : TimeSpan.Zero;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;

        TimeSpan remaining = TimeSpan.Zero;
        if (!job.CompletedAt.HasValue && job.ProcessedFiles > 0 && job.TotalFiles > job.ProcessedFiles)
        {
            var avgSeconds = elapsed.TotalSeconds / job.ProcessedFiles;
            var remainingSeconds = avgSeconds * (job.TotalFiles - job.ProcessedFiles);
            remaining = TimeSpan.FromSeconds(Math.Max(0, remainingSeconds));
        }

        var resultsCopy = job.Results.ToList();
        var validCount = resultsCopy.Count(r => r.FinalStatus == "VALID");
        var mismatchCount = resultsCopy.Count(r => r.FinalStatus.Contains("Mismatch", StringComparison.OrdinalIgnoreCase));
        var missingCount = resultsCopy.Count(r => r.FinalStatus.Contains("Pages Count", StringComparison.OrdinalIgnoreCase));
        var notDetectedCount = resultsCopy.Count(r => r.FinalStatus.Contains("Not Detected", StringComparison.OrdinalIgnoreCase));
        var invalidCount = resultsCopy.Count - validCount;

        return Json(new
        {
            job.Id,
            job.Status,
            job.TotalFiles,
            job.ProcessedFiles,
            job.CurrentFile,
            Percent = job.TotalFiles == 0 ? 0 : (int)Math.Round(job.ProcessedFiles * 100.0 / job.TotalFiles),
            Elapsed = FormatTime(elapsed),
            ElapsedSeconds = (int)elapsed.TotalSeconds,
            Remaining = FormatTime(remaining),
            RemainingSeconds = (int)remaining.TotalSeconds,
            TotalElapsed = FormatTime(elapsed),
            Completed = job.Status is "Completed" or "Process Cancelled ❌" || job.Status.StartsWith("Error:"),
            ReportCsv = job.ReportCsvPath != null ? Path.GetFileName(job.ReportCsvPath) : null,
            ReportExcel = job.ReportExcelPath != null ? Path.GetFileName(job.ReportExcelPath) : null,
            Results = resultsCopy,
            Summary = new
            {
                total = job.TotalFiles,
                processed = job.ProcessedFiles,
                valid = validCount,
                invalid = invalidCount,
                mismatch = mismatchCount,
                notDetected = notDetectedCount,
                missing = missingCount,
                metrics = job.Summary
            }
        });
    }

    [HttpPost]
    public IActionResult Cancel(Guid id)
    {
        _jobs.Cancel(id);
        return Ok();
    }

    [HttpPost]
    public IActionResult TogglePause(Guid id)
    {
        _jobs.TogglePause(id);
        return Ok();
    }

    [HttpGet]
    public IActionResult DownloadCsv(Guid id)
    {
        var job = _jobs.Get(id);
        if (job?.ReportCsvPath is null || !System.IO.File.Exists(job.ReportCsvPath)) return NotFound();
        return PhysicalFile(job.ReportCsvPath, "text/csv", Path.GetFileName(job.ReportCsvPath));
    }

    [HttpGet]
    public IActionResult DownloadExcel(Guid id)
    {
        var job = _jobs.Get(id);
        if (job?.ReportExcelPath is null || !System.IO.File.Exists(job.ReportExcelPath)) return NotFound();
        return PhysicalFile(job.ReportExcelPath, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Path.GetFileName(job.ReportExcelPath));
    }

    [HttpGet]
    public IActionResult History()
    {
        var history = _db.GetHistory();
        return Json(history);
    }

    private static string FormatTime(TimeSpan value)
    {
        if (value < TimeSpan.Zero) value = TimeSpan.Zero;
        int hours = (int)value.TotalHours;
        int minutes = value.Minutes;
        int seconds = value.Seconds;
        return $"{hours:D2}h {minutes:D2}m {seconds:D2}s";
    }
}
