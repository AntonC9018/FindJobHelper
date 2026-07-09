using CommandDotNet;

return await new AppRunner<CvGenerationCommand>()
    .UseDefaultMiddleware()
    .RunAsync(args);
