# 🚀 Simple Barcode Validator Integration Guide

Integrating this module into your existing ASP.NET Core project requires only **4 quick steps**:

---

### 1. Add NuGet Packages
Add these two packages to your `.csproj`:
```xml
<PackageReference Include="Docnet.Core" Version="2.6.0" />
<PackageReference Include="ZXing.Net" Version="0.16.11" />
```

---

### 2. Copy the Following Files to Your Project

| Destination Folder | Files to Copy |
|---|---|
| 📁 `Models/` | `ValidationJob.cs`, `ValidationResultModel.cs` |
| 📁 `Services/` | `BarcodeValidationService.cs`, `ValidationJobService.cs` |
| 📁 `Controllers/` | `HomeController.cs` *(or copy its methods into your existing controller)* |
| 📁 `Views/Home/` | `Index.cshtml` |
| 📁 `wwwroot/css/` | `site.css` |

---

### 3. Register Services in `Program.cs`

Add these 2 lines in your `Program.cs`:
```csharp
using AUSBarcodeValidator.Services;

builder.Services.AddSingleton<BarcodeValidationService>();
builder.Services.AddSingleton<ValidationJobService>();
```

---

### 4. Run the Project
```bash
dotnet run
```
Open `http://localhost:5000` (or your application port) to access the Barcode Validator UI!
