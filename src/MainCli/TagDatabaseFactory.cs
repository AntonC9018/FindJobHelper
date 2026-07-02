using FindJobHelper.Core;

namespace MainCli;

public sealed class Tags<T> : KnownTags<T>
{
    public Tags<U> Map<U>(Func<T, U> f) => (Tags<U>) MapImpl(f);

    public required T programming { get; init; }
    public required T dotnet { get; init; }
    public required T go { get; init; }
    public required T aspnet { get; init; }
    public required T htmx { get; init; }
    public required T cpp { get; init; }
    public required T python { get; init; }
    public required T designPatterns { get; init; }
    public required T xml { get; init; }
    public required T webScraping { get; init; }
    public required T teachingSkill { get; init; }
    public required T openApi { get; init; }
    public required T restApi { get; init; }
    public required T roslyn { get; init; }
    public required T postman { get; init; }
    public required T nswag { get; init; }
    public required T sql { get; init; }
    public required T efCore { get; init; }
    public required T sqlServer { get; init; }
    public required T hotChocolate { get; init; }
    public required T graphql { get; init; }
    public required T security { get; init; }
    public required T fluentValidation { get; init; }
    public required T excel { get; init; }
    public required T csv { get; init; }
    public required T mediator { get; init; }
    public required T unitTests { get; init; }
    public required T linq2db { get; init; }
    public required T parser { get; init; }
    public required T msBuild { get; init; }
    public required T github { get; init; }
    public required T git { get; init; }
    public required T java { get; init; }
    public required T mysql { get; init; }
    public required T aws { get; init; }
    public required T docker { get; init; }
    public required T cicd { get; init; }
    public required T openGl { get; init; }
    public required T graphics { get; init; }
    public required T linux { get; init; }
    public required T nginx { get; init; }
    public required T gameProgramming { get; init; }
    public required T communicationWithClient { get; init; }
    public required T typeScript { get; init; }
    public required T javaScript { get; init; }
    public required T frontend { get; init; }
    public required T vite { get; init; }
    public required T tailwind { get; init; }
    public required T nodejs { get; init; }
    public required T shaders { get; init; }
    public required T imageProcessing { get; init; }
    public required T d { get; init; }
    public required T postgres { get; init; }
    public required T vue { get; init; }
    public required T css { get; init; }
    public required T jquery { get; init; }
    public required T dotween { get; init; }
    public required T unity { get; init; }
    public required T blender { get; init; }
    public required T uiToolkit { get; init; }
    public required T imgui { get; init; }
    public required T ugui { get; init; }
    public required T grpc { get; init; }
    public required T protobuf { get; init; }
    public required T azure { get; init; }
    public required T neon { get; init; }
    public required T backend { get; init; }
    public required T microservices { get; init; }
    public required T json { get; init; }
    public required T docx { get; init; }
    public required T blazor { get; init; }
    public required T webforms { get; init; }
    public required T aspnetmvc { get; init; }
    public required T thesis { get; init; }
}

public static class TagsDatabaseFactory
{
    public static (Tags<Tag> Tags, TagsDatabase TagsDatabase) Create()
    {
        var db = new TagsDatabaseBuilder();

        var t = new Tags<TagBuilder>
        {
            dotnet = db.Tag(".NET", "DotNet", "C#"),
            graphql = db.Tag("GraphQL"),
            aspnet = db.Tag("ASP.NET Core", "ASP.NET"),
            go = db.Tag("Go"),
            htmx = db.Tag("HTMX"),
            cpp = db.Tag("C++"),
            python = db.Tag("Python"),
            designPatterns = db.Tag("Design Patterns"),
            webScraping = db.Tag("Web Scraping"),
            teachingSkill = db.Tag("Teaching"),
            openApi = db.Tag("OpenAPI"),
            restApi = db.Tag("REST API", "REST"),
            roslyn = db.Tag("Roslyn"),
            postman = db.Tag("Postman"),
            nswag = db.Tag("NSwag"),
            sql = db.Tag("SQL"),
            efCore = db.Tag("EF Core", "EF"),
            sqlServer = db.Tag("SQL Server", "SQLServer"),
            hotChocolate = db.Tag("HotChocolate"),
            security = db.Tag("Security"),
            fluentValidation = db.Tag("FluentValidation"),
            excel = db.Tag("Excel"),
            mediator = db.Tag("Mediator", "CQRS"),
            unitTests = db.Tag("Unit Tests", "xUnit", "Verify"),
            linq2db = db.Tag("Linq2db"),
            parser = db.Tag("Parser"),
            msBuild = db.Tag("MSBuild"),
            github = db.Tag("GitHub"),
            git = db.Tag("Git"),
            java = db.Tag("Java"),
            mysql = db.Tag("MySql"),
            aws = db.Tag("AWS"),
            docker = db.Tag("Docker"),
            openGl = db.Tag("OpenGL"),
            graphics = db.Tag("Graphics"),
            linux = db.Tag("Linux", "Ubuntu", "VPS"),
            nginx = db.Tag("nginx"),
            gameProgramming = db.Tag("Game Programming"),
            communicationWithClient = db.Tag("Communication with the client"),
            typeScript = db.Tag("TypeScript"),
            javaScript = db.Tag("JavaScript"),
            frontend = db.Tag("FrontEnd"),
            jquery = db.Tag("JQuery"),
            vite = db.Tag("Vite"),
            tailwind = db.Tag("Tailwind", "Tailwind CSS"),
            nodejs = db.Tag("NodeJS"),
            shaders = db.Tag("Shaders"),
            imageProcessing = db.Tag("Image Processing"),
            d = db.Tag("D"),
            postgres = db.Tag("PostgreSQL"),
            vue = db.Tag("Vue", "VueJS"),
            css = db.Tag("CSS"),
            programming = db.Tag("Programming"),
            dotween = db.Tag("DOTween"),
            unity = db.Tag("Unity", "Unity3D"),
            blender = db.Tag("Blender"),
            imgui = db.Tag("IMGUI"),
            ugui = db.Tag("ugui"),
            uiToolkit = db.Tag("UI Toolkit"),
            grpc = db.Tag("GRPC"),
            protobuf = db.Tag("protobuf"),
            azure = db.Tag("Azure"),
            neon = db.Tag("Neon"),
            backend = db.Tag("Backend"),
            microservices = db.Tag("microservices"),
            cicd = db.Tag("CI/CD"),
            xml = db.Tag("XML"),
            json = db.Tag("JSON"),
            docx = db.Tag("DOCX"),
            blazor = db.Tag("Blazor"),
            webforms = db.Tag("WebForms"),
            aspnetmvc = db.Tag("ASP.NET MVC"),
            csv = db.Tag("CSV"),
            thesis = db.Tag("Thesis"),
        };

        t.dotnet.IsIncludedIn(t.nswag).By(0.05f).WhichIsIncludedInIt().By(0.4f);
        t.dotnet.IsIncludedIn(t.aspnet).By(0.1f).WhichIsIncludedInIt().By(0.3f);
        // t.dotnet.OverlapsWith(t.cpp).By(0.2f).WhichOverlaps().By(0.2f);
        t.dotnet.IsIncludedIn(t.designPatterns).By(0.2f).WhichIsIncludedInIt().By(0.75f);
        t.dotnet.IsIncludedIn(t.efCore).By(0.1f).WhichIsIncludedInIt().By(0.8f);
        t.dotnet.IsIncludedIn(t.fluentValidation).By(0.1f).WhichIsIncludedInIt().By(0.9f);
        t.dotnet.IsIncludedIn(t.gameProgramming).By(0.15f).WhichIsIncludedInIt().By(0.2f);
        t.dotnet.IsIncludedIn(t.hotChocolate).By(0.05f).WhichIsIncludedInIt().By(0.2f);
        // t.dotnet.OverlapsWith(t.java).By(0.4f).WhichOverlaps().By(0.4f);
        t.dotnet.IsIncludedIn(t.linq2db).By(0.1f).WhichIsIncludedInIt().By(0.8f);
        t.dotnet.IsIncludedIn(t.mediator).By(0.1f).WhichIsIncludedInIt().By(0.95f);
        // t.dotnet.OverlapsWith(t.typeScript).By(0.2f).WhichOverlaps().By(0.25f);

        t.dotnet.IsIncludedIn(t.programming).By(0.25f).WhichIsIncludedInIt().Fully();
        t.cpp.IsIncludedIn(t.programming).By(0.2f).WhichIsIncludedInIt().Fully();
        t.typeScript.IsIncludedIn(t.programming).By(0.3f).WhichIsIncludedInIt().Fully();
        t.java.IsIncludedIn(t.programming).By(0.3f).WhichIsIncludedInIt().Fully();
        t.javaScript.IsIncludedIn(t.programming).By(0.4f).WhichIsIncludedInIt().Fully();
        t.d.IsIncludedIn(t.programming).By(0.25f).WhichIsIncludedInIt().Fully();
        t.python.IsIncludedIn(t.programming).By(0.3f).WhichIsIncludedInIt().Fully();
        t.go.IsIncludedIn(t.programming).By(0.55f).WhichIsIncludedInIt().Fully();

        t.javaScript.IsIncludedIn(t.typeScript).Fully().WhichIsIncludedInIt().By(0.3f);

        t.dotnet.IsIncludedIn(t.graphql).By(0.05f);
        t.hotChocolate.IsIncludedIn(t.graphql).By(0.7f).WhichIsIncludedInIt().By(0.9f);
        t.openApi.IsIncludedIn(t.nswag).By(0.9f).WhichIsIncludedInIt().By(0.6f);
        t.restApi.IsIncludedIn(t.aspnet).By(0.8f).WhichIsIncludedInIt().By(0.15f);

        t.typeScript.IsIncludedIn(t.frontend).By(0.4f).WhichIsIncludedInIt().By(0.15f);
        t.javaScript.IsIncludedIn(t.frontend).By(0.7f).WhichIsIncludedInIt().By(0.07f);
        // build tools
        t.vite.IsIncludedIn(t.frontend).By(0.75f).WhichIsIncludedInIt().By(0.05f);
        t.restApi.IsIncludedIn(t.frontend).By(0.35f).WhichIsIncludedInIt().By(0.04f);
        // dom manipulation
        t.jquery.IsIncludedIn(t.frontend).By(0.95f).WhichIsIncludedInIt().By(0.05f);
        t.vue.IsIncludedIn(t.frontend).By(0.7f).WhichIsIncludedInIt().By(0.15f);

        t.unity.IsIncludedIn(t.dotween).By(0.05f).WhichIsIncludedInIt().By(0.95f);
        t.unity.IsIncludedIn(t.dotnet).By(0.15f).WhichIsIncludedInIt().By(0.5f);

        t.unity.IsIncludedIn(t.ugui).By(0.05f).WhichIsIncludedInIt().Fully();
        t.graphics.IsIncludedIn(t.blender).By(0.1f).WhichIsIncludedInIt().By(0.7f);
        t.graphics.IsIncludedIn(t.unity).By(0.25f).WhichIsIncludedInIt().By(0.25f);
        t.unity.IsIncludedIn(t.uiToolkit).By(0.05f).WhichIsIncludedInIt().By(1.0f);
        t.unity.IsIncludedIn(t.imgui).By(0.05f).WhichIsIncludedInIt().By(0.70f);
        t.graphics.IsIncludedIn(t.imgui).By(0.05f).WhichIsIncludedInIt().By(0.70f);
        t.unity.IsIncludedIn(t.gameProgramming).By(0.5f).WhichIsIncludedInIt().By(0.4f);
        t.graphics.IsIncludedIn(t.shaders).By(0.1f).WhichIsIncludedInIt().Fully();
        t.unity.IsIncludedIn(t.shaders).By(0.05f).WhichIsIncludedInIt().By(0.9f);

        t.grpc.IsIncludedIn(t.protobuf).By(0.2f).WhichIsIncludedInIt().Fully();

        t.azure.IsIncludedIn(t.aws).By(0.8f).WhichIsIncludedInIt().By(0.8f);

        t.sql.IsIncludedIn(t.sqlServer).Fully().WhichIsIncludedInIt().By(0.4f);
        t.sql.IsIncludedIn(t.postgres).Fully().WhichIsIncludedInIt().By(0.3f);
        t.sql.IsIncludedIn(t.mysql).Fully().WhichIsIncludedInIt().By(0.5f);

        t.sqlServer.IsIncludedIn(t.postgres).By(0.5f).WhichIsIncludedInIt().By(0.35f);
        t.sqlServer.IsIncludedIn(t.mysql).By(0.5f).WhichIsIncludedInIt().By(0.5f);
        t.sql.IsIncludedIn(t.efCore).By(0.7f).WhichIsIncludedInIt().By(0.3f);

        t.backend.IsIncludedIn(t.go).By(0.2f).WhichIsIncludedInIt().By(0.2f);
        t.backend.IsIncludedIn(t.grpc).By(0.05f).WhichIsIncludedInIt().By(0.35f);
        t.backend.IsIncludedIn(t.aspnet).By(0.2f).WhichIsIncludedInIt().By(0.6f);
        t.backend.IsIncludedIn(t.sql).By(0.15f).WhichIsIncludedInIt().By(0.9f);
        t.backend.IsIncludedIn(t.restApi).By(0.21f).WhichIsIncludedInIt().By(0.5f);
        t.backend.IsIncludedIn(t.graphql).By(0.15f).WhichIsIncludedInIt().By(0.2f);
        t.backend.IsIncludedIn(t.openApi).By(0.10f).WhichIsIncludedInIt().By(0.8f);
        t.backend.IsIncludedIn(t.nodejs).By(0.10f).WhichIsIncludedInIt().By(0.45f);

        t.backend.IsIncludedIn(t.microservices).By(0.1f).WhichIsIncludedInIt().By(0.8f);

        foreach (var cloudProvider in new[] { t.aws, t.azure })
        {
            t.cicd.IsIncludedIn(cloudProvider).By(0.9f).WhichIsIncludedInIt().By(0.2f);
        }

        t.json.IsIncludedIn(t.javaScript).Fully().WhichIsIncludedInIt().By(0.1f);
        t.json.IsIncludedIn(t.typeScript).Fully().WhichIsIncludedInIt().By(0.05f);
        t.json.IsIncludedIn(t.xml).By(0.2f).WhichIsIncludedInIt().By(0.1f);
        t.xml.IsIncludedIn(t.msBuild).Fully().WhichIsIncludedInIt().By(0.1f);
        t.xml.IsIncludedIn(t.docx).Fully().WhichIsIncludedInIt().By(0.05f);
        t.xml.IsIncludedIn(t.excel).Fully().WhichIsIncludedInIt().By(0.08f);

        t.aspnetmvc.IsIncludedIn(t.aspnet).Fully().WhichIsIncludedInIt().By(0.10f);
        // avalonia

        var r = db.Build();
        if (r.Errors != null)
        {
            throw new Exception(string.Join("\n", r.Errors));
        }

        var tags1 = t.Map(t1 => new Tag(t1.Name));
        return (tags1, r.Database!);
    }
}
