using Avalonia.Input;
using ModelicaStudio.Domain.Modeling;

namespace ModelicaStudio.UI.Controls;

public static class ModelicaDragData
{
    public static DataFormat<ModelicaClassInfo> LibraryClassFormat { get; } =
        DataFormat.CreateInProcessFormat<ModelicaClassInfo>("modelica-studio-library-class");
}
