using Abo.Core.Connectors;

namespace Abo.Tests;

/// <summary>
/// Tests for LocalWorkspaceConnector.RunPythonAsync — verifies that the correct Python
/// executable is selected based on ConnectorEnvironment.Os.
///
/// Strategy: RunProcessAsync catches process-launch exceptions and returns
/// "Failed to start process '{executable}': ..." when the binary does not exist.
/// We exploit this to verify which executable name was passed without needing a
/// real Python installation or DI-injectable process factory.
///
/// On CI environments where python/python3 IS installed, the result will be a
/// version string (or an error exit code for bad arguments) — neither of which
/// starts with "Failed to start process", so the assertions still hold.
/// </summary>
public class RunPythonAsyncTests : IDisposable
{
    private readonly string _tempDir;

    public RunPythonAsyncTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"RunPythonAsyncTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private LocalWorkspaceConnector CreateConnector(string os) =>
        new LocalWorkspaceConnector(new ConnectorEnvironment { Dir = _tempDir, Os = os });

    // -------------------------------------------------------------------------
    // Helper — returns the executable name that was attempted (null if process
    // started successfully or returned a non-launch error).
    // -------------------------------------------------------------------------

    private static string? ExtractFailedExecutable(string result)
    {
        // Pattern: "Failed to start process 'executable': ..."
        const string prefix = "Failed to start process '";
        if (!result.StartsWith(prefix)) return null;

        var afterPrefix = result[prefix.Length..];
        var endQuote = afterPrefix.IndexOf('\'');
        return endQuote >= 0 ? afterPrefix[..endQuote] : null;
    }

    // =========================================================================
    // Windows — Os = "win" → expects "python" executable
    // =========================================================================

    [Fact]
    public async Task RunPythonAsync_OnWindows_UsesPythonExecutable()
    {
        // Arrange
        var connector = CreateConnector("win");

        // Act
        var result = await connector.RunPythonAsync("--version");

        // Assert: if the process failed to start, it must have tried "python" (not "python3")
        var failedExe = ExtractFailedExecutable(result);
        if (failedExe != null)
        {
            Assert.Equal("python", failedExe);
        }
        else
        {
            // Process started successfully — result should contain version info or a non-launch error
            Assert.DoesNotContain("Failed to start process 'python3'", result);
        }
    }

    // =========================================================================
    // Linux — Os = "linux" → expects "python3" executable
    // =========================================================================

    [Fact]
    public async Task RunPythonAsync_OnLinux_UsesPython3Executable()
    {
        // Arrange
        var connector = CreateConnector("linux");

        // Act
        var result = await connector.RunPythonAsync("--version");

        // Assert: if the process failed to start, it must have tried "python3" (not "python")
        var failedExe = ExtractFailedExecutable(result);
        if (failedExe != null)
        {
            Assert.Equal("python3", failedExe);
        }
        else
        {
            // Process started successfully — must not have tried the wrong executable
            Assert.DoesNotContain("Failed to start process 'python'", result);
        }
    }

    // =========================================================================
    // Mac — Os = "mac" → expects "python3" executable
    // =========================================================================

    [Fact]
    public async Task RunPythonAsync_OnMac_UsesPython3Executable()
    {
        // Arrange
        var connector = CreateConnector("mac");

        // Act
        var result = await connector.RunPythonAsync("--version");

        // Assert: if the process failed to start, it must have tried "python3" (not "python")
        var failedExe = ExtractFailedExecutable(result);
        if (failedExe != null)
        {
            Assert.Equal("python3", failedExe);
        }
        else
        {
            Assert.DoesNotContain("Failed to start process 'python'", result);
        }
    }

    // =========================================================================
    // Unknown OS — any unrecognized value → falls back to "python3"
    // =========================================================================

    [Fact]
    public async Task RunPythonAsync_OnUnknownOs_UsesPython3Executable()
    {
        // Arrange
        var connector = CreateConnector("freebsd");

        // Act
        var result = await connector.RunPythonAsync("--version");

        // Assert: unrecognized OS must fall back to python3
        var failedExe = ExtractFailedExecutable(result);
        if (failedExe != null)
        {
            Assert.Equal("python3", failedExe);
        }
        else
        {
            Assert.DoesNotContain("Failed to start process 'python'", result);
        }
    }

    // =========================================================================
    // Negative — verifies "win" never invokes python3
    // =========================================================================

    [Fact]
    public async Task RunPythonAsync_OnWindows_NeverInvokesPython3()
    {
        // Arrange
        var connector = CreateConnector("win");

        // Act
        var result = await connector.RunPythonAsync("--version");

        // Assert: "python3" must never appear as the failed executable on Windows
        Assert.DoesNotContain("Failed to start process 'python3'", result);
    }

    // =========================================================================
    // Negative — verifies "linux" never invokes bare "python"
    // =========================================================================

    [Fact]
    public async Task RunPythonAsync_OnLinux_NeverInvokesBareP()
    {
        // Arrange
        var connector = CreateConnector("linux");

        // Act
        var result = await connector.RunPythonAsync("--version");

        // Assert: bare "python" must never appear as the failed executable on Linux
        Assert.DoesNotContain("Failed to start process 'python'", result);
    }
}
