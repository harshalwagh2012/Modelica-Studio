using ModelicaStudio.Domain.Modeling;

namespace ModelicaStudio.Domain.Tests;

public sealed class ModelicaComponentAuthoringTests
{
    [Theory]
    [InlineData(ModelicaClassKind.Model, false, true)]
    [InlineData(ModelicaClassKind.Block, false, true)]
    [InlineData(ModelicaClassKind.Connector, false, true)]
    [InlineData(ModelicaClassKind.Package, false, false)]
    [InlineData(ModelicaClassKind.Function, false, false)]
    [InlineData(ModelicaClassKind.Model, true, false)]
    public void IsInstantiable_RejectsNamespacesFunctionsAndPartialTypes(
        ModelicaClassKind kind,
        bool isPartial,
        bool expected) =>
        Assert.Equal(
            expected,
            ModelicaComponentType.IsInstantiable(new ModelicaClassInfo("Demo.Item", "Item", kind, isPartial)));

    [Fact]
    public void CreateUniqueName_UsesRecommendedNameAndIncrementsItsNumericSuffix()
    {
        Assert.Equal("plant", ModelicaComponentNaming.CreateUniqueName("Demo.Plant", "plant", []));
        Assert.Equal("plant3", ModelicaComponentNaming.CreateUniqueName("Demo.Plant", "plant2", ["plant2"]));
    }

    [Fact]
    public void CreateUniqueName_UsesLowerCamelTypeWithFirstAvailableNumber()
    {
        var name = ModelicaComponentNaming.CreateUniqueName(
            "Modelica.Electrical.Analog.Basic.Resistor",
            string.Empty,
            ["resistor1", "resistor2", "other"]);

        Assert.Equal("resistor3", name);
    }

    [Theory]
    [InlineData("plant.port", "plant", true)]
    [InlineData("plant[2].port", "plant", true)]
    [InlineData("plant", "plant", true)]
    [InlineData("'control unit'.port", "control unit", true)]
    [InlineData("plantController.port", "plant", false)]
    [InlineData("other.plant", "plant", false)]
    public void ReferencesComponent_RecognizesEndpointRoot(
        string endpoint,
        string componentName,
        bool expected) =>
        Assert.Equal(expected, ModelicaConnectionEndpoint.ReferencesComponent(endpoint, componentName));
}
