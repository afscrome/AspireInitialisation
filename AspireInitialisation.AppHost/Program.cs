using AspireInitialisation.AppHost;
using Microsoft.Extensions.DependencyInjection;
using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var sql = builder.AddSqlServer("sql")
    //.WithLifetime(ContainerLifetime.Persistent)
    ;

var db = sql.AddDatabase("mydb");

var migrator = builder.AddExecutable("db-migrations", "dotnet-ef", "..")
    .WithArgs("database",
    "update",
    "--connection", db.Resource.ConnectionStringExpression,
    "--project", new AspireInitialisation_DbMigrations().ProjectPath,
    "--no-build",
    "-v")
    ;

//TODO: Work out how to make this a child of sql or db
// This work sometimes, but usually doesn't
builder.Eventing.Subscribe<BeforeResourceStartedEvent>(migrator.Resource, async (evt, ct) =>
{
    var resourceNotificationService = evt.Services.GetRequiredService<ResourceNotificationService>();

    await resourceNotificationService.PublishUpdateAsync(migrator.Resource, x => x with
    {
        Properties = [
            ..x.Properties,
            new("resource.parentName", sql.Resource.Name)
            ]
    });
});

db.WithInitialiser(migrator);

builder
    .Build()
    .Run();


