using DeadLinks.Services;
using Xunit;

namespace DeadLinks.Tests;

public class PathDisplayTests
{
    private const string Profile = @"C:\Users\Someone";

    [Theory]
    [InlineData(@"C:\Users\Someone\Desktop", @"%USERPROFILE%\Desktop")]
    [InlineData(@"c:\users\someone\AppData\Roaming\x", @"%USERPROFILE%\AppData\Roaming\x")]   // case-insensitive
    [InlineData(@"C:\Users\Someone", "%USERPROFILE%")]
    [InlineData(@"C:\Users\Someone\", "%USERPROFILE%")]                                      // the profile folder itself
    public void Paths_under_the_profile_are_shortened(string path, string expected)
    {
        Assert.Equal(expected, PathDisplay.Shorten(path, Profile));
    }

    [Theory]
    [InlineData(@"C:\Users\Someone2\Desktop")]        // a different user whose name starts the same
    [InlineData(@"C:\Users\SomeoneElse")]
    [InlineData(@"C:\Users\Public\Desktop")]
    [InlineData(@"D:\Demo\Desktop")]
    [InlineData("")]
    public void Other_paths_are_unchanged(string path)
    {
        Assert.Equal(path, PathDisplay.Shorten(path, Profile));
    }

    [Fact]
    public void Profile_with_trailing_separator_still_matches()
    {
        Assert.Equal(@"%USERPROFILE%\Desktop", PathDisplay.Shorten(@"C:\Users\Someone\Desktop", Profile + @"\"));
    }

    [Fact]
    public void Defaults_to_the_current_user_profile()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(@"%USERPROFILE%\Desktop", PathDisplay.Shorten(System.IO.Path.Combine(profile, "Desktop")));
    }
}
