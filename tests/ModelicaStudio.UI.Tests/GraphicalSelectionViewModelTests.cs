using ModelicaStudio.Domain.Graphics;
using ModelicaStudio.UI.ViewModels;

namespace ModelicaStudio.UI.Tests;

public sealed class GraphicalSelectionViewModelTests
{
    [Fact]
    public void FromConnection_ExposesEndpointsAndLineProperties()
    {
        var line = new ModelicaGraphicPrimitive
        {
            Kind = ModelicaGraphicKind.Line,
            Visible = new ModelicaAnnotationValue<bool>(true),
            Origin = new ModelicaAnnotationValue<ModelicaPoint>(new ModelicaPoint(0, 0)),
            Rotation = new ModelicaAnnotationValue<double>(0),
            Points = [new ModelicaPoint(0, 0), new ModelicaPoint(10, 0), new ModelicaPoint(10, 20)],
            Style = new ModelicaGraphicStyle
            {
                LineColor = new ModelicaColor(0, 0, 255),
                LinePattern = ModelicaLinePattern.Dash,
                LineThickness = 0.5,
            },
            RawJson = "{}",
        };

        var selection = GraphicalSelectionViewModel.FromConnection(
            new ModelicaDiagramConnection("source.y", "plant.u", line, true, "Demo.Base", "{}"));

        Assert.Equal("CONNECTION", selection.Kind);
        Assert.Equal("source.y → plant.u", selection.Title);
        Assert.Contains(selection.Properties, property => property.Name == "Scope" && property.Value == "Inherited");
        Assert.Contains(selection.Properties, property => property.Name == "Route points" && property.Value == "3");
        Assert.Contains(selection.Properties, property => property.Name == "Line pattern" && property.Value == "Dash");
    }
}
