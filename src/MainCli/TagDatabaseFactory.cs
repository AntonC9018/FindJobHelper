public sealed class Tags<T>
{
    public required T programming { get; init; }
    public required T dotnet { get; init; }
    public required T go { get; init; }
    public required T aspnet { get; init; }
    public required T htmx { get; init; }
    public required T cpp { get; init; }
    public required T python { get; init; }
    public required T designPatterns { get; init; }
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

    public Tags<U> Map<U>(Func<T, U> f)
    {
        var retType = typeof(Tags<U>);
        var ret = Activator.CreateInstance(retType);
        foreach (var prop in this.GetType().GetProperties())
        {
            prop.SetValue(ret, f((T) prop.GetValue(this)!));
        }
        return (Tags<U>) ret!;
    }
}

public static class TagDatabaseFactory
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
        };

        t.dotnet.Overlaps(t.nswag).By(0.05f).WhichOverlaps().By(0.4f);
        t.dotnet.Overlaps(t.aspnet).By(0.1f).WhichOverlaps().By(0.3f);
        t.dotnet.Overlaps(t.cpp).By(0.2f).WhichOverlaps().By(0.2f);
        t.dotnet.Overlaps(t.designPatterns).By(0.2f).WhichOverlaps().By(0.75f);
        t.dotnet.Overlaps(t.efCore).By(0.1f).WhichOverlaps().By(0.8f);
        t.dotnet.Overlaps(t.fluentValidation).By(0.1f).WhichOverlaps().By(0.9f);
        t.dotnet.Overlaps(t.gameProgramming).By(0.15f).WhichOverlaps().By(0.2f);
        t.dotnet.Overlaps(t.hotChocolate).By(0.05f).WhichOverlaps().By(0.2f);
        t.dotnet.Overlaps(t.java).By(0.4f).WhichOverlaps().By(0.4f);
        t.dotnet.Overlaps(t.linq2db).By(0.1f).WhichOverlaps().By(0.8f);
        t.dotnet.Overlaps(t.mediator).By(0.1f).WhichOverlaps().By(0.95f);
        t.dotnet.Overlaps(t.typeScript).By(0.2f).WhichOverlaps().By(0.25f);

        t.dotnet.Overlaps(t.programming).By(0.25f).WhichOverlaps().Fully();
        t.cpp.Overlaps(t.programming).By(0.2f).WhichOverlaps().Fully();
        t.typeScript.Overlaps(t.programming).By(0.3f).WhichOverlaps().Fully();
        t.java.Overlaps(t.programming).By(0.3f).WhichOverlaps().Fully();
        t.javaScript.Overlaps(t.programming).By(0.4f).WhichOverlaps().Fully();
        t.d.Overlaps(t.programming).By(0.25f).WhichOverlaps().Fully();
        t.python.Overlaps(t.programming).By(0.3f).WhichOverlaps().Fully();

        t.javaScript.Overlaps(t.typeScript).Fully().WhichOverlaps().By(0.3f);

        t.hotChocolate.Overlaps(t.graphql).By(0.7f).WhichOverlaps().By(0.9f);
        t.openApi.Overlaps(t.nswag).By(0.9f).WhichOverlaps().By(0.6f);
        t.restApi.Overlaps(t.aspnet).By(0.8f).WhichOverlaps().By(0.15f);

        t.typeScript.Overlaps(t.frontend).By(0.4f).WhichOverlaps().By(0.15f);
        t.javaScript.Overlaps(t.frontend).By(0.7f).WhichOverlaps().By(0.07f);
        // build tools
        t.vite.Overlaps(t.frontend).By(0.75f).WhichOverlaps().By(0.05f);
        t.restApi.Overlaps(t.frontend).By(0.35f).WhichOverlaps().By(0.04f);
        // dom manipulation
        t.jquery.Overlaps(t.frontend).By(0.95f).WhichOverlaps().By(0.05f);
        t.vue.Overlaps(t.frontend).By(0.7f).WhichOverlaps().By(0.15f);

        var r = db.Build();
        if (r.Errors != null)
        {
            throw new Exception(string.Join("\n", r.Errors));
        }

        var tags1 = t.Map(t1 => new Tag(t1.Name));
        return (tags1, r.Database!);
    }
}
