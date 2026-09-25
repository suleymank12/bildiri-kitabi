using BildiriKitabi.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddBookGeneration();

var app = builder.Build();

app.MapControllers();

app.Run();
