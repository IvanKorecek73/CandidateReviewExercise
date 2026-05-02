# Interni hodnotici klic

Tento soubor neni urceny pro kandidata. Slouzi jako voditko pro hodnotitele.

## Ocekavane nalezy

1. Opakovana enumerace `IEnumerable`
   - `GetUnpaidInvoices(customerId)` vraci lazy sekvenci.
   - Nad `invoices` se vola `Count()`, `foreach`, `Any()` a `Sum()`.
   - V realne DB nebo externi sluzbe by to mohlo znamenat vice dotazu/volani, nekonzistentni data a horsi vykon.
   - Oprava: materializovat jednou (`ToList()` / `ToListAsync()`), pripadne zmenit kontrakt repository/sluzby.

2. Opakovane volani fake microservice v cyklu
   - `GetOrdersFromBillingService(customerId)` se vola pro kazdou fakturu.
   - `BillingClient.GetOrders` je navic lazy pres `yield return`.
   - Oprava: nacist objednavky jednou mimo cyklus, materializovat a pouzit lookup.

3. Vytvareni zavislosti pres `new` uvnitr endpointu/metod
   - `CrmClient`, `PaymentGatewayClient`, `EmailSender`, `RiskClient`, `FakeSqlDatabase`, logger atd.
   - Horsí testovatelnost, konfigurace v kodu, nereseny lifetime, horsi observabilita.
   - Oprava: DI/IoC, konfigurace pres options, `HttpClientFactory`, abstrakce pro klienty.

4. Hardcoded konfigurace a tajemstvi
   - API klice, URL a SMTP host jsou primo v kodu.
   - Oprava: konfigurace, secret store, user-secrets/key vault podle prostredi.

5. Blokovani async kodu pres `.Result`
   - `crmClient.GetCustomer(...).Result`.
   - Riziko thread starvation/deadlocku a spatna skalovatelnost.
   - Oprava: `await`.

6. Chybejici validace vstupu
   - `PayInvoiceRequest` nekontroluje kartu, CVV, menu, e-mail.
   - `ChangeOrderStatusRequest` nekontroluje povolene stavy.
   - Oprava: validacni vrstva, DTO anotace/FluentValidation, kontrola business pravidel.

7. Slaba autorizace a overeni vlastnictvi dat
   - `customerId`, `invoiceId` a `userEmail` jsou brany z requestu bez overeni identity.
   - Oprava: autentizace, autorizacni policy, kontrola ownershipu na serveru.

8. SQL injection styl kodu
   - Skladani SQL stringu v `/orders/{orderId}/status` a `/reports/customer/{customerId}`.
   - I kdyz je DB fake, kandidat by mel rozpoznat riziko.
   - Oprava: parametrizovane dotazy, EF/LINQ, stored procedures s parametry.

9. Vraceni internich exception detailu klientovi
   - `return Results.BadRequest(ex.ToString())`.
   - Oprava: centralizovane exception handling middleware, obecna chyba pro klienta, detail do logu.

10. Nevhodne HTTP status kody
    - Nenalezeny customer/order/invoice vraci `200 OK` nebo `400 BadRequest`.
    - Oprava: `404 NotFound`, `409 Conflict`, `400 ValidationProblem`, pripadne `403`.

11. Logovani citlivych dat
    - Loguje se cely platebni request vcetne karty a CVV.
    - E-mail obsahuje cislo karty.
    - Oprava: maskovani, nelogovat tajemstvi, PCI-DSS principy.

12. Race condition / dvojita platba
    - Stav faktury se kontroluje a meni bez zamku, transakce nebo concurrency tokenu.
    - Paralelni requesty mohou zaplatit stejnou fakturu vicekrat.
    - Oprava: transakce, optimistic concurrency, idempotency key, stavove prechody v DB.

13. Chybejici transakcni hranice
    - Platba, zmena faktury, ulozeni a poslani e-mailu jsou bez jasne konzistence.
    - Oprava: transakcni outbox, idempotentni integrace, lepsi hranice procesu.

14. Magicke stringy a nekonzistentni stavy
    - `"Unpaid"`, `"Paid"`, `"FAILED"`, libovolny `request.Status`.
    - Oprava: enum/value object, centralizovana pravidla prechodu stavu.

15. `DateTime.Now`
    - Pouziva se pro business casy a vypocty.
    - Oprava: `TimeProvider`, `DateTimeOffset`, UTC.

16. Fake SQL query metoda ignoruje filtr
    - `QueryInvoices` vraci vsechny faktury bez ohledu na SQL.
    - Endpoint reportu tedy vraci spatny vysledek, i kdyz vypada funkcne.

17. Staticka mutable data
    - `static List<T>` neni thread-safe.
    - Oprava: realna DB, concurrency control, thread-safe kolekce pouze pokud davaji smysl.

18. Business logika v endpointu
    - Endpointy resi validaci, integrace, stavove zmeny, e-maily i reporting.
    - Oprava: aplikacni sluzby/use-cases, tenke endpointy.

19. Ignorovani cancellation tokenu
    - Endpointy a fake klienti nepropaguji `CancellationToken`.
    - Oprava: prijimat a predavat token do async operaci.

20. `Thread.Sleep` v request flow
    - `BillingClient.GetOrders` blokuje vlakno.
    - Oprava: async API a `Task.Delay`/realne neblokujici I/O.

## Silny kandidat by mel navic zminit

- idempotenci plateb,
- audit a korelacni ID,
- structured logging misto interpolovanych stringu,
- oddeleni DTO od domenovych modelu,
- centralizovanou konfiguraci a health checks pro externi sluzby,
- testy pro stavove prechody, autorizaci a konkurencni platby.

