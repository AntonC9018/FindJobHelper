using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

public static class ConfigurationHelper
{
    public static OptionsBuilder<T> RegisterOptionsValueAsService<T>(this OptionsBuilder<T> builder)
        where T : class
    {
        var name = builder.Name;
        if (name is null or "")
        {
            builder.Services.AddTransient<T>(services =>
            {
                var options = services.GetRequiredService<IOptions<T>>().Value;
                return options;
            });
        }
        else
        {
            builder.Services.AddKeyedTransient<T>(name, (services, obj) =>
            {
                _ = obj;
                var options = services.GetRequiredService<IOptionsMonitor<T>>().Get(name);
                return options;
            });
        }
        return builder;
    }
}
