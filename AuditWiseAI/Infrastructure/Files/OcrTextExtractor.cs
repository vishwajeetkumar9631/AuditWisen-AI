using Tesseract;

namespace AuditWiseAI.Services;

public interface IOcrTextExtractor
{
    Task<OcrTextResult> ExtractAsync(IFormFile file, CancellationToken cancellationToken);
}

public sealed record OcrTextResult(string Text, float Confidence);

public sealed class TesseractOcrTextExtractor(
    IWebHostEnvironment environment,
    ILogger<TesseractOcrTextExtractor> logger) : IOcrTextExtractor
{
    public async Task<OcrTextResult> ExtractAsync(IFormFile file, CancellationToken cancellationToken)
    {
        var tessdataPath = Path.Combine(environment.ContentRootPath, "tessdata");
        if (!File.Exists(Path.Combine(tessdataPath, "eng.traineddata")))
        {
            throw new InvalidOperationException($"OCR language data was not found at '{tessdataPath}'.");
        }

        await using var stream = file.OpenReadStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);

        try
        {
            using var engine = new TesseractEngine(tessdataPath, "eng", EngineMode.Default);
            using var image = Pix.LoadFromMemory(memory.ToArray());
            using var page = engine.Process(image);

            var text = page.GetText() ?? string.Empty;
            return new OcrTextResult(text.Trim(), page.GetMeanConfidence());
        }
        catch (TesseractException exception)
        {
            logger.LogWarning(exception, "OCR extraction failed for file {FileName}.", file.FileName);
            throw new InvalidOperationException("OCR could not read text from the uploaded image.", exception);
        }
    }
}
