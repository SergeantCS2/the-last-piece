using Tlp.Viewer.Core;

namespace Tlp.Tests;

public class AppLogTests
{
    [Fact]
    public void Writes_lines_and_reads_back_the_tail()
    {
        var old = AppLog.Folder;
        AppLog.Folder = Directory.CreateTempSubdirectory("tlp-log-").FullName;
        try
        {
            for (var i = 0; i < 5; i++) AppLog.Write($"line {i}\nwith a break");
            var tail = AppLog.Tail(2);
            Assert.Equal(2, tail.Count);
            Assert.EndsWith("line 4 with a break", tail[1]);
        }
        finally { AppLog.Folder = old; }
    }

    [Fact]
    public void Rolls_over_past_a_megabyte()
    {
        var old = AppLog.Folder;
        AppLog.Folder = Directory.CreateTempSubdirectory("tlp-log-").FullName;
        try
        {
            File.WriteAllText(AppLog.FilePath, new string('x', 1_100_000));
            AppLog.Write("fresh");
            Assert.True(File.Exists(Path.Combine(AppLog.Folder, "app.old.log")));
            Assert.Single(AppLog.Tail(10));
        }
        finally { AppLog.Folder = old; }
    }

    [Fact]
    public void Old_settings_files_start_with_the_welcome_unfinished()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("tlp-set-").FullName, "settings.json");
        File.WriteAllText(path, """{ "name": "Alex" }""");
        Assert.False(ViewerSettings.Load(path).SetupDone);
        ViewerSettings.Load(path).Save(path);
        new ViewerSettings { SetupDone = true }.Save(path);
        Assert.True(ViewerSettings.Load(path).SetupDone);
    }
}
