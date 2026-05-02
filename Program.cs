using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

SeedData();

app.MapGet("/", () => Results.Ok(new
{
    Service = "Invoice API",
    Version = "1.0",
    Endpoints = new[]
    {
        "GET /customers/{customerId}/invoices",
        "POST /invoices/{invoiceId}/pay",
        "POST /orders/{orderId}/status",
        "GET /reports/customer/{customerId}"
    }
}));

app.MapGet("/customers/{customerId}/invoices", (int customerId, string? userEmail) =>
{
    var logger = new ConsoleAuditLogger();
    var crmClient = new CrmClient("https://crm.internal.local", "crm-secret-key");

    logger.Info($"User {userEmail} is reading invoices for customer {customerId}");

    var customer = crmClient.GetCustomer(customerId).Result;
    if (customer == null)
    {
        return Results.Ok(new { Message = "Customer not found", Invoices = Array.Empty<Invoice>() });
    }

    var invoices = GetUnpaidInvoices(customerId);

    logger.Info($"Found {invoices.Count()} unpaid invoices for {customer.Email}");

    var result = new List<object>();
    foreach (var invoice in invoices)
    {
        var orders = GetOrdersFromBillingService(customerId);
        result.Add(new
        {
            invoice.Id,
            invoice.CustomerId,
            invoice.Amount,
            invoice.Status,
            DueDays = (DateTime.Now - invoice.DueDate).Days,
            RelatedOrderCount = orders.Count(o => o.InvoiceId == invoice.Id)
        });
    }

    if (invoices.Any(i => i.Amount > 10000))
    {
        var riskClient = new RiskClient();
        riskClient.ReportLargeDebt(customerId, invoices.Sum(i => i.Amount));
    }

    return Results.Ok(result);
});

app.MapPost("/invoices/{invoiceId}/pay", async (int invoiceId, PayInvoiceRequest request) =>
{
    var logger = new ConsoleAuditLogger();
    var paymentGateway = new PaymentGatewayClient("https://payments.example.com", "live_payment_key_123");
    var emailSender = new EmailSender("smtp.company.local", 25);

    logger.Info("Payment request: " + JsonSerializer.Serialize(request));

    try
    {
        var invoice = DataStore.Invoices.FirstOrDefault(i => i.Id == invoiceId);
        if (invoice == null)
        {
            return Results.BadRequest("Invoice does not exist.");
        }

        if (invoice.Status == "Paid")
        {
            return Results.Ok(new { Message = "Invoice already paid." });
        }

        var customer = new CrmClient("https://crm.internal.local", "crm-secret-key")
            .GetCustomer(invoice.CustomerId)
            .Result;

        var paymentResult = await paymentGateway.ChargeAsync(
            request.CardNumber,
            request.Cvv,
            invoice.Amount,
            request.Currency);

        invoice.Status = paymentResult.Success ? "Paid" : "FAILED";
        invoice.PaidAt = DateTime.Now;
        invoice.PaymentReference = paymentResult.Reference;

        await SaveInvoice(invoice);

        await emailSender.SendAsync(
            customer?.Email ?? request.Email,
            "Payment confirmation",
            $"Invoice {invoice.Id} was paid using card {request.CardNumber}.");

        return Results.Ok(new
        {
            invoice.Id,
            invoice.Status,
            invoice.Amount,
            PaymentReference = paymentResult.Reference
        });
    }
    catch (Exception ex)
    {
        logger.Error(ex.ToString());
        return Results.BadRequest(ex.ToString());
    }
});

app.MapPost("/orders/{orderId}/status", async (int orderId, ChangeOrderStatusRequest request) =>
{
    var logger = new ConsoleAuditLogger();
    var order = DataStore.Orders.FirstOrDefault(o => o.Id == orderId);

    if (order == null)
    {
        return Results.Ok("Order not found.");
    }

    logger.Info($"Changing order {orderId} from {order.Status} to {request.Status} by {request.ChangedBy}");

    var sql = $"update Orders set Status = '{request.Status}', ChangedBy = '{request.ChangedBy}' where Id = {orderId}";
    new FakeSqlDatabase().Execute(sql);

    order.Status = request.Status;
    order.ChangedBy = request.ChangedBy;
    order.ChangedAt = DateTime.Now;

    await new NotificationClient().PublishOrderChanged(order);

    return Results.Ok(order);
});

app.MapGet("/reports/customer/{customerId}", (int customerId, string status) =>
{
    var logger = new ConsoleAuditLogger();
    logger.Info($"Generating report for customer {customerId} and status {status}");

    var sql = "select * from Invoices where CustomerId = " + customerId + " and Status = '" + status + "'";
    var rows = new FakeSqlDatabase().QueryInvoices(sql);

    var total = rows.Sum(i => i.Amount);
    var newest = rows.OrderByDescending(i => i.CreatedAt).FirstOrDefault();

    return Results.Ok(new
    {
        CustomerId = customerId,
        Status = status,
        Count = rows.Count(),
        Total = total,
        NewestInvoiceId = newest?.Id
    });
});

app.Run();

static IEnumerable<Invoice> GetUnpaidInvoices(int customerId)
{
    Console.WriteLine($"Loading unpaid invoices for customer {customerId} from database...");
    return DataStore.Invoices.Where(i => i.CustomerId == customerId && i.Status == "Unpaid");
}

static IEnumerable<Order> GetOrdersFromBillingService(int customerId)
{
    var billingClient = new BillingClient("https://billing.internal.local", TimeSpan.FromSeconds(2));
    return billingClient.GetOrders(customerId);
}

static async Task SaveInvoice(Invoice invoice)
{
    await Task.Delay(25);
    Console.WriteLine($"Invoice {invoice.Id} saved.");
}

static void SeedData()
{
    DataStore.Customers.Clear();
    DataStore.Customers.AddRange(new[]
    {
        new Customer(1, "Acme Ltd.", "billing@acme.example"),
        new Customer(2, "Northwind s.r.o.", "payments@northwind.example")
    });

    DataStore.Invoices.Clear();
    DataStore.Invoices.AddRange(new[]
    {
        new Invoice(101, 1, 1250, "Unpaid", DateTime.Now.AddDays(-12), DateTime.Now.AddMonths(-1), null, null),
        new Invoice(102, 1, 12990, "Unpaid", DateTime.Now.AddDays(-30), DateTime.Now.AddMonths(-2), null, null),
        new Invoice(103, 1, 300, "Paid", DateTime.Now.AddDays(-5), DateTime.Now.AddMonths(-1), DateTime.Now.AddDays(-2), "pay_old_103"),
        new Invoice(201, 2, 870, "Unpaid", DateTime.Now.AddDays(5), DateTime.Now, null, null)
    });

    DataStore.Orders.Clear();
    DataStore.Orders.AddRange(new[]
    {
        new Order(501, 1, 101, "New", null, null),
        new Order(502, 1, 102, "Processing", null, null),
        new Order(503, 2, 201, "New", null, null)
    });
}

static class DataStore
{
    public static readonly List<Customer> Customers = new();
    public static readonly List<Invoice> Invoices = new();
    public static readonly List<Order> Orders = new();
}

record Customer(int Id, string Name, string Email);

record Invoice(
    int Id,
    int CustomerId,
    decimal Amount,
    string Status,
    DateTime DueDate,
    DateTime CreatedAt,
    DateTime? PaidAt,
    string? PaymentReference)
{
    public string Status { get; set; } = Status;
    public DateTime? PaidAt { get; set; } = PaidAt;
    public string? PaymentReference { get; set; } = PaymentReference;
}

record Order(int Id, int CustomerId, int InvoiceId, string Status, string? ChangedBy, DateTime? ChangedAt)
{
    public string Status { get; set; } = Status;
    public string? ChangedBy { get; set; } = ChangedBy;
    public DateTime? ChangedAt { get; set; } = ChangedAt;
}

record PayInvoiceRequest(string CardNumber, string Cvv, string Currency, string Email);

record ChangeOrderStatusRequest(string Status, string ChangedBy);

record PaymentResult(bool Success, string Reference);

class CrmClient(string baseUrl, string apiKey)
{
    public async Task<Customer?> GetCustomer(int customerId)
    {
        Console.WriteLine($"Calling CRM at {baseUrl} with key {apiKey}...");
        await Task.Delay(50);
        return DataStore.Customers.FirstOrDefault(c => c.Id == customerId);
    }
}

class BillingClient(string baseUrl, TimeSpan timeout)
{
    public IEnumerable<Order> GetOrders(int customerId)
    {
        Console.WriteLine($"Calling Billing API at {baseUrl} with timeout {timeout}...");
        Thread.Sleep(50);

        foreach (var order in DataStore.Orders.Where(o => o.CustomerId == customerId))
        {
            yield return order;
        }
    }
}

class PaymentGatewayClient(string baseUrl, string apiKey)
{
    public async Task<PaymentResult> ChargeAsync(string cardNumber, string cvv, decimal amount, string currency)
    {
        Console.WriteLine($"Charging {amount} {currency} through {baseUrl} using key {apiKey}...");
        await Task.Delay(150);

        if (string.IsNullOrWhiteSpace(cardNumber) || string.IsNullOrWhiteSpace(cvv))
        {
            throw new InvalidOperationException("Payment gateway rejected empty card data.");
        }

        return new PaymentResult(true, "pay_" + Guid.NewGuid().ToString("N")[..12]);
    }
}

class EmailSender(string host, int port)
{
    public async Task SendAsync(string to, string subject, string body)
    {
        Console.WriteLine($"Sending e-mail through {host}:{port} to {to}: {subject} - {body}");
        await Task.Delay(20);
    }
}

class NotificationClient
{
    public async Task PublishOrderChanged(Order order)
    {
        Console.WriteLine($"Publishing order changed event for {order.Id}...");
        await Task.Delay(20);
    }
}

class RiskClient
{
    public void ReportLargeDebt(int customerId, decimal amount)
    {
        Console.WriteLine($"Reporting large debt for customer {customerId}: {amount}");
    }
}

class FakeSqlDatabase
{
    public void Execute(string sql)
    {
        Console.WriteLine("Executing SQL: " + sql);
    }

    public IEnumerable<Invoice> QueryInvoices(string sql)
    {
        Console.WriteLine("Querying SQL: " + sql);
        return DataStore.Invoices;
    }
}

class ConsoleAuditLogger
{
    public void Info(string message) => Console.WriteLine("[INFO] " + message);

    public void Error(string message) => Console.WriteLine("[ERROR] " + message);
}
