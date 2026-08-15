using MedGuard.API.Middleware;
using MedGuard.Application;
using MedGuard.Application.Bases;
using MedGuard.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddScoped<ResponseHandler>();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddCors(conf =>
{
    conf.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyHeader()
        .AllowAnyOrigin()
        .AllowAnyMethod();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAll");

app.UseMedGuardExceptionHandling();
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
