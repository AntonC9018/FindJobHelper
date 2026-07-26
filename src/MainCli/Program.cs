using CommandDotNet;

var appSettings = new AppSettings();
appSettings.ArgumentTypeDescriptors.Add(new CvOutputFormatTypeDescriptor());

return await new AppRunner<CvGenerationCommand>(appSettings)
    .UseDefaultMiddleware()
    .RunAsync(args);
