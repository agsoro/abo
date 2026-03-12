using Abo.Core.Connectors;
using Abo.Tools.Connector;
using Moq;

namespace Abo.Tests;

/// <summary>
/// Unit tests for PythonTool.
/// Verifies that the tool correctly delegates to IConnector.RunPythonAsync
/// and handles edge cases (missing args, bad JSON) gracefully.
/// </summary>
public class PythonToolTests
{
    private readonly Mock<IConnector> _mockConnector;
    private readonly PythonTool _tool;

    public PythonToolTests()
    {
        _mockConnector = new Mock<IConnector>();
        _tool = new PythonTool(_mockConnector.Object);
    }

    // -------------------------------------------------------------------
    // Test 1: Tool metadata — Name
    // -------------------------------------------------------------------

    [Fact]
    public void Tool_HasCorrectName()
    {
        Assert.Equal("python", _tool.Name);
    }

    // -------------------------------------------------------------------
    // Test 2: Tool metadata — Description not empty
    // -------------------------------------------------------------------

    [Fact]
    public void Tool_HasNonEmptyDescription()
    {
        Assert.False(string.IsNullOrWhiteSpace(_tool.Description));
    }

    // -------------------------------------------------------------------
    // Test 3: ParametersSchema — contains 'arguments' as required property
    // -------------------------------------------------------------------

    [Fact]
    public void Tool_ParametersSchema_ContainsRequiredArgumentsProperty()
    {
        var schema = _tool.ParametersSchema;
        Assert.NotNull(schema);

        var schemaJson = System.Text.Json.JsonSerializer.Serialize(schema);
        Assert.Contains("\"arguments\"", schemaJson);
        Assert.Contains("\"required\"", schemaJson);
    }

    // -------------------------------------------------------------------
    // Test 4: Valid call — delegates to IConnector.RunPythonAsync
    // -------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_ValidArguments_CallsRunPythonAsync()
    {
        // Arrange
        _mockConnector
            .Setup(c => c.RunPythonAsync("main.py"))
            .ReturnsAsync("Hello from Python!");

        // Act
        var result = await _tool.ExecuteAsync("{\"arguments\": \"main.py\"}");

        // Assert
        Assert.Equal("Hello from Python!", result);
        _mockConnector.Verify(c => c.RunPythonAsync("main.py"), Times.Once);
    }

    // -------------------------------------------------------------------
    // Test 5: Valid call with pytest — delegates correctly
    // -------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_PytestArguments_CallsRunPythonAsync()
    {
        // Arrange
        _mockConnector
            .Setup(c => c.RunPythonAsync("-m pytest"))
            .ReturnsAsync("5 passed in 0.12s");

        // Act
        var result = await _tool.ExecuteAsync("{\"arguments\": \"-m pytest\"}");

        // Assert
        Assert.Equal("5 passed in 0.12s", result);
        _mockConnector.Verify(c => c.RunPythonAsync("-m pytest"), Times.Once);
    }

    // -------------------------------------------------------------------
    // Test 6: Missing 'arguments' key — returns error message
    // -------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_MissingArgumentsKey_ReturnsErrorMessage()
    {
        // Act
        var result = await _tool.ExecuteAsync("{}");

        // Assert
        Assert.Contains("Error: arguments parameter is required.", result);
        _mockConnector.Verify(c => c.RunPythonAsync(It.IsAny<string>()), Times.Never);
    }

    // -------------------------------------------------------------------
    // Test 7: Invalid JSON — returns parsing error message
    // -------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_InvalidJson_ReturnsParsingError()
    {
        // Act
        var result = await _tool.ExecuteAsync("NOT_VALID_JSON");

        // Assert
        Assert.StartsWith("Error parsing arguments:", result);
        _mockConnector.Verify(c => c.RunPythonAsync(It.IsAny<string>()), Times.Never);
    }

    // -------------------------------------------------------------------
    // Test 8: Connector returns an error string — tool passes it through
    // -------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_ConnectorReturnsError_PassesThroughToResult()
    {
        // Arrange
        _mockConnector
            .Setup(c => c.RunPythonAsync("bad_script.py"))
            .ReturnsAsync("Error (1): ModuleNotFoundError: No module named 'xyz'");

        // Act
        var result = await _tool.ExecuteAsync("{\"arguments\": \"bad_script.py\"}");

        // Assert
        Assert.Contains("ModuleNotFoundError", result);
    }

    // -------------------------------------------------------------------
    // Test 9: venv creation — delegates correctly
    // -------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_VenvCreation_CallsRunPythonAsync()
    {
        // Arrange
        _mockConnector
            .Setup(c => c.RunPythonAsync("-m venv .venv"))
            .ReturnsAsync("Command executed successfully with no output.");

        // Act
        var result = await _tool.ExecuteAsync("{\"arguments\": \"-m venv .venv\"}");

        // Assert
        Assert.Equal("Command executed successfully with no output.", result);
        _mockConnector.Verify(c => c.RunPythonAsync("-m venv .venv"), Times.Once);
    }
}
