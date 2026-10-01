using System.Diagnostics;
using AUSBarcodeValidator.Models;
using Docnet.Core.Readers;
using ZXing;
using ZXing.Common;

namespace AUSBarcodeValidator.Services;

public sealed class BarcodeDetectionService
{
    // High-precision formats (excludes noise-prone ITF/CODABAR unless explicitly needed)
    private static readonly List<BarcodeFormat> StandardFormats = new()
    {
        BarcodeFormat.CODE_128,
        BarcodeFormat.CODE_39,
        BarcodeFormat.EAN_13,
        BarcodeFormat.EAN_8,
        BarcodeFormat.QR_CODE,
        BarcodeFormat.PDF_417,
        BarcodeFormat.DATA_MATRIX
    };

    private static readonly List<BarcodeFormat> Code128Only = new()
    {
        BarcodeFormat.CODE_128,
        BarcodeFormat.CODE_39
    };

    public BarcodeDetectionResult DetectBarcode(
        IDocReader docReader,
        int pageIndex,
        string fileName,
        ValidationSettings settings,
        string? expectedReferenceBarcode = null)
    {
        var sw = Stopwatch.StartNew();
        var pageNumber = pageIndex + 1;

        try
        {
            using var pageReader = docReader.GetPageReader(pageIndex);
            var width = pageReader.GetPageWidth();
            var height = pageReader.GetPageHeight();
            var bgraBytes = pageReader.GetImage(); // BGRA 32-bit (width * height * 4)

            var targetFormats = settings.ExpectedFormat == "CODE_128" ? Code128Only : StandardFormats;

            // Attempt 1: Barcode Target Zones (Bottom 35% Footer & Top 35% Header) - Maximizes accuracy, avoids body text noise
            var (barcode1, format1) = TryDecodeBarcodeZones(bgraBytes, width, height, settings.ContrastAlpha, targetFormats, expectedReferenceBarcode);
            if (!string.IsNullOrWhiteSpace(barcode1))
            {
                sw.Stop();
                return new BarcodeDetectionResult
                {
                    FileName = fileName,
                    PageNumber = pageNumber,
                    Barcode = barcode1,
                    BarcodeFormat = format1,
                    DetectionStatus = DetectionStatus.DETECTED,
                    ProcessingTimeMs = sw.ElapsedMilliseconds,
                    AttemptNumber = 1
                };
            }

            // Attempt 2: Full Page Scan (Contrast-Enhanced with GlobalHistogram & Hybrid)
            var (barcode2, format2) = TryDecodeFullPage(bgraBytes, width, height, settings.ContrastAlpha, targetFormats);
            if (!string.IsNullOrWhiteSpace(barcode2))
            {
                sw.Stop();
                return new BarcodeDetectionResult
                {
                    FileName = fileName,
                    PageNumber = pageNumber,
                    Barcode = barcode2,
                    BarcodeFormat = format2,
                    DetectionStatus = DetectionStatus.DETECTED,
                    ProcessingTimeMs = sw.ElapsedMilliseconds,
                    AttemptNumber = 2
                };
            }

            // Attempt 3: Rotated Versions (90°, 180°, 270°)
            var (barcode3, format3) = TryDecodeRotations(bgraBytes, width, height, settings.ContrastAlpha, targetFormats);
            if (!string.IsNullOrWhiteSpace(barcode3))
            {
                sw.Stop();
                return new BarcodeDetectionResult
                {
                    FileName = fileName,
                    PageNumber = pageNumber,
                    Barcode = barcode3,
                    BarcodeFormat = format3,
                    DetectionStatus = DetectionStatus.DETECTED,
                    ProcessingTimeMs = sw.ElapsedMilliseconds,
                    AttemptNumber = 3
                };
            }

            // Attempt 4: Raw RGBLuminanceSource with Inversion Fallback
            var (barcode4, format4) = TryDecodeRaw(bgraBytes, width, height, targetFormats);
            if (!string.IsNullOrWhiteSpace(barcode4))
            {
                sw.Stop();
                return new BarcodeDetectionResult
                {
                    FileName = fileName,
                    PageNumber = pageNumber,
                    Barcode = barcode4,
                    BarcodeFormat = format4,
                    DetectionStatus = DetectionStatus.DETECTED,
                    ProcessingTimeMs = sw.ElapsedMilliseconds,
                    AttemptNumber = 4
                };
            }

            sw.Stop();
            return new BarcodeDetectionResult
            {
                FileName = fileName,
                PageNumber = pageNumber,
                Barcode = null,
                BarcodeFormat = null,
                DetectionStatus = DetectionStatus.NOT_DETECTED,
                ProcessingTimeMs = sw.ElapsedMilliseconds,
                AttemptNumber = settings.MaxRetries
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new BarcodeDetectionResult
            {
                FileName = fileName,
                PageNumber = pageNumber,
                Barcode = null,
                BarcodeFormat = null,
                DetectionStatus = DetectionStatus.ERROR,
                ProcessingTimeMs = sw.ElapsedMilliseconds,
                AttemptNumber = 1,
                ErrorMessage = ex.Message
            };
        }
    }

    private static (string? text, string? format) TryDecodeBarcodeZones(
        byte[] bgra,
        int width,
        int height,
        float alpha,
        List<BarcodeFormat> formats,
        string? expectedRef)
    {
        byte[] gray = ToContrastEnhancedGrayscale(bgra, width, height, alpha);
        var lumSource = new PlanarYUVLuminanceSource(gray, width, height, 0, 0, width, height, false);

        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                TryInverted = true,
                PossibleFormats = formats
            }
        };

        var multiReader = new MultiFormatReader
        {
            Hints = new Dictionary<DecodeHintType, object>
            {
                { DecodeHintType.TRY_HARDER, true },
                { DecodeHintType.POSSIBLE_FORMATS, formats }
            }
        };

        var cropH = (int)(height * 0.35);
        if (cropH > 50)
        {
            // 1. Zone A: Bottom 35% (Footer Barcode - Standard Exam Page Placement)
            var topOffset = height - cropH;
            if (topOffset >= 0)
            {
                var bottomCrop = lumSource.crop(0, topOffset, width, cropH);

                var res = reader.Decode(bottomCrop);
                if (res != null && !string.IsNullOrWhiteSpace(res.Text))
                    return (res.Text.Trim(), res.BarcodeFormat.ToString());

                try
                {
                    var bottomGh = new BinaryBitmap(new GlobalHistogramBinarizer(bottomCrop));
                    var resGh = multiReader.decodeWithState(bottomGh);
                    if (resGh != null && !string.IsNullOrWhiteSpace(resGh.Text))
                        return (resGh.Text.Trim(), resGh.BarcodeFormat.ToString());
                }
                catch { }
            }

            // 2. Zone B: Top 35% (Header Barcode - Standard Front Page / Hall Ticket Placement)
            var topCrop = lumSource.crop(0, 0, width, cropH);
            var resTop = reader.Decode(topCrop);
            if (resTop != null && !string.IsNullOrWhiteSpace(resTop.Text))
                return (resTop.Text.Trim(), resTop.BarcodeFormat.ToString());

            try
            {
                var topGh = new BinaryBitmap(new GlobalHistogramBinarizer(topCrop));
                var resGh = multiReader.decodeWithState(topGh);
                if (resGh != null && !string.IsNullOrWhiteSpace(resGh.Text))
                    return (resGh.Text.Trim(), resGh.BarcodeFormat.ToString());
            }
            catch { }
        }

        return (null, null);
    }

    private static (string? text, string? format) TryDecodeFullPage(
        byte[] bgra,
        int width,
        int height,
        float alpha,
        List<BarcodeFormat> formats)
    {
        byte[] gray = ToContrastEnhancedGrayscale(bgra, width, height, alpha);
        var lumSource = new PlanarYUVLuminanceSource(gray, width, height, 0, 0, width, height, false);

        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                TryInverted = true,
                PossibleFormats = formats
            }
        };

        var res = reader.Decode(lumSource);
        if (res != null && !string.IsNullOrWhiteSpace(res.Text))
            return (res.Text.Trim(), res.BarcodeFormat.ToString());

        var multiReader = new MultiFormatReader
        {
            Hints = new Dictionary<DecodeHintType, object>
            {
                { DecodeHintType.TRY_HARDER, true },
                { DecodeHintType.POSSIBLE_FORMATS, formats }
            }
        };

        try
        {
            var ghBitmap = new BinaryBitmap(new GlobalHistogramBinarizer(lumSource));
            var resGh = multiReader.decodeWithState(ghBitmap);
            if (resGh != null && !string.IsNullOrWhiteSpace(resGh.Text))
                return (resGh.Text.Trim(), resGh.BarcodeFormat.ToString());
        }
        catch { }

        return (null, null);
    }

    private static (string? text, string? format) TryDecodeRotations(
        byte[] bgra,
        int width,
        int height,
        float alpha,
        List<BarcodeFormat> formats)
    {
        byte[] gray = ToContrastEnhancedGrayscale(bgra, width, height, alpha);
        var multiReader = new MultiFormatReader
        {
            Hints = new Dictionary<DecodeHintType, object>
            {
                { DecodeHintType.TRY_HARDER, true },
                { DecodeHintType.POSSIBLE_FORMATS, formats }
            }
        };

        // 90 degrees rotation
        byte[] rot90 = RotateGray90(gray, width, height);
        var lum90 = new PlanarYUVLuminanceSource(rot90, height, width, 0, 0, height, width, false);
        try
        {
            var bm90 = new BinaryBitmap(new GlobalHistogramBinarizer(lum90));
            var res = multiReader.decodeWithState(bm90);
            if (res != null && !string.IsNullOrWhiteSpace(res.Text))
                return (res.Text.Trim(), res.BarcodeFormat.ToString());
        }
        catch { }

        // 180 degrees rotation
        byte[] rot180 = RotateGray180(gray, width, height);
        var lum180 = new PlanarYUVLuminanceSource(rot180, width, height, 0, 0, width, height, false);
        try
        {
            var bm180 = new BinaryBitmap(new GlobalHistogramBinarizer(lum180));
            var res = multiReader.decodeWithState(bm180);
            if (res != null && !string.IsNullOrWhiteSpace(res.Text))
                return (res.Text.Trim(), res.BarcodeFormat.ToString());
        }
        catch { }

        // 270 degrees rotation
        byte[] rot270 = RotateGray270(gray, width, height);
        var lum270 = new PlanarYUVLuminanceSource(rot270, height, width, 0, 0, height, width, false);
        try
        {
            var bm270 = new BinaryBitmap(new GlobalHistogramBinarizer(lum270));
            var res = multiReader.decodeWithState(bm270);
            if (res != null && !string.IsNullOrWhiteSpace(res.Text))
                return (res.Text.Trim(), res.BarcodeFormat.ToString());
        }
        catch { }

        return (null, null);
    }

    private static (string? text, string? format) TryDecodeRaw(
        byte[] bgra,
        int width,
        int height,
        List<BarcodeFormat> formats)
    {
        var rgbLum = new RGBLuminanceSource(bgra, width, height, RGBLuminanceSource.BitmapFormat.BGRA32);
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                TryInverted = true,
                PossibleFormats = formats
            }
        };

        var res = reader.Decode(rgbLum);
        if (res != null && !string.IsNullOrWhiteSpace(res.Text))
            return (res.Text.Trim(), res.BarcodeFormat.ToString());

        if (rgbLum.InversionSupported)
        {
            var inverted = rgbLum.invert();
            res = reader.Decode(inverted);
            if (res != null && !string.IsNullOrWhiteSpace(res.Text))
                return (res.Text.Trim(), res.BarcodeFormat.ToString());
        }

        return (null, null);
    }

    private static byte[] ToContrastEnhancedGrayscale(byte[] bgra, int width, int height, float alpha)
    {
        var totalPixels = width * height;
        var gray = new byte[totalPixels];

        for (int p = 0, b = 0; p < totalPixels; p++, b += 4)
        {
            byte blue = bgra[b];
            byte green = bgra[b + 1];
            byte red = bgra[b + 2];

            int grayVal = (red * 299 + green * 587 + blue * 114) / 1000;
            int enhanced = (int)(grayVal * alpha);
            if (enhanced > 255) enhanced = 255;
            else if (enhanced < 0) enhanced = 0;

            gray[p] = (byte)enhanced;
        }

        return gray;
    }

    private static byte[] RotateGray90(byte[] gray, int w, int h)
    {
        var rotated = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                rotated[x * h + (h - 1 - y)] = gray[y * w + x];
            }
        }
        return rotated;
    }

    private static byte[] RotateGray180(byte[] gray, int w, int h)
    {
        var rotated = new byte[w * h];
        var total = w * h;
        for (int i = 0; i < total; i++)
        {
            rotated[total - 1 - i] = gray[i];
        }
        return rotated;
    }

    private static byte[] RotateGray270(byte[] gray, int w, int h)
    {
        var rotated = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                rotated[(w - 1 - x) * h + y] = gray[y * w + x];
            }
        }
        return rotated;
    }
}
