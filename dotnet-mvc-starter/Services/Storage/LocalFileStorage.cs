namespace api.Services.Storage;

public class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(string root)
    {
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public async Task<Result<string>> SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        var resolved = ResolveSafePath(key);
        if (resolved.IsFailure) return Result.Fail<string>(resolved.Error);

        var fullPath = resolved.Value;
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        await using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(fs, ct);

        return Result.Ok(key);
    }

    public Task<Result<FileObject>> GetAsync(string key, CancellationToken ct = default)
    {
        var resolved = ResolveSafePath(key);
        if (resolved.IsFailure) return Task.FromResult(Result.Fail<FileObject>(resolved.Error));

        var fullPath = resolved.Value;
        if (!File.Exists(fullPath))
            return Task.FromResult(Result.Fail<FileObject>("File not found."));

        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var contentType = GuessContentType(fullPath);
        return Task.FromResult(Result.Ok(new FileObject(stream, contentType, stream.Length)));
    }

    public Task<Result> DeleteAsync(string key, CancellationToken ct = default)
    {
        var resolved = ResolveSafePath(key);
        if (resolved.IsFailure) return Task.FromResult(Result.Fail(resolved.Error));

        var fullPath = resolved.Value;
        if (File.Exists(fullPath)) File.Delete(fullPath);
        return Task.FromResult(Result.Ok());
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        var resolved = ResolveSafePath(key);
        if (resolved.IsFailure) return Task.FromResult(false);
        return Task.FromResult(File.Exists(resolved.Value));
    }

    private Result<string> ResolveSafePath(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return Result.Fail<string>("Storage key cannot be empty.");

        var combined = Path.GetFullPath(Path.Combine(_root, key));
        var rootWithSep = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(rootWithSep, StringComparison.Ordinal) && combined != _root)
            return Result.Fail<string>("Path traversal detected.");

        return Result.Ok(combined);
    }

    private static string GuessContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "application/octet-stream"
    };
}
