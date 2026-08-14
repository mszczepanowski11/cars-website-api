# Dedykowany silnik wyszukiwania (Meilisearch / OpenSearch) — ocena

Status: **kod zaimplementowany i zweryfikowany end-to-end na żywo (lokalny Meilisearch 1.53.1 +
realny MySQL, ta sama sesja), instancja produkcyjna NIE jest jeszcze provisionowana.**
Zaimplementowano `IAdvertSearchIndexService`/`MeilisearchAdvertIndexService` (`CarsWebsite.Services`),
hook synchronizujący w `AdvertService` przy tworzeniu/edycji/usuwaniu/sprzedaniu/publikacji
ogłoszenia, zamianę zapytania MATCH...AGAINST na Meilisearch w `SearchCarAdvertsAsync` z fail-open
fallbackiem do MySQL FULLTEXT, endpoint `POST /api/Admin/search-index/reindex` do pełnego reindeksu
na żądanie, **oraz (CTO audit Etap 4, druga faza) routing filtrów atrybutów EAV
(`SearchCarAdvertDto.AttributeFilters`) przez Meilisearch zamiast per-filtra zapytań EF `EXISTS` do
MySQL** — `MeilisearchAttributeFilterBuilder` tłumaczy `AttributeFilterDto` na wyrażenie filtra
Meilisearch (`attr_{AttributeDefinitionId} = ...`/`>= .. AND <= ..`/`CONTAINS ...`), a
`MeilisearchAdvertIndexService` indeksuje każdą wartość atrybutu ogłoszenia jako osobne pole
dokumentu i rejestruje ją jako filterable dynamicznie (nowy atrybut dodany przez admina staje się
filtrowalny po najbliższym reindeksie, bez migracji schematu indeksu). Konfiguracja:
`Meilisearch:Host`/`Meilisearch:ApiKey` w appsettings lub `MEILISEARCH_HOST`/`MEILISEARCH_API_KEY`
jako zmienne środowiskowe — puste/nieustawione = usługa wyłączona, wyszukiwanie działa dokładnie
jak przed tą zmianą (zero narzutu, zero zmiany zachowania). Frontend nie wymaga żadnych zmian —
ten sam request/response kontrakt na `/api/Advert/search`.

**Weryfikacja end-to-end (ta sesja):** pobrany bezpośrednio binarny release Meilisearch 1.53.1
(sandbox miał dostęp do `github.com` przez skonfigurowane proxy, mimo że Docker/inne rejestry były
niedostępne), uruchomiony lokalnie, wskazany przez `MEILISEARCH_HOST`. Utworzone testowe definicje
atrybutów (bool/number/text z wieloma wartościami) i wartości na dwóch realnych ogłoszeniach w
lokalnym MySQL, pełny reindeks przez `MeilisearchAdvertIndexService.ReindexAllAsync`, a następnie
zapytania przez faktyczny `POST /api/Advert/search` potwierdziły: filtr bool (`=`), filtr liczbowy
(zakres `>=`/`<=`), filtr tekstowy wielowartościowy (`CONTAINS`, odpowiednik `ValueText.Contains`
z wersji SQL — wymaga eksperymentalnej flagi Meilisearch `containsFilter`, włączanej idempotentnie
w `ReindexAllAsync`), połączenie TextSearch+AttributeFilters w jednym zapytaniu, oraz identyczne
wyniki przy Meilisearch wyłączonym (fallback do EF `EXISTS`) — wszystkie dały poprawne, oczekiwane
rozróżnienie między ogłoszeniami. Testowe dane usunięte po weryfikacji.

**Do zrobienia, żeby to zaczęło faktycznie działać na produkcji:** (1) provisioning instancji
Meilisearch (Railway addon lub inny hosting — wymaga akcji właściciela), (2) ustawienie
`Meilisearch:Host`/`ApiKey`, (3) jednorazowe wywołanie `POST /api/Admin/search-index/reindex` do
populacji indeksu (rejestruje też filterable attributes i włącza `containsFilter`), (4) obserwacja
pierwszych zapytań na żywo (logi `[Meilisearch]` w razie problemów — kod jest fail-open, więc awaria
tu nigdy nie blokuje wyszukiwania, tylko cicho wraca do dotychczasowej ścieżki MySQL).

## Stan obecny (opis historyczny — sprzed drugiej fazy powyżej, zostawiony dla kontekstu)

Wyszukiwanie tekstowe działa na MySQL FULLTEXT (`MATCH...AGAINST` w trybie BOOLEAN, indeks
`FT_Adverts_TitleDescription` na `Title`+`Description`, patrz `AdvertService.cs` — naprawione w
tej samej sesji audytu, wcześniej było `LIKE '%term%'` bez użycia indeksu). Filtry atrybutów EAV
teraz również przechodzą przez Meilisearch, gdy jest włączony (patrz wyżej) — pozostałe ~14 filtrów
faset (marka, model, cena, przebieg, rok, paliwo, skrzynia, napęd, kolor, moc, kategoria, podtyp...)
nadal idą przez standardowe zapytania EF Core `.Where()` na tej samej bazie MySQL; przeniesienie
ich też do Meilisearch pozostaje przyszłą pracą, jeśli/gdy pojawi się do tego konkretny sygnał.

To rozwiązanie jest **wystarczające przy obecnej skali** (start nowego marketplace, zapewne
niskie tysiące ogłoszeń). Ograniczenia FULLTEXT w MySQL, które staną się realnym problemem
dopiero przy większym ruchu/wolumenie:

- **Brak tolerancji literówek** — "Volkswagen" wpisane jako "Volkswagn" nie znajdzie nic; obecny
  `+word*` (prefix match) łagodzi tylko brakujące końcówki słowa, nie literówki w środku.
- **Brak rankingu trafności poza dopasowaniem boolowskim** — MySQL FULLTEXT ma wbudowany
  relevance score, ale nie jest tu używany do sortowania (wyniki sortowane są dalej po
  cenie/dacie/wyróżnieniu, nie po trafności tekstowej) — do rozważenia jako szybsza, tańsza
  łatka niezależnie od decyzji o Meilisearch/OpenSearch.
- **Brak agregacji faset "na żywo"** (np. "Ile wyników zostanie, jeśli dodam filtr 'diesel'?" bez
  wykonania osobnego zapytania) — dziś każda zmiana filtra to nowe zapytanie do API.
- **Skalowanie zapisów vs. odczytów** — FULLTEXT index na dużej tabeli spowalnia INSERT/UPDATE
  proporcjonalnie do wolumenu ogłoszeń; przy fasetowanym wyszukiwaniu (wiele `.Where()` na
  nieindeksowanych lub słabo selektywnych kolumnach) czas zapytania rośnie nieliniowo z liczbą
  jednocześnie aktywnych filtrów.
- **Brak synonimów/tolerancji na warianty pisowni** (np. "BMW"/"Bawaria", "combi"/"kombi") bez
  ręcznego mapowania w kodzie aplikacji.

Żaden z powyższych punktów nie jest dziś obserwowalnym problemem — to są granice, na które
projekt natrafi przy realnym wzroście ruchu/wolumenu ogłoszeń, nie przy obecnej skali.

## Opcje

### Meilisearch
- Prostszy w konfiguracji i utrzymaniu (jeden proces, jeden plik konfiguracyjny), typowany pod
  wyszukiwanie faset + trafność tekstową out-of-the-box (literówki, synonimy, ranking, faceting
  z licznikami — dokładnie te braki wypisane wyżej).
- Mniejszy narzut operacyjny niż OpenSearch — dobry pierwszy wybór dla zespołu bez
  doświadczenia w utrzymaniu klastra wyszukiwania.
- Dostępny jako gotowy addon/template na Railway (ta sama platforma co reszta infrastruktury
  CARIZO) — analogicznie do tego, jak Redis (zadanie #40 z tego samego audytu) czeka na
  provisioning przez właściciela.
- Słabszy przy bardzo dużym wolumenie (dziesiątki milionów dokumentów) niż OpenSearch/Elastic,
  ale to poza realistycznym horyzontem tego marketplace na najbliższe lata.

### OpenSearch
- Znacznie potężniejszy (pełny silnik klasy Elasticsearch — agregacje, geo-search, ważona
  trafność wielopolowa), ale proporcjonalnie cięższy operacyjnie: wymaga JVM, więcej pamięci,
  osobnego tuningu indeksów/mapowań, realnie klastra (nie pojedynczego procesu) dla produkcyjnej
  odporności na awarie.
- Uzasadniony dopiero, gdy wymagania przerosną to, co Meilisearch oferuje z pudełka (np. bardzo
  złożone zapytania geograficzne, miliony dokumentów, potrzeba pełnej kontroli nad relevance
  scoring).

### Rekomendacja
**Meilisearch, jeśli/gdy decyzja zapadnie** — rozmiar i profil ruchu tego projektu (marketplace
motoryzacyjny, ogłoszenia liczone w tysiącach/dziesiątkach tysięcy, nie miliony) pasuje dokładnie
w to, do czego Meilisearch jest projektowany, przy dużo niższym koszcie utrzymania niż
OpenSearch. **Nie ma dziś pilnej potrzeby wdrożenia** — obecne MySQL FULLTEXT wystarcza; to
rozwiązanie warto wdrożyć, gdy pojawi się konkretny sygnał (skargi na jakość wyszukiwania,
mierzalne spowolnienie przy realnym wolumenie ogłoszeń), a nie prewencyjnie.

## Szkic planu wdrożenia (jeśli zapadnie decyzja "tak")

Kroki 2-5 poniżej są zrealizowane (patrz Status na górze tego dokumentu) - krok 3 obecnie tylko dla
TextSearch + filtrów atrybutów EAV, nie jeszcze dla pozostałych ~14 filtrów faset. Krok 1
(provisioning) pozostaje jedyną rzeczą, której nie da się zrobić z poziomu tej sesji.

1. Provisioning instancji Meilisearch (Railway addon, analogicznie do Redis z zadania #40 tego
   audytu — wymaga akcji właściciela, nie da się zrobić z tej sesji).
2. Indeksowanie: job synchronizujący `adverts`/`caradverts` → dokumenty Meilisearch przy
   tworzeniu/edycji/usunięciu ogłoszenia (najprościej: hook w `AdvertService` po `SaveChangesAsync`
   dla operacji tworzenia/edycji/usuwania ogłoszenia, plus pełny reindex startowy dla istniejących
   danych).
3. `AdvertService.SearchCarAdvertsAsync` (i analogiczne metody dla innych kategorii): zamiast
   `MATCH...AGAINST` + EF `.Where()`, zapytanie do Meilisearch z filtrami faset przekazanymi jako
   jego natywny filter-query language; wynikowa lista ID nadal trafia przez EF do pobrania pełnych
   rekordów (albo, dla maksymalnej wydajności, samo Meilisearch trzyma wystarczająco danych do
   renderowania kart wyników bez dodatkowego zapytania do MySQL).
4. Fallback: jeśli Meilisearch jest niedostępny (awaria/restart), zapytanie powinno cicho spadać z
   powrotem na dzisiejszą ścieżkę MySQL FULLTEXT, żeby wyszukiwanie nigdy nie było twardo
   zależne od dodatkowej usługi — ten sam wzorzec "fail open", jaki stosuje już ten kod dla
   zewnętrznych usług (np. walidacja CEPiK w `add-advert.vue`).
5. Weryfikacja: porównanie wyników wyszukiwania przed/po dla identycznych zapytań na realnych
   danych, upewnienie się że filtry faset dają identyczne (lub lepsze) wyniki.

Szacowany nakład: to nowa usługa + zmiana ścieżki zapisu i odczytu ogłoszeń, realistycznie osobny,
wieloetapowy projekt (podobny rozmiarem do migracji EAV z wcześniejszej fazy tej sesji), nie
jednorazowa poprawka.
