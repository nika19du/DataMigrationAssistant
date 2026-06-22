namespace DataMigrationAssistant.Core.Agents;

public sealed class MigrationAgentRouter : IMigrationAgentRouter
{
    private readonly GtnAgent              _gtn;
    private readonly SchemaAgent           _schema;
    private readonly ValidationAgent       _validation;
    private readonly DataAnalysisAgent     _dataAnalysis;
    private readonly NormalizationAgent    _normalization;
    private readonly SqlGenerationAgent    _sqlGeneration;
    private readonly GeneralMigrationAgent _general;

    public MigrationAgentRouter(
        GtnAgent              gtn,
        SchemaAgent           schema,
        ValidationAgent       validation,
        DataAnalysisAgent     dataAnalysis,
        NormalizationAgent    normalization,
        SqlGenerationAgent    sqlGeneration,
        GeneralMigrationAgent general)
    {
        _gtn           = gtn;
        _schema        = schema;
        _validation    = validation;
        _dataAnalysis  = dataAnalysis;
        _normalization = normalization;
        _sqlGeneration = sqlGeneration;
        _general       = general;
    }

    // Evaluation order: GTN → Schema → Validation → DataAnalysis → Normalization → SQL → General (fallback)
    public IMigrationAgent Route(string question)
    {
        if (_gtn.CanHandle(question))           return _gtn;
        if (_schema.CanHandle(question))        return _schema;
        if (_validation.CanHandle(question))    return _validation;
        if (_dataAnalysis.CanHandle(question))  return _dataAnalysis;
        if (_normalization.CanHandle(question)) return _normalization;
        if (_sqlGeneration.CanHandle(question)) return _sqlGeneration;
        return _general;
    }
}
