using JaliscoProblemsAPI.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace JaliscoProblemsAPI.Tests;

public sealed class PhotoStorageConfigurationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DevelopmentUsesContentRootWhenStoragePathIsBlank(string? configured)
    {
        var directory = Path.Combine(Path.GetTempPath(), "jalisco-storage-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PhotoStorage:RootPath"] = configured
            }).Build();
            var storage = new ReportPhotoStorage(configuration, new TestEnvironment(directory, "Development"), NullLogger<ReportPhotoStorage>.Instance);
            var key = Guid.NewGuid().ToString("N") + ".jpg";
            Assert.Equal(Path.Combine(directory, "App_Data", "report-photos", key), storage.PathFor(key));
            Assert.True(Directory.Exists(Path.Combine(directory, "App_Data", "report-photos")));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative/path")]
    public void ProductionRequiresAnAbsoluteStoragePath(string? configured)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PhotoStorage:RootPath"] = configured
        }).Build();
        Assert.Throws<InvalidOperationException>(() => new ReportPhotoStorage(configuration,
            new TestEnvironment(Path.GetTempPath(), "Production"), NullLogger<ReportPhotoStorage>.Instance));
    }

    private sealed class TestEnvironment(string contentRoot, string environmentName) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "JaliscoProblemsAPI.Tests";
        public string EnvironmentName { get; set; } = environmentName;
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = contentRoot;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
