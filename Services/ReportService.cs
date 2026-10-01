using System.Globalization;
using AUSBarcodeValidator.Models;
using ClosedXML.Excel;
using CsvHelper;

namespace AUSBarcodeValidator.Services;

public sealed class ReportService
{
    private readonly IWebHostEnvironment _env;

    public ReportService(IWebHostEnvironment env)
    {
        _env = env;
    }

    public (string csvPath, string excelPath) GenerateReports(Guid jobId, List<PdfValidationResult> results, ValidationSummary summary)
    {
        var reportsDir = Path.Combine(_env.ContentRootPath, "App_Data", "Reports");
        Directory.CreateDirectory(reportsDir);

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var csvPath = Path.Combine(reportsDir, $"barcode_report_{timestamp}.csv");
        var excelPath = Path.Combine(reportsDir, $"barcode_report_{timestamp}.xlsx");

        // 1. Generate CSV Report using CsvHelper
        using (var writer = new StreamWriter(csvPath, false, System.Text.Encoding.UTF8))
        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            csv.WriteField("Sr No");
            csv.WriteField("Folder Name");
            csv.WriteField("File Name");
            csv.WriteField("Total PDFs in Folder");
            csv.WriteField("Expected Pages");
            csv.WriteField("Total Pages Found");
            csv.WriteField("Reference Barcode");
            csv.WriteField("Mismatch Pages");
            csv.WriteField("Mismatch Details");
            csv.WriteField("Not Detected Pages");
            csv.WriteField("Missing Pages Count");
            csv.WriteField("Final Status");
            csv.WriteField("Processing Time (s)");
            csv.WriteField("Processed Time");
            csv.NextRecord();

            foreach (var r in results)
            {
                csv.WriteField(r.SrNo);
                csv.WriteField(r.FolderName);
                csv.WriteField(r.FileName);
                csv.WriteField(r.TotalPdfsInFolder);
                csv.WriteField(r.ExpectedPages);
                csv.WriteField(r.TotalPagesFound);
                csv.WriteField(r.ReferenceBarcode);
                csv.WriteField(r.MismatchPages);
                csv.WriteField(r.MismatchDetails);
                csv.WriteField(r.NotDetectedPages);
                csv.WriteField(r.MissingPagesCount);
                csv.WriteField(r.FinalStatus);
                csv.WriteField(r.ProcessingSeconds);
                csv.WriteField(r.ProcessedTime.ToString("dd-MM-yyyy HH:mm:ss"));
                csv.NextRecord();
            }
        }

        // 2. Generate Professional Excel XLSX using ClosedXML
        using (var workbook = new XLWorkbook())
        {
            // Sheet 1: Validation Summary
            var wsSummary = workbook.Worksheets.Add("Summary");
            wsSummary.Cell(1, 1).Value = "PDF Barcode Validation Summary Report";
            wsSummary.Cell(1, 1).Style.Font.Bold = true;
            wsSummary.Cell(1, 1).Style.Font.FontSize = 14;
            wsSummary.Cell(1, 1).Style.Font.FontColor = XLColor.FromHtml("#00457c");

            wsSummary.Cell(3, 1).Value = "Metric";
            wsSummary.Cell(3, 2).Value = "Value";
            wsSummary.Range(3, 1, 3, 2).Style.Font.Bold = true;
            wsSummary.Range(3, 1, 3, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#00457c");
            wsSummary.Range(3, 1, 3, 2).Style.Font.FontColor = XLColor.White;

            var metrics = new (string Name, object Val)[]
            {
                ("Total PDFs", summary.TotalPdfs),
                ("Processed PDFs", summary.ProcessedPdfs),
                ("Valid / Matched PDFs", summary.ValidCount),
                ("Barcode Mismatch PDFs", summary.MismatchCount),
                ("Barcode Not Detected PDFs", summary.NotDetectedCount),
                ("Invalid Count / Other Issues", summary.InvalidCount),
                ("Total Pages Scanned", summary.TotalPages),
                ("Total Matched Pages", summary.MatchedPages),
                ("Total Mismatched Pages", summary.NotMatchedPages),
                ("Total Processing Time (Seconds)", summary.TotalProcessingSeconds),
                ("Average Time Per PDF (Seconds)", summary.AveragePdfSeconds),
                ("Average Time Per Page (Seconds)", summary.AveragePageSeconds),
                ("Processing Speed (Pages/Sec)", summary.PagesPerSecond),
                ("Detection Success Rate (%)", $"{summary.DetectionSuccessRate}%"),
                ("Report Generated At", DateTime.Now.ToString("dd-MM-yyyy HH:mm:ss"))
            };

            for (int i = 0; i < metrics.Length; i++)
            {
                int row = 4 + i;
                wsSummary.Cell(row, 1).Value = metrics[i].Name;
                wsSummary.Cell(row, 2).SetValue(metrics[i].Val.ToString());
                if (i % 2 == 1) wsSummary.Range(row, 1, row, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#f4f7f9");
            }
            wsSummary.Columns().AdjustToContents();

            // Sheet 2: Detailed Results
            var wsDetails = workbook.Worksheets.Add("PDF Validation Details");
            var headers = new[]
            {
                "Sr No", "Folder Name", "File Name", "Total PDFs", "Expected Pages",
                "Total Pages Found", "Reference Barcode", "Mismatch Pages", "Mismatch Details",
                "Not Detected Pages", "Missing Count", "Final Status", "Time (s)", "Processed Date"
            };

            for (int col = 0; col < headers.Length; col++)
            {
                var cell = wsDetails.Cell(1, col + 1);
                cell.Value = headers[col];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#00457c");
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            int currRow = 2;
            foreach (var r in results)
            {
                wsDetails.Cell(currRow, 1).Value = r.SrNo;
                wsDetails.Cell(currRow, 2).Value = r.FolderName;
                wsDetails.Cell(currRow, 3).Value = r.FileName;
                wsDetails.Cell(currRow, 4).Value = r.TotalPdfsInFolder;
                wsDetails.Cell(currRow, 5).Value = r.ExpectedPages;
                wsDetails.Cell(currRow, 6).Value = r.TotalPagesFound;
                wsDetails.Cell(currRow, 7).Value = r.ReferenceBarcode;
                wsDetails.Cell(currRow, 8).Value = r.MismatchPages;
                wsDetails.Cell(currRow, 9).Value = r.MismatchDetails;
                wsDetails.Cell(currRow, 10).Value = r.NotDetectedPages;
                wsDetails.Cell(currRow, 11).Value = r.MissingPagesCount;
                wsDetails.Cell(currRow, 12).Value = r.FinalStatus;
                wsDetails.Cell(currRow, 13).Value = r.ProcessingSeconds;
                wsDetails.Cell(currRow, 14).Value = r.ProcessedTime.ToString("dd-MM-yyyy HH:mm:ss");

                // Status highlighting
                var statusCell = wsDetails.Cell(currRow, 12);
                if (r.FinalStatus == "VALID")
                {
                    statusCell.Style.Font.FontColor = XLColor.FromHtml("#155724");
                    statusCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#d4edda");
                }
                else
                {
                    statusCell.Style.Font.FontColor = XLColor.FromHtml("#721c24");
                    statusCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#f8d7da");
                }

                if (currRow % 2 == 1 && r.FinalStatus == "VALID")
                {
                    wsDetails.Range(currRow, 1, currRow, 11).Style.Fill.BackgroundColor = XLColor.FromHtml("#fbfcfd");
                }

                currRow++;
            }
            wsDetails.Columns().AdjustToContents();

            workbook.SaveAs(excelPath);
        }

        // Rotate old reports - keep only the most recent N reports to limit disk usage
        try
        {
            const int keepLatest = 2;
            // Handle CSV files
            var csvFiles = new DirectoryInfo(reportsDir).GetFiles("*.csv").OrderByDescending(f => f.CreationTime).ToList();
            foreach (var f in csvFiles.Skip(keepLatest))
            {
                try { f.Delete(); } catch { /* ignore individual delete failures */ }
            }

            // Handle XLSX files
            var xlsxFiles = new DirectoryInfo(reportsDir).GetFiles("*.xlsx").OrderByDescending(f => f.CreationTime).ToList();
            foreach (var f in xlsxFiles.Skip(keepLatest))
            {
                try { f.Delete(); } catch { /* ignore individual delete failures */ }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Report rotation error: {ex.Message}");
        }

        return (csvPath, excelPath);
    }
}
