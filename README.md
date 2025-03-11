# Aspire Initialisers

This is a proof of concept for doing initialisers in Aspire.
Initialisers are code which run in between a resource becoming healthy, and other dependant resources start up.
My thoughts so far have been mainly focused around database migrations before an app starts up, but these concepts broadly apply to any kind of initialisation.
e.g. Running CLI commands within a container

## Examples

Initialiser with inline code:

```cs
var sql = builder.AddSqlServer("sql");
var database = sql.AddDatabase("database");

database.WithInitialiser(async ctx =>
{
    var connectionString = await database.Resource.ConnectionStringExpression.GetValueAsync(ctx.CancellationToken);
    //Do something with the connection string
    await Task.Delay(TimeSpan.FromSeconds(5));
});
```

Initialisation using another resource (e.g. executable or container)

```cs
var sql = builder.AddSqlServer("sql");
var database = sql.AddDatabase("database");

// Or use another resource:
var migrator = builder.AddExecutable("db-migrations", "dotnet-ef", "..")
    .WithArgs("database",
        "update",
        "--connection", database.Resource.ConnectionStringExpression,
        "--project", new Projects.AspireInitialisation_DbMigrations().ProjectPath,
        "--no-build",
        "-v")
    .WithParentRelationship(database);

database.AddInitialiser("migrator", migrator);
```

### Known Issues
- Error handling is hardly tested
- There's no support for ordering initialisers amongst each other.
  If you need ordering, you'll need to co-ordinate the initialisers yourself.

## Previous Versions  History

### Health Check based

This implementation was a follow on from https://github.com/dotnet/aspire/discussions/6117 following David Fowler's suggestion of using Health Checks.
Whilst using Health Checks works for these initialisers, the UX is a bit funky in places, including
- Resources show as "Unhealthy" whilst they are being initialised
- Resources do not go healthy immediately after all initialisers are complete - needs to wait for the next health check poll interval (p to 5 seconds).

[Operators](https://github.com/dotnet/aspire/issues/6040) could be a better way to plumb this in over the longer term.

Problems with this approach included:

- Resources show as `Unhealthy` whilst initialisers are running
- There can be up to 5 seconds delay between initialisers completing and any dependencies starting.
  This is due to Health Checks only being polled every 5 seconds.
  During this time, UX is a bit odd as the resource has progressed from `Initialising` to `Running`, but still shows as unhealthy.
  Not much can be done about this unless [Operators](https://github.com/dotnet/aspire/issues/6040) are introduced.
- Initialisers won't re-run if a resource is restarted.

### Custom Resource

A previous version (allthough not committed to Github) relied on implementing my own version of `SqlDatabaseResource`

This resulted in the best user experience as I had full end to end control to tune the UX as I wanted.
The downside was it was not a generic solution, and involved implementing my own version of every Resource.
It worked great as a proof of concept of what could be, but was a lot of maintenance.
