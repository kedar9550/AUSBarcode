using Docnet.Core;
using Docnet.Core.Models;
using Docnet.Core.Readers;

namespace AUSBarcodeValidator.Services;

public sealed class PdfService
{
    public IDocReader OpenDocument(string pdfPath, int dpi = 250)
    {
        // 72 standard PDF points per inch. 250 DPI = 250.0 / 72.0 = ~3.472 scaling factor
        // 300 DPI = 300.0 / 72.0 = ~4.167 scaling factor
        double scalingFactor = Math.Max(1.0, dpi / 72.0);
        return DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(scalingFactor));
    }
}
