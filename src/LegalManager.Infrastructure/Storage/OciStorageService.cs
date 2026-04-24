using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using LegalManager.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace LegalManager.Infrastructure.Storage;

public class OciStorageService : IStorageService
{
    private readonly string _bucketName;
    private readonly string _region;
    private readonly string _accessKey;
    private readonly string _secretKey;
    private readonly string _endpoint;

    public OciStorageService(IConfiguration configuration)
    {
        var oci = configuration.GetSection("OciStorage");
        _bucketName = oci["BucketName"] ?? "legal-manager";
        _region = oci["Region"]!;
        _accessKey = oci["AccessKey"]!;
        _secretKey = oci["SecretKey"]!;
        _endpoint = $"https://{oci["Namespace"]}.compat.objectstorage.{_region}.oraclecloud.com";
    }

    private static string Sha256Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static byte[] HmacSha256(byte[] key, string data)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
    }

    private static string BytesToHex(byte[] data) => Convert.ToHexString(data).ToLowerInvariant();

    private void AppendCommonHeaders(StringBuilder sb, string host, string payloadHash, string datetimeStr)
    {
        sb.Append($"host:{host}\n");
        sb.Append($"x-amz-content-sha256:{payloadHash}\n");
        sb.Append($"x-amz-date:{datetimeStr}\n");
    }

    private async Task<string> ExecCurlAsync(string method, string objectKey, string? contentType, string payloadHash, string datetimeStr, string authHeader, string tempFile, CancellationToken ct)
    {
        var uri = $"/{_bucketName}/{objectKey}";
        var script = $"curl -s -X {method} '{_endpoint}{uri}' " +
            (contentType != null ? $"-H 'Content-Type: {contentType}' " : "") +
            $"-H 'Content-Length: 0' " +
            $"-H 'x-amz-content-sha256: {payloadHash}' " +
            $"-H 'x-amz-date: {datetimeStr}' " +
            $"-H 'Authorization: {authHeader}' " +
            $"--data-binary '@{tempFile}'";

        var startInfo = new ProcessStartInfo
        {
            FileName = "bash",
            Arguments = $"-c \"{script}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        await process.WaitForExitAsync(ct);
        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);

        if (process.ExitCode != 0)
            throw new Exception($"OCI request failed: {stderr}");

        return stdout;
    }

    private string ComputeAuthHeader(string method, string objectKey, string payloadHash, string datetimeStr)
    {
        var now = DateTime.UtcNow;
        var dateStamp = now.ToString("yyyyMMdd");
        var host = new Uri(_endpoint).Host;
        var canonicalUri = $"/{_bucketName}/{objectKey}";
        var signedHeaders = "host;x-amz-content-sha256;x-amz-date";

        var sb = new StringBuilder();
        sb.AppendLine($"host:{host}");
        sb.AppendLine($"x-amz-content-sha256:{payloadHash}");
        sb.AppendLine($"x-amz-date:{datetimeStr}");
        var canonicalHeaders = sb.ToString();

        var canonicalRequest = $"{method}\n{canonicalUri}\n\n{canonicalHeaders}\n{signedHeaders}\n{payloadHash}";
        var credentialScope = $"{dateStamp}/{_region}/s3/aws4_request";
        var stringToSign = $"AWS4-HMAC-SHA256\n{datetimeStr}\n{credentialScope}\n{Sha256Hex(Encoding.UTF8.GetBytes(canonicalRequest))}";

        var kDate = HmacSha256(Encoding.UTF8.GetBytes("AWS4" + _secretKey), dateStamp);
        var kRegion = HmacSha256(kDate, _region);
        var kService = HmacSha256(kRegion, "s3");
        var kSigning = HmacSha256(kService, "aws4_request");
        var signature = BytesToHex(HmacSha256(kSigning, stringToSign));

        return $"AWS4-HMAC-SHA256 Credential={_accessKey}/{dateStamp}/{_region}/s3/aws4_request, SignedHeaders={signedHeaders}, Signature={signature}";
    }

    public async Task<string> UploadAsync(Stream fileStream, string objectKey, string contentType, CancellationToken ct = default)
    {
        fileStream.Position = 0;
        byte[] data;
        using (var ms = new MemoryStream())
        {
            await fileStream.CopyToAsync(ms, ct);
            data = ms.ToArray();
        }

        var datetimeStr = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
        var payloadHash = Sha256Hex(data);
        var authHeader = ComputeAuthHeader("PUT", objectKey, payloadHash, datetimeStr);

        var tempFile = Path.Combine(Path.GetTempPath(), $"oci_upload_{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(tempFile, data, ct);

        try
        {
            var uri = $"/{_bucketName}/{objectKey}";
            var script = $"curl -s --globoff -X PUT '{_endpoint}{uri}' " +
                $"-H 'Content-Type: {contentType}' " +
                $"-H 'Content-Length: {data.Length}' " +
                $"-H 'Expect:' " +
                $"-H 'x-amz-content-sha256: {payloadHash}' " +
                $"-H 'x-amz-date: {datetimeStr}' " +
                $"-H 'Authorization: {authHeader}' " +
                $"--data-binary '@{tempFile}'";

            var startInfo = new ProcessStartInfo
            {
                FileName = "bash",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(script);

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await Task.WhenAll(stdoutTask, stderrTask);
            await process.WaitForExitAsync(ct);

            var output = stdoutTask.Result;
            var error = stderrTask.Result;

            if (process.ExitCode != 0)
                throw new Exception($"Upload failed (exit {process.ExitCode}): {error?.Trim()} | response: {output?.Trim()}");

            if (!string.IsNullOrWhiteSpace(output) && output.Contains("<Error>"))
                throw new Exception($"OCI upload error: {output.Trim()}");

            return objectKey;
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    public async Task<Stream> DownloadAsync(string objectKey, CancellationToken ct = default)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"oci_download_{Guid.NewGuid():N}.tmp");

        var datetimeStr = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
        var payloadHash = Sha256Hex(Array.Empty<byte>());
        var authHeader = ComputeAuthHeader("GET", objectKey, payloadHash, datetimeStr);

        var uri = $"/{_bucketName}/{objectKey}";
        var script = $"curl -s -X GET '{_endpoint}{uri}' " +
            $"-H 'x-amz-content-sha256: {payloadHash}' " +
            $"-H 'x-amz-date: {datetimeStr}' " +
            $"-H 'Authorization: {authHeader}' " +
            $"-o '{tempFile}'";

        var startInfo = new ProcessStartInfo
        {
            FileName = "bash",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(script);

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await Task.WhenAll(stdoutTask, stderrTask);
        await process.WaitForExitAsync(ct);

        var stderr = stderrTask.Result;

        if (process.ExitCode != 0)
            throw new Exception($"Download failed (exit {process.ExitCode}): {stderr?.Trim()}");

        return new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.Delete, 4096, FileOptions.DeleteOnClose);
    }

    public async Task<string> GetPresignedUrlAsync(string objectKey, int expiresInMinutes = 30, CancellationToken ct = default)
    {
        var script = $@"
import botocore.config
import botocore.session

session = botocore.session.get_session()
s3 = session.create_client(
    's3',
    region_name='{_region}',
    config=botocore.config.Config(
        signature_version='s3v4',
        s3=dict(addressing_style='path')
    ),
    endpoint_url='{_endpoint}',
    aws_access_key_id='{_accessKey}',
    aws_secret_access_key='{_secretKey}'
)

url = s3.generate_presigned_url(
    'get_object',
    Params={{'Bucket': '{_bucketName}', 'Key': '{objectKey}'}},
    ExpiresIn={expiresInMinutes}
)
print(url)
";

        var tempScript = Path.Combine(Path.GetTempPath(), $"presign_{Guid.NewGuid():N}.py");
        await File.WriteAllTextAsync(tempScript, script, ct);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "python3",
                Arguments = tempScript,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(ct);
            var error = await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
                throw new Exception($"Presigned URL failed: {error}");

            return output.Trim();
        }
        finally
        {
            try { File.Delete(tempScript); } catch { }
        }
    }

    public async Task DeleteAsync(string objectKey, CancellationToken ct = default)
    {
        var datetimeStr = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
        var payloadHash = Sha256Hex(Array.Empty<byte>());
        var authHeader = ComputeAuthHeader("DELETE", objectKey, payloadHash, datetimeStr);

        var uri = $"/{_bucketName}/{objectKey}";
        var script = $"curl -s -X DELETE '{_endpoint}{uri}' " +
            $"-H 'x-amz-content-sha256: {payloadHash}' " +
            $"-H 'x-amz-date: {datetimeStr}' " +
            $"-H 'Authorization: {authHeader}'";

        var startInfo = new ProcessStartInfo
        {
            FileName = "bash",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(script);

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await Task.WhenAll(stdoutTask, stderrTask);
        await process.WaitForExitAsync(ct);

        var stdout = stdoutTask.Result;
        var stderr = stderrTask.Result;

        if (process.ExitCode != 0 && !stderr.Contains("NoContent") && !stdout.Contains("NoContent"))
            throw new Exception($"Delete failed (exit {process.ExitCode}): {stderr?.Trim()} | response: {stdout?.Trim()}");
    }
}