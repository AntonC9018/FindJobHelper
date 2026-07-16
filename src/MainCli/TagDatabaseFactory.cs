using FindJobHelper.Core;

namespace MainCli;

public sealed class Tags<T> : KnownTags<T>
{
    public Tags<U> Map<U>(Func<T, U> f) => (Tags<U>) MapImpl(f);

    public required T programming { get; init; }
    public required T algorithms { get; init; }
    public required T dataStructures { get; init; }
    public required T graphs { get; init; }
    public required T concurrency { get; init; }
    public required T multithreading { get; init; }
    public required T networking { get; init; }
    public required T http { get; init; }
    public required T peerToPeer { get; init; }
    public required T tcp { get; init; }
    public required T udp { get; init; }
    public required T sockets { get; init; }
    public required T natTraversal { get; init; }
    public required T dotnet { get; init; }
    public required T googleApi { get; init; }
    public required T go { get; init; }
    public required T aspnet { get; init; }
    public required T htmx { get; init; }
    public required T cpp { get; init; }
    public required T python { get; init; }
    public required T designPatterns { get; init; }
    public required T xml { get; init; }
    public required T webScraping { get; init; }
    public required T teachingSkill { get; init; }
    public required T mentoring { get; init; }
    public required T openApi { get; init; }
    public required T restApi { get; init; }
    public required T roslyn { get; init; }
    public required T sourceGeneration { get; init; }
    public required T toolingDevelopment { get; init; }
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
    public required T debugging { get; init; }
    public required T linq2db { get; init; }
    public required T parser { get; init; }
    public required T msBuild { get; init; }
    public required T nuget { get; init; }
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
    public required T physics { get; init; }
    public required T communicationWithClient { get; init; }
    public required T typeScript { get; init; }
    public required T javaScript { get; init; }
    public required T frontend { get; init; }
    public required T express { get; init; }
    public required T socketIo { get; init; }
    public required T htmlCanvas { get; init; }
    public required T ebayApi { get; init; }
    public required T webhooks { get; init; }
    public required T vite { get; init; }
    public required T tailwind { get; init; }
    public required T nodejs { get; init; }
    public required T shaders { get; init; }
    public required T imageProcessing { get; init; }
    public required T ffmpeg { get; init; }
    public required T youtubeApi { get; init; }
    public required T oauth { get; init; }
    public required T compression { get; init; }
    public required T d { get; init; }
    public required T zig { get; init; }
    public required T postgres { get; init; }
    public required T vue { get; init; }
    public required T css { get; init; }
    public required T jquery { get; init; }
    public required T dotween { get; init; }
    public required T unity { get; init; }
    public required T blender { get; init; }
    public required T uiToolkit { get; init; }
    public required T imgui { get; init; }
    public required T raylib { get; init; }
    public required T ugui { get; init; }
    public required T grpc { get; init; }
    public required T protobuf { get; init; }
    public required T azure { get; init; }
    public required T neon { get; init; }
    public required T backend { get; init; }
    public required T highVolumeDataProcessing { get; init; }
    public required T microservices { get; init; }
    public required T json { get; init; }
    public required T docx { get; init; }
    public required T latex { get; init; }
    public required T blazor { get; init; }
    public required T razor { get; init; }
    public required T serverSideRendering { get; init; }
    public required T caching { get; init; }
    public required T ocr { get; init; }
    public required T browserAutomation { get; init; }
    public required T infrastructureAsCode { get; init; }
    public required T aiAssistedDevelopment { get; init; }
    public required T refactoring { get; init; }
    public required T webforms { get; init; }
    public required T aspnetmvc { get; init; }
    public required T thesis { get; init; }
    public required T apiDesign { get; init; }
    public required T png { get; init; }
    public required T jpeg { get; init; }
    public required T tiff { get; init; }
    public required T _3d { get; init; }
}

public static class TagsDatabaseFactory
{
    public static (Tags<Tag> Tags, TagsDatabase TagsDatabase) Create()
    {
        var db = new TagsDatabaseBuilder();

        var t = new Tags<TagBuilder>
        {
            dotnet = db.Tag(".NET", "DotNet", "C#"),
            googleApi = db.Tag("Google API"),
            graphql = db.Tag("GraphQL"),
            aspnet = db.Tag("ASP.NET Core", "ASP.NET"),
            go = db.Tag("Go"),
            htmx = db.Tag("HTMX"),
            cpp = db.Tag("C++"),
            python = db.Tag("Python"),
            designPatterns = db.Tag("Design Patterns"),
            algorithms = db.Tag("Algorithms"),
            dataStructures = db.Tag("Data Structures"),
            graphs = db.Tag("Graphs", "Graph Algorithms", "Graph Data Structures"),
            webScraping = db.Tag("Web Scraping"),
            teachingSkill = db.Tag("Teaching"),
            mentoring = db.Tag("Mentoring"),
            openApi = db.Tag("OpenAPI"),
            restApi = db.Tag("REST API", "REST"),
            roslyn = db.Tag("Roslyn"),
            sourceGeneration = db.Tag("Source Generation", "Source Generators", ".NET Source Generators", "Code Generation"),
            toolingDevelopment = db.Tag("Tooling Development", "Developer Tooling", "Development Tools", ".NET Tooling"),
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
            debugging = db.Tag("Debugging", "Bug Fixing", "Troubleshooting", "Root-Cause Analysis"),
            linq2db = db.Tag("Linq2db"),
            parser = db.Tag("Parser"),
            msBuild = db.Tag("MSBuild"),
            nuget = db.Tag("NuGet"),
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
            physics = db.Tag("Physics", "Vehicle Physics", "Physics Simulation"),
            communicationWithClient = db.Tag("Communication with the client"),
            typeScript = db.Tag("TypeScript"),
            javaScript = db.Tag("JavaScript"),
            frontend = db.Tag("FrontEnd"),
            express = db.Tag("Express.js", "Express", "ExpressJS"),
            socketIo = db.Tag("Socket.IO", "SocketIO"),
            htmlCanvas = db.Tag("HTML5 Canvas", "HTML Canvas", "Canvas API"),
            ebayApi = db.Tag("eBay API"),
            webhooks = db.Tag("Webhooks", "Webhook"),
            jquery = db.Tag("JQuery"),
            vite = db.Tag("Vite"),
            tailwind = db.Tag("Tailwind", "Tailwind CSS"),
            nodejs = db.Tag("NodeJS"),
            shaders = db.Tag("Shaders"),
            imageProcessing = db.Tag("Image Processing"),
            ffmpeg = db.Tag("FFmpeg"),
            youtubeApi = db.Tag("YouTube API"),
            oauth = db.Tag("OAuth 2.0", "OAuth"),
            compression = db.Tag("Compression", "Zlib", "DEFLATE", "Huffman", "Checksum", "CRC", "Adler32"),
            d = db.Tag("D"),
            zig = db.Tag("Zig"),
            postgres = db.Tag("PostgreSQL"),
            vue = db.Tag("Vue", "VueJS"),
            css = db.Tag("CSS"),
            programming = db.Tag("Programming"),
            concurrency = db.Tag("Concurrency", "Concurrency Programming", "Concurrent Programming", "Parallel Programming"),
            multithreading = db.Tag("Multithreading", "Multi-threading", "Threading"),
            networking = db.Tag("Networking", "Network Programming"),
            http = db.Tag("HTTP", "HTTP Protocol"),
            peerToPeer = db.Tag("Peer-to-Peer", "Peer to Peer", "P2P"),
            tcp = db.Tag("TCP"),
            udp = db.Tag("UDP"),
            sockets = db.Tag("Sockets", "Socket Programming"),
            natTraversal = db.Tag("NAT Traversal", "NAT Hole Punching", "Hole Punching"),
            dotween = db.Tag("DOTween"),
            unity = db.Tag("Unity", "Unity3D"),
            blender = db.Tag("Blender"),
            imgui = db.Tag("IMGUI"),
            raylib = db.Tag("Raylib"),
            ugui = db.Tag("ugui"),
            uiToolkit = db.Tag("UI Toolkit"),
            grpc = db.Tag("GRPC"),
            protobuf = db.Tag("protobuf"),
            azure = db.Tag("Azure"),
            neon = db.Tag("Neon"),
            backend = db.Tag("Backend"),
            highVolumeDataProcessing = db.Tag("High-Volume Data Processing"),
            microservices = db.Tag("microservices"),
            cicd = db.Tag("CI/CD"),
            xml = db.Tag("XML"),
            json = db.Tag("JSON"),
            docx = db.Tag("DOCX"),
            latex = db.Tag("LaTeX"),
            blazor = db.Tag("Blazor"),
            razor = db.Tag("Razor", "Razor Pages"),
            serverSideRendering = db.Tag("Server-Side Rendering", "SSR"),
            caching = db.Tag("Caching"),
            ocr = db.Tag("OCR", "Optical Character Recognition"),
            browserAutomation = db.Tag("Browser Automation", "Playwright"),
            infrastructureAsCode = db.Tag("Infrastructure as Code", "IaC", "Pulumi"),
            aiAssistedDevelopment = db.Tag("AI-Assisted Development", "AI Development Tools", "Codex", "Agentic Coding"),
            refactoring = db.Tag("Refactoring", "Legacy Code Modernization"),
            webforms = db.Tag("WebForms"),
            aspnetmvc = db.Tag("ASP.NET MVC"),
            csv = db.Tag("CSV"),
            thesis = db.Tag("Thesis"),
            apiDesign = db.Tag("API Design"),
            _3d = db.Tag("3D"),
            jpeg = db.Tag("JPEG"),
            png = db.Tag("PNG"),
            tiff = db.Tag("TIFF"),
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

        t.sourceGeneration.IsIncludedIn(t.roslyn).By(0.8f).WhichIsIncludedInIt().By(0.6f);
        t.sourceGeneration.IsIncludedIn(t.dotnet).By(0.5f).WhichIsIncludedInIt().By(0.2f);
        t.nuget.IsIncludedIn(t.dotnet).By(0.4f).WhichIsIncludedInIt().By(0.15f);
        t.nuget.IsIncludedIn(t.msBuild).By(0.3f).WhichIsIncludedInIt().By(0.2f);
        // Source generators, Roslyn analyzers and MSBuild integrations are concrete forms of developer tooling;
        // keep the umbrella-to-tool weights modest because tooling development is broader than any one mechanism.
        t.toolingDevelopment.IsIncludedIn(t.sourceGeneration).By(0.35f).WhichIsIncludedInIt().By(0.85f);
        t.toolingDevelopment.IsIncludedIn(t.roslyn).By(0.35f).WhichIsIncludedInIt().By(0.65f);
        t.toolingDevelopment.IsIncludedIn(t.msBuild).By(0.3f).WhichIsIncludedInIt().By(0.75f);

        t.dotnet.IsIncludedIn(t.programming).By(0.25f).WhichIsIncludedInIt().Fully();
        t.cpp.IsIncludedIn(t.programming).By(0.2f).WhichIsIncludedInIt().Fully();
        t.typeScript.IsIncludedIn(t.programming).By(0.3f).WhichIsIncludedInIt().Fully();
        t.java.IsIncludedIn(t.programming).By(0.3f).WhichIsIncludedInIt().Fully();
        t.javaScript.IsIncludedIn(t.programming).By(0.4f).WhichIsIncludedInIt().Fully();
        t.d.IsIncludedIn(t.programming).By(0.25f).WhichIsIncludedInIt().Fully();
        t.zig.IsIncludedIn(t.programming).By(0.35f).WhichIsIncludedInIt().Fully();
        t.python.IsIncludedIn(t.programming).By(0.3f).WhichIsIncludedInIt().Fully();
        t.go.IsIncludedIn(t.programming).By(0.55f).WhichIsIncludedInIt().Fully();

        // Algorithms and data structures are core programming fundamentals, but keep their graph
        // weights modest so a requirement for them does not over-rank every language-only item.
        // Graph work is both a concrete data-structure specialization and a setting for traversal algorithms.
        t.algorithms.IsIncludedIn(t.programming).By(0.1f).WhichIsIncludedInIt().By(0.05f);
        t.dataStructures.IsIncludedIn(t.programming).By(0.1f).WhichIsIncludedInIt().By(0.05f);
        t.graphs.IsIncludedIn(t.dataStructures).By(0.9f).WhichIsIncludedInIt().By(0.25f);
        t.graphs.IsIncludedIn(t.algorithms).By(0.65f).WhichIsIncludedInIt().By(0.2f);
        t.concurrency.IsIncludedIn(t.programming).By(0.35f).WhichIsIncludedInIt().By(0.1f);
        t.multithreading.IsIncludedIn(t.concurrency).By(0.85f).WhichIsIncludedInIt().By(0.55f);

        // TCP, UDP, socket programming, peer-to-peer communication and NAT traversal are concrete networking work.
        // The reverse weights stay low because a broad networking role need not use any one protocol or technique.
        t.networking.IsIncludedIn(t.programming).By(0.45f).WhichIsIncludedInIt().By(0.1f);
        t.tcp.IsIncludedIn(t.networking).By(0.95f).WhichIsIncludedInIt().By(0.15f);
        t.udp.IsIncludedIn(t.networking).By(0.95f).WhichIsIncludedInIt().By(0.15f);
        t.sockets.IsIncludedIn(t.networking).By(0.9f).WhichIsIncludedInIt().By(0.12f);
        t.peerToPeer.IsIncludedIn(t.networking).By(0.85f).WhichIsIncludedInIt().By(0.08f);
        t.natTraversal.IsIncludedIn(t.networking).By(0.9f).WhichIsIncludedInIt().By(0.05f);

        // Hole punching is a peer-to-peer connectivity technique, while peer-to-peer systems can use other approaches.
        t.natTraversal.IsIncludedIn(t.peerToPeer).By(0.85f).WhichIsIncludedInIt().By(0.25f);

        // HTTP is an application-layer networking protocol normally carried over TCP. REST APIs
        // and OpenAPI-described services are strong HTTP evidence, while HTTP itself is broader.
        t.http.IsIncludedIn(t.networking).By(0.1f).WhichIsIncludedInIt().By(0.05f);
        t.http.IsIncludedIn(t.tcp).By(0.1f).WhichIsIncludedInIt().By(0.05f);
        t.restApi.IsIncludedIn(t.http).By(0.1f).WhichIsIncludedInIt().By(0.1f);
        t.openApi.IsIncludedIn(t.http).By(0.1f).WhichIsIncludedInIt().By(0.05f);

        // Testing, debugging and refactoring are transferable programming activities, not language-specific skills;
        // modest reverse weights let general programming searches find them without making them interchangeable.
        t.unitTests.IsIncludedIn(t.programming).By(0.45f).WhichIsIncludedInIt().By(0.1f);
        t.debugging.IsIncludedIn(t.programming).By(0.5f).WhichIsIncludedInIt().By(0.12f);
        t.refactoring.IsIncludedIn(t.programming).By(0.55f).WhichIsIncludedInIt().By(0.12f);

        // Refactoring often applies design-pattern knowledge, while design-pattern work does not necessarily refactor code.
        t.refactoring.IsIncludedIn(t.designPatterns).By(0.5f).WhichIsIncludedInIt().By(0.15f);

        // Teaching is strong evidence of mentoring, while workplace mentoring does not necessarily imply formal teaching.
        t.teachingSkill.IsIncludedIn(t.mentoring).By(0.85f).WhichIsIncludedInIt().By(0.45f);

        t.javaScript.IsIncludedIn(t.typeScript).Fully().WhichIsIncludedInIt().By(0.3f);

        t.dotnet.IsIncludedIn(t.graphql).By(0.05f);
        t.hotChocolate.IsIncludedIn(t.graphql).By(0.7f).WhichIsIncludedInIt().By(0.9f);
        t.openApi.IsIncludedIn(t.nswag).By(0.9f).WhichIsIncludedInIt().By(0.6f);
        t.restApi.IsIncludedIn(t.aspnet).By(0.8f).WhichIsIncludedInIt().By(0.15f);
        // Postman exercises REST APIs directly, and the Google APIs represented in the experience database are REST APIs;
        // low reverse weights avoid treating either vendor/tool tag as a requirement of REST work in general.
        t.postman.IsIncludedIn(t.restApi).By(0.85f).WhichIsIncludedInIt().By(0.08f);
        t.googleApi.IsIncludedIn(t.restApi).By(0.6f).WhichIsIncludedInIt().By(0.08f);

        t.typeScript.IsIncludedIn(t.frontend).By(0.4f).WhichIsIncludedInIt().By(0.15f);
        t.javaScript.IsIncludedIn(t.frontend).By(0.7f).WhichIsIncludedInIt().By(0.07f);
        // build tools
        t.vite.IsIncludedIn(t.frontend).By(0.75f).WhichIsIncludedInIt().By(0.05f);
        t.restApi.IsIncludedIn(t.frontend).By(0.35f).WhichIsIncludedInIt().By(0.04f);
        // dom manipulation
        t.jquery.IsIncludedIn(t.frontend).By(0.95f).WhichIsIncludedInIt().By(0.05f);
        t.vue.IsIncludedIn(t.frontend).By(0.7f).WhichIsIncludedInIt().By(0.15f);
        // HTMX is a frontend interaction library; CSS is a core frontend technology; and Tailwind is implemented in CSS.
        // The asymmetric reverse weights keep broad frontend/CSS requests from over-ranking a particular library.
        t.htmx.IsIncludedIn(t.frontend).By(0.9f).WhichIsIncludedInIt().By(0.05f);
        t.css.IsIncludedIn(t.frontend).By(0.9f).WhichIsIncludedInIt().By(0.12f);
        t.tailwind.IsIncludedIn(t.css).By(0.95f).WhichIsIncludedInIt().By(0.15f);

        t.unity.IsIncludedIn(t.dotween).By(0.05f).WhichIsIncludedInIt().By(0.95f);
        t.unity.IsIncludedIn(t.dotnet).By(0.15f).WhichIsIncludedInIt().By(0.5f);

        t.unity.IsIncludedIn(t.ugui).By(0.05f).WhichIsIncludedInIt().Fully();
        t.graphics.IsIncludedIn(t.blender).By(0.1f).WhichIsIncludedInIt().By(0.7f);
        t.graphics.IsIncludedIn(t.unity).By(0.25f).WhichIsIncludedInIt().By(0.25f);
        t.unity.IsIncludedIn(t.uiToolkit).By(0.05f).WhichIsIncludedInIt().By(1.0f);
        t.unity.IsIncludedIn(t.imgui).By(0.05f).WhichIsIncludedInIt().By(0.70f);
        t.graphics.IsIncludedIn(t.imgui).By(0.05f).WhichIsIncludedInIt().By(0.70f);
        t.unity.IsIncludedIn(t.gameProgramming).By(0.5f).WhichIsIncludedInIt().By(0.4f);
        // Vehicle-physics work is strong game-programming evidence, while game programming also covers many non-physics systems.
        t.physics.IsIncludedIn(t.gameProgramming).By(0.8f).WhichIsIncludedInIt().By(0.08f);
        t.graphics.IsIncludedIn(t.shaders).By(0.1f).WhichIsIncludedInIt().Fully();
        t.unity.IsIncludedIn(t.shaders).By(0.05f).WhichIsIncludedInIt().By(0.9f);
        t.raylib.IsIncludedIn(t.graphics).By(0.8f).WhichIsIncludedInIt().By(0.2f);
        // OpenGL work is directly graphics work, but graphics experience is much broader than OpenGL.
        t.openGl.IsIncludedIn(t.graphics).By(0.95f).WhichIsIncludedInIt().By(0.15f);
        t.compression.IsIncludedIn(t.programming).By(0.2f).WhichIsIncludedInIt().By(0.1f);
        t.compression.IsIncludedIn(t.imageProcessing).By(0.35f).WhichIsIncludedInIt().By(0.2f);
        t.apiDesign.IsIncludedIn(t.designPatterns).By(0.5f).WhichIsIncludedInIt().By(0.2f);
        t.apiDesign.IsIncludedIn(t.programming).By(0.2f).WhichIsIncludedInIt().By(0.1f);
        // AI-assisted development and browser automation are concrete forms of developer tooling.
        // Infrastructure as code is also cloud-relevant, but provider-independent.
        t.aiAssistedDevelopment.IsIncludedIn(t.toolingDevelopment).By(0.7f).WhichIsIncludedInIt().By(0.08f);
        t.browserAutomation.IsIncludedIn(t.toolingDevelopment).By(0.65f).WhichIsIncludedInIt().By(0.06f);
        t.infrastructureAsCode.IsIncludedIn(t.programming).By(0.4f).WhichIsIncludedInIt().By(0.05f);
        t.infrastructureAsCode.IsIncludedIn(t.aws).By(0.55f).WhichIsIncludedInIt().By(0.08f);
        t.ocr.IsIncludedIn(t.imageProcessing).By(0.8f).WhichIsIncludedInIt().By(0.15f);
        t.ocr.IsIncludedIn(t.parser).By(0.45f).WhichIsIncludedInIt().By(0.08f);

        t.grpc.IsIncludedIn(t.protobuf).By(0.2f).WhichIsIncludedInIt().Fully();

        // Azure and AWS share transferable cloud concepts, but they are not interchangeable skills;
        // keep the relation modest so an Azure-targeted CV does not over-rank AWS-only experience.
        t.azure.IsIncludedIn(t.aws).By(0.25f).WhichIsIncludedInIt().By(0.25f);

        t.sql.IsIncludedIn(t.sqlServer).Fully().WhichIsIncludedInIt().By(0.4f);
        t.sql.IsIncludedIn(t.postgres).Fully().WhichIsIncludedInIt().By(0.3f);
        t.sql.IsIncludedIn(t.mysql).Fully().WhichIsIncludedInIt().By(0.5f);

        t.sqlServer.IsIncludedIn(t.postgres).By(0.5f).WhichIsIncludedInIt().By(0.35f);
        t.sqlServer.IsIncludedIn(t.mysql).By(0.5f).WhichIsIncludedInIt().By(0.5f);
        t.sql.IsIncludedIn(t.efCore).By(0.7f).WhichIsIncludedInIt().By(0.3f);

        // Neon is a managed PostgreSQL platform, so Neon experience is strongly PostgreSQL-relevant;
        // PostgreSQL itself is provider-independent, hence the small reverse weight.
        t.neon.IsIncludedIn(t.postgres).By(0.95f).WhichIsIncludedInIt().By(0.12f);

        t.backend.IsIncludedIn(t.go).By(0.2f).WhichIsIncludedInIt().By(0.2f);
        t.backend.IsIncludedIn(t.grpc).By(0.05f).WhichIsIncludedInIt().By(0.35f);
        t.backend.IsIncludedIn(t.aspnet).By(0.2f).WhichIsIncludedInIt().By(0.6f);
        t.backend.IsIncludedIn(t.sql).By(0.15f).WhichIsIncludedInIt().By(0.9f);
        t.backend.IsIncludedIn(t.restApi).By(0.21f).WhichIsIncludedInIt().By(0.5f);
        t.backend.IsIncludedIn(t.graphql).By(0.15f).WhichIsIncludedInIt().By(0.2f);
        t.backend.IsIncludedIn(t.openApi).By(0.10f).WhichIsIncludedInIt().By(0.8f);
        t.backend.IsIncludedIn(t.nodejs).By(0.10f).WhichIsIncludedInIt().By(0.45f);

        t.backend.IsIncludedIn(t.microservices).By(0.1f).WhichIsIncludedInIt().By(0.8f);

        // Application security is cross-cutting but strongly represented by backend authorization in this database;
        // the low reverse weight prevents ordinary backend work from scoring as dedicated security experience.
        t.security.IsIncludedIn(t.backend).By(0.55f).WhichIsIncludedInIt().By(0.08f);

        // nginx commonly serves or proxies backends on Linux; Linux/backend knowledge only weakly implies nginx.
        t.nginx.IsIncludedIn(t.linux).By(0.75f).WhichIsIncludedInIt().By(0.08f);
        t.nginx.IsIncludedIn(t.backend).By(0.55f).WhichIsIncludedInIt().By(0.05f);

        foreach (var cloudProvider in new[] { t.aws, t.azure })
        {
            t.cicd.IsIncludedIn(cloudProvider).By(0.9f).WhichIsIncludedInIt().By(0.2f);
        }

        // Containers are commonly built and deployed by CI/CD pipelines, while CI/CD is not necessarily container-based.
        t.docker.IsIncludedIn(t.cicd).By(0.65f).WhichIsIncludedInIt().By(0.35f);

        // GitHub is built around Git repositories, but Git skills transfer beyond a single hosting provider.
        t.github.IsIncludedIn(t.git).By(0.9f).WhichIsIncludedInIt().By(0.65f);

        t.json.IsIncludedIn(t.javaScript).Fully().WhichIsIncludedInIt().By(0.1f);
        t.json.IsIncludedIn(t.typeScript).Fully().WhichIsIncludedInIt().By(0.05f);
        t.json.IsIncludedIn(t.xml).By(0.2f).WhichIsIncludedInIt().By(0.1f);
        t.xml.IsIncludedIn(t.msBuild).Fully().WhichIsIncludedInIt().By(0.1f);
        t.xml.IsIncludedIn(t.docx).Fully().WhichIsIncludedInIt().By(0.05f);
        t.xml.IsIncludedIn(t.excel).Fully().WhichIsIncludedInIt().By(0.08f);
        // CSV and Excel both represent tabular data, and CSV work usually involves parsing a structured text format;
        // partial weights preserve the important format and tooling differences.
        t.csv.IsIncludedIn(t.excel).By(0.65f).WhichIsIncludedInIt().By(0.3f);
        t.csv.IsIncludedIn(t.parser).By(0.55f).WhichIsIncludedInIt().By(0.12f);
        // Web scraping extracts information by parsing remote documents, but generic parser work is rarely web scraping.
        t.webScraping.IsIncludedIn(t.parser).By(0.6f).WhichIsIncludedInIt().By(0.1f);

        t.aspnetmvc.IsIncludedIn(t.aspnet).Fully().WhichIsIncludedInIt().By(0.10f);

        // Razor is part of the ASP.NET web stack and also exercises frontend concerns;
        // model both links so ASP.NET tooling roles can find concrete Razor work automatically.
        t.razor.IsIncludedIn(t.aspnet).Fully().WhichIsIncludedInIt().By(0.25f);
        t.razor.IsIncludedIn(t.frontend).By(0.70f).WhichIsIncludedInIt().By(0.10f);

        // Razor Pages renders HTML on the server, so it is direct SSR evidence. SSR remains
        // framework-independent, hence the deliberately small reverse relation to Razor.
        t.razor.IsIncludedIn(t.serverSideRendering).By(0.85f).WhichIsIncludedInIt().By(0.2f);
        t.serverSideRendering.IsIncludedIn(t.frontend).By(0.7f).WhichIsIncludedInIt().By(0.08f);

        // Blazor is an ASP.NET frontend framework; WebForms is a legacy ASP.NET frontend framework.
        // Reverse weights stay low because neither framework represents ASP.NET or frontend work as a whole.
        t.blazor.IsIncludedIn(t.aspnet).By(0.9f).WhichIsIncludedInIt().By(0.18f);
        t.blazor.IsIncludedIn(t.frontend).By(0.75f).WhichIsIncludedInIt().By(0.08f);
        t.webforms.IsIncludedIn(t.aspnet).By(0.95f).WhichIsIncludedInIt().By(0.08f);
        t.webforms.IsIncludedIn(t.frontend).By(0.65f).WhichIsIncludedInIt().By(0.04f);
        // avalonia

        t._3d.IsIncludedIn(t.graphics).Fully().WhichIsIncludedInIt().By(0.3f);
        t.png.IsIncludedIn(t.jpeg).By(0.3f).WhichIsIncludedInIt().By(0.3f);
        t.png.IsIncludedIn(t.tiff).By(0.3f).WhichIsIncludedInIt().By(0.3f);
        t.png.IsIncludedIn(t.parser).By(0.2f).WhichIsIncludedInIt().By(0.1f);
        t.jpeg.IsIncludedIn(t.parser).By(0.2f).WhichIsIncludedInIt().By(0.1f);
        t.tiff.IsIncludedIn(t.parser).By(0.2f).WhichIsIncludedInIt().By(0.1f);

        var r = db.Build();
        if (r.Errors != null)
        {
            throw new Exception(string.Join("\n", r.Errors));
        }

        var tags1 = t.Map(t1 => new Tag(t1.Name));
        return (tags1, r.Database!);
    }
}
