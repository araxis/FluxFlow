namespace FluxFlow.Engine.DurableOutput.TSql;

/// <summary>
/// Controls whether the provider may create known schema versions or only validate them.
/// </summary>
public enum TSqlDurableOutputSchemaManagement
{
    CreateOrMigrate = 0,
    ValidateOnly = 1
}
