#!/usr/bin/env python3
"""
CARIZO – Pelny import z autoplac.pl v3
=======================================
KROK 1: python import_autoplac_v3.py --scan
  - Skanuje autoplac.pl i zapisuje dane do autoplac_data.json
  - Tworzy podfoldery w C:\\zdjecia komis\\CARIZO\\ dla kazdego auta
  - Np. 01_Nissan_Juke\, 02_Renault_Megane\ itd.

KROK 2: Wrzuc swoje zdjecia do odpowiednich podfolderow

KROK 3: python import_autoplac_v3.py --import
  - Usuwa stare ogloszenia
  - Tworzy nowe ogloszenia z danymi z autoplac.pl
  - Uploaduje Twoje zdjecia z podfolderow (bez znaku wodnego!)

Wymagania:
  pip install playwright requests beautifulsoup4
  playwright install chromium
"""

import sys, json, time, re, io, os, math
from pathlib import Path

try:
    import requests
except ImportError:
    sys.exit("Zainstaluj: pip install requests")

try:
    from bs4 import BeautifulSoup
except ImportError:
    sys.exit("Zainstaluj: pip install beautifulsoup4")

try:
    from playwright.sync_api import sync_playwright
except ImportError:
    sys.exit("Zainstaluj: pip install playwright && playwright install chromium")

# ================================================================
#  CONFIG
# ================================================================

AUTOPLAC_DEALER = "https://autoplac.pl/dealer/autokomisostrowiec"
CARIZO_API      = "https://cars-website-api-production.up.railway.app"
PHOTOS_ROOT     = r"C:\CARIZO"
DATA_FILE       = "autoplac_data.json"

ACCOUNT = {
    "email":    "carizokontakt@gmail.com",
    "password": "KomisCarizo2024!",
}

# ================================================================
#  HELPERS
# ================================================================

def log(msg):  print(f"    {msg}", flush=True)
def ok(msg):   print(f"  OK  {msg}", flush=True)
def warn(msg): print(f"  !!  {msg}", flush=True)
def err(msg):  print(f"  XX  {msg}", flush=True)

def api(method, path, session, **kwargs):
    url = f"{CARIZO_API.rstrip('/')}/{path.lstrip('/')}"
    kwargs.setdefault('timeout', 60)
    for attempt in range(3):
        try:
            r = session.request(method, url, **kwargs)
            try:    return r.status_code, r.json()
            except: return r.status_code, r.text
        except requests.exceptions.Timeout:
            wait = (attempt + 1) * 10
            log(f"Timeout, czekam {wait}s...")
            time.sleep(wait)
        except Exception as e:
            err(f"Request error: {e}")
            return 0, str(e)
    return 0, "Max retries"

def safe_folder_name(name, index):
    clean = re.sub(r'[<>:"/\\|?*]', '', name)
    clean = clean.strip()[:40]
    return f"{index:02d}_{clean.replace(' ', '_')}"

# ================================================================
#  SCRAPING
# ================================================================

def scrape_all(pw):
    print("\n  Otwieram Chromium...")
    browser = pw.chromium.launch(headless=False)
    ctx = browser.new_context(
        user_agent=(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
            "AppleWebKit/537.36 (KHTML, like Gecko) "
            "Chrome/124.0.0.0 Safari/537.36"
        ),
        locale="pl-PL",
        viewport={"width": 1280, "height": 900},
    )
    page = ctx.new_page()

    listing_urls = []
    current = AUTOPLAC_DEALER
    page_num = 1

    while current:
        print(f"  Strona {page_num}: {current}")
        page.goto(current, wait_until="domcontentloaded", timeout=30_000)
        page.wait_for_timeout(2000)

        html = page.content()
        soup = BeautifulSoup(html, "html.parser")

        found = set()
        for a in soup.find_all("a", href=True):
            href = a["href"]
            if not href.startswith("http"):
                href = "https://autoplac.pl" + href
            if re.search(r"/oferta/|/ogloszenie/|/auto/|/samochod/|/pojazd/", href):
                found.add(href.split("?")[0])

        if not found:
            for a in soup.find_all("a", href=True):
                href = a["href"]
                if not href.startswith("http"):
                    href = "https://autoplac.pl" + href
                if "autoplac.pl" in href and re.search(r"/\d+", href) and "dealer" not in href and "strona" not in href:
                    found.add(href.split("?")[0])

        new_found = [u for u in found if u not in listing_urls]
        listing_urls.extend(new_found)
        print(f"    +{len(new_found)} ogloszen (lacznie: {len(listing_urls)})")

        next_link = (
            soup.find("a", rel="next") or
            soup.find("a", string=re.compile(r"nastep|next", re.I)) or
            soup.find("a", class_=re.compile(r"next|pagination.*next", re.I))
        )
        if next_link and next_link.get("href"):
            href = next_link["href"]
            next_url = href if href.startswith("http") else "https://autoplac.pl" + href
            if next_url != current:
                current = next_url
                page_num += 1
                continue
        break

    ok(f"Znaleziono: {len(listing_urls)} ogloszen")

    cars = []
    for i, url in enumerate(listing_urls, 1):
        print(f"\n  [{i}/{len(listing_urls)}] {url}")
        try:
            page.goto(url, wait_until="domcontentloaded", timeout=30_000)
            page.wait_for_timeout(2000)
            html = page.content()
            car = parse_listing(html, url)
            if car:
                car["_index"] = i
                cars.append(car)
                ok(f"{car['title']} — {car.get('price','?')} zl | {len(car.get('equipment',[]))} cech")
            else:
                warn("Nie udalo sie sparsowac")
        except Exception as e:
            err(f"Blad: {e}")
        time.sleep(1)

    browser.close()
    return cars


def _num(text, pattern, scale=1, is_float=False):
    """Wyciaga liczbe z tekstu na podstawie wzorca."""
    m = re.search(pattern, text, re.I)
    if not m: return None
    raw = re.sub(r"\s", "", m.group(1)).replace(",", ".")
    try:
        return round(float(raw) * scale, 1) if is_float else int(float(raw) * scale)
    except:
        return None

def _parse_date(text, pattern):
    """Wyciaga date i zwraca jako YYYY-MM-DDT00:00:00Z lub None."""
    m = re.search(pattern, text, re.I)
    if not m: return None
    raw = m.group(1)
    parts = re.split(r"[./-]", raw)
    if len(parts) == 3:
        d, mo, y = parts
        if len(y) == 2:
            y = "20" + y if int(y) < 50 else "19" + y
        try:
            return f"{y}-{mo.zfill(2)}-{d.zfill(2)}T00:00:00Z"
        except:
            pass
    return None


def parse_listing(html, url):
    soup = BeautifulSoup(html, "html.parser")
    car = {"source_url": url}
    text = soup.get_text(" ")
    tl = text.lower()

    # ── Tytul ──
    for sel in ["h1", ".offer-title", ".title", '[class*="title"]', "h2"]:
        el = soup.select_one(sel)
        if el and len(el.get_text(strip=True)) > 3:
            car["title"] = el.get_text(strip=True)[:200]
            break
    if "title" not in car:
        car["title"] = "Ogloszenie"

    parts = car["title"].split()
    car["brand_raw"] = parts[0] if parts else ""
    car["model_raw"] = parts[1] if len(parts) > 1 else ""

    # ── Cena ──
    m = re.search(r"([\d\s]{4,10})\s*(?:zl|PLN|zł)", text)
    car["price"] = int(re.sub(r"\s", "", m.group(1))) if m else 0

    # ── Rok ──
    m = re.search(r"\b(19[89]\d|20[012]\d)\b", text)
    car["year"] = int(m.group(1)) if m else 2010

    # ── Przebieg ──
    m = re.search(r"([\d\s]{3,7})\s*km", text, re.I)
    car["mileage"] = int(re.sub(r"\s", "", m.group(1))) if m else 0

    # ── Moc ──
    car["power_hp"] = _num(text, r"(\d+)\s*KM")
    car["power_kw"] = _num(text, r"(\d+)\s*kW")

    # ── Pojemnosc silnika ──
    m = re.search(r"(\d[\d\s]{2,4})\s*(?:cm3|cm³|ccm)", text, re.I)
    if m:
        v = int(re.sub(r"\s", "", m.group(1)))
        car["engine_cc"] = v if 50 < v < 10000 else None
    else:
        m = re.search(r"\b(\d)\.(\d)\b", car["title"])
        car["engine_cc"] = int(m.group(1)) * 1000 + int(m.group(2)) * 100 if m else None

    # ── Moment obrotowy (Nm) ──
    car["torque"] = _num(text, r"moment\s+obrotow\w*[:\s]+([\d\s,]+)\s*Nm")
    if not car["torque"]:
        car["torque"] = _num(text, r"([\d]+)\s*Nm")

    # ── Przyspieszenie 0-100 ──
    car["acceleration"] = _num(text,
        r"(?:przyspieszen\w*\s*0[-–]100|0[-–]100\s*km/h)[:\s]+([\d,\.]+)\s*s",
        is_float=True)

    # ── Spalanie ──
    car["fuel_city"]     = _num(text,
        r"(?:zuzycie\s+paliwa\s+w\s+miesci\w*|miasto)[:\s]+([\d,\.]+)\s*l/100",
        is_float=True)
    car["fuel_highway"]  = _num(text,
        r"(?:zuzycie\s+paliwa\s+poza\s+miastem|poza\s+miastem|trasa)[:\s]+([\d,\.]+)\s*l/100",
        is_float=True)
    car["fuel_combined"] = _num(text,
        r"(?:zuzycie\s+(?:paliwa\s+)?mieszane|srednie|combined)[:\s]+([\d,\.]+)\s*l/100",
        is_float=True)

    # ── Emisja CO2 ──
    car["co2"] = _num(text, r"(?:emisja\s+CO2|CO2)[:\s]+([\d]+)\s*g/km")

    # ── Norma Euro ──
    car["euro_norm"] = None
    m = re.search(r"Euro\s*(\d)", text, re.I)
    if m: car["euro_norm"] = f"Euro {m.group(1)}"

    # ── Masa wlasna ──
    car["curb_weight"] = _num(text, r"(?:masa\s+wlasna|masa\s+własna|waga)[:\s]+([\d\s]+)\s*kg")

    # ── Paliwo ──
    car["fuel"] = None
    for kw in ["benzyna", "diesel", "hybryda", "elektryczny", "lpg", "cng"]:
        if kw in tl: car["fuel"] = kw; break

    # ── Skrzynia ──
    car["gearbox"] = None
    for kw in ["automatyczna", "automat", "manualna", "manual"]:
        if kw in tl: car["gearbox"] = kw; break

    # ── Nadwozie ──
    car["body"] = None
    for kw in ["suv", "crossover", "hatchback", "sedan", "kombi", "coupe", "kabriolet", "van", "pickup", "minivan"]:
        if kw in tl: car["body"] = kw; break

    # ── Naped ──
    car["drive"] = None
    for kw in ["4x4", "awd", "4wd", "quattro", "xdrive", "4motion", "syncro", "permanentny"]:
        if kw in tl: car["drive"] = kw; break

    # ── Kolor ──
    car["color"] = None
    for c in ["czarny","bialy","srebrny","szary","czerwony","niebieski","zielony",
              "zolty","brazowy","bezowy","granatowy","bordowy","zloty","pomaranczowy",
              "biały","żółty","brązowy","beżowy","złoty","pomarańczowy"]:
        if c in tl: car["color"] = c; break

    # ── Drzwi / miejsca ──
    m = re.search(r"(\d)\s*drzwi", text, re.I)
    car["doors"] = int(m.group(1)) if m else None
    m = re.search(r"(\d+)\s*miejsc", text, re.I)
    car["seats"] = int(m.group(1)) if m and int(m.group(1)) <= 9 else None

    # ── Data pierwszej rejestracji ──
    car["first_reg"] = (
        _parse_date(text, r"(?:pierwsz\w+\s+rejestr\w*|data\s+rejestr\w*)[:\s]+(\d{1,2}[./-]\d{1,2}[./-]\d{2,4})")
        or f"{car['year']}-01-01T00:00:00Z"
    )

    # ── Nastepny przeglad ──
    car["next_inspection"] = _parse_date(text,
        r"(?:nastepn\w+\s+przeglad\w*|nastepny\s+przeglad|badanie\s+techniczne)[:\s]+(\d{1,2}[./-]\d{1,2}[./-]\d{2,4})")

    # ── VIN ──
    m = re.search(r"\b([A-HJ-NPR-Z0-9]{17})\b", text)
    car["vin"] = m.group(1) if m else None

    # ── Kraj rejestracji / import ──
    car["reg_country"] = "Polska"
    car["is_imported"] = bool(re.search(r"importowan|sprowadzon", tl))
    car["import_country"] = None
    for country in ["niemcy", "francja", "holandia", "belgia", "wlochy", "italia", "szwajcaria",
                    "austria", "szwecja", "dania", "hiszpania", "usa", "japonia"]:
        if country in tl:
            COUNTRY_MAP = {
                "niemcy": "Niemcy", "francja": "Francja", "holandia": "Holandia",
                "belgia": "Belgia", "wlochy": "Wlochy", "italia": "Wlochy",
                "szwajcaria": "Szwajcaria", "austria": "Austria", "szwecja": "Szwecja",
                "dania": "Dania", "hiszpania": "Hiszpania", "usa": "USA", "japonia": "Japonia",
            }
            car["import_country"] = COUNTRY_MAP.get(country, country.capitalize())
            car["is_imported"] = True
            break

    # ── Historia serwisowa ──
    car["has_service_book"] = bool(re.search(
        r"ksi[aą]żka\s*serwis|serwisow\w+\s*ksi|ksiazka\s*serwis", tl))
    car["has_full_service_history"] = bool(re.search(
        r"pełna\s*historia|pelna\s*historia|full\s*service\s*history", tl))

    # ── Uszkodzenia / gwarancja ──
    car["has_damage"] = bool(re.search(r"uszkodzon\w|po\s+kolizji|powypadkow\w", tl))
    car["damage_desc"] = None
    if car["has_damage"]:
        m = re.search(r"(uszkodzon\w[^.]{0,150})", text, re.I)
        if m: car["damage_desc"] = m.group(1)[:200]

    car["has_warranty"] = bool(re.search(r"gwarancj", tl))
    car["warranty_until"] = _parse_date(text,
        r"gwarancja\s+do[:\s]+(\d{1,2}[./-]\d{1,2}[./-]\d{2,4})")

    # ── Liczba wlascicieli ──
    m = re.search(r"(\d+)\s*w[lł]a[sś]ciciel", text, re.I)
    car["owners"] = int(m.group(1)) if m else None

    # ── Wyposazenie — szukamy agresywniej ──
    equipment = []
    EQUIP_SELECTORS = [
        "ul.features li", "ul.equipment li", "ul.extras li",
        "div.features li", "div.equipment li", "div.extras li",
        "[class*='feature'] li", "[class*='equipment'] li",
        "[class*='wyposa'] li", "[class*='wyposaz'] li",
        ".offer-features li", "div[class*='detail'] ul li",
        "ul[class*='list'] li", ".parameters li", ".specs li",
    ]
    for sel in EQUIP_SELECTORS:
        items = soup.select(sel)
        for item in items:
            t = item.get_text(strip=True)
            if t and 2 < len(t) < 100:
                equipment.append(t)
        if len(equipment) >= 3:
            break

    # Fallback: divy/spany z klasami wskazujacymi na wyposazenie
    if len(equipment) < 3:
        for el in soup.find_all(["span", "div", "p"],
                                class_=re.compile(r"feature|equipment|wyposaz|wyposa|param", re.I)):
            t = el.get_text(strip=True)
            if 3 < len(t) < 80 and t not in equipment:
                equipment.append(t)

    car["equipment"] = list(dict.fromkeys(equipment))[:60]

    # ── Opis ──
    car["description"] = ""
    for sel in [".description", '[class*="desc"]', ".offer-description", "article", ".content", "main"]:
        el = soup.select_one(sel)
        if el and len(el.get_text(strip=True)) > 50:
            car["description"] = el.get_text("\n", strip=True)[:4000]
            break

    # ── Lokalizacja ──
    car["city"]   = "Ostrowiec Swietokrzyski"
    car["region"] = "swietokrzyskie"

    return car

# ================================================================
#  TAKSONOMIA
# ================================================================

def load_ref_data(session):
    print("\n  Pobieranie slownikow...")
    _, bd  = api("GET", "/api/Taxonomy/brands", session)
    _, fd  = api("GET", "/api/Taxonomy/fueltypes", session)
    _, gd  = api("GET", "/api/Taxonomy/gearboxes", session)
    _, bod = api("GET", "/api/Taxonomy/bodytypes", session)
    _, drd = api("GET", "/api/Taxonomy/drivetypes", session)
    _, cod = api("GET", "/api/Taxonomy/colors", session)
    _, frd = api("GET", "/api/Taxonomy/features", session)
    _, vcd = api("GET", "/api/Taxonomy/vehiclecategories", session)

    def to_dict(data):
        if not isinstance(data, list): return {}
        return {item["name"].lower(): item["id"] for item in data if "name" in item and "id" in item}

    brands    = to_dict(bd)
    fuels     = to_dict(fd)
    gears     = to_dict(gd)
    bodies    = to_dict(bod)
    drives    = to_dict(drd)
    colors    = to_dict(cod)
    veh_cats  = to_dict(vcd)

    features = {}
    if isinstance(frd, list):
        for item in frd:
            if "features" in item:
                for f in item["features"]:
                    if "name" in f and "id" in f:
                        features[f["name"].lower()] = f["id"]
            elif "name" in item and "id" in item:
                features[item["name"].lower()] = item["id"]

    # Znajdz ID kategorii "Osobowe" (samochody osobowe)
    car_cat_id = None
    for name, cid in veh_cats.items():
        if any(k in name for k in ["osobow", "car", "samochod", "pkw"]):
            car_cat_id = cid
            break
    if car_cat_id is None and veh_cats:
        car_cat_id = list(veh_cats.values())[0]

    ok(f"Marki:{len(brands)} Paliwa:{len(fuels)} Skrzynie:{len(gears)} Nadwozia:{len(bodies)} Napedy:{len(drives)} Kolory:{len(colors)} Cechy:{len(features)} KatPojazdu:{car_cat_id}")
    return brands, fuels, gears, bodies, drives, colors, features, veh_cats, car_cat_id


def get_models(session, brand_id):
    _, data = api("GET", f"/api/Taxonomy/brands/{brand_id}/models", session)
    if isinstance(data, list):
        return {m["name"].lower(): m["id"] for m in data}
    return {}


def fuzzy(raw, lookup):
    if not raw or not lookup: return None
    r = raw.lower().strip()
    if r in lookup: return lookup[r]
    for k, v in lookup.items():
        if r in k or k in r: return v
    r0 = r.split()[0]
    for k, v in lookup.items():
        if r0 in k: return v
    return None


FUEL_MAP = {
    "benzyna":     ["benzyna", "petrol", "pb"],
    "diesel":      ["diesel", "on", "olej"],
    "hybryda":     ["hybryda", "hybrid", "hev", "phev"],
    "elektryczny": ["elektryczny", "electric", "ev", "bev"],
    "lpg":         ["lpg", "gaz", "cng"],
}
GEAR_MAP = {
    "manualna":     ["manualna", "manual", "mechaniczna"],
    "automatyczna": ["automatyczna", "automat", "automatic", "dsg", "cvt"],
}
BODY_MAP = {
    "sedan":     ["sedan", "limuzyna"],
    "hatchback": ["hatchback"],
    "suv":       ["suv", "crossover", "terenowy"],
    "kombi":     ["kombi", "wagon", "estate", "avant", "touring", "variant"],
    "coupe":     ["coupe"],
    "kabriolet": ["kabriolet", "cabrio", "roadster", "convertible"],
    "van":       ["van", "minivan", "minibus"],
    "pickup":    ["pickup"],
}
DRIVE_MAP = {
    "przod":  ["fwd", "ff", "przod", "przód", "front"],
    "tyl":    ["rwd", "fr", "tyl", "tył", "rear"],
    "4x4":    ["4x4", "awd", "4wd", "quattro", "xdrive", "4motion", "syncro"],
}

def map_val(raw, lookup, aliases):
    if not raw: return None
    r = raw.lower()
    for canon, als in aliases.items():
        if any(a in r for a in als):
            return fuzzy(canon, lookup)
    return None


def match_features(equip_list, feat_lookup):
    if not equip_list or not feat_lookup: return []
    matched = []
    for eq in equip_list:
        eq_l = eq.lower()
        best, best_score = None, 0
        for fname, fid in feat_lookup.items():
            if eq_l == fname:
                score = 100
            elif eq_l in fname or fname in eq_l:
                score = 80
            else:
                eq_w = set(re.split(r'\W+', eq_l))
                f_w  = set(re.split(r'\W+', fname))
                common = eq_w & f_w
                score = (50 + len(common) * 10) if (common and max((len(w) for w in common), default=0) > 3) else 0
            if score > best_score:
                best_score, best = score, fid
        if best and best_score >= 50:
            matched.append(best)
    return list(set(matched))

# ================================================================
#  USUWANIE STARYCH OGLOSZEN
# ================================================================

def delete_my_adverts(session):
    print("\n  Pobieranie moich ogloszen...")
    page_num, deleted = 1, 0
    while True:
        code, body = api("GET", f"/api/Advert/user?page={page_num}&pageSize=50", session)
        if code != 200 or not isinstance(body, dict):
            break
        items = body.get("items") or body.get("data") or []
        if not isinstance(items, list) or not items:
            break
        for advert in items:
            aid = advert.get("id")
            if not aid:
                continue
            dc, _ = api("DELETE", f"/api/Advert/{aid}", session)
            if dc in (200, 204):
                deleted += 1
                log(f"Usunieto #{aid}")
            else:
                warn(f"Nie mozna usunac #{aid}: {dc}")
            time.sleep(0.3)
        if len(items) < 50:
            break
        page_num += 1
    ok(f"Usunieto {deleted} starych ogloszen")

# ================================================================
#  TWORZENIE OGLOSZENIA
# ================================================================

def create_advert(session, car, brands, fuels, gears, bodies, drives, colors, feat_lookup, car_cat_id=None):
    brand_id = fuzzy(car.get("brand_raw"), brands)
    if not brand_id:
        warn(f"Nieznana marka: '{car.get('brand_raw')}' — pomijam")
        return None

    models   = get_models(session, brand_id)
    model_id = fuzzy(car.get("model_raw"), models)
    if not model_id:
        warn(f"Nieznany model: '{car.get('model_raw')}' — pomijam")
        return None

    fuel_id  = map_val(car.get("fuel"),    fuels,  FUEL_MAP) or (list(fuels.values())[0] if fuels else None)
    gear_id  = map_val(car.get("gearbox"), gears,  GEAR_MAP)
    body_id  = map_val(car.get("body"),    bodies, BODY_MAP)
    drive_id = map_val(car.get("drive"),   drives, DRIVE_MAP)
    color_id = fuzzy(car.get("color"),     colors)
    feat_ids = match_features(car.get("equipment", []), feat_lookup)

    desc = car.get("description", "")
    if car.get("equipment"):
        desc += "\n\nWyposazenie:\n- " + "\n- ".join(car["equipment"])
    if car.get("source_url"):
        desc += f"\n\nZrodlo: {car['source_url']}"

    dto = {
        "vehicleCategoryId": car_cat_id,
        "brandId":     brand_id,
        "modelId":     model_id,
        "fuelTypeId":  fuel_id,
        "gearboxId":   gear_id,
        "bodyTypeId":  body_id,
        "driveTypeId": drive_id,
        "colorId":     color_id,
        "year":        max(1980, min(car.get("year") or 2000, 2025)),
        "mileage":     min(car.get("mileage") or 0, 2_000_000),
        "price":       max(1, min(car.get("price") or 1000, 10_000_000)),
        "isNegotiable": True,
        "title":       (car.get("title") or "Ogloszenie")[:200],
        "description": desc[:5000],
        "city":        car.get("city", "Ostrowiec Swietokrzyski"),
        "region":      car.get("region", "swietokrzyskie"),
        "sellerType":  "Dealer",
        "condition":   "Used",
        # Dane techniczne
        "powerHP":     car.get("power_hp"),
        "powerKW":     car.get("power_kw"),
        "engineSize":  car.get("engine_cc"),
        "torque":      car.get("torque"),
        "acceleration": car.get("acceleration"),
        # Spalanie i emisje
        "fuelConsumptionCity":     car.get("fuel_city"),
        "fuelConsumptionHighway":  car.get("fuel_highway"),
        "fuelConsumptionCombined": car.get("fuel_combined"),
        "co2Emission":  car.get("co2"),
        "euroNorm":     car.get("euro_norm"),
        # Masa
        "curbWeight":  car.get("curb_weight"),
        # Nadwozie
        "doorCount":   car.get("doors"),
        "seatsCount":  car.get("seats"),
        # Identyfikacja
        "vin":         car.get("vin"),
        # Historia i rejestracja
        "firstRegistrationDate": car.get("first_reg"),
        "nextInspection":        car.get("next_inspection"),
        "registrationCountry":   car.get("reg_country"),
        "isImported":            car.get("is_imported", False),
        "importCountry":         car.get("import_country"),
        "ownersCount":           car.get("owners"),
        "hasServiceBook":        car.get("has_service_book", False),
        "hasFullServiceHistory": car.get("has_full_service_history", False),
        # Uszkodzenia
        "hasDamage":         car.get("has_damage", False),
        "damageDescription": car.get("damage_desc"),
        # Gwarancja
        "hasWarranty":  car.get("has_warranty", False),
        "warrantyUntil": car.get("warranty_until"),
        # Wyposazenie
        "featureIds":  feat_ids if feat_ids else None,
    }

    dto = {k: v for k, v in dto.items() if v is not None and v is not False}
    # Zawsze dodaj boolowe False jesli istotne
    for bkey in ["isNegotiable", "isImported", "hasServiceBook", "hasFullServiceHistory", "hasDamage", "hasWarranty"]:
        if bkey not in dto:
            dto[bkey] = car.get(bkey.replace("has", "has_").replace("is", "is_")
                                   .replace("HasS", "has_s").replace("HasF", "has_f")
                                   .replace("HasD", "has_d").replace("HasW", "has_w")
                                   .replace("IsI", "is_i"), False)

    code, body = api("POST", "/api/Advert", session, json=dto)
    if code in (200, 201):
        adv_id = body.get("id") if isinstance(body, dict) else body
        log(f"  Pola: paliwo={car.get('fuel')} | {car.get('fuel_city')}l/100km miasto | CO2={car.get('co2')} | Euro={car.get('euro_norm')} | VIN={'tak' if car.get('vin') else 'brak'}")
        return adv_id
    else:
        err(f"Tworzenie: {code} — {body}")
        return None

# ================================================================
#  UPLOAD ZDJEC Z LOKALNYCH PODFOLDEROW
# ================================================================

def upload_local_photos(session, advert_id, folder_path):
    exts = {".jpg", ".jpeg", ".png", ".webp"}
    photos = sorted([f for f in Path(folder_path).iterdir() if f.suffix.lower() in exts], key=lambda f: f.name)
    if not photos:
        warn(f"Brak zdjec w {folder_path}")
        return 0

    ct_backup = session.headers.pop("Content-Type", None)
    uploaded = 0
    for photo in photos[:20]:
        try:
            with open(photo, "rb") as f:
                data = f.read()
            ext = photo.suffix.lower()
            mime = "image/jpeg" if ext in (".jpg", ".jpeg") else f"image/{ext[1:]}"
            files = {"file": (photo.name, io.BytesIO(data), mime)}
            code, body = api("POST", f"/api/Advert/{advert_id}/images", session, files=files)
            if code == 200:
                uploaded += 1
            else:
                warn(f"  {photo.name}: {code}")
            time.sleep(0.3)
        except Exception as e:
            warn(f"  Blad {photo.name}: {e}")

    if ct_backup:
        session.headers["Content-Type"] = ct_backup
    return uploaded

# ================================================================
#  KROK 1: SKAN
# ================================================================

def cmd_scan():
    print("\n" + "="*60)
    print("  KROK 1: Skanowanie autoplac.pl")
    print("="*60)

    cars = []
    with sync_playwright() as pw:
        cars = scrape_all(pw)

    if not cars:
        sys.exit("Nie znaleziono ogloszen.")

    # Zapisz dane
    Path(DATA_FILE).write_text(json.dumps(cars, ensure_ascii=False, indent=2), encoding="utf-8")
    ok(f"Zapisano dane: {DATA_FILE}")

    # Stworz podfoldery
    root = Path(PHOTOS_ROOT)
    root.mkdir(parents=True, exist_ok=True)

    print(f"\n  Tworze podfoldery w: {PHOTOS_ROOT}")
    print()
    for car in cars:
        idx = car.get("_index", cars.index(car) + 1)
        folder_name = safe_folder_name(car["title"], idx)
        folder = root / folder_name
        folder.mkdir(exist_ok=True)
        print(f"  [{idx:02d}] {folder_name}")

    print()
    print("="*60)
    print(f"  Gotowe! Stworzono {len(cars)} podfolderow w:")
    print(f"  {PHOTOS_ROOT}")
    print()
    print("  CO TERAZ ZROBIC:")
    print("  1. Otworz folder: " + PHOTOS_ROOT)
    print("  2. Do kazdego podfolderu wrzuc odpowiednie zdjecia")
    print("     (swoje oryginalne, bez znaku wodnego)")
    print("  3. Uruchom: python import_autoplac_v3.py --import")
    print("="*60)

# ================================================================
#  KROK 3: IMPORT
# ================================================================

def cmd_import():
    print("\n" + "="*60)
    print("  KROK 3: Import do CARIZO")
    print("="*60)

    # Wczytaj dane
    if not Path(DATA_FILE).exists():
        sys.exit(f"Brak pliku {DATA_FILE}. Najpierw uruchom: python import_autoplac_v3.py --scan")

    cars = json.loads(Path(DATA_FILE).read_text(encoding="utf-8"))
    ok(f"Wczytano {len(cars)} ogloszen z {DATA_FILE}")

    # Sprawdz zdjecia
    root = Path(PHOTOS_ROOT)
    folders = {f.name: f for f in root.iterdir() if f.is_dir()} if root.exists() else {}
    if not folders:
        print(f"\n  UWAGA: Brak podfolderow w {PHOTOS_ROOT}")
        print("  Ogloszenia zostana dodane bez zdjec.")
        resp = input("  Kontynuowac? (t/n): ").strip().lower()
        if resp != "t":
            sys.exit("Anulowano.")

    # Logowanie
    print("\n  Logowanie do CARIZO...")
    session = requests.Session()
    session.headers["Content-Type"] = "application/json"
    code, body = api("POST", "/api/Auth/login", session,
                     json={"email": ACCOUNT["email"], "password": ACCOUNT["password"]})
    if code == 200:
        token = body.get("token") or body.get("accessToken") or body.get("access_token")
        if token:
            session.headers["Authorization"] = f"Bearer {token}"
            ok("Zalogowano")
        else:
            sys.exit(f"Brak tokenu: {body}")
    else:
        sys.exit(f"Logowanie: {code} — {body}")

    # Usun stare
    print("\n  Usuwanie starych ogloszen komisu...")
    delete_my_adverts(session)

    # Slowniki
    brands, fuels, gears, bodies, drives, colors, feat_lookup, veh_cats, car_cat_id = load_ref_data(session)

    # Import
    print(f"\n  Import {len(cars)} ogloszen...\n")
    success, failed = 0, 0

    for car in cars:
        idx = car.get("_index", cars.index(car) + 1)
        folder_name = safe_folder_name(car["title"], idx)
        print(f"\n  [{idx:02d}] {car.get('title','?')}  {car.get('price','?')} zl")

        advert_id = create_advert(session, car, brands, fuels, gears, bodies, drives, colors, feat_lookup, car_cat_id)
        if advert_id:
            ok(f"Ogloszenie #{advert_id}")

            # Szukaj pasujacego folderu
            matched_folder = None
            if folder_name in folders:
                matched_folder = folders[folder_name]
            else:
                for fn, fp in folders.items():
                    if fn.startswith(f"{idx:02d}_"):
                        matched_folder = fp
                        break

            if matched_folder:
                photo_list = [f for f in matched_folder.iterdir() if f.suffix.lower() in {".jpg", ".jpeg", ".png", ".webp"}]
                if photo_list:
                    n = upload_local_photos(session, advert_id, matched_folder)
                    ok(f"Wgrano {n} zdjec z {matched_folder.name}")
                else:
                    warn(f"Folder {matched_folder.name} jest pusty — brak zdjec")
            else:
                warn(f"Brak folderu dla: {folder_name}")

            success += 1
        else:
            failed += 1

        time.sleep(1)

    print("\n" + "="*60)
    print(f"  Zaimportowano:  {success} ogloszen")
    if failed:
        print(f"  Pominieto:      {failed} (nieznana marka/model)")
    print("="*60 + "\n")

# ================================================================
#  MAIN
# ================================================================

def main():
    if "--scan" in sys.argv:
        cmd_scan()
    elif "--import" in sys.argv:
        cmd_import()
    else:
        print()
        print("Uzycie:")
        print("  python import_autoplac_v3.py --scan    # Skanuj i stworz foldery")
        print("  python import_autoplac_v3.py --import  # Importuj do CARIZO")
        print()
        print("KOLEJNOSC:")
        print("  1. python import_autoplac_v3.py --scan")
        print(f"  2. Wrzuc zdjecia do podfolderow w: {PHOTOS_ROOT}")
        print("  3. python import_autoplac_v3.py --import")
        print()

if __name__ == "__main__":
    main()
