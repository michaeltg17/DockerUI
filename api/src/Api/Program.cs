using Api;

WebApplication
    .CreateBuilder(args)
    .AddDependencies()
    .Build()
    .Configure()
    .Run();
