# Dedykowany silnik wyszukiwania (Meilisearch / OpenSearch) — ocena

Status: **do rozważenia, nie zaimplementowane.** Ten dokument to ocena, o którą prosił audyt
("rozważ dedykowany silnik wyszukiwania") — decyzja o wdrożeniu należy do właściciela produktu,
ponieważ wiąże się z nowym komponentem infrastruktury (koszt hostingu, dodatkowa usługa do
utrzymania) i nie jest tak jednoznacznie bezpieczna jak reszta poprawek z tej sesji.

## Stan obecny

Wyszukiwanie tekstowe działa na MySQL FULLTEXT (`MATCH...AGAINST` w trybie BOOLEAN, indeks
`FT_Adverts_TitleDescription` na `Title`+`Description`, patrz `AdvertService.cs` — naprawione w
tej samej sesji audytu, wcześniej było `LIKE '%term%'` bez użycia indeksu). Wyniki tekstowe są
łączone z ~15 filtrami faset (marka, model, cena, przebieg, rok, paliwo, skrzynia, napęd, kolor,
moc, kategoria, podtyp, atrybuty EAV...) przez standardowe zapytania EF Core `.Where()` na tej
samej bazie MySQL.

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
