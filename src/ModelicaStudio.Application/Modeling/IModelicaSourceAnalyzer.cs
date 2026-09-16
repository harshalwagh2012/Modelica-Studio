using ModelicaStudio.Domain.Modeling;

namespace ModelicaStudio.Application.Modeling;

public interface IModelicaSourceAnalyzer
{
    ModelicaSourceAnalysis Analyze(string source);
}
