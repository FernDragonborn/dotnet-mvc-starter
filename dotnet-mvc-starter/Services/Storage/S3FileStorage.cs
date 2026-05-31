using Amazon.S3;
using Amazon.S3.Model;

namespace api.Services.Storage;

public class S3FileStorage : IFileStorage
{
    private readonly IAmazonS3 _client;
    private readonly string _bucket;

    public S3FileStorage(IAmazonS3 client, string bucket)
    {
        _client = client;
        _bucket = bucket;
    }

    public async Task<Result<string>> SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        try
        {
            var req = new PutObjectRequest
            {
                BucketName = _bucket,
                Key = key,
                InputStream = content,
                ContentType = contentType,
                AutoCloseStream = false
            };
            await _client.PutObjectAsync(req, ct);
            return Result.Ok(key);
        }
        catch (AmazonS3Exception ex)
        {
            return Result.Fail<string>($"S3 save failed: {ex.Message}");
        }
    }

    public async Task<Result<FileObject>> GetAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var resp = await _client.GetObjectAsync(_bucket, key, ct);
            return Result.Ok(new FileObject(resp.ResponseStream, resp.Headers.ContentType ?? "application/octet-stream", resp.ContentLength));
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Result.Fail<FileObject>("File not found.");
        }
        catch (AmazonS3Exception ex)
        {
            return Result.Fail<FileObject>($"S3 get failed: {ex.Message}");
        }
    }

    public async Task<Result> DeleteAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _client.DeleteObjectAsync(_bucket, key, ct);
            return Result.Ok();
        }
        catch (AmazonS3Exception ex)
        {
            return Result.Fail($"S3 delete failed: {ex.Message}");
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _client.GetObjectMetadataAsync(_bucket, key, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
