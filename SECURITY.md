# Sekrety i konfiguracja

## Zasada

**Żaden sekret nie należy do repozytorium.** Klucze, hasła i tokeny czyta się ze zmiennych
środowiskowych (produkcja) albo z `dotnet user-secrets` (praca lokalna). Pliki `appsettings.*`
zawierają wyłącznie ustawienia jawne: poziomy logowania, adresy dozwolonych źródeł, wystawcę
i odbiorcę tokenu, czas jego ważności.

---

## Co się wydarzyło i co zostało naprawione

Plik `appsettings.Development.json` przez pewien czas zawierał prawdziwy, 64-znakowy klucz
podpisujący tokeny (`Jwt:Key`) oraz hasło do lokalnej bazy. Oba trafiły do historii repozytorium.

Istniejące zabezpieczenie w `Program.cs` odrzucało *znany placeholder*, ale nie odróżniało go od
prawdziwego klucza — więc przepuszczało ten z pliku bez słowa. Realne ryzyko nie polegało na tym,
że klucz „wyciekł" (repozytorium jest prywatne), tylko na tym, że **jedna brakująca zmienna
środowiskowa dzieliła serwis od podpisywania wszystkich tokenów kluczem znanym każdemu, kto ma
dostęp do kodu** — czyli od możliwości podrobienia tokenu dowolnego konta, włącznie
z administratorem. Aplikacja wystartowałaby normalnie i niczego by nie zgłosiła.

**Naprawione w kodzie:**

1. Poza środowiskiem deweloperskim `Jwt:Key` z plików konfiguracyjnych **nie jest w ogóle brany
   pod uwagę**. Brak `JWT_SECRET_KEY` zatrzymuje start aplikacji z jasnym komunikatem.
   Dzięki temu klucz z historii jest bezużyteczny niezależnie od tego, czy historia zostanie
   wyczyszczona.
2. Wartości w `appsettings.Development.json` zastąpiono placeholderami, które `Program.cs`
   odrzuca.

**Co zostało do zrobienia przez człowieka** — opisane niżej.

---

## 1. Rotacja klucza podpisującego tokeny (do zrobienia)

Klucz z historii należy uznać za spalony i wymienić. Wygenerowanie nowego:

```bash
openssl rand -base64 48
```

Nowa wartość trafia do zmiennej środowiskowej `JWT_SECRET_KEY` w panelu Railway
(Variables → New Variable). Musi mieć co najmniej 32 bajty — `Program.cs` to sprawdza.

> **Skutek uboczny, o którym trzeba wiedzieć:** zmiana klucza unieważnia wszystkie wydane
> tokeny. Wszyscy zalogowani użytkownicy zostaną wylogowani i będą musieli zalogować się
> ponownie. Warto zrobić to w godzinach najmniejszego ruchu.

## 2. Rotacja hasła do bazy (do zrobienia)

Hasło w historii dotyczyło bazy lokalnej (`Server=localhost`), więc ryzyko jest niskie —
ale jeśli to samo hasło jest używane gdziekolwiek indziej, trzeba je zmienić tam.

Produkcja czyta połączenie ze zmiennej środowiskowej i nie korzysta z tego pliku.

## 3. Czyszczenie historii repozytorium (decyzja właściciela)

Klucz nadal leży w dwóch commitach. Po wykonaniu kroku 1 jest bezużyteczny, więc czyszczenie
historii jest **porządkiem, a nie koniecznością**.

Jeśli ma zostać wykonane — wymaga przepisania historii i wymuszonego wypchnięcia, co zepsuje
wszystkie istniejące klony i otwarte gałęzie. Należy to uzgodnić z każdym, kto pracuje na
repozytorium, i zrobić w jednym oknie czasowym:

```bash
# git-filter-repo, nie filter-branch (ten drugi jest wolny i ma pułapki)
pip install git-filter-repo
git filter-repo --path appsettings.Development.json --invert-paths
# potem: przywrócić plik z placeholderami, wypchnąć --force, wszyscy klonują od nowa
```

---

## Praca lokalna — jak ustawić sekrety u siebie

Nie edytuj `appsettings.Development.json`. Użyj magazynu sekretów .NET, który trzyma je
poza katalogiem projektu:

```bash
dotnet user-secrets init
dotnet user-secrets set "Jwt:Key" "$(openssl rand -base64 48)"
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Server=localhost;Database=cars_website;User=root;Password=twoje-haslo;Port=3306;"
```

Aplikacja startuje z jasnym błędem, jeśli któregoś sekretu brakuje — to celowe.

---

## Zmienne środowiskowe wymagane na produkcji

| Zmienna | Do czego |
|---|---|
| `JWT_SECRET_KEY` | podpisywanie tokenów — **bez niej aplikacja nie wystartuje** |
| `ConnectionStrings__DefaultConnection` | połączenie z bazą |
| `IMOJE_MERCHANT_ID`, `IMOJE_API_KEY`, `IMOJE_WEBHOOK_SECRET`, `IMOJE_SERVICE_ID` | płatności |
| `SMTP_HOST`, `SMTP_USER`, `SMTP_PASS`, `SMTP_FROM` | wysyłka poczty |

`Program.cs` sprawdza komplet przy starcie i zatrzymuje aplikację, jeśli czegoś brakuje —
lepiej nie wstać w ogóle niż działać po cichu w trybie, w którym płatności albo poczta nie
zadziałają.

---

## Zgłaszanie podatności

Znalezione problemy bezpieczeństwa proszę zgłaszać bezpośrednio do właściciela repozytorium,
nie przez publiczne zgłoszenie.
