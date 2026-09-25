namespace FindJobHelper.Core;

/// <summary>
/// Optionally implemented by the experience database provider to expose the
/// MSBuild <c>UserSecretsId</c> of the provider assembly. The provider
/// assembly is loaded in an isolated collectible
/// <see cref="System.Runtime.Loader.AssemblyLoadContext"/>, so the host
/// cannot read its <c>UserSecretsIdAttribute</c> by type: the attribute
/// resolves to a second copy of the UserSecrets assembly and the typed
/// lookup misses. Reading the ID through this shared contract instead
/// executes inside the provider context, where the attribute and the lookup
/// use the same copy.
/// </summary>
public interface IUserSecretsIdProvider
{
    public string? UserSecretsId { get; }
}
