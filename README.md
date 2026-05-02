# Candidate Review Exercise

Toto je male ASP.NET Core 10 Minimal API pro code review uchazece na pozici .NET vyvojare.

Aplikace je zamerne zjednodusena: misto skutecne databaze a skutecnych microservices pouziva staticka data a fake klienty. Kod je ale napsany tak, aby reprezentoval bezne problemy, ktere se v produkcnim kodu objevují.

## Zadani pro kandidata

Aplikace je spustitelna a na prvni pohled funguje.

Provedte code review souboru `Program.cs`. Zamerte se zejmena na:

- spravnost a konzistenci chovani,
- bezpecnost,
- vykon,
- praci s async kodem,
- navrh zavislosti a testovatelnost,
- praci s daty a kolekcemi,
- provozni rizika.

U nalezenych problemu popiste:

1. v cem je problem,
2. jaky muze mit dopad,
3. jak byste jej opravil/a.

Nemusite opravit cely projekt. Pokud mate cas na implementaci, vyberte 2 az 3 nejzavaznejsi problemy a navrhnete nebo provedte jejich opravu.

## Spusteni

```powershell
dotnet run
```

Priklady volani:

```powershell
curl http://localhost:5000/customers/1/invoices?userEmail=test@example.com
curl "http://localhost:5000/reports/customer/1?status=Unpaid"
curl -X POST http://localhost:5000/invoices/101/pay -H "Content-Type: application/json" -d "{\"cardNumber\":\"4111111111111111\",\"cvv\":\"123\",\"currency\":\"CZK\",\"email\":\"buyer@example.com\"}"
curl -X POST http://localhost:5000/orders/501/status -H "Content-Type: application/json" -d "{\"status\":\"Shipped\",\"changedBy\":\"admin@example.com\"}"
```

