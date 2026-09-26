using System.Reflection;
using FindJobHelper.Core;
using Microsoft.Extensions.Configuration.UserSecrets;

namespace FindJobHelper.UserSecrets;

/// <summary>
/// Default <see cref="IUserSecretsIdProvider"/> implementation for
/// experience database providers. Inherit the provider class from this base
/// instead of implementing the interface by hand.
/// </summary>
/// <remarks>
/// The lookup runs inside the provider's isolated load context, where the
/// <see cref="UserSecretsIdAttribute"/> on the provider assembly and the
/// attribute type referenced here resolve to the same copy. The host must
/// not do the lookup itself: from the default context the typed attribute
/// misses and user secrets load silently nothing.
/// </remarks>
public abstract class UserSecretsIdProviderBase : IUserSecretsIdProvider
{
    public virtual string? UserSecretsId =>
        GetType().Assembly.GetCustomAttribute<UserSecretsIdAttribute>()?.UserSecretsId;
}
