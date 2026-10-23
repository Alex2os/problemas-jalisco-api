using JaliscoProblemsAPI.Data.Repositories;
using JaliscoProblemsAPI.Infrastructure;
using JaliscoProblemsAPI.Services;
using StbImageWriteSharp;
namespace JaliscoProblemsAPI.Tests;

public sealed class ReportMediaTests
{
    [Theory]
    [InlineData("reported", "still_present", true)]
    [InlineData("reported", "resolved", true)]
    [InlineData("still_present", "still_present", true)]
    [InlineData("still_present", "resolved", true)]
    [InlineData("resolved", "still_present", true)]
    [InlineData("resolved", "reported", false)]
    [InlineData("still_present", "reported", false)]
    [InlineData("reported", "in_progress", false)]
    public void StatusTransitionsAreExplicit(string from, string to, bool expected) => Assert.Equal(expected, ReportRepository.ValidTransition(from, to));
    [Theory]
    [InlineData("<svg><script/></svg>")]
    [InlineData("not an image")]
    public void RejectsArbitraryFiles(string value) => Assert.Equal(400, Assert.Throws<ReportOperationException>(() => ReportPhotoStorage.Normalize(System.Text.Encoding.UTF8.GetBytes(value))).Status);
    [Fact]
    public void DecodesAndReencodesPixelsWithoutUploadedMetadata()
    {
        using var input = new MemoryStream();
        new ImageWriter().WritePng(new byte[] { 255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255 }, 2, 2, ColorComponents.RedGreenBlue, input);
        input.Write(System.Text.Encoding.UTF8.GetBytes("private metadata or appended script"));
        var output = ReportPhotoStorage.Normalize(input.ToArray());
        Assert.Equal(new byte[] { 255, 216, 255 }, output.Take(3));
        Assert.DoesNotContain("private metadata", System.Text.Encoding.UTF8.GetString(output));
        var decoded = StbImageSharp.ImageResult.FromMemory(output);
        Assert.Equal(2, decoded.Width); Assert.Equal(2, decoded.Height);
    }
    [Fact]
    public void RejectsBrokenJpegWithCorrectMagic() => Assert.Throws<ReportOperationException>(() => ReportPhotoStorage.Normalize(new byte[] { 255, 216, 255, 0, 0, 0 }));
    [Fact]
    public void RejectsOversizedDimensionsBeforeDecodingPixels()
    {
        using var input = new MemoryStream();
        new ImageWriter().WritePng(new byte[6001 * 3], 6001, 1, ColorComponents.RedGreenBlue, input);
        Assert.Throws<ReportOperationException>(() => ReportPhotoStorage.Normalize(input.ToArray()));
    }
}
