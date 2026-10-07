namespace BBDown.Tests;

public class FormatHelperTests
{
    [Theory]
    [InlineData(0, "0 bytes")]
    [InlineData(512, "512 bytes")]
    [InlineData(1024, "1.00 KB")]
    [InlineData(1536, "1.50 KB")]
    [InlineData(1024 * 1024, "1.00 MB")]
    [InlineData(1024 * 1024 * 1024, "1.00 GB")]
    [InlineData(2.5 * 1024 * 1024 * 1024, "2.50 GB")]
    public void FormatFileSize_ReturnsExpectedString(double size, string expected)
    {
        var result = BBDownUtil.FormatFileSize(size);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FormatFileSize_Negative_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BBDownUtil.FormatFileSize(-1));
    }

    [Theory]
    [InlineData(0, false, "00m00s")]
    [InlineData(65, false, "01m05s")]
    [InlineData(3661, false, "1h01m01s")]
    [InlineData(3661, true, "01:01:01")]
    public void FormatTime_ReturnsExpectedString(int seconds, bool absolute, string expected)
    {
        var result = BBDownUtil.FormatTime(seconds, absolute);
        Assert.Equal(expected, result);
    }
}
