using KARTCompanion;

namespace KARTCompanion.Tests;

public class CrashLogTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _logPath;

    public CrashLogTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "kart-companion-crashlog-test-" + Guid.NewGuid());
        _logPath = Path.Combine(_tempDir, "error.log");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Log_WritesExceptionDetailsToTheLogFile()
    {
        CrashLog.Log(new InvalidOperationException("boom"), _logPath);

        var content = File.ReadAllText(_logPath);
        Assert.Contains("boom", content);
        Assert.Contains(nameof(InvalidOperationException), content);
    }

    [Fact]
    public void Log_CalledTwice_AppendsRatherThanOverwrites()
    {
        CrashLog.Log(new Exception("first"), _logPath);
        CrashLog.Log(new Exception("second"), _logPath);

        var content = File.ReadAllText(_logPath);
        Assert.Contains("first", content);
        Assert.Contains("second", content);
    }

    [Fact]
    public void Log_CreatesTheDirectoryIfItDoesNotExist()
    {
        Assert.False(Directory.Exists(_tempDir));

        CrashLog.Log(new Exception("boom"), _logPath);

        Assert.True(File.Exists(_logPath));
    }
}
