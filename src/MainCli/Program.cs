using System.Text;
using CommandDotNet;

Console.OutputEncoding = new UTF8Encoding(
    encoderShouldEmitUTF8Identifier: false);

var appSettings = new AppSettings();
appSettings.ArgumentTypeDescriptors.Add(new CvOutputFormatTypeDescriptor());

return await new AppRunner<CvGenerationCommand>(appSettings)
    .UseDefaultMiddleware()
    .RunAsync(args);
