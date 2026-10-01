using System.Collections.Concurrent;
using System.Diagnostics;
using AUSBarcodeValidator.Models;

namespace AUSBarcodeValidator.Services;

public sealed class ValidationService
{
    private readonly PdfService _pdfService;
    private readonly BarcodeDetectionService _detectionService;
    private readonly ILogger<ValidationService> _logger;

    public ValidationService(
        PdfService pdfService,
        BarcodeDetectionService detectionService,
        ILogger<ValidationService> logger)
    {
        _pdfService = pdfService;
        _detectionService = detectionService;
        _logger = logger;
    }

    public async Task ProcessBatchAsync(
        string folderPath,
        int expectedPages,
        ValidationJob job,
        ValidationSettings settings,
        CancellationToken token,
        string? folderDisplayName = null,
        ValidationDbService? db = null,
        Action<PdfValidationResult>? onResultProcessed = null)
    {
        var pdfFiles = Directory.EnumerateFiles(folderPath, "*.pdf", SearchOption.TopDirectoryOnly)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        job.TotalFiles = pdfFiles.Count;
        if (pdfFiles.Count == 0)
            throw new InvalidOperationException("No PDF files found in folder!");

        var folderName = folderDisplayName ?? Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var results = new ConcurrentBag<PdfValidationResult>();
        var totalSw = Stopwatch.StartNew();

        int processedCounter = 0;
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, settings.MaxParallelWorkers),
            CancellationToken = token
        };

        // Assign serial numbers beforehand for ordered processing
        var indexedFiles = pdfFiles.Select((path, idx) => (Path: path, SrNo: idx + 1)).ToList();

        await Parallel.ForEachAsync(indexedFiles, parallelOptions, async (item, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            while (job.PauseRequested && !job.CancelRequested)
            {
                await Task.Delay(500, ct);
            }

            if (job.CancelRequested) return;

            var fileName = Path.GetFileName(item.Path);
            job.CurrentFile = fileName;

            var result = ValidatePdf(item.Path, folderName, expectedPages, job.TotalFiles, item.SrNo, settings, ct);
            results.Add(result);

            try
            {
                db?.SaveResult(job.Id, result);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist result for {File}", fileName);
            }

            lock (job)
            {
                job.Results.Add(result);
                Interlocked.Increment(ref processedCounter);
                job.ProcessedFiles = processedCounter;
                job.Status = $"Processing {job.ProcessedFiles}/{job.TotalFiles} : {fileName}";
            }

            onResultProcessed?.Invoke(result);
        });

        totalSw.Stop();

        // Sort results by SrNo
        var orderedResults = job.Results.OrderBy(r => r.SrNo).ToList();
        job.Results.Clear();
        job.Results.AddRange(orderedResults);

        // Compute Summary
        var validCount = orderedResults.Count(r => r.FinalStatus == "VALID");
        var mismatchCount = orderedResults.Count(r => r.FinalStatus.Contains("Mismatch", StringComparison.OrdinalIgnoreCase));
        var missingCount = orderedResults.Count(r => r.FinalStatus.Contains("Pages Count", StringComparison.OrdinalIgnoreCase));
        var notDetectedCount = orderedResults.Count(r => r.FinalStatus.Contains("Not Detected", StringComparison.OrdinalIgnoreCase));
        var invalidCount = orderedResults.Count - validCount;
        var totalPages = orderedResults.Sum(r => r.TotalPagesFound);
        var totalMatchedPages = orderedResults.Sum(r => r.MatchedPagesCount);
        var totalNotMatchedPages = orderedResults.Sum(r => r.MismatchPagesCount);
        var totalNotDetectedPages = orderedResults.Sum(r => r.NotDetectedPagesCount);

        double totalSecs = totalSw.Elapsed.TotalSeconds;
        double avgPdf = orderedResults.Count > 0 ? totalSecs / orderedResults.Count : 0;
        double avgPage = totalPages > 0 ? totalSecs / totalPages : 0;

        job.Summary = new ValidationSummary
        {
            TotalPdfs = job.TotalFiles,
            ProcessedPdfs = job.ProcessedFiles,
            ValidCount = validCount,
            MismatchCount = mismatchCount,
            NotDetectedCount = notDetectedCount,
            InvalidCount = invalidCount,
            ErrorCount = orderedResults.Count(r => r.FinalStatus == "ERROR"),
            TotalPages = totalPages,
            MatchedPages = totalMatchedPages,
            NotMatchedPages = totalNotMatchedPages,
            NotDetectedPages = totalNotDetectedPages,
            TotalProcessingSeconds = Math.Round(totalSecs, 2),
            AveragePdfSeconds = Math.Round(avgPdf, 2),
            AveragePageSeconds = Math.Round(avgPage, 3),
            PagesPerSecond = avgPage > 0 ? Math.Round(1.0 / avgPage, 1) : 0,
            PdfsPerMinute = avgPdf > 0 ? Math.Round(60.0 / avgPdf, 1) : 0,
            DetectionSuccessRate = totalPages > 0 ? Math.Round((double)(totalMatchedPages + totalNotMatchedPages) / totalPages * 100.0, 1) : 0
        };
    }

    public PdfValidationResult ValidatePdf(
        string pdfPath,
        string folderName,
        int expectedPages,
        int totalFiles,
        int srNo,
        ValidationSettings settings,
        CancellationToken token)
    {
        var pdfSw = Stopwatch.StartNew();
        var fileName = Path.GetFileName(pdfPath);
        var pageDetails = new List<BarcodeDetectionResult>();
        var mismatchPages = new List<string>();
        var mismatchDetails = new List<string>();
        var notDetectedPages = new List<string>();
        string? referenceBarcode = null;
        int totalPages = 0;
        int matchedPages = 0;
        int mismatchCount = 0;
        int notDetectedCount = 0;

        try
        {
            // Initial DPI attempt (default 250 DPI for crystal clear barcode decoding)
            using var docReader = _pdfService.OpenDocument(pdfPath, settings.Dpi);
            totalPages = docReader.GetPageCount();

            int step = settings.CheckOnlyOddPages ? 2 : 1;

            for (int i = 0; i < totalPages; i += step)
            {
                token.ThrowIfCancellationRequested();
                var pageNumber = i + 1;

                var detection = _detectionService.DetectBarcode(docReader, i, fileName, settings, referenceBarcode);

                // If first attempt failed at initial DPI, auto-retry with higher DPI (300 DPI)
                if (detection.DetectionStatus == DetectionStatus.NOT_DETECTED && settings.RetryDpi > settings.Dpi)
                {
                    try
                    {
                        using var retryReader = _pdfService.OpenDocument(pdfPath, settings.RetryDpi);
                        var retryDetection = _detectionService.DetectBarcode(retryReader, i, fileName, settings, referenceBarcode);
                        if (retryDetection.DetectionStatus == DetectionStatus.DETECTED)
                        {
                            detection = retryDetection;
                            detection.AttemptNumber = 2;
                        }
                    }
                    catch { }
                }

                if (detection.DetectionStatus == DetectionStatus.DETECTED && !string.IsNullOrWhiteSpace(detection.Barcode))
                {
                    if (referenceBarcode == null)
                    {
                        referenceBarcode = detection.Barcode;
                        detection.DetectionStatus = DetectionStatus.REFERENCE;
                        matchedPages++;
                    }
                    else if (string.Equals(detection.Barcode, referenceBarcode, StringComparison.Ordinal))
                    {
                        detection.DetectionStatus = DetectionStatus.MATCH;
                        matchedPages++;
                    }
                    else
                    {
                        detection.DetectionStatus = DetectionStatus.NOT_MATCH;
                        mismatchCount++;
                        mismatchPages.Add(pageNumber.ToString());
                        mismatchDetails.Add($"Page {pageNumber}: {detection.Barcode}");
                    }
                }
                else
                {
                    detection.DetectionStatus = DetectionStatus.NOT_DETECTED;
                    notDetectedCount++;
                    notDetectedPages.Add(pageNumber.ToString());
                }

                pageDetails.Add(detection);
            }

            int missingCount = Math.Max(0, expectedPages - totalPages);

            string status;
            if (missingCount > 0)
                status = "INVALID (Pages Count not Match)";
            else if (mismatchCount > 0)
                status = "INVALID (Barcode Mismatch)";
            else if (notDetectedCount > 0)
                status = "INVALID (Barcode Not Detected)";
            else if (string.IsNullOrWhiteSpace(referenceBarcode))
                status = "INVALID (No Barcode Found)";
            else
                status = "VALID";

            pdfSw.Stop();

            return new PdfValidationResult
            {
                SrNo = srNo,
                FolderName = folderName,
                FileName = fileName,
                FilePath = pdfPath,
                TotalPdfsInFolder = totalFiles,
                ExpectedPages = expectedPages,
                TotalPagesFound = totalPages,
                ReferenceBarcode = referenceBarcode ?? "-",
                MatchedPagesCount = matchedPages,
                MismatchPagesCount = mismatchCount,
                MismatchPages = mismatchPages.Count == 0 ? "-" : string.Join(", ", mismatchPages),
                MismatchDetails = mismatchDetails.Count == 0 ? "-" : string.Join(", ", mismatchDetails),
                NotDetectedPagesCount = notDetectedCount,
                NotDetectedPages = notDetectedPages.Count == 0 ? "-" : string.Join(", ", notDetectedPages),
                MissingPagesCount = missingCount,
                FinalStatus = status,
                PageDetails = pageDetails,
                ProcessingSeconds = Math.Round(pdfSw.Elapsed.TotalSeconds, 2),
                ProcessedTime = DateTime.Now
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            pdfSw.Stop();
            _logger.LogError(ex, "Error processing PDF {File}", fileName);

            return new PdfValidationResult
            {
                SrNo = srNo,
                FolderName = folderName,
                FileName = fileName,
                FilePath = pdfPath,
                TotalPdfsInFolder = totalFiles,
                ExpectedPages = expectedPages,
                TotalPagesFound = totalPages,
                ReferenceBarcode = "Error",
                MismatchPages = "-",
                MismatchDetails = ex.Message,
                NotDetectedPages = "-",
                MissingPagesCount = Math.Max(0, expectedPages - totalPages),
                FinalStatus = "ERROR",
                ProcessingSeconds = Math.Round(pdfSw.Elapsed.TotalSeconds, 2),
                ProcessedTime = DateTime.Now
            };
        }
    }
}
