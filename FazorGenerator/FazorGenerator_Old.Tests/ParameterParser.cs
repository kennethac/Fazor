using System.Linq;
using FazorGenerator.Parser.Parameters;
using Sprache;
using Xunit;

namespace Fazor.Tests;

public class ParameterParser
{
    [Fact]
    public void BasicString()
    {
        var parameters = SpracheParser.ParseParameters(new Input("string name, int age"));
        Assert.True(parameters.WasSuccessful);
        var parametersValues = parameters.Value!.ToArray();

        Assert.Equal(2, parametersValues.Length);
        Assert.Equal("string", parametersValues[0].Type);
        Assert.Equal("name", parametersValues[0].Identifier);
        Assert.True(parametersValues[0].DefaultValue.IsEmpty);
        Assert.True(parametersValues[0].Attributes.IsEmpty);
        Assert.Equal("int", parametersValues[1].Type);
        Assert.Equal("age", parametersValues[1].Identifier);
        Assert.True(parametersValues[1].DefaultValue.IsEmpty);
        Assert.True(parametersValues[1].Attributes.IsEmpty);
    }

    [Fact]
    public void SingleNestedGenerics()
    {
        var parameters =
            SpracheParser.ParseParameters(new Input("List<int> ages"));
        Assert.True(parameters.WasSuccessful, $"Failed with error {parameters.Message}");
        var parametersValues = parameters.Value!.ToArray();

        Assert.Single(parametersValues);
        Assert.Equal("List<int>", parametersValues[0].Type);
        Assert.Equal("ages", parametersValues[0].Identifier);
        Assert.True(parametersValues[0].DefaultValue.IsEmpty);
        Assert.True(parametersValues[0].Attributes.IsEmpty);
    }

    [Fact]
    public void ComplexTypes()
    {
        var parameters =
            SpracheParser.ParseParameters(new Input("IDictionary<string, int> ages, List<List<int>> matrix"));
        Assert.True(parameters.WasSuccessful, $"Failed with error {parameters.Message}");
        var parametersValues = parameters.Value!.ToArray();

        Assert.Equal(2, parametersValues.Length);
        Assert.Equal("IDictionary<string, int>", parametersValues[0].Type);
        Assert.Equal("ages", parametersValues[0].Identifier);
        Assert.True(parametersValues[0].DefaultValue.IsEmpty);
        Assert.True(parametersValues[0].Attributes.IsEmpty);
        Assert.Equal("List<List<int>>", parametersValues[1].Type);
        Assert.Equal("matrix", parametersValues[1].Identifier);
        Assert.True(parametersValues[1].DefaultValue.IsEmpty);
        Assert.True(parametersValues[1].Attributes.IsEmpty);
    }
}