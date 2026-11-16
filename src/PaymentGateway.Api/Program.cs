using PaymentGateway.Api.Clients;
using PaymentGateway.Api.ExceptionHandling;
using PaymentGateway.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Read bank settings up front so a misconfigured app fails at startup, not on the first payment.
var bankBaseUrl = builder.Configuration["BankClient:BaseUrl"]
                  ?? throw new InvalidOperationException("BankClient:BaseUrl is not configured.");
var bankTimeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("BankClient:TimeoutSeconds", 10));

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddSingleton<IPaymentsRepository, PaymentsRepository>();

// No retry policy on purpose: a payment POST isn't idempotent, so retrying after a lost
// response could charge the card twice.
builder.Services.AddHttpClient<IBankClient, BankClient>(client =>
{
    client.BaseAddress = new Uri(bankBaseUrl);
    client.Timeout = bankTimeout;
});

builder.Services.AddScoped<IPaymentService, PaymentService>();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
