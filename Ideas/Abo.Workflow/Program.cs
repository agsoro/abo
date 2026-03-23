using Abo.Tools;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");



// Register BPMN Tools for whoever needs them locally
builder.Services.AddTransient<IAboTool, CreateProcessTool>();
builder.Services.AddTransient<IAboTool, UpdateProcessTool>();
builder.Services.AddTransient<IAboTool, CheckBpmnTool>();

app.MapGet("/api/processes", () =>
{
    var processesDir = Path.Combine(AppContext.BaseDirectory, "Data", "Processes");
    if (!Directory.Exists(processesDir)) return Results.Ok(new List<string>());

    var files = Directory.GetFiles(processesDir, "*.bpmn")
                         .Select(f => Path.GetFileNameWithoutExtension(f))
                         .ToList();
    return Results.Ok(files);
});

app.MapGet("/api/processes/{id}", async (string id) =>
{
    // Basic sanitization
    if (id.Contains("..") || id.Contains("/") || id.Contains("\\")) return Results.BadRequest("Invalid process id.");

    var path = Path.Combine(AppContext.BaseDirectory, "Data", "Processes", $"{id}.bpmn");
    if (!File.Exists(path)) return Results.NotFound();

    var xml = await File.ReadAllTextAsync(path);
    return Results.Text(xml, "application/xml");
});

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
