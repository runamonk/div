using System.IO.Compression;
using Div.Core;

namespace Div.Tests;

public sealed class UpdateInstallerTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "Div-update-test-" + Guid.NewGuid().ToString("N"));
    string App => Path.Combine(_root, "app");

    string Write(string relative, string text)
    {
        var path = Path.Combine(App, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    string Release(params string[] names)
    {
        var zip = Path.Combine(_root, "release.zip");
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        foreach (var name in names)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write("new");
        }
        return zip;
    }

    [Fact]
    public void Install_ReplacesAppFiles_KeepsOthersAndOnlyTheLastBackup()
    {
        Write("Div.exe", "old");
        Write("licenses/old.txt", "old");
        Write("notes.txt", "mine");
        Write("backup/0.9.0/Div.exe", "older");
        var zip = Release("Div.exe", "licenses/new.txt", "Added.dll");

        UpdateInstaller.Install(zip, App, "1.0.0");

        Assert.Equal("new", File.ReadAllText(Path.Combine(App, "Div.exe")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(App, "licenses", "new.txt")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(App, "Added.dll")));
        Assert.False(File.Exists(Path.Combine(App, "licenses", "old.txt")));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(App, "notes.txt")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(App, "backup", "1.0.0", "Div.exe")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(App, "backup", "1.0.0", "licenses", "old.txt")));
        Assert.False(Directory.Exists(Path.Combine(App, "backup", "0.9.0")));
    }

    [Fact]
    public void FailedInstall_PutsThePreviousVersionBack()
    {
        Write("Div.exe", "old");
        var locked = Write("Locked.dll", "old");
        var zip = Release("Div.exe", "Locked.dll", "Added.dll");

        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Throws<IOException>(() => UpdateInstaller.Install(zip, App, "1.0.0"));

        Assert.Equal("old", File.ReadAllText(Path.Combine(App, "Div.exe")));
        Assert.Equal("old", File.ReadAllText(locked));
        Assert.False(File.Exists(Path.Combine(App, "Added.dll")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
