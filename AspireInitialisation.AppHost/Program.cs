using AspireInitialisation.AppHost;
using Microsoft.Extensions.DependencyInjection;
using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var sql = builder.AddSqlServer("sql")
    .WithLifetime(ContainerLifetime.Persistent)
    ;

var db = sql.AddDatabase("mydb");

// Dummy migrator app that just waits 3 seconds
//var init = builder.AddExecutable("init", "pwsh", ".", "-Command", """
//        $wait = 3
//        Write-Host Waiting $wait Seconds
//        Start-Sleep -Seconds $wait
//        Write-Host Done
//        """);

// dotnet ef update
var migrator = builder.AddExecutable("db-migrations", "dotnet-ef", "..")
    .WithArgs("database",
    "update",
    "--connection", db.Resource.ConnectionStringExpression,
    "--project", new AspireInitialisation_DbMigrations().ProjectPath,
    "--no-build",
    "-v");

db.WithInitialiser(migrator);

var app = builder.AddProject<AspireInitialisation_ApiService>("app")
    .WaitFor(db);

builder
    .Build()
    .Run();
