namespace api.Services.Storage;

public record FileObject(Stream Content, string ContentType, long Length);

public interface IFileStorage
{
    Task<Result<string>> SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default);

    Task<Result<FileObject>> GetAsync(string key, CancellationToken ct = default);

    Task<Result> DeleteAsync(string key, CancellationToken ct = default);

    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
}
