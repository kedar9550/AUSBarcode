using System.Collections.Concurrent;
using AUSBarcodeValidator.Models;
using Docnet.Core;
using Docnet.Core.Models;
using ZXing;
using ZXing.Common;

namespace AUSBarcodeValidator.Services;

public sealed class BarcodeValidationService
{
    private static readonly BarcodeReaderGeneric Reader = new()
    {
        AutoRotate = true,
        Options = new DecodingOptions
        {
            TryHarder = true,
            TryInverted = true,
            PossibleFormats = new List<BarcodeFormat>
            {
                BarcodeFormat.CODE_128,
                BarcodeFormat.CODE_39,
                BarcodeFormat.EAN_13,
                BarcodeFormat.EAN_8,
                BarcodeFormat.QR_CODE,
                BarcodeFormat.PDF_417,
                BarcodeFormat.ITF,
                BarcodeFormat.CODABAR,
                BarcodeFormat.DATA_MATRIX
            }
        }
    };

    public List<ValidationResultModel> ProcessFolder(
        string folderPath,
        int expectedPages,
        ValidationJob job,
        CancellationToken token,
        string? folderDisplayName = null,
        ValidationDbService? db = null)
    {
        var pdfFiles = Directory.EnumerateFiles(folderPath, "*.pdf", SearchOption.TopDirectoryOnly)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        job.TotalFiles = pdfFiles.Count;
        if (pdfFiles.Count == 0)
            throw new InvalidOperationException("No PDF files found!");

        var results = new List<ValidationResultModel>();
        var srNo = 1;
        var folderName = folderDisplayName ?? Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        foreach (var pdfPath in pdfFiles)
        {
            token.ThrowIfCancellationRequested();
            if (job.CancelRequested) break;

            var fileName = Path.GetFileName(pdfPath);
            job.CurrentFile = fileName;
            job.Status = $"Processing {job.ProcessedFiles + 1}/{job.TotalFiles} : {fileName}";

            var result = ValidatePdf(pdfPath, folderName, expectedPages, job.TotalFiles, srNo, token);
            results.Add(result);
            job.Results.Add(result);
            
            // Persist immediately to Backend Database
            try
            {
                db?.SaveResult(job.Id, result);
            }
            catch
            {
                // Non-blocking database write
            }

            srNo++;
            job.ProcessedFiles++;
        }

        return results;
    }

    public ValidationResultModel ValidatePdf(
        string pdfPath,
        string folderName,
        int expectedPages,
        int totalFiles,
        int srNo,
        CancellationToken token)
    {
        var mismatchPages = new List<string>();
        var mismatchDetails = new List<string>();
        var missingPages = new List<string>();
        string? referenceBarcode = null;
        var totalPages = 0;

        try
        {
            using var docReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(1400, 2000));
            totalPages = docReader.GetPageCount();

            if (totalPages > 0)
            {
                referenceBarcode = ReadBarcodeFromPage(docReader, 0);
            }

            // Odd pages in 0-index: 0, 2, 4, 6... (page numbers 1, 3, 5, 7...)
            for (var i = 0; i < totalPages; i += 2)
            {
                token.ThrowIfCancellationRequested();

                var barcode = (i == 0) ? referenceBarcode : ReadBarcodeFromPage(docReader, i);
                var pageNumber = i + 1;

                if (!string.IsNullOrWhiteSpace(barcode) &&
                    !string.IsNullOrWhiteSpace(referenceBarcode) &&
                    !string.Equals(barcode, referenceBarcode, StringComparison.Ordinal))
                {
                    mismatchPages.Add(pageNumber.ToString());
                    mismatchDetails.Add($"Page {pageNumber}: {barcode}");
                }
            }

            for (var page = 1; page <= expectedPages; page += 2)
            {
                if (page > totalPages)
                    missingPages.Add(page.ToString());
            }

            string status;
            if (mismatchPages.Count == 0 && missingPages.Count == 0)
                status = "VALID";
            else if (missingPages.Count > 0)
                status = "INVALID (Pages Count not Match)";
            else if (mismatchPages.Count > 0)
                status = "INVALID (Barcode Mismatch)";
            else
                status = "INVALID";

            return new ValidationResultModel
            {
                SrNo = srNo,
                FolderName = folderName,
                FileName = Path.GetFileName(pdfPath),
                TotalPdfsInFolder = totalFiles,
                ExpectedPages = expectedPages,
                TotalPagesFound = totalPages,
                ReferenceBarcode = string.IsNullOrWhiteSpace(referenceBarcode) ? "-" : referenceBarcode,
                MismatchPages = mismatchPages.Count == 0 ? "-" : string.Join(", ", mismatchPages),
                MismatchDetails = mismatchDetails.Count == 0 ? "-" : string.Join(", ", mismatchDetails),
                MissingPagesCount = Math.Max(0, expectedPages - totalPages),
                FinalStatus = status,
                ProcessedTime = DateTime.Now
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ValidationResultModel
            {
                SrNo = srNo,
                FolderName = folderName,
                FileName = Path.GetFileName(pdfPath),
                TotalPdfsInFolder = totalFiles,
                ExpectedPages = expectedPages,
                TotalPagesFound = totalPages,
                ReferenceBarcode = "Error",
                MismatchPages = "-",
                MismatchDetails = ex.Message,
                MissingPagesCount = Math.Max(0, expectedPages - totalPages),
                FinalStatus = "ERROR",
                ProcessedTime = DateTime.Now
            };
        }
    }

    private static string? ReadBarcodeFromPage(Docnet.Core.Readers.IDocReader docReader, int pageIndex)
    {
        try
        {
            using var pageReader = docReader.GetPageReader(pageIndex);
            var width = pageReader.GetPageWidth();
            var height = pageReader.GetPageHeight();
            var bytes = pageReader.GetImage(); // BGRA 32-bit

            var lumSource = new RGBLuminanceSource(bytes, width, height, RGBLuminanceSource.BitmapFormat.BGRA32);
            
            // 1. Full-page decode
            var result = Reader.Decode(lumSource);
            if (result != null && !string.IsNullOrWhiteSpace(result.Text))
                return result.Text.Trim();

            // 2. Header crop (top 40%)
            var headerHeight = (int)(height * 0.4);
            if (headerHeight > 50)
            {
                var cropTop = lumSource.crop(0, 0, width, headerHeight);
                result = Reader.Decode(cropTop);
                if (result != null && !string.IsNullOrWhiteSpace(result.Text))
                    return result.Text.Trim();
            }

            // 3. Footer crop (bottom 40%)
            var footerHeight = (int)(height * 0.4);
            var topOffset = height - footerHeight;
            if (footerHeight > 50 && topOffset >= 0)
            {
                var cropBottom = lumSource.crop(0, topOffset, width, footerHeight);
                result = Reader.Decode(cropBottom);
                if (result != null && !string.IsNullOrWhiteSpace(result.Text))
                    return result.Text.Trim();
            }

            // 4. Inversion fallback
            if (lumSource.InversionSupported)
            {
                var inverted = lumSource.invert();
                result = Reader.Decode(inverted);
                if (result != null && !string.IsNullOrWhiteSpace(result.Text))
                    return result.Text.Trim();
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
