# AUS Examcell PDF Barcode Validator - ASP.NET Core

Converted from the supplied Python/Tkinter application.

## Requirements
- .NET 8 SDK
- Windows recommended because PdfiumViewer uses native Windows PDF rendering.

## Run
```bash
dotnet restore
dotnet run
```
Then open the displayed localhost URL.

## Current behavior
- Multiple PDF upload
- Expected page count
- 300 DPI PDF rendering
- First-page reference barcode
- Odd-page barcode comparison
- Missing-page validation
- VALID / INVALID statuses
- Background processing
- Progress and estimated remaining time
- Cancellation
- CSV report download

## Important
The original application selected a local folder with Tkinter. A browser cannot directly expose an arbitrary server/local folder to ASP.NET. This version therefore uploads the PDFs through the browser and processes them server-side.
