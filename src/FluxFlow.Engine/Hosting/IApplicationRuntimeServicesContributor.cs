namespace FluxFlow.Engine.Hosting;

public interface IApplicationRuntimeServicesContributor
{
    void Configure(ApplicationRuntimeServicesContext context);
}
