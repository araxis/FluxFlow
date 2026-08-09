namespace FluxFlow.Composition;

public interface IApplicationResourceRegistrar
{
    IApplicationResourceRegistrar RegistrationIdentity => this;

    void Register(ApplicationResourceRegistrationContext context);
}
