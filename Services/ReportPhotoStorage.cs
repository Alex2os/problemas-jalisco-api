using StbImageSharp;
using StbImageWriteSharp;
using JaliscoProblemsAPI.Infrastructure;
namespace JaliscoProblemsAPI.Services;

public interface IReportPhotoStorage
{
    Task<StoredPhoto> SaveAsync(IFormFile file, CancellationToken cancellationToken);
    string PathFor(string key);
    void Delete(string key);
}
public sealed record StoredPhoto(string Key, int Size);

// Store only freshly encoded JPEG pixels: filenames, metadata and uploaded bytes are never served.
public sealed class ReportPhotoStorage : IReportPhotoStorage
{
    public const int MaxBytes = 5 * 1024 * 1024;
    private readonly string root;
    private readonly ILogger<ReportPhotoStorage> logger;
    private static readonly SemaphoreSlim DecodeGate = new(2);
    public ReportPhotoStorage(IConfiguration config, IWebHostEnvironment environment, ILogger<ReportPhotoStorage> logger)
    {
        this.logger = logger;
        var configured = config["PhotoStorage:RootPath"];
        if (string.IsNullOrWhiteSpace(configured) && !environment.IsDevelopment())
            throw new InvalidOperationException("Configure PhotoStorage:RootPath to a persistent, writable absolute directory.");
        root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(environment.ContentRootPath, "App_Data", "report-photos")
            : configured;
        if (!Path.IsPathFullyQualified(root)) throw new InvalidOperationException("PhotoStorage:RootPath must be absolute.");
        Directory.CreateDirectory(root);
    }
    public string PathFor(string key)
    {
        if (key.Length != 36 || !key.EndsWith(".jpg", StringComparison.Ordinal) || !Guid.TryParseExact(key[..32], "N", out _))
            throw new InvalidOperationException("Invalid internal storage key.");
        return Path.Combine(root, key);
    }
    public void Delete(string key)
    {
        try { File.Delete(PathFor(key)); }
        catch (IOException error) { logger.LogWarning(error, "Photo cleanup failed for {StorageKey}", key); }
        catch (UnauthorizedAccessException error) { logger.LogWarning(error, "Photo cleanup permission failure for {StorageKey}", key); }
    }
    public static byte[] Normalize(byte[] bytes)
    {
        // Restrict the decoder to JPEG/PNG signatures before parsing. No SVG, GIF, arbitrary files or animations.
        var jpeg = bytes.Length >= 4 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255;
        var png = bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        if (!jpeg && !png) throw new ReportOperationException(400, "Solo se admiten imágenes JPEG o PNG válidas.");
        try
        {
            using var stream = new MemoryStream(bytes, false);
            var info = ImageInfo.FromStream(stream);
            if (info is null || info.Value.Width <= 0 || info.Value.Height <= 0 || info.Value.Width > 6000 || info.Value.Height > 6000
                || (long)info.Value.Width * info.Value.Height > 12_000_000)
                throw new ReportOperationException(400, "La imagen supera el límite de 12 megapíxeles o 6000 píxeles por lado.");
            stream.Position = 0;
            var image = ImageResult.FromStream(stream, StbImageSharp.ColorComponents.RedGreenBlue);
            using var output = new MemoryStream();
            new ImageWriter().WriteJpg(image.Data, image.Width, image.Height, StbImageWriteSharp.ColorComponents.RedGreenBlue, output, 85);
            if (output.Length > MaxBytes) throw new ReportOperationException(400, "La imagen procesada supera 5 MB. Reduce su tamaño.");
            return output.ToArray();
        }
        catch (ReportOperationException) { throw; }
        catch (Exception error) when (error is not OutOfMemoryException)
        { throw new ReportOperationException(400, "La imagen está dañada o no es compatible."); }
    }
    public async Task<StoredPhoto> SaveAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length <= 0 || file.Length > MaxBytes) throw new ReportOperationException(400, "Cada foto debe ocupar entre 1 byte y 5 MB.");
        await DecodeGate.WaitAsync(cancellationToken);
        try
        {
            await using var input = file.OpenReadStream();
            using var bytes = new MemoryStream();
            // Bound the actual stream as well as the declared multipart length.
            var buffer = new byte[81920];
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (bytes.Length + read > MaxBytes) throw new ReportOperationException(400, "Cada foto debe ocupar como máximo 5 MB.");
                bytes.Write(buffer, 0, read);
            }
            cancellationToken.ThrowIfCancellationRequested();
            var encoded = Normalize(bytes.ToArray());
            var key = Guid.NewGuid().ToString("N") + ".jpg";
            try { await File.WriteAllBytesAsync(PathFor(key), encoded, cancellationToken); }
            catch { Delete(key); throw; }
            return new StoredPhoto(key, encoded.Length);
        }
        finally { DecodeGate.Release(); }
    }
}
