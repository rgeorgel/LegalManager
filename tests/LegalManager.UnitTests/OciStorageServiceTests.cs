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
}