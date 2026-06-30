using System.Text;
using AuditWiseAI.Models;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig;

namespace AuditWiseAI.Services;

public static class AuditFileLimits
{
    public const int MaxPayloadCharacters = 1_000_000;
    public const long MaxUploadBytes = 10_000_000;
}

public interface IAuditFileReader
{
    Task<AuditFileReadResult> ReadAsync(HttpRequest httpRequest, CancellationToken cancellationToken);
}

public sealed record AuditFileReadResult(AuditRequest? Request, IReadOnlyDictionary<string, string[]> Errors)
{
    public bool Succeeded => Request is not null && Errors.Count == 0;

    public static AuditFileReadResult Success(AuditRequest request) => new(request, new Dictionary<string, string[]>());

    public static AuditFileReadResult Failure(string key, string error) =>
        new(null, new Dictionary<string, string[]> { [key] = [error] });

    public static AuditFileReadResult Failure(IReadOnlyDictionary<string, string[]> errors) => new(null, errors);
}

public sealed class MultipartAuditFileReader(IOcrTextExtractor ocrTextExtractor) : IAuditFileReader
{
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/json",
        "application/pdf",
        "application/xml",
        "application/x-ndjson"
    };

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csv",
        ".diff",
        ".json",
        ".log",
        ".md",
        ".patch",
        ".pdf",
        ".txt",
        ".xml"
    };

    private static readonly HashSet<string> AllowedImageContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/bmp",
        "image/jpeg",
        "image/png",
        "image/tiff"
    };

    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bmp",
        ".jpeg",
        ".jpg",
        ".png",
        ".tif",
        ".tiff"
    };

    public async Task<AuditFileReadResult> ReadAsync(HttpRequest httpRequest, CancellationToken cancellationToken)
    {
        if (!httpRequest.HasFormContentType)
        {
            return AuditFileReadResult.Failure("file", "Use multipart/form-data with one text file field named 'file'.");
        }

        IFormCollection form;
        try
        {
            form = await httpRequest.ReadFormAsync(cancellationToken);
        }
        catch (InvalidDataException)
        {
            return AuditFileReadResult.Failure("file", $"Multipart upload must be {AuditFileLimits.MaxUploadBytes:N0} bytes or fewer.");
        }
        catch (BadHttpRequestException exception)
        {
            return AuditFileReadResult.Failure("file", exception.Message);
        }

        var errors = new Dictionary<string, string[]>();
        var file = form.Files.GetFile("file");

        if (form.Files.Count == 0)
        {
            errors["file"] = ["A file is required."];
            return AuditFileReadResult.Failure(errors);
        }

        if (form.Files.Count > 1)
        {
            errors["file"] = ["Upload exactly one file per audit request."];
            return AuditFileReadResult.Failure(errors);
        }

        if (file is null)
        {
            errors["file"] = ["The uploaded file field must be named 'file'."];
            return AuditFileReadResult.Failure(errors);
        }

        ValidateFileMetadata(file, errors);
        if (errors.Count > 0)
        {
            return AuditFileReadResult.Failure(errors);
        }

        var normalizedContentType = NormalizeContentType(file.ContentType);
        var payload = await ReadPayloadAsync(file, normalizedContentType, errors, cancellationToken);

        if (errors.Count > 0)
        {
            return AuditFileReadResult.Failure(errors);
        }

        ValidatePayload(payload, errors);
        var callbackUrl = ReadOptionalUri(form, "callbackUrl", errors);
        if (errors.Count > 0)
        {
            return AuditFileReadResult.Failure(errors);
        }

        var request = new AuditRequest(
            payload,
            normalizedContentType,
            ReadOptionalFormValue(form, "sourceSystem") ?? $"file:{GetSafeFileName(file.FileName)}",
            ReadOptionalFormValue(form, "correlationId"),
            callbackUrl);

        return AuditFileReadResult.Success(request);
    }

    private static void ValidateFileMetadata(IFormFile file, IDictionary<string, string[]> errors)
    {
        if (file.Length == 0)
        {
            errors["file"] = ["File must not be empty."];
            return;
        }

        if (file.Length > AuditFileLimits.MaxUploadBytes)
        {
            errors["file"] = [$"File must be {AuditFileLimits.MaxUploadBytes:N0} bytes or fewer."];
            return;
        }

        if (!IsSupportedTextFile(file.FileName, file.ContentType) && !IsSupportedImageFile(file.FileName, file.ContentType))
        {
            errors["file"] = ["Only text, PDF, JSON, Markdown, CSV, XML, log, diff, patch, PNG, JPEG, TIFF, or BMP files are supported."];
        }
    }

    private async Task<string> ReadPayloadAsync(
        IFormFile file,
        string normalizedContentType,
        IDictionary<string, string[]> errors,
        CancellationToken cancellationToken)
    {
        if (IsSupportedPdfFile(file.FileName, normalizedContentType))
        {
            return await ReadPdfTextAsync(file, errors, cancellationToken);
        }

        if (IsSupportedImageFile(file.FileName, normalizedContentType))
        {
            return await ReadImageTextAsync(file, errors, cancellationToken);
        }

        return await ReadUtf8TextAsync(file, errors, cancellationToken);
    }

    private static async Task<string> ReadUtf8TextAsync(
        IFormFile file,
        IDictionary<string, string[]> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = file.OpenReadStream();
            using var reader = new StreamReader(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 8192);

            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (DecoderFallbackException)
        {
            errors["file"] = ["File must be valid UTF-8 text."];
            return string.Empty;
        }
    }

    private async Task<string> ReadImageTextAsync(
        IFormFile file,
        IDictionary<string, string[]> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await ocrTextExtractor.ExtractAsync(file, cancellationToken);
            if (string.IsNullOrWhiteSpace(result.Text))
            {
                errors["file"] = ["OCR did not find readable text in the uploaded image."];
                return string.Empty;
            }

            return result.Text;
        }
        catch (InvalidOperationException exception)
        {
            errors["file"] = [exception.Message];
            return string.Empty;
        }
    }

    private static async Task<string> ReadPdfTextAsync(
        IFormFile file,
        IDictionary<string, string[]> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var input = file.OpenReadStream();
            using var memory = new MemoryStream();
            await input.CopyToAsync(memory, cancellationToken);
            memory.Position = 0;

            using var document = PdfDocument.Open(memory);
            var text = new StringBuilder();

            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pageText = page.Text;
                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    text.AppendLine(pageText.Trim());
                }
            }

            if (text.Length == 0)
            {
                errors["file"] = ["PDF text could not be extracted. If this is a scanned PDF, upload an image page or run OCR before upload."];
                return string.Empty;
            }

            return text.ToString();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is PdfDocumentFormatException or IOException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            errors["file"] = ["PDF could not be read. Check that the file is not encrypted, corrupted, or image-only."];
            return string.Empty;
        }
    }

    private static void ValidatePayload(string payload, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            errors[nameof(AuditRequest.Payload)] = ["Payload is required."];
        }
        else if (payload.Length > AuditFileLimits.MaxPayloadCharacters)
        {
            errors[nameof(AuditRequest.Payload)] = [$"Payload must be {AuditFileLimits.MaxPayloadCharacters:N0} characters or fewer."];
        }
    }

    private static bool IsSupportedTextFile(string fileName, string contentType)
    {
        var mediaType = NormalizeContentType(contentType);
        if (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (AllowedContentTypes.Contains(mediaType))
        {
            return true;
        }

        return AllowedExtensions.Contains(Path.GetExtension(fileName));
    }

    private static bool IsSupportedImageFile(string fileName, string contentType)
    {
        var mediaType = NormalizeContentType(contentType);
        if (AllowedImageContentTypes.Contains(mediaType))
        {
            return true;
        }

        return AllowedImageExtensions.Contains(Path.GetExtension(fileName));
    }

    private static bool IsSupportedPdfFile(string fileName, string contentType)
    {
        return NormalizeContentType(contentType).Equals("application/pdf", StringComparison.OrdinalIgnoreCase) ||
               Path.GetExtension(fileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeContentType(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return "text/plain";
        }

        return contentType.Split(';', 2)[0].Trim();
    }

    private static string? ReadOptionalFormValue(IFormCollection form, string key)
    {
        return form.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.ToString()
            : null;
    }

    private static Uri? ReadOptionalUri(IFormCollection form, string key, IDictionary<string, string[]> errors)
    {
        var value = ReadOptionalFormValue(form, key);
        if (value is null)
        {
            return null;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return uri;
        }

        errors[key] = ["Callback URL must be an absolute HTTP or HTTPS URL."];
        return null;
    }

    private static string GetSafeFileName(string fileName)
    {
        var safeName = Path.GetFileName(fileName);
        return string.IsNullOrWhiteSpace(safeName) ? "upload.txt" : safeName;
    }
}
