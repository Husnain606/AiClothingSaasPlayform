using Mapster;
using System.Reflection;

namespace FashionSaaS.Application.Mapping;

/// <summary>
/// Central Mappster configuration. Scans all mapping profiles in the Application assembly.
/// </summary>
public static class MappingConfiguration
{
    public static TypeAdapterConfig GetMappingConfig()
    {
        var config = TypeAdapterConfig.GlobalSettings;

        // Auto-register all IRegister implementations from this assembly
        config.Scan(Assembly.GetExecutingAssembly());

        return config;
    }
}
