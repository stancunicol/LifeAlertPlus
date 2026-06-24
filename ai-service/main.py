# Microserviciul AI (Python + FastAPI) — primește date vitale de la backend-ul .NET și returnează
# clasificarea stării pacientului (NORMAL/ALERT/CRITICAL) folosind un model Random Forest pre-antrenat,
# combinat cu reguli medicale explicite bazate pe pragurile personalizate ale pacientului.
#
# Arhitectura deciziei (stratificată, de la prioritate maximă la minimă):
#   1. Validare senzori (date fizic imposibile → UNKNOWN, se oprește procesarea)
#   2. Reguli bazate pe praguri (if spo2 < X → CRITICAL) — decizia finală returnată
#   3. Suprascrieri specifice afecțiunilor (escaladează starea, nu o coboară niciodată)
#   4. Modelul ML (Random Forest) — semnal suplimentar, menționat în explicație dacă diferă
#   5. health_score — scor separat 0-100 calculat paralel, nu influențează clasificarea
#
# Această stratificare garantează că un SpO2 critic e mereu CRITICAL indiferent de ce prezice ML-ul.

from fastapi import FastAPI, HTTPException
# FastAPI: framework web Python async, similar cu ASP.NET Core — definești rute cu decoratori (@app.get etc.)
# HTTPException: excepție FastAPI ce generează automat un răspuns HTTP cu status code (ex: 503, 500)

from fastapi.middleware.cors import CORSMiddleware
# Middleware CORS: interceptează fiecare cerere și adaugă header-ele Access-Control-Allow-Origin,
# necesare browserelor când frontend-ul apelează un API de pe un domeniu diferit

from pydantic import BaseModel
# Pydantic: librărie de validare a datelor — BaseModel definește schema unui JSON primit/trimis.
# FastAPI o folosește pentru a valida automat body-ul requesturilor și a genera documentație OpenAPI.
# Dacă un câmp obligatoriu lipsește sau are tipul greșit, FastAPI returnează automat 422 Unprocessable Entity.

from typing import Optional, List, Dict
# Tipuri Python pentru type hints — Optional[X] = X sau None, List[X] = listă de X, Dict[K,V] = dicționar

import joblib
# joblib: librărie de serializare Python optimizată pentru obiecte NumPy/sklearn (mai rapid decât pickle
# pentru array-uri mari). joblib.load() deserializează fișierul .joblib salvat din Colab/aiAgent.py
# înapoi în obiectele Python originale (RandomForestClassifier, MinMaxScaler, LabelEncoder).

import numpy as np
# NumPy: librărie pentru calcul numeric pe array-uri. Folosit aici pentru:
#   - np.sqrt() în calculate_acceleration_magnitude (magnitudinea vectorului 3D)
#   - np.max(proba) pentru a extrage cea mai mare probabilitate din predict_proba()

import pandas as pd
# Pandas: librărie pentru date tabulare (DataFrame = tabel în memorie).
# Folosit pentru a construi un DataFrame cu coloanele în ordinea exactă așteptată de model/scaler,
# deoarece sklearn are nevoie că feature-urile să fie ordonate identic cu ordinea din antrenament.

import os
# os.environ.get(): citim variabilele de mediu setate în Dockerfile sau Azure Container Instances,
# os.path.join/dirname: construim căi de fișier portabile (funcționează pe Linux în container și Windows local)

import logging
# Logging standard Python: mesajele de nivel INFO/ERROR apar în stdout containerului,
# colectate de Azure Monitor / Docker logs

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)
# __name__ = "main" (numele modulului curent) — permite filtrarea log-urilor per modul în sisteme mari

# Obiectul central FastAPI — fiecare rută se atașează la el printr-un decorator (@app.get, @app.post etc.)
# și e expusă de serverul Uvicorn la portul 8000 (vezi CMD din Dockerfile: uvicorn main:app --host 0.0.0.0 --port 8000)
# title și version apar în documentația automată Swagger UI la /docs
app = FastAPI(title="LifeAlertPlus AI Service", version="1.0.0")

# CORS deschis ("*") — serviciul e apelat doar server-to-server de backend-ul .NET (nu direct din browser),
# deci nu există risc de expunere a unor date sensibile către alte origini necunoscute.
# Dacă serviciul ar fi apelat direct din browser, ar trebui restricționat la domeniul specific al frontend-ului.
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],       # Orice origine poate apela serviciul — OK pentru server-to-server
    allow_credentials=True,    # Permite cookies/Authorization headers în cererile cross-origin
    allow_methods=["*"],       # Toate metodele HTTP permise (GET, POST, OPTIONS etc.)
    allow_headers=["*"],       # Toate header-ele permise (Content-Type, Authorization etc.)
)

# Calea și numele artefactului model — configurabile prin variabile de mediu (vezi Dockerfile),
# ca să poată fi schimbate fără a reconstrui imaginea Docker la reantrenarea modelului.
# MODEL_DIR: directorul cu fișierul .joblib (implicit: subdirectorul "models/" din același director cu main.py)
# ARTIFACT_FILE: numele exact al fișierului .joblib salvat din Google Colab/aiAgent.py
MODEL_DIR = os.environ.get("MODEL_DIR", os.path.join(os.path.dirname(__file__), "models"))
ARTIFACT_FILE = os.environ.get("ARTIFACT_FILE", "patient_monitor_artifacts (3) (2).joblib")

# Variabile globale populate de load_models() la pornirea aplicației — evită reîncărcarea modelului la fiecare cerere.
# În Python, variabilele globale la nivel de modul sunt inițializate o singură dată și reutilizate pe toată durata
# procesului — echivalentul unui singleton din C#.
model = None               # RandomForestClassifier antrenat — conține un ansamblu de arbori de decizie
scaler = None              # MinMaxScaler (sau StandardScaler) fit pe coloanele MPU din setul de antrenament —
                           # stochează min/max per coloană și normalizează datele noi pe același interval
label_encoder = None       # LabelEncoder — tabel de mapare bidirecțional NORMAL/ALERT/CRITICAL ↔ 0/1/2
scaler_feature_names: List[str] = []   # Numele coloanelor pe care scaler-ul a fost fit (din atributul .feature_names_in_)
model_feature_names: List[str] = []    # Numele coloanelor pe care modelul a fost antrenat (din .feature_names_in_)
feature_order: List[str] = []          # Ordinea exactă a coloanelor salvată explicit în artefact la antrenare —
                                       # are prioritate față de model_feature_names dacă există


# Încarcă artefactul .joblib (model + scaler + label encoder, salvate împreună din Colab) o singură dată,
# la pornirea serverului — apelat din evenimentul "startup" al FastAPI (vezi mai jos).
# Un .joblib poate fi creat cu: joblib.dump({"model": clf, "scaler": sc, "encoder": le, "feature_order": cols}, "artifact.joblib")
def load_models():
    # "global" declară că asignările din această funcție modifică variabilele la nivel de modul,
    # nu creează variabile locale noi cu același nume
    global model, scaler, label_encoder, scaler_feature_names, model_feature_names, feature_order
    try:
        artifact_path = os.path.join(MODEL_DIR, ARTIFACT_FILE)
        # joblib.load() deserializează tot dicționarul salvat la antrenare:
        # {"model": RandomForestClassifier, "scaler": MinMaxScaler, "encoder": LabelEncoder, "feature_order": [...]}
        artifacts = joblib.load(artifact_path)

        model         = artifacts["model"]     # RandomForestClassifier: o pădure de n_estimators arbori de decizie
                                               # predict() → votul majoritar al tuturor arborilor
                                               # predict_proba() → fracția de arbori care votează fiecare clasă
        scaler        = artifacts["scaler"]    # MinMaxScaler: transformă fiecare feature în intervalul [0,1]
                                               # formula: x_scaled = (x - x_min) / (x_max - x_min)
                                               # x_min și x_max sunt valorile din setul de antrenare, nu din datele curente
        label_encoder = artifacts["encoder"]   # LabelEncoder: fit pe ["ALERT","CRITICAL","NORMAL"] (ordine alfabetică)
                                               # .transform(["CRITICAL"]) → [1]; .inverse_transform([0]) → ["ALERT"]

        # .get() cu fallback: dacă artefactul a fost salvat fără cheia "feature_order" (versiune veche),
        # căutăm cheia alternativă "features" — retro-compatibilitate fără a reimporta artefactul
        feature_order = artifacts.get("feature_order", list(artifacts.get("features", [])))

        logger.info("AI artifacts loaded from %s", ARTIFACT_FILE)
        logger.info(f"Label encoder classes: {list(label_encoder.classes_)}")
        logger.info(f"Feature order: {feature_order}")

        # Logăm explicit numele feature-urilor pe care scaler-ul și modelul le "cunosc" (din antrenare).
        # hasattr() verifică dacă atributul există — sklearn populează .feature_names_in_ doar dacă
        # fit() a primit un DataFrame cu coloane numite, nu un array NumPy anonim.
        if hasattr(scaler, "feature_names_in_"):
            scaler_feature_names = list(scaler.feature_names_in_)
            logger.info(f"Scaler features ({len(scaler_feature_names)}): {scaler_feature_names}")

        if hasattr(model, "feature_names_in_"):
            model_feature_names = list(model.feature_names_in_)
            logger.info(f"Model features ({len(model_feature_names)}): {model_feature_names}")

    except Exception as e:
        # Re-aruncăm excepția după logare — startup_event() nu o prinde, deci aplicația FastAPI
        # nu pornește deloc dacă artefactul e lipsă/corupt (fail-fast: mai sigur decât a porni parțial
        # și a servi predicții greșite fără să știe nimeni că modelul nu s-a încărcat)
        logger.error(f"Error loading models: {e}")
        raise


# Calculează magnitudinea (lungimea) vectorului de accelerație din componentele pe cele 3 axe (X, Y, Z).
# Formula: |a| = sqrt(ax² + ay² + az²) — teorema lui Pitagora generalizată la 3 dimensiuni.
# La repaus, magnitudinea e ~1g (gravitația terestră); la mers ~1.1-1.3g; la cădere poate ajunge la 3-5g
# urmat de aproape 0g (imobilitate post-cădere). Această valoare scalară e mai ușor de clasificat
# decât cele 3 componente separate, deoarece nu depinde de orientarea dispozitivului.
def calculate_acceleration_magnitude(ax: float, ay: float, az: float) -> float:
    return float(np.sqrt(ax**2 + ay**2 + az**2))


def compute_health_score(
    temp: float, hr: float, spo2: float, acc: float,
    conditions: Optional[List[str]] = None,
    max_hr: Optional[int] = None,
    min_hr: Optional[int] = None,
    max_temp: Optional[float] = None,
    min_temp: Optional[float] = None,
    min_spo2: Optional[int] = None,
) -> int:
    """
    Calculează un scor de pericol brut în intervalul 0–100 (mai mare = mai rău).
    UI-ul afișează 100 - health_score ca "scor de sănătate" (100 = perfect, 0 = critic).

    Scorul e suma penalizărilor din mai multe surse (SpO2, HR, Temp, afecțiuni). Poate depăși
    teoretic 100 dacă se acumulează penalizări multiple severe, de aceea e plafonat la return.
    Nu determină clasificarea finală (NORMAL/ALERT/CRITICAL) — e un indicator cantitativ separat.
    """
    score = 0.0
    conds = conditions or []

    # Praguri efective: folosim valorile personalizate ale pacientului dacă există,
    # altfel valorile clinice implicite (bazate pe intervale normale pentru adulți).
    # Acest pattern ("if max_hr else 130") se repetă în toată funcția pentru fiecare semn vital.
    eff_max_hr   = max_hr   if max_hr   else 130
    eff_min_hr   = min_hr   if min_hr   else 50
    eff_max_temp = max_temp if max_temp else 39.0
    eff_min_temp = min_temp if min_temp else 35.0
    eff_min_spo2 = min_spo2 if min_spo2 else 95
    # Pragul critic SpO2 = pragul minim al pacientului - 5 puncte procentuale, dar niciodată sub 70%
    # (sub 70% orice organ vital suferă leziuni ireversibile rapid)
    eff_crit_spo2 = max(70, eff_min_spo2 - 5)

    # Praguri de avertizare timpurie ("pre-alert"): zona gri înainte de a atinge pragul critic.
    # 85% din max HR = dacă pacientul are max 130 bpm, zona de avertizare începe la 110 bpm.
    # 120% din min HR = dacă pacientul are min 50 bpm, zona de avertizare e sub 60 bpm.
    hr_warn_high   = int(eff_max_hr  * 0.85)
    hr_warn_low    = int(eff_min_hr  * 1.20)
    temp_warn_high = eff_max_temp - 1.0   # Cu 1°C sub pragul maxim
    temp_warn_low  = eff_min_temp + 1.0   # Cu 1°C deasupra pragului minim

    # --- SpO2: dominant în scor — SpO2 scăzut e cel mai periculos semn vital izolat ---
    if spo2 < eff_crit_spo2:
        # Sub pragul critic → penalizare fixă mare (60 din 100 posibile).
        # Indiferent de celelalte semne vitale, SpO2 critic singur produce un scor de pericol ridicat.
        score += 60
    elif spo2 < eff_min_spo2:
        # Între pragul critic și cel de alertă → penalizare gradată.
        # Cu cât SpO2 e mai departe sub prag, cu atât penalizarea crește liniar (+3 per punct procentual).
        # Ex: eff_min_spo2=95, spo2=91 → score += 30 + (95-91)*3 = 30 + 12 = 42
        score += 30 + (eff_min_spo2 - spo2) * 3

    # --- Puls: penalizare pe 4 niveluri (critic sus, avertizare sus, critic jos, avertizare jos) ---
    if hr > eff_max_hr:
        score += 25          # Tahicardie confirmată — peste pragul maxim al pacientului
    elif hr > hr_warn_high:
        score += 12          # Zona de avertizare (85–100% din max): puls în creștere spre prag
    elif 0 < hr < eff_min_hr:
        # "0 < hr" exclude valoarea 0 care înseamnă "senzor fără citire validă" (deget ridicat de pe senzor),
        # nu bradicardie reală — nu penalizăm absența datelor
        score += 20          # Bradicardie confirmată — sub pragul minim al pacientului
    elif 0 < hr < hr_warn_low:
        score += 10          # Zona de avertizare (min–120% din min): puls în scădere spre prag

    # --- Temperatură: aceeași structură în 4 niveluri ca pulsul ---
    if temp > eff_max_temp:
        score += 20          # Febră peste pragul maxim al pacientului (ex: >39°C implicit)
    elif temp > temp_warn_high:
        score += 10          # Febră incipientă (între prag-1°C și prag)
    elif 0 < temp < eff_min_temp:
        # "0 < temp" exclude valoarea 0 = senzor neinițializat (MLX90614 returnează 0 la eroare)
        score += 20          # Hipotermie sub pragul minim al pacientului (ex: <35°C implicit)
    elif 0 < temp < temp_warn_low:
        score += 10          # Temperatură scăzută dar nu critică

    # --- Accelerație: magnitudine mică = posibilă imobilitate prelungită ---
    # Valoarea normală la repaus e ~1g (gravitație). Sub 0.2g = dispozitivul e aproape nemișcat,
    # ceea ce poate indica că pacientul e căzut sau inconștient. Penalizare mică (8) deoarece
    # imobilitatea singură nu e neapărat periculoasă (poate dormi).
    if acc < 0.2:
        score += 8

    # --- Penalizări specifice afecțiunilor diagnosticate ---
    # Fiecare afecțiune adaugă penalizare suplimentară NUMAI când semnele vitale depășesc
    # pragurile relevante pentru acea afecțiune — nu penalizăm automat doar pentru că există boala.
    # Acumularea acestor penalizări peste cele de mai sus poate face scorul brut să depășească 100.
    for cond in conds:
        if cond == "hypertension":
            # Hipertensivi au risc cardiovascular crescut la puls > 100 (chiar dacă max_hr personal e 130)
            if hr > 100:
                score += 8
        elif cond == "arrhythmia":
            # La aritmici, ORICE anomalie de ritm (puls prea mare SAU prea mic) e mai periculoasă
            # decât la un pacient fără boli de inimă — ambele direcții sunt riscante
            if hr > 110 or (0 < hr < 52):
                score += 10
        elif cond == "mi_risk":
            # Risc de infarct: combinația puls mare + febră ușoară = stres cardiac crescut
            # Febra crește cererea de oxigen a inimii, agravând riscul de infarct
            if hr > 110 and temp > 37.8:
                score += 12
        elif cond == "diabetes":
            # Diabeticii au sistem imunitar afectat — orice infecție (semnalată de febră) e mai periculoasă
            if temp > 38.0:
                score += 8
        elif cond in ("parkinson", "epilepsy"):
            # La acești pacienți, imobilitatea prelungită e mai îngrijorătoare (prag mai permisiv 0.5 vs 0.2)
            # deoarece au risc mai mare de cădere și episoade de rigiditate/convulsie cu imobilitate secundară
            if acc < 0.5:
                score += 5

    # Plafonare finală în [0, 100]: indiferent de câte penalizări s-au acumulat
    # (teoretic scorul brut poate ajunge la 60+25+20+8+12 = 125 la o urgență maximă cu diabet și mi_risk)
    return int(max(0, min(100, round(score))))


def analyze_patient_data(
    temp: float, hr: float, spo2: float,
    ax: float, ay: float, az: float,
    gx: float, gy: float, gz: float,
    conditions: Optional[List[str]] = None,
    max_hr: Optional[int] = None,
    min_hr: Optional[int] = None,
    max_temp: Optional[float] = None,
    min_temp: Optional[float] = None,
    min_spo2: Optional[int] = None,
    max_spo2: Optional[int] = None,
) -> Dict:
    """
    Funcția principală de analiză: combină predicția ML cu reguli medicale explicite.

    Parametri:
        temp, hr, spo2       — semne vitale de la senzori
        ax/ay/az, gx/gy/gz  — date accelerometru și giroscop de la MPU6050
        conditions           — lista afecțiunilor diagnosticate (chei string, ex: "hypertension")
        max_hr ... min_spo2  — praguri personalizate ale pacientului (None = se folosesc valorile implicite)

    Returnează un Dict cu: prediction, confidence, risk_level, details, health_score, all_probabilities.

    IMPORTANT: final_state (prediction/risk_level) e determinat de regulile bazate pe praguri,
    nu de predicția ML. Modelul e folosit ca semnal suplimentar de explicabilitate.
    """
    conds = conditions or []

    # Validare date senzori: valori fizic improbabile indică o eroare de senzor sau transmisie,
    # nu o stare medicală reală. Returnăm UNKNOWN imediat fără a rula ML sau regulile.
    # Intervale acceptate:
    #   spo2: (0, 100] — 0 înseamnă fără citire; >100 e imposibil fizic
    #   hr:   (20, 250) — sub 20 = asistolă (mort clinic); peste 250 = imposibil susținut
    #   temp: (30, 45)  — sub 30 = hipotermie profundă severă; peste 45 = incompatibil cu viața
    if not (0 < spo2 <= 100 and 20 < hr < 250 and 30 < temp < 45):
        return {
            "prediction": "UNKNOWN",
            "confidence": 0.0,
            "risk_level": "UNKNOWN",
            "details": "Invalid sensor data detected.",
            "health_score": 0,
            "all_probabilities": {},
        }

    # Construim dicționarul de date interne cu toate valorile senzorilor + magnitudinea calculată.
    # Magnitudinea e adăugată ca feature suplimentar deoarece sintetizează mișcarea din toate cele 3 axe.
    data: Dict[str, float] = {
        "temp": temp, "hr": hr, "spo2": spo2,
        "ax": ax, "ay": ay, "az": az,
        "gx": gx, "gy": gy, "gz": gz,
    }
    data["acc"] = calculate_acceleration_magnitude(ax, ay, az)

    # Pragurile efective ale pacientului (același pattern ca în compute_health_score).
    # Duplicat intenționat (nu DRY) pentru că fiecare funcție poate evolua independent.
    eff_max_hr    = max_hr   if max_hr   else 130
    eff_min_hr    = min_hr   if min_hr   else 50
    eff_max_temp  = max_temp if max_temp else 39.0
    eff_min_temp  = min_temp if min_temp else 35.0
    eff_min_spo2  = min_spo2 if min_spo2 else 95
    eff_crit_spo2 = max(70, eff_min_spo2 - 5)

    # Praguri de alertă timpurie, calculate relativ la pragurile pacientului:
    # hr_alert_high = 85% din max → zona "aproape de prag" (avertizare)
    # temp_alert_high = max - 0.5°C → cu jumătate de grad sub pragul maxim
    hr_alert_high   = int(eff_max_hr * 0.85)
    hr_alert_low    = int(eff_min_hr * 1.20)
    temp_alert_high = eff_max_temp - 0.5

    # =========================================================================
    # PASUL 1: Predicția modelului ML (Random Forest)
    # =========================================================================
    # Coloanele MPU trebuie normalizate cu ACELAȘI scaler fit la antrenare —
    # dacă la antrenare ax era în [-2g, +2g], scaler-ul l-a transformat în [0,1],
    # deci datele live trebuie tratate identic înainte de a fi date modelului.
    # Semnele vitale (temp, hr, spo2) NU sunt normalizate cu scaler-ul MPU —
    # ele intră direct în model sau cu transformări proprii (spo2 × 2 mai jos).
    sensor_columns = ["ax", "ay", "az", "gx", "gy", "gz"]
    # Construim un DataFrame cu o singură linie și coloanele în ordinea așteptată de scaler
    mpu_data = pd.DataFrame([[data[col] for col in sensor_columns]], columns=sensor_columns)
    # scaler.transform() aplică formula: (x - x_min) / (x_max - x_min) per coloană
    normalized_mpu_data = scaler.transform(mpu_data)

    # SpO2 e dublat (* 2) pentru a-i da o pondere mai mare în spațiul de feature-uri al modelului.
    # Aceasta e o decizie luată la antrenare (în aiAgent.py/Colab): dacă SpO2 e în [85,100]
    # și hr e în [50,130], scalele lor naturale sunt similare, dar clinic SpO2 e mai important.
    # Dublând SpO2, creștem distanța relativă dintre un 90% (alert) și un 97% (normal),
    # ajutând arborii de decizie să separe mai bine clasele.
    input_data_for_model: Dict[str, float] = {
        "temp": data["temp"], "hr": data["hr"], "spo2": data["spo2"] * 2, "acc": data["acc"]
    }
    for i, col in enumerate(sensor_columns):
        input_data_for_model[col] = normalized_mpu_data[0][i]   # Extragem valorile normalizate din array-ul NumPy

    # Construim DataFrame-ul final pentru model cu coloanele în EXACT aceeași ordine ca la antrenare.
    # Ordinea coloanelor contează la sklearn: dacă la antrenare feature-ul 0 era "temp" și acum
    # e "hr", modelul va interpreta greșit datele (fără eroare, dar cu predicții incorecte).
    input_df = pd.DataFrame([input_data_for_model])
    cols = feature_order if feature_order else model_feature_names   # feature_order din artefact are prioritate
    input_features = input_df[cols]

    # model.predict() → returnează un array cu eticheta codificată numeric (ex: [1] pentru CRITICAL)
    predicted_label_encoded = model.predict(input_features)[0]
    # label_encoder.inverse_transform() → decodifică înapoi în string (ex: [1] → ["CRITICAL"])
    predicted_state_model = label_encoder.inverse_transform([predicted_label_encoded])[0]

    # model.predict_proba() → distribuția de probabilități pentru toate clasele:
    # ex: [0.05, 0.80, 0.15] = 5% ALERT, 80% CRITICAL, 15% NORMAL (ordinea = label_encoder.classes_)
    proba = model.predict_proba(input_features)[0]
    # Confidence = probabilitatea clasei prezise (= max din distribuție)
    model_confidence = float(np.max(proba)) * 100

    # Corecție medicală a predicției ML: dacă modelul spune NORMAL/ALERT dar SpO2 e critic,
    # forțăm CRITICAL. ML-ul poate greși statistic; un SpO2 critic e un fapt clinic obiectiv.
    # Această corecție se aplică ÎNAINTE de a calcula all_probabilities, ca răspunsul să fie coerent.
    if spo2 < eff_crit_spo2 and predicted_state_model != "CRITICAL":
        predicted_state_model = "CRITICAL"
        model_confidence = 100.0   # Certitudine maximă — SpO2 critic e o regulă hardcodata, nu o estimare
    elif spo2 < eff_min_spo2 and predicted_state_model == "NORMAL":
        predicted_state_model = "ALERT"
        model_confidence *= 0.5    # Reducem confidence-ul la jumătate ca să semnalăm corecția manuală

    # Construim dicționarul de probabilități (cls → float) din array-ul NumPy retournat de predict_proba.
    # label_encoder.classes_ e mereu în ordine alfabetică: ["ALERT", "CRITICAL", "NORMAL"]
    # proba[0] = probabilitatea pentru "ALERT", proba[1] = "CRITICAL", proba[2] = "NORMAL"
    probabilities: Dict[str, float] = {}
    for i, cls in enumerate(label_encoder.classes_):
        probabilities[cls] = round(float(proba[i]), 4)

    # Actualizăm și distribuția de probabilități pentru coerență cu predicția corectată.
    # Redistribuim probabilitatea "confiscată" de la NORMAL: 70% → ALERT, 30% → CRITICAL
    if spo2 < eff_crit_spo2:
        # SpO2 critic: distribuție deterministă (nu mai are sens să afișăm probabilități ML)
        probabilities = {"CRITICAL": 1.0, "ALERT": 0.0, "NORMAL": 0.0}
    elif spo2 < eff_min_spo2:
        normal_prob = probabilities.get("NORMAL", 0.0)
        probabilities["NORMAL"] = 0.0
        probabilities["ALERT"] = min(1.0, round(probabilities.get("ALERT", 0.0) + normal_prob * 0.7, 4))
        probabilities["CRITICAL"] = min(1.0, round(probabilities.get("CRITICAL", 0.0) + normal_prob * 0.3, 4))

    # =========================================================================
    # PASUL 2: Sistem de reguli combinat (scor discret pe mai multe semne vitale)
    # =========================================================================
    # base_score nu e health_score — e un contor simplu de "câte reguli de alertă sunt violate".
    # Folosit DOAR pentru combined_rule_state, care apare în explicația finală când starea e NORMAL.
    # Nu influențează direct final_state (decizia finală).
    reasons: List[str] = []
    base_score: int = 0

    if data["spo2"] < eff_min_spo2:
        base_score += 1
        reasons.append(f"SpO2 ({data['spo2']:.1f}%) is below patient threshold (<{eff_min_spo2:.0f}%).")
    if data["hr"] > hr_alert_high + 20:
        # Cu 20 bpm peste pragul de avertizare = cu 20 bpm în zona critică (>85%*max + 20) → 3 puncte
        base_score += 3
        reasons.append(f"Heart rate ({data['hr']:.0f} bpm) is critically high.")
    elif data["hr"] > hr_alert_high:
        base_score += 1
        reasons.append(f"Heart rate ({data['hr']:.0f} bpm) is elevated.")
    if data["temp"] > eff_max_temp:
        base_score += 2
        reasons.append(f"Temperature ({data['temp']:.1f}°C) exceeds patient threshold ({eff_max_temp}°C).")
    elif data["temp"] > temp_alert_high:
        base_score += 1
        reasons.append(f"Temperature ({data['temp']:.1f}°C) is elevated.")
    # Tripla combinație: imobilitate + bradicardie + hipoxie = risc mare de șoc sau pierdere a cunoștinței
    if data["acc"] < 0.2 and data["hr"] < eff_min_hr and data["spo2"] < eff_min_spo2:
        base_score += 1
        reasons.append(f"Low acceleration, low heart rate and low SpO2 indicate potential immobility/fall.")

    # Clasificare bazată pe scorul punctual (praguri arbitrare calibrate empiric):
    # ≥4 = multiple semne vitale depășesc pragurile → CRITICAL
    # ≥3 = cel puțin un semn vital clar depășit → ALERT
    # <3 = situație normală sau subclinică → NORMAL
    if base_score >= 4:
        combined_rule_state = "CRITICAL"
    elif base_score >= 3:
        combined_rule_state = "ALERT"
    else:
        combined_rule_state = "NORMAL"

    # health_score e calculat independent cu propria sa logică de penalizare (mai granulară decât base_score).
    # Sunt două scoruri separate cu scopuri diferite:
    #   base_score → combined_rule_state → explicație textuală
    #   health_score → afișat în UI ca indicator cantitativ (0–100)
    health_score = compute_health_score(
        temp=temp, hr=hr, spo2=spo2, acc=data["acc"],
        conditions=conds,
        max_hr=max_hr, min_hr=min_hr,
        max_temp=max_temp, min_temp=min_temp,
        min_spo2=min_spo2,
    )

    # Convertim base_score (0-7 discret) în procent de confidence (0-100) pentru combined_rule_state.
    # Formula: 50% la limita dintre categorii, +15% per punct de distanță față de limită.
    # Logica: confidence e maximă când starea e "clar" într-o direcție, incertă la 50% la limită.
    # Ex CRITICAL (prag = 4): base_score=4 → 50%, base_score=5 → 65%, base_score=6 → 80%
    # Ex NORMAL (prag = 3): base_score=2 → 65%, base_score=1 → 80%, base_score=0 → 95%
    system_confidence_combined: int = 0
    if combined_rule_state == "CRITICAL":
        system_confidence_combined = max(0, min(100, int(50 + (base_score - 4.0) * 15)))
    elif combined_rule_state == "ALERT":
        dist_up = 4.0 - base_score    # distanța față de pragul CRITICAL
        dist_down = base_score - 3.0  # distanța față de pragul ALERT
        system_confidence_combined = max(0, min(100, int(50 + min(dist_up, dist_down) * 15)))
    else:
        system_confidence_combined = max(0, min(100, int(50 + (3.0 - base_score) * 15)))

    # =========================================================================
    # PASUL 3: Starea finală prin reguli medicale explicite (decizia returnată)
    # =========================================================================
    # Verificările sunt ordonate de la severitate maximă la minimă — primul "elif" adevărat câștigă.
    # Aceasta garantează că SpO2 critic nu poate fi "ascuns" de un puls normal (ar intra în prima ramură).
    final_state = ""
    explanation = ""
    system_confidence = 0

    if spo2 < eff_crit_spo2:
        # Urgență maximă: SpO2 sub pragul critic (eff_min_spo2 - 5, minim 70%).
        # La o saturație sub 85%, creierul suferă hipoxie în câteva minute.
        final_state = "CRITICAL"
        explanation = f"Critical: SpO2 ({data['spo2']:.1f}%) is dangerously low (threshold: <{eff_crit_spo2:.0f}%). Requires immediate attention."
        system_confidence = 100   # Certitudine maximă — regulă hardcodată, nu estimare probabilistică

    elif spo2 < eff_min_spo2 and (hr > eff_max_hr or temp >= eff_max_temp):
        # SpO2 moderat scăzut + fie puls ridicat, fie febră: combinația indică stres fiziologic semnificativ.
        # Fiecare semn izolat ar fi ALERT; combinația lor indică o situație mai gravă → CRITICAL.
        final_state = "CRITICAL"
        explanation = f"Critical combination: SpO2 ({data['spo2']:.1f}%) below patient threshold with severe physiological stress (high HR or high fever)."
        system_confidence = 98

    elif temp >= temp_alert_high:
        # Temperatură peste pragul de avertizare (max_temp - 0.5°C) — verificată înainte de SpO2 alert izolat
        # deoarece febra mare e un semn vital mai imediat vizibil/periculos decât SpO2 ușor scăzut
        final_state = "ALERT"
        explanation = f"ALERT: Temperature ({data['temp']:.1f}°C) exceeds patient alert threshold ({temp_alert_high:.1f}°C)."
        system_confidence = 85

    elif spo2 < eff_min_spo2:
        # SpO2 scăzut izolat (fără puls sau temperatură ridicate):
        # Între eff_crit_spo2 și eff_min_spo2 — alert, dar nu urgență maximă
        final_state = "ALERT"
        explanation = f"ALERT: SpO2 ({data['spo2']:.1f}%) is below patient threshold ({eff_crit_spo2:.0f}–{eff_min_spo2:.0f}%). Potential respiratory issue."
        system_confidence = 90

    elif hr > hr_alert_high:
        # Tahicardie: puls peste 85% din pragul maxim al pacientului
        final_state = "ALERT"
        explanation = f"ALERT: Heart rate ({data['hr']:.0f} bpm) exceeds patient alert threshold ({hr_alert_high} bpm)."
        system_confidence = 80

    elif 0 < hr < hr_alert_low:
        # Bradicardie: puls sub 120% din pragul minim al pacientului (0 < hr exclude citire lipsă)
        final_state = "ALERT"
        explanation = f"ALERT: Heart rate ({data['hr']:.0f} bpm) is below patient alert threshold ({hr_alert_low} bpm)."
        system_confidence = 80

    else:
        # Nicio regulă de alertă/critic declanșată → NORMAL
        final_state = "NORMAL"
        explanation = "All vital signs are stable and within the patient's defined thresholds. "
        if combined_rule_state != "NORMAL":
            # Cazul "NORMAL dar cu tendință": regulile combinate (pasul 2) sugerează ceva îngrijorător
            # chiar dacă nicio regulă individuală nu a declanșat alert. Informăm medicul/îngrijitorul.
            explanation += f"Combined rule analysis suggests '{combined_rule_state}' tendency: " + "; ".join(reasons) + "."
            system_confidence = system_confidence_combined
        else:
            explanation += "No significant risks identified."
            system_confidence = 100

    # =========================================================================
    # PASUL 4: Suprascrieri specifice afecțiunilor diagnosticate
    # =========================================================================
    # Pot ESCALADA starea (NORMAL→ALERT sau ALERT→CRITICAL), niciodată nu o retrogradeaza.
    # Principiu de siguranță: dacă o regulă a zis CRITICAL, nicio afecțiune nu poate schimba asta în ALERT.
    condition_notes: List[str] = []
    for cond in conds:
        if cond == "arrhythmia" and (hr > 110 or (0 < hr < 52)) and final_state == "NORMAL":
            # Aritmicii au prag de alarmă mai strict pentru puls: >110 sau <52 bpm sunt riscante
            # chiar dacă pentru un pacient normal fără aritmie ar fi NORMAL
            final_state = "ALERT"
            system_confidence = max(system_confidence, 80)
            condition_notes.append(f"[arrhythmia] Heart rate ({hr:.0f} bpm) is abnormal for this patient.")
        elif cond == "mi_risk" and hr > 110 and temp > 37.8 and final_state != "CRITICAL":
            # Risc de infarct + puls > 110 + febră ușoară = stres cardiac crescut, escaladăm la ALERT
            # (nu la CRITICAL deoarece această combinație nu e imediat letală, dar necesită monitorizare)
            final_state = "ALERT"
            system_confidence = max(system_confidence, 85)
            condition_notes.append(f"[mi_risk] Cardiac stress markers detected (HR {hr:.0f} bpm, Temp {temp:.1f}°C).")

    if condition_notes:
        explanation += " " + " ".join(condition_notes)

    # Dacă modelul ML a ajuns la o concluzie diferită de sistemul de reguli cu suficientă încredere (>70%),
    # menționăm diferența în explicație ca informație suplimentară pentru medic.
    # Nu schimbăm decizia finală — regulile au prioritate față de ML.
    if predicted_state_model != final_state and model_confidence > 70:
        explanation += f" AI model suggests '{predicted_state_model}' (differs from rule-based decision)."

    # Adăugăm o notă pozitivă dacă semnele vitale sunt în intervalul ideal (nu doar "în prag")
    if final_state == "NORMAL" and 60 <= hr <= 90 and spo2 >= 96:
        explanation += " Vital signs are consistent with a healthy baseline."

    return {
        "prediction": final_state,                         # NORMAL / ALERT / CRITICAL (din regulile medicale)
        "confidence": round(system_confidence / 100.0, 4), # 0.0–1.0 (din regulile medicale, nu din ML)
        "risk_level": final_state,                         # Identic cu prediction (redundant, pentru compatibilitate API)
        "details": explanation,                            # Text explicativ pentru medic/îngrijitor
        "health_score": health_score,                      # 0–100 scor de pericol (100 = critic, 0 = perfect)
        "all_probabilities": probabilities,                # Distribuția de probabilități a modelului ML (corectată)
    }


# Schema cererii JSON primite de la AIController (.NET) — Pydantic validează automat tipurile la deserializare.
# Dacă un câmp obligatoriu (pulse, temperature) lipsește sau are tipul greșit, FastAPI returnează 422.
# Câmpurile opționale cu valori implicite (Optional[X] = Y) nu cauzează eroare dacă lipsesc din JSON.
class PredictionRequest(BaseModel):
    pulse: float                          # Obligatoriu — pulsul pacientului în bpm de la MAX30102
    temperature: float                    # Obligatoriu — temperatura corporală în °C de la MLX90614

    spo2: Optional[float] = 97.0          # Saturația de oxigen (%) — 97.0 implicit = valoare sănătoasă sigură
                                          # (dacă senzorul nu a transmis, nu vrem să generăm fals ALERT)

    accel_x: Optional[float] = 0.0       # Axele accelerometrului MPU6050 (în g, ±2g interval implicit)
    accel_y: Optional[float] = 0.0       # 0.0 implicit = dispozitiv la repaus complet (no movement)
    accel_z: Optional[float] = 0.0       # Toate trei axe necesare pentru magnitudinea vectorului de accelerație

    gyro_x: Optional[float] = 0.0        # Axele giroscopului MPU6050 (în grade/secundă)
    gyro_y: Optional[float] = 0.0        # Giroscopul detectează rotația, util pentru detecția căderilor
    gyro_z: Optional[float] = 0.0        # Valori mari brusc la toate 3 axe = potențială cădere

    conditions: Optional[List[str]] = [] # Afecțiunile diagnosticate ale pacientului, enriched de AIController
                                          # din MonitoredConditions din DB înainte de a apela /predict
                                          # Valori posibile: "hypertension","arrhythmia","diabetes","copd",
                                          # "heart_failure","parkinson","epilepsy","mi_risk" etc.

    max_heart_rate: Optional[int] = None  # Praguri personalizate configurate de medic în UI —
    min_heart_rate: Optional[int] = None  # None = se folosesc valorile implicite clinice din analyze_patient_data
    max_temperature: Optional[float] = None
    min_temperature: Optional[float] = None
    min_spo2: Optional[int] = None
    max_spo2: Optional[int] = None        # Primit dar neutilizat — SpO2 are clinic relevanță doar pentru pragul minim


# Schema răspunsului JSON trimis înapoi la AIController — corespunde exact cu dicționarul din analyze_patient_data.
# FastAPI serializează automat obiectul PredictionResponse în JSON folosind Pydantic.
class PredictionResponse(BaseModel):
    prediction: str        # "NORMAL", "ALERT", "CRITICAL" sau "UNKNOWN"
    confidence: float      # 0.0–1.0 — cât de cert e sistemul de reguli în clasificarea sa
    risk_level: str        # Identic cu prediction (duplicat pentru compatibilitate cu clienții API existenți)
    details: str           # Explicație textuală a deciziei (motivele alertei sau confirmarea normalului)
    health_score: int      # 0–100 scor de pericol calculat de compute_health_score()
    all_probabilities: dict # {"NORMAL": 0.05, "ALERT": 0.80, "CRITICAL": 0.15} — distribuția modelului ML


# Evenimentul "startup" al FastAPI — rulat automat de Uvicorn înainte de a accepta orice cerere.
# Încarcă modelul o singură dată în memorie. Dacă load_models() aruncă excepție (fișier lipsă, corupt),
# serverul nu pornește și eșuează vizibil în loguri — fail-fast intenționat.
@app.on_event("startup")
async def startup_event():
    load_models()


# GET /health — endpoint de verificare a stării serviciului.
# Apelat de backend-ul .NET (AIController) ÎNAINTE de a trimite date la /predict —
# dacă răspunde cu model_loaded=False, AIController returnează 502 fără să mai apeleze /predict.
# De asemenea folosit de Azure Container Instances pentru health probes (verificare periodică a containerului).
@app.get("/health")
async def health():
    return {
        "status": "healthy",                # Mereu "healthy" dacă serverul răspunde (Uvicorn e pornit)
        "model_loaded": model is not None,  # False dacă load_models() a eșuat la startup
        "scaler_loaded": scaler is not None,
        "encoder_loaded": label_encoder is not None,
    }


# GET /model-info — endpoint de diagnosticare, expune detalii despre artefactele încărcate.
# Util la depanare pentru a verifica că ordinea feature-urilor, clasele și tipul modelului sunt corecte,
# fără a fi nevoie să te conectezi la container sau să citești log-urile.
@app.get("/model-info")
async def model_info():
    info = {
        # label_encoder.classes_ = array NumPy cu clasele în ordine alfabetică (["ALERT","CRITICAL","NORMAL"])
        "classes": list(label_encoder.classes_) if label_encoder else [],
        # n_features_in_ = numărul de feature-uri așteptate de model (ar trebui să fie 10: temp,hr,spo2,ax,ay,az,gx,gy,gz,acc)
        "n_features": getattr(model, "n_features_in_", None),
        # feature_names_in_ există doar dacă modelul a fost antrenat cu un DataFrame cu coloane numite
        "feature_names": list(model.feature_names_in_) if hasattr(model, "feature_names_in_") else None,
        # type(model).__name__ = "RandomForestClassifier" — util dacă se schimbă tipul de model în viitor
        "model_type": type(model).__name__ if model else None,
        "scaler_features": scaler_feature_names,          # Populat de load_models() din scaler.feature_names_in_
        "scaler_n_features": getattr(scaler, "n_features_in_", None) if scaler else None,
        "feature_order": feature_order,                   # Ordinea citită din artefact, folosită la predict
    }
    return info


# POST /predict — endpoint principal, apelat de AIController (.NET) la fiecare măsurătoare nouă primită de la ESP32.
# response_model=PredictionResponse: FastAPI validează că răspunsul are forma corectă înainte de a-l trimite.
@app.post("/predict", response_model=PredictionResponse)
async def predict(request: PredictionRequest):
    # 503 Service Unavailable: artefactele nu s-au încărcat la startup (model lipsă/corupt).
    # AIController primește 503 și returnează 502 Bad Gateway clientului Blazor —
    # semnalează clar că problema e de infrastructură, nu de date trimise.
    if model is None or scaler is None or label_encoder is None:
        raise HTTPException(status_code=503, detail="Models not loaded")

    try:
        # Mapăm câmpurile request-ului pe parametrii funcției analyze_patient_data.
        # "or valoare_default" asigură fallback și pentru câmpurile opționale care sunt trimise ca None explicit
        # (Pydantic aplică valoarea implicită din schema, dar "or" e un strat suplimentar de siguranță).
        result = analyze_patient_data(
            temp=request.temperature,
            hr=request.pulse,
            spo2=request.spo2 or 97.0,
            ax=request.accel_x or 0.0,
            ay=request.accel_y or 0.0,
            az=request.accel_z or 0.0,
            gx=request.gyro_x or 0.0,
            gy=request.gyro_y or 0.0,
            gz=request.gyro_z or 0.0,
            conditions=request.conditions or [],
            max_hr=request.max_heart_rate,
            min_hr=request.min_heart_rate,
            max_temp=request.max_temperature,
            min_temp=request.min_temperature,
            min_spo2=request.min_spo2,
            max_spo2=request.max_spo2,
        )

        # PredictionResponse(**result): despachetăm dicționarul returnat de analyze_patient_data
        # direct în constructorul modelului Pydantic (echivalentul spread operator-ului)
        return PredictionResponse(**result)

    except Exception as e:
        # Erori neașteptate: model corupt, date neconforme care trec de validarea Pydantic dar pică
        # în pandas/sklearn (ex: NaN în loc de float), erori de memorie etc.
        # Logăm stack trace-ul complet (exc_info=True) pentru depanare, returnăm 500 generic clientului.
        logger.error(f"Prediction error: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Prediction failed: {str(e)}")
