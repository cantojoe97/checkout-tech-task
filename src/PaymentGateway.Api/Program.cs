using PaymentGateway.Api.Clients;
using PaymentGateway.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<IPaymentsRepository, PaymentsRepository>();

builder.Services.AddHttpClient<IBankClient, BankClient>(client =>
{
    var baseUrl = builder.Configuration["BankClient:BaseUrl"]
                  ?? throw new InvalidOperationException("BankClient:BaseUrl is not configured.");
    client.BaseAddress = new Uri(baseUrl);
});

builder.Services.AddSingleton<IPaymentService, PaymentService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
