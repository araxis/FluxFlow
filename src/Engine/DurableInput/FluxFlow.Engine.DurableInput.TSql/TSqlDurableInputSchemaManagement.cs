namespace FluxFlow.Engine.DurableInput.TSql;

/// <summary>
/// Controls whether the provider may create known schema versions or only validate them.
/// </summary>
public enum TSqlDurableInputSchemaManagement
{
    CreateOrMigrate = 0,
    ValidateOnly = 1
}
