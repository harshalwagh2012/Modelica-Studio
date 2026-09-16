using ModelicaStudio.Application.Modeling;
using ModelicaStudio.Domain.Modeling;

namespace ModelicaStudio.Application.Tests;

public sealed class ModelicaSourceGeneratorTests
{
    [Theory]
    [InlineData(ModelicaClassType.Model, "model")]
    [InlineData(ModelicaClassType.Package, "package")]
    [InlineData(ModelicaClassType.Block, "block")]
    [InlineData(ModelicaClassType.Connector, "connector")]
    [InlineData(ModelicaClassType.Record, "record")]
    [InlineData(ModelicaClassType.Function, "function")]
    [InlineData(ModelicaClassType.Class, "class")]
    public void Generate_CreatesRequestedClassType(ModelicaClassType classType, string keyword)
    {
        var source = ModelicaSourceGenerator.Generate(new NewModelicaClass("Plant", classType));

        Assert.Equal($"{keyword} Plant\n\nend Plant;\n", source.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Generate_IncludesWithinAndEscapedDescription()
    {
        var source = ModelicaSourceGenerator.Generate(
            new NewModelicaClass("Motor", Description: "A \"motor\"", WithinPackage: "Vehicle.Components"));

        Assert.Equal(
            "within Vehicle.Components;\nmodel Motor \"A \\\"motor\\\"\"\n\nend Motor;\n",
            source.ReplaceLineEndings("\n"));
    }

    [Theory]
    [InlineData("Bad Name")]
    [InlineData("1Model")]
    [InlineData("Model);quit()")]
    public void Generate_RejectsInvalidIdentifier(string name) =>
        Assert.Throws<ArgumentException>(() => ModelicaSourceGenerator.Generate(new NewModelicaClass(name)));

    [Fact]
    public void TryGetTopLevelClassName_HandlesPartialEncapsulatedClass()
    {
        const string source = "within;\nencapsulated partial model Controller\nend Controller;";

        Assert.Equal("Controller", ModelicaSourceGenerator.TryGetTopLevelClassName(source));
    }
}
