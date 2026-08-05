namespace FindJobHelper.Core.Tests;

internal static class FrozenDatabaseFixtures
{
    public static async Task<TagsDatabase> LoadTags(
        CancellationToken cancellationToken = default)
    {
        await using var input = File.OpenRead("data/tags.json");
        return await TagDatabaseSerializer.Deserialize(input, cancellationToken);
    }
}
