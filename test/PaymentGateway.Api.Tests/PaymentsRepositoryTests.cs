using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Repositories;

namespace PaymentGateway.Api.Tests;

public class PaymentsRepositoryTests
{
    [Fact]
    public void Add_WhenIdAlreadyExists_ThrowsAndKeepsTheOriginal()
    {
        var repository = new PaymentsRepository();
        var id = Guid.NewGuid();
        var original = new PaymentResponse { Id = id, Amount = 100 };
        repository.Add(original);

        Assert.Throws<InvalidOperationException>(() => repository.Add(new PaymentResponse { Id = id, Amount = 999 }));
        Assert.Same(original, repository.Get(id));
    }
}
