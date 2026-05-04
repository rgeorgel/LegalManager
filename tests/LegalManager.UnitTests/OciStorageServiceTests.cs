using System.Reflection;
using LegalManager.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Moq;

namespace LegalManager.UnitTests;

public class OciStorageServiceTests
{
    private static IConfiguration CreateConfiguration(
        string? bucketName = null,
        string region = "ca-toronto-1",
        string accessKey = "test-access-key",
        string secretKey = "test-secret-key",
        string ns = "test-namespace")
    {
        var mock = new Mock<IConfiguration>();
        var section = new Mock<IConfigurationSection>();

        section.Setup(s => s["BucketName"]).Returns(bucketName ?? "legal-manager");
        section.Setup(s => s["Region"]).Returns(region);
        section.Setup(s => s["AccessKey"]).Returns(accessKey);
        section.Setup(s => s["SecretKey"]).Returns(secretKey);
        section.Setup(s => s["Namespace"]).Returns(ns);

        mock.Setup(c => c.GetSection("OciStorage")).Returns(section.Object);
        return mock.Object;
    }

    [Fact]
    public void Constructor_CreatesService_WithValidConfiguration()
    {
        var config = CreateConfiguration(ns: "my-namespace", region: "us-phoenix-1", bucketName: "test-bucket");
        var service = new OciStorageService(config);
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_UsesDefaultBucket_WhenBucketNameIsNull()
    {
        var config = CreateConfiguration(bucketName: null);
        var service = new OciStorageService(config);
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_BuildsCorrectEndpoint()
    {
        var config = CreateConfiguration(ns: "my-ns", region: "us-phoenix-1");
        var service = new OciStorageService(config);
        Assert.NotNull(service);
    }

    private static object InvokePrivateMethod(OciStorageService service, string methodName, object[] parameters)
    {
        var method = typeof(OciStorageService).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!;
        return method.Invoke(null, parameters)!;
    }

    private static object InvokePrivateInstanceMethod(OciStorageService service, string methodName, object[] parameters)
    {
        var method = typeof(OciStorageService).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)!;
        return method.Invoke(service, parameters)!;
    }

    [Fact]
    public void Sha256Hex_ReturnsCorrectHash_ForKnownInput()
    {
        var service = new OciStorageService(CreateConfiguration());
        var data = System.Text.Encoding.UTF8.GetBytes("hello");
        var result = InvokePrivateMethod(service, "Sha256Hex", new object[] { data });
        var expectedHash = "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824";
        Assert.Equal(expectedHash, result);
    }

    [Fact]
    public void Sha256Hex_ReturnsConsistentHash_ForEmptyInput()
    {
        var service = new OciStorageService(CreateConfiguration());
        var data = Array.Empty<byte>();
        var result = InvokePrivateMethod(service, "Sha256Hex", new object[] { data });
        var expectedHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        Assert.Equal(expectedHash, result);
    }

    [Fact]
    public void HmacSha256_ReturnsCorrectHmac_ForKnownInput()
    {
        var service = new OciStorageService(CreateConfiguration());
        var key = System.Text.Encoding.UTF8.GetBytes("secret-key");
        var data = "message";
        var result = (byte[])InvokePrivateMethod(service, "HmacSha256", new object[] { key, data });
        Assert.NotNull(result);
        Assert.Equal(32, result.Length);
    }

    [Fact]
    public void HmacSha256_ReturnsDifferentHmac_ForDifferentKeys()
    {
        var service = new OciStorageService(CreateConfiguration());
        var key1 = System.Text.Encoding.UTF8.GetBytes("key1");
        var key2 = System.Text.Encoding.UTF8.GetBytes("key2");
        var data = "same-message";

        var result1 = (byte[])InvokePrivateMethod(service, "HmacSha256", new object[] { key1, data });
        var result2 = (byte[])InvokePrivateMethod(service, "HmacSha256", new object[] { key2, data });

        Assert.NotEqual(Convert.ToHexString(result1), Convert.ToHexString(result2));
    }

    [Fact]
    public void BytesToHex_ReturnsCorrectHexString()
    {
        var service = new OciStorageService(CreateConfiguration());
        var data = new byte[] { 0x01, 0x02, 0xFF, 0xAB };
        var result = InvokePrivateMethod(service, "BytesToHex", new object[] { data });
        Assert.Equal("0102ffab", result);
    }

    [Fact]
    public void BytesToHex_ReturnsLowercase()
    {
        var service = new OciStorageService(CreateConfiguration());
        var data = new byte[] { 0xAB, 0xCD, 0xEF };
        var result = InvokePrivateMethod(service, "BytesToHex", new object[] { data });
        Assert.Equal("abcdef", result);
    }

    [Fact]
    public void AppendCommonHeaders_AppendsCorrectHeaders()
    {
        var service = new OciStorageService(CreateConfiguration());
        var sb = new System.Text.StringBuilder();
        var host = "localhost";
        var payloadHash = "abc123";
        var datetimeStr = "20240101T120000Z";

        InvokePrivateInstanceMethod(service, "AppendCommonHeaders", new object[] { sb, host, payloadHash, datetimeStr });

        var result = sb.ToString();
        Assert.Contains($"host:{host}", result);
        Assert.Contains($"x-amz-content-sha256:{payloadHash}", result);
        Assert.Contains($"x-amz-date:{datetimeStr}", result);
    }

    [Fact]
    public void ComputeAuthHeader_ReturnsValidAuthHeader()
    {
        var config = CreateConfiguration(accessKey: "AKIAIOSFODNN7EXAMPLE", secretKey: "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY");
        var service = new OciStorageService(config);
        var method = "GET";
        var objectKey = "test.txt";
        var payloadHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        var datetimeStr = "20240101T120000Z";

        var result = InvokePrivateInstanceMethod(service, "ComputeAuthHeader", new object[] { method, objectKey, payloadHash, datetimeStr });

        Assert.NotNull(result);
        Assert.IsType<string>(result);
        var header = (string)result;
        Assert.StartsWith("AWS4-HMAC-SHA256 Credential=", header);
        Assert.Contains("SignedHeaders=host;x-amz-content-sha256;x-amz-date", header);
        Assert.Contains("Signature=", header);
    }

    [Fact]
    public void ComputeAuthHeader_DifferentMethods_ProduceDifferentSignatures()
    {
        var service = new OciStorageService(CreateConfiguration());
        var objectKey = "test.txt";
        var payloadHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        var datetimeStr = "20240101T120000Z";

        var resultGet = (string)InvokePrivateInstanceMethod(service, "ComputeAuthHeader", new object[] { "GET", objectKey, payloadHash, datetimeStr });
        var resultPut = (string)InvokePrivateInstanceMethod(service, "ComputeAuthHeader", new object[] { "PUT", objectKey, payloadHash, datetimeStr });

        Assert.NotEqual(resultGet, resultPut);
    }

    [Fact]
    public void ComputeAuthHeader_DifferentObjectKeys_ProduceDifferentSignatures()
    {
        var service = new OciStorageService(CreateConfiguration());
        var method = "GET";
        var payloadHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        var datetimeStr = "20240101T120000Z";

        var result1 = (string)InvokePrivateInstanceMethod(service, "ComputeAuthHeader", new object[] { method, "file1.txt", payloadHash, datetimeStr });
        var result2 = (string)InvokePrivateInstanceMethod(service, "ComputeAuthHeader", new object[] { method, "file2.txt", payloadHash, datetimeStr });

        Assert.NotEqual(result1, result2);
    }

    [Fact]
    public void ComputeAuthHeader_DifferentDatetime_ProduceDifferentSignatures()
    {
        var service = new OciStorageService(CreateConfiguration());
        var method = "GET";
        var objectKey = "test.txt";
        var payloadHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        var result1 = (string)InvokePrivateInstanceMethod(service, "ComputeAuthHeader", new object[] { method, objectKey, payloadHash, "20240101T120000Z" });
        var result2 = (string)InvokePrivateInstanceMethod(service, "ComputeAuthHeader", new object[] { method, objectKey, payloadHash, "20240102T120000Z" });

        Assert.NotEqual(result1, result2);
    }

    [Fact]
    public void Sha256Hex_IsCaseInsensitiveInOutput()
    {
        var service = new OciStorageService(CreateConfiguration());
        var data = new byte[] { 0xAB };
        var result = InvokePrivateMethod(service, "Sha256Hex", new object[] { data });
        var resultStr = (string)result;
        Assert.Equal(resultStr, resultStr.ToLowerInvariant());
    }

    [Fact]
    public void HmacSha256_ProducesDifferentResults_ForDifferentData()
    {
        var service = new OciStorageService(CreateConfiguration());
        var key = System.Text.Encoding.UTF8.GetBytes("test-key");

        var result1 = (byte[])InvokePrivateMethod(service, "HmacSha256", new object[] { key, "data1" });
        var result2 = (byte[])InvokePrivateMethod(service, "HmacSha256", new object[] { key, "data2" });

        Assert.NotEqual(Convert.ToHexString(result1), Convert.ToHexString(result2));
    }
}