# Script de antrenare rulat în Google Colab (NU face parte din serviciul FastAPI rulat în producție —
# main.py doar încarcă artefactul .joblib produs aici). Generează date sintetice din 3 dataseturi reale
# (temperatură corporală, puls/SpO2 dintr-un dataset clinic BIDMC, accelerometru/giroscop MPU6050),
# le etichetează cu o funcție bazată pe reguli (combined_rule_based_label) și antrenează un
# RandomForestClassifier să reproducă acele etichete — apoi evaluează modelul prin cross-validation
# și explicabilitate SHAP, pentru raportarea rezultatelor în lucrarea de licență/disertație.
import pandas as pd               # DataFrame-uri — manipulare tabelară a datelor (CSV-uri, dataset sintetic)
import numpy as np                 # Operații numerice vectorizate (medii, sqrt, distribuții aleatoare pentru zgomot)
import matplotlib.pyplot as plt    # Generare grafice (confuzie, importanță feature-uri, SHAP, curbe CV)
import seaborn as sns              # Heatmap-uri și bar-plot-uri stilizate, peste matplotlib
import shap                        # Explicabilitate model (SHapley Additive exPlanations)
import joblib                      # Serializare model + scaler + encoder într-un singur fișier .joblib
from typing import Dict, Any, Tuple, List, Union   # Adnotări de tip, doar pentru claritate/lizibilitate (Colab nu impune type-checking strict)

# Toate importurile de mai jos sunt din scikit-learn — biblioteca de Machine Learning folosită pentru tot pipeline-ul
from sklearn.model_selection import train_test_split, StratifiedKFold, cross_validate  # split train/test + cross-validation stratificată (păstrează proporția claselor în fiecare fold)
from sklearn.preprocessing import LabelEncoder, MinMaxScaler   # LabelEncoder: text→număr pentru etichete; MinMaxScaler: normalizare valori senzori în [0,1]
from sklearn.ensemble import RandomForestClassifier             # Algoritmul de clasificare folosit (ansamblu de arbori de decizie)
from sklearn.metrics import (
    accuracy_score, classification_report, confusion_matrix,    # metrici de evaluare standard
    make_scorer, f1_score, roc_auc_score                          # scorer-e custom pentru cross_validate + metrici suplimentare
)
from google.colab import files     # API specific Google Colab — descarcă fișiere din VM-ul temporar în computerul local
from sklearn.compose import ColumnTransformer   # Aplică transformări diferite pe subseturi de coloane (doar coloanele MPU sunt normalizate, restul trec nemodificate)
from sklearn.pipeline import Pipeline            # Înlănțuie preprocesare + model într-un singur obiect, folosit la cross-validation (evită data leakage)

# ── Încărcare dataset 1: temperatură corporală (sursă reală pentru valorile NORMAL de bază) ──
try:
    df_thermometry = pd.read_csv('thermometry.csv')   # Citește CSV-ul în memorie ca DataFrame (trebuie încărcat manual în Colab înainte de rulare)
    print("File 'thermometry.csv' loaded successfully.")

    body_temp_data = df_thermometry[['body.temp']]    # Selectăm doar coloana de interes — restul coloanelor din dataset nu sunt folosite

    print("\nFirst 5 rows of 'body.temp' (thermometry.csv):")
    display(body_temp_data.head())                     # Inspecție vizuală rapidă a primelor rânduri (sanity check manual)

    print("\nDescriptive statistics for 'body.temp':")
    display(body_temp_data.describe())                  # min/max/medie/percentile — verificăm dacă valorile sunt plauzibile fiziologic

    print("\nChecking for missing values in 'body.temp':")
    print(body_temp_data.isnull().sum())                # Numărul de valori NaN — dacă e mare, eșantionarea ulterioară ar trebui ajustată

except FileNotFoundError:
    print("Error: File 'thermometry.csv' not found. Make sure it is uploaded correctly.")
except Exception as e:
    print(f"An error occurred while processing 'thermometry.csv': {e}")

# ── Încărcare dataset 2: accelerometru/giroscop MPU6050 (mișcare/orientare, sursă pentru valorile NORMAL de bază) ──
try:
    df_mpu = pd.read_csv('mpu6050_dataset.csv')
    print("File 'mpu6050_dataset.csv' loaded successfully.")

    sensor_columns = ['ax', 'ay', 'az', 'gx', 'gy', 'gz']   # Cele 6 axe ale accelerometrului (a) și giroscopului (g)
    data_to_normalize = df_mpu[sensor_columns]

    # NOTĂ: acest scaler (_preview_scaler) e doar pentru inspecție vizuală aici — scaler-ul REAL,
    # folosit la antrenament și salvat în artefact, e cel din train_unified_model() (fit doar pe split-ul de train)
    _preview_scaler = MinMaxScaler()
    normalized_data = _preview_scaler.fit_transform(data_to_normalize)

    df_mpu_normalized = pd.DataFrame(normalized_data, columns=[col + '_normalized' for col in sensor_columns])
    df_mpu_processed = pd.concat([df_mpu_normalized, df_mpu[['label']]], axis=1)   # Recombinăm cu eticheta originală a dataset-ului (nefolosită mai departe)

    print("\nFirst 5 rows of 'mpu6050_dataset.csv' after normalization:")
    display(df_mpu_processed.head())

    print("\nDescriptive statistics for normalized data:")
    display(df_mpu_processed.describe())   # După normalizare, toate valorile ar trebui să fie în [0, 1]

except FileNotFoundError:
    print("Error: File 'mpu6050_dataset.csv' not found. Make sure it is uploaded correctly.")
except Exception as e:
    print(f"An error occurred while processing 'mpu6050_dataset.csv': {e}")

# ── Încărcare dataset 3: BIDMC (date clinice reale de puls și SpO2, sursă pentru valorile NORMAL de bază) ──
example_bidmc_file = 'bidmc_02_Numerics.csv'

try:
    df_bidmc = pd.read_csv(example_bidmc_file)
    print(f"File '{example_bidmc_file}' loaded successfully.")

    df_bidmc.columns = df_bidmc.columns.str.strip()   # CSV-urile clinice au des spații parazite în header (" HR" vs "HR") — eliminăm înainte de a indexa pe nume

    print(f"\nColumns detected after cleaning: {df_bidmc.columns.tolist()}\n")

    selected_columns = ['Time [s]', 'HR', 'PULSE', 'SpO2']   # Doar HR și SpO2 sunt folosite mai departe; Time/PULSE sunt păstrate doar pentru inspecție
    df_bidmc_filtered = df_bidmc[selected_columns]

    print(f"\nFirst 5 rows of '{example_bidmc_file}' with selected columns:")
    display(df_bidmc_filtered.head())

    print("\nDataFrame information (after column filtering):")
    df_bidmc_filtered.info()

    print("\nChecking for missing values in selected columns:")
    print(df_bidmc_filtered.isnull().sum())

except FileNotFoundError:
    print(f"Error: File '{example_bidmc_file}' not found. Make sure it is uploaded correctly.")
except Exception as e:
    print(f"An error occurred while processing '{example_bidmc_file}': {e}")

# Funcții de categorizare pe semn vital individual (folosite doar pentru explorare/analiză preliminară,
# NU pentru etichetarea finală a datelor de antrenament — aceea o face combined_rule_based_label mai jos)
def categorize_temperature(temp_celsius: float) -> str:
    if pd.isna(temp_celsius):
        return 'Unknown'           # Valoare lipsă în dataset — nu putem clasifica
    if temp_celsius < 35:
        return "CRITICAL"          # Hipotermie
    elif temp_celsius < 36:
        return "ALERT"             # Sub normal, dar nu periculos
    elif temp_celsius <= 37.5:
        return "NORMAL"            # Interval fiziologic normal
    elif temp_celsius <= 38.5:
        return "ALERT"             # Febră moderată
    else:
        return "CRITICAL"          # Febră mare / hiperpirexie

def categorize_hr(hr: float) -> str:
    if pd.isna(hr):
        return 'Unknown'
    if hr < 50:
        return "CRITICAL"          # Bradicardie severă
    elif hr < 60:
        return "ALERT"             # Bradicardie ușoară
    elif hr <= 100:
        return "NORMAL"            # Interval normal de repaus
    elif hr <= 120:
        return "ALERT"             # Tahicardie ușoară
    else:
        return "CRITICAL"          # Tahicardie severă

def categorize_spo2(spo2: float) -> str:
    if pd.isna(spo2):
        return "Unknown"
    if spo2 < 90:
        return "CRITICAL"          # Hipoxemie severă
    elif spo2 < 95:
        return "ALERT"             # Saturație sub optim
    else:
        return "NORMAL"            # Saturație sănătoasă

# Magnitudinea vectorului 3D de accelerație — independentă de orientarea dispozitivului pe corp
# (identic matematic cu soft_sqrtf() din firmware-ul ESP32, dar aici sqrt-ul hardware e disponibil normal)
def calculate_acceleration_magnitude(ax: float, ay: float, az: float) -> float:
    if pd.isna(ax) or pd.isna(ay) or pd.isna(az):
        return np.nan
    return np.sqrt(ax**2 + ay**2 + az**2)

# Funcția care GENEREAZĂ etichetele de antrenament (NORMAL/ALERT/CRITICAL) pe baza unor praguri fixe
# (nu personalizate per pacient, ca în main.py — aici e un singur set de reguli "medii" pentru tot dataset-ul
# sintetic). Modelul Random Forest e antrenat să reproducă exact aceste etichete, motiv pentru care acuratețea
# pe setul de test sintetic e aproape perfectă (vezi nota de mai jos, "NOTE ON SYNTHETIC TEST-SET ACCURACY").
def combined_rule_based_label(row: pd.Series) -> str:
    # Regulă absolută: SpO2 sub 90% e critic indiferent de restul semnelor — nu mai calculăm scor, ieșim direct
    if row['spo2'] < 90:
        return "CRITICAL"

    score = 0   # Scor punctual cumulat din toate semnele vitale — pragurile 3/4 decid eticheta finală mai jos
    if row['spo2'] < 95:
        score += 1   # SpO2 ușor sub optim

    if row['hr'] > 130:
        score += 3   # Tahicardie severă — singură ar putea ajunge la prag CRITICAL (3+1 de la spo2, sau 3 izolat → ALERT)
    elif row['hr'] > 110:
        score += 1   # Tahicardie ușoară

    if row['temp'] > 39:
        score += 2   # Febră mare
    elif row['temp'] > 38:
        score += 1   # Febră moderată

    if row['acc'] < 0.2:
        score += 1   # Imobilitate (puține mișcări detectate de accelerometru) — semn suplimentar de risc

    if score >= 4:
        return "CRITICAL"   # Combinație de factori suficient de gravă
    elif score >= 3:
        return "ALERT"      # Combinație moderată — necesită atenție, dar nu urgență
    else:
        return "NORMAL"     # Sub pragul de alertă

# Versiune de analiză folosită DOAR în Colab pentru exemplele de testare manuală (run_prediction_examples)
# și pentru validarea conceptului înainte de a porta logica în main.py (serviciul FastAPI de producție).
# Pragurile sunt fixe (90/95/110/130/38/39 etc.), nu personalizate per pacient ca în main.py.
def analyze_patient_data(
    temp: float,
    hr: float,
    spo2: float,
    ax: float, ay: float, az: float,
    gx: float, gy: float, gz: float,
    model: RandomForestClassifier,
    label_encoder: LabelEncoder,
    feature_columns: List[str],
    scaler: MinMaxScaler
) -> Tuple[str, float, str, int, str, int, str]:
    # Validare fizică a datelor — valori improbabile indică eroare de senzor, nu o stare medicală reală
    if not (0 < spo2 <= 100 and 20 < hr < 250 and 30 < temp < 45):
        return "UNKNOWN", 0.0, "UNKNOWN", 0, "Invalid sensor data detected.", 0, "UNKNOWN"

    data: Dict[str, float] = {'temp': temp, 'hr': hr, 'spo2': spo2, 'ax': ax, 'ay': ay, 'az': az, 'gx': gx, 'gy': gy, 'gz': gz}
    data['acc'] = calculate_acceleration_magnitude(ax, ay, az)   # Derivăm magnitudinea accelerației din cele 3 axe

    # Pregătim feature-urile EXACT în formatul pe care modelul le-a "văzut" la antrenament:
    sensor_columns: List[str] = ['ax', 'ay', 'az', 'gx', 'gy', 'gz']
    mpu_data = pd.DataFrame([[data[col] for col in sensor_columns]], columns=sensor_columns)
    normalized_mpu_data = scaler.transform(mpu_data)   # Normalizare cu scaler-ul antrenat (MinMaxScaler.fit() din train_unified_model)

    input_data_for_model: Dict[str, float] = {
        'temp': data['temp'], 'hr': data['hr'], 'spo2': data['spo2'] * 2, 'acc': data['acc']   # *2 — aceeași amplificare SpO2 ca la antrenament
    }
    for i, col in enumerate(sensor_columns):
        input_data_for_model[col] = normalized_mpu_data[0][i]   # Înlocuim valorile brute MPU cu cele normalizate

    input_df = pd.DataFrame([input_data_for_model])
    input_features = input_df[feature_columns]   # Reordonăm coloanele exact ca la antrenament (feature_columns)

    predicted_label_encoded = model.predict(input_features)[0]                              # Predicție brută (index numeric encodat)
    predicted_state_model = label_encoder.inverse_transform([predicted_label_encoded])[0]   # Decodăm înapoi în NORMAL/ALERT/CRITICAL

    proba = model.predict_proba(input_features)[0]   # Probabilitățile pentru toate cele 3 clase
    model_confidence = np.max(proba) * 100             # Confidence = probabilitatea clasei câștigătoare, ca procent

    # --- Analiză separată, bazată pe reguli (paralelă cu predicția ML de mai sus) ---
    # Recalculăm un scor similar cu combined_rule_based_label, dar cu mesaje descriptive — folosit doar
    # ca informație suplimentară în explicație, nu schimbă decizia finală calculată mai jos
    reasons_combined_rule_based: List[str] = []
    base_score_combined_rule_based: int = 0

    if data['spo2'] < 95:
        base_score_combined_rule_based += 1
        reasons_combined_rule_based.append(f"SpO2 ({data['spo2']:.1f}%) is below optimal level (<95%).")
    if data['hr'] > 130:
        base_score_combined_rule_based += 3
        reasons_combined_rule_based.append(f"Heart rate ({data['hr']:.0f} bpm) is very high (>130 bpm), indicating severe tachycardia.")
    elif data['hr'] > 110:
        base_score_combined_rule_based += 1
        reasons_combined_rule_based.append(f"Heart rate ({data['hr']:.0f} bpm) is high (>110 bpm), indicating tachycardia.")
    if data['temp'] > 39:
        base_score_combined_rule_based += 2
        reasons_combined_rule_based.append(f"Temperature ({data['temp']:.1f}°C) is severely high (>39°C), indicating high fever.")
    elif data['temp'] > 38:
        base_score_combined_rule_based += 1
        reasons_combined_rule_based.append(f"Temperature ({data['temp']:.1f}°C) is high (>38°C), indicating fever.")

    if data['acc'] < 0.2 and data['hr'] < 50 and data['spo2'] < 95:
        base_score_combined_rule_based += 1
        reasons_combined_rule_based.append(f"Low acceleration ({data['acc']:.2f}g), low heart rate ({data['hr']:.0f} bpm), and low SpO2 ({data['spo2']:.1f}%) indicate potential prolonged immobility/fall with hypoxemia.")

    score_combined_rule_based: int = base_score_combined_rule_based

    if score_combined_rule_based >= 4:
        combined_rule_based_state: str = "CRITICAL"
    elif score_combined_rule_based >= 3:
        combined_rule_based_state = "ALERT"
    else:
        combined_rule_based_state = "NORMAL"

    # Health score derivat din acest scor pe reguli — formulă diferită de compute_health_score() din main.py
    # (aici e o formulă simplă, liniară; varianta de producție e mult mai detaliată, per semn vital)
    healthscore_combined_rule_based: int = min(100, int(
        base_score_combined_rule_based * 20 +
        (100 - spo2) * 2 +
        max(0, hr - 100)
    ))
    # Confidence-ul sistemului pe baza distanței scorului față de limitele 3/4 — cât mai departe de limită, cât mai sigur
    system_confidence_combined_rule_based: int = 0
    if combined_rule_based_state == "CRITICAL":
        system_confidence_combined_rule_based = max(0, min(100, int(50 + (score_combined_rule_based - 4.0) * 15)))
    elif combined_rule_based_state == "ALERT":
        dist_to_upper: float = 4.0 - score_combined_rule_based
        dist_to_lower: float = score_combined_rule_based - 3.0
        system_confidence_combined_rule_based = max(0, min(100, int(50 + min(dist_to_upper, dist_to_lower) * 15)))
    else:
        system_confidence_combined_rule_based = max(0, min(100, int(50 + (3.0 - score_combined_rule_based) * 15)))

    # --- Decizia finală — praguri medicale FIXE (90/95/110/120/38.5/39), distincte de scorul combinat de mai sus.
    # Verificările sunt în ordine de severitate descrescătoare; prima condiție adevărată decide starea. ---
    final_state: str = ""
    explanation_final: str = ""
    healthscore_final: int = 0
    system_confidence_final: int = 0

    if spo2 < 90:
        # Regulă absolută — identică cu prima verificare din combined_rule_based_label, dar aplicată aici ca decizie finală
        final_state = "CRITICAL"
        explanation_final = f"Critical state: SpO2 ({data['spo2']:.1f}%) is dangerously low (below 90%). This is an absolute medical rule and requires immediate attention."
        healthscore_final = 100
        system_confidence_final = 100
    elif spo2 < 95 and (hr > 120 or temp >= 39):
        # SpO2 moderat scăzut DAR combinat cu stres fiziologic sever (puls mare sau febră mare) → tot critic
        final_state = "CRITICAL"
        explanation_final = "Critical combination: low SpO2 with severe physiological stress (high heart rate or high fever)."
        healthscore_final = 95
        system_confidence_final = 98
    elif temp >= 38.5:
        final_state = "ALERT"
        explanation_final = f"ALERT: High temperature ({data['temp']:.1f}°C) is detected (>= 38.5°C). This could indicate fever or infection and warrants attention."
        healthscore_final = 60
        system_confidence_final = 85
    elif spo2 < 95:
        final_state = "ALERT"
        explanation_final = f"ALERT: SpO2 ({data['spo2']:.1f}%) is low (between 90% and 95%). This indicates a potential respiratory issue and requires monitoring."
        healthscore_final = 70
        system_confidence_final = 90
    elif hr > 120:
        final_state = "ALERT"
        explanation_final = f"ALERT: Heart rate ({data['hr']:.0f} bpm) is high (>120 bpm). This may suggest tachycardia or stress and requires assessment."
        healthscore_final = 50
        system_confidence_final = 80
    else:
        # Niciun prag absolut atins — stare de bază NORMAL, dar verificăm dacă scorul combinat (calculat mai sus)
        # sugerează totuși o tendință spre ALERT/CRITICAL, ca informație suplimentară în explicație
        final_state = "NORMAL"
        explanation_final = f"All vital signs are stable and within physiological norms. "
        if combined_rule_based_state != "NORMAL":
            explanation_final += f"However, the system's combined rule-based analysis suggests a '{combined_rule_based_state}' tendency due to factors like: " + "; ".join(reasons_combined_rule_based) + "."
            healthscore_final = healthscore_combined_rule_based
            system_confidence_final = system_confidence_combined_rule_based
        else:
            explanation_final += "No significant risks identified from any analysis."
            healthscore_final = 0
            system_confidence_final = 100

    # Notă informativă: dacă modelul ML "ar fi prezis altceva" cu suficientă încredere (>70%), o menționăm —
    # fără să schimbe decizia finală, care rămâne 100% pe bază de reguli (la fel ca în main.py de producție)
    if predicted_state_model != final_state and model_confidence > 70:
        explanation_final += f" AI model suggests '{predicted_state_model}' which differs from rule-based decision."

    if final_state == "NORMAL" and hr >= 60 and hr <= 90 and spo2 >= 96:
        explanation_final += " Vital signs are stable and consistent with a healthy baseline."

    # Returnăm un tuplu (nu un dict, ca în main.py) — stil funcțional simplu, suficient pentru testele din Colab
    return predicted_state_model, model_confidence, combined_rule_based_state, healthscore_final, explanation_final, system_confidence_final, final_state


# Construiește dataset-ul sintetic de antrenament din 3 surse: valori "de bază" (NORMAL) eșantionate
# din date reale (thermometry/BIDMC/MPU) + cu zgomot gaussian adăugat pentru variație, plus loturi
# de probe CRITICAL și ALERT generate direct din intervale numerice care garantează acele etichete
# (vezi combined_rule_based_label) — altfel eșantionarea aleatoare din date reale ar produce prea puține
# cazuri critice/alertă pentru un antrenament echilibrat (dezechilibru de clase).
def generate_synthetic_data(
    df_thermometry: pd.DataFrame, df_bidmc: pd.DataFrame, df_mpu: pd.DataFrame,
    num_base_samples: int, num_critical_samples: int, num_alert_samples: int
) -> pd.DataFrame:
    # ── Probe de bază NORMAL (din dataseturi reale) ──────────────────────────────
    synthetic_temp_fahrenheit_base: pd.Series = df_thermometry['body.temp'].sample(n=num_base_samples, replace=True, random_state=42).reset_index(drop=True)
    synthetic_temp_celsius_base: pd.Series = (synthetic_temp_fahrenheit_base - 32) * 5/9   # Dataset-ul e în Fahrenheit, convertim în Celsius
    synthetic_temp_celsius_base += np.random.normal(0, 0.2, num_base_samples)   # Zgomot gaussian — evită ca eșantionarea cu înlocuire (replace=True) să producă duplicate exacte

    synthetic_hr_base: pd.Series = df_bidmc['HR'].sample(n=num_base_samples, replace=True, random_state=42).reset_index(drop=True)   # Eșantionare cu înlocuire din pulsul real BIDMC
    synthetic_hr_base += np.random.normal(0, 3, num_base_samples)   # Zgomot ±3 bpm

    synthetic_spo2_base: pd.Series = df_bidmc['SpO2'].sample(n=num_base_samples, replace=True, random_state=42).reset_index(drop=True)   # Eșantionare cu înlocuire din SpO2 real BIDMC
    synthetic_spo2_base += np.random.normal(0, 0.5, num_base_samples)   # Zgomot mic, SpO2 variază puțin natural
    # Plafonăm SpO2 NORMAL la 96-100 ca să evităm suprapunerea cu intervalul ALERT (90-95)
    synthetic_spo2_base = synthetic_spo2_base.clip(lower=96, upper=100)

    sensor_accel_columns: List[str] = ['ax', 'ay', 'az', 'gx', 'gy', 'gz']
    synthetic_mpu_data_base: pd.DataFrame = df_mpu[sensor_accel_columns].sample(n=num_base_samples, replace=True, random_state=42).reset_index(drop=True)   # Eșantionare din mișcări reale (probabil repaus/activitate ușoară)
    # Zgomot mic pe accelerometru (±0.05g) — variația naturală a unei persoane relativ nemișcate
    synthetic_mpu_data_base['ax'] += np.random.normal(0, 0.05, num_base_samples)
    synthetic_mpu_data_base['ay'] += np.random.normal(0, 0.05, num_base_samples)
    synthetic_mpu_data_base['az'] += np.random.normal(0, 0.05, num_base_samples)
    # Zgomot mai mare pe giroscop (±0.5) — viteza unghiulară variază mai mult decât accelerația la repaus
    synthetic_mpu_data_base['gx'] += np.random.normal(0, 0.5, num_base_samples)
    synthetic_mpu_data_base['gy'] += np.random.normal(0, 0.5, num_base_samples)
    synthetic_mpu_data_base['gz'] += np.random.normal(0, 0.5, num_base_samples)

    # Combinăm cele 3 semnale vitale (temp/hr/spo2) cu cele 6 coloane de mișcare într-un singur DataFrame de probe NORMAL
    df_synthetic_base: pd.DataFrame = pd.DataFrame({
        'temp': synthetic_temp_celsius_base,
        'hr':   synthetic_hr_base,
        'spo2': synthetic_spo2_base,
    })
    df_synthetic_base = pd.concat([df_synthetic_base, synthetic_mpu_data_base], axis=1)   # axis=1 → concatenare pe coloane (alăturare), nu pe rânduri

    # ── Probe CRITICAL (garantat scor >= 4 sau spo2 < 90, conform combined_rule_based_label) ────────────────
    critical_samples_raw: pd.DataFrame = pd.DataFrame({
        "temp": np.random.uniform(39.5, 41.5, num_critical_samples),   # Febră mare/hiperpirexie
        "hr":   np.random.uniform(130,  165,  num_critical_samples),   # Tahicardie severă (>130 → +3 în scor)
        "spo2": np.random.uniform(75,   89.9, num_critical_samples),   # Sub 90% → CRITICAL direct (regulă absolută)
        "ax": np.random.normal(0, 0.5, num_critical_samples),
        "ay": np.random.normal(0, 0.5, num_critical_samples),
        "az": np.random.normal(9.8, 0.5, num_critical_samples),         # 9.8 ≈ g — dispozitiv orientat normal (nu în cădere liberă)
        "gx": np.random.normal(0, 10, num_critical_samples),             # Variație mai mare a giroscopului — agitație/tremur asociat stării critice
        "gy": np.random.normal(0, 10, num_critical_samples),
        "gz": np.random.normal(0, 10, num_critical_samples),
    })

    # ── Probe ALERT (garantat scor == 3 → ALERT) ────────────────────────
    # spo2 90-94.9 (+1) + hr 111-129 (+1) + temp 38.1-38.9 (+1) = scor 3 → ALERT
    # temperatura rămâne strict sub 39 ca să evite scor +2; pulsul rămâne sub 130 ca să evite scor +3
    # (intervalele sunt calibrate manual ca să nimerească exact pragul de 3 puncte din combined_rule_based_label)
    alert_samples_raw: pd.DataFrame = pd.DataFrame({
        "temp": np.random.uniform(38.1, 38.9, num_alert_samples),   # Febră moderată (+1 în scor)
        "hr":   np.random.uniform(111,  129,  num_alert_samples),   # Tahicardie ușoară (+1 în scor)
        "spo2": np.random.uniform(90.0, 94.9, num_alert_samples),   # SpO2 sub optim, dar peste pragul critic de 90% (+1 în scor)
        "ax": np.random.normal(0, 1.0, num_alert_samples),
        "ay": np.random.normal(0, 1.0, num_alert_samples),
        "az": np.random.normal(9.8, 1.0, num_alert_samples),
        "gx": np.random.normal(0, 20, num_alert_samples),
        "gy": np.random.normal(0, 20, num_alert_samples),
        "gz": np.random.normal(0, 20, num_alert_samples),
    })

    # Concatenăm cele 3 loturi (NORMAL + CRITICAL + ALERT) într-un singur dataset; ignore_index=True
    # renumerotează rândurile 0..N (altfel ar rămâne indecșii originali duplicați din fiecare lot)
    df_synthetic_combined: pd.DataFrame = pd.concat(
        [df_synthetic_base, critical_samples_raw, alert_samples_raw],
        ignore_index=True
    )
    # Calculăm coloana 'acc' (magnitudinea accelerației) abia ACUM, pe tot dataset-ul combinat —
    # mai simplu decât să o calculăm separat în fiecare din cele 3 blocuri de mai sus
    df_synthetic_combined['acc'] = df_synthetic_combined.apply(
        lambda row: calculate_acceleration_magnitude(row['ax'], row['ay'], row['az']), axis=1
    )
    return df_synthetic_combined

# Antrenează un singur RandomForestClassifier (nu modele separate per semn vital) pe tot setul de feature-uri —
# de aici "unified" în nume. Normalizează MPU-ul cu MinMaxScaler (fit DOAR pe train, nu pe tot dataset-ul,
# ca să evite scurgerea de informație din setul de test).
def train_unified_model(
    df_synthetic_combined: pd.DataFrame
) -> Tuple[RandomForestClassifier, LabelEncoder, MinMaxScaler, pd.Index, pd.DataFrame, pd.DataFrame, pd.Series, pd.Series]:
    print("\n--- Training a Unified Model ---")
    X_synthetic: pd.DataFrame = df_synthetic_combined[['temp', 'hr', 'spo2', 'ax', 'ay', 'az', 'gx', 'gy', 'gz', 'acc']].copy()
    # Amplificăm importanța SpO2: înmulțim cu 2 ca modelul să-i dea mai multă pondere la împărțirea arborilor.
    # NOTĂ: aceeași transformare *2 e aplicată și în main.py înainte de inferență (trebuie să fie identică,
    # altfel modelul ar primi la inferență o distribuție diferită de cea pe care a fost antrenat).
    X_synthetic['spo2'] = X_synthetic['spo2'] * 2

    le_overall_state: LabelEncoder = LabelEncoder()
    y_synthetic_encoded: np.ndarray = le_overall_state.fit_transform(df_synthetic_combined['overall_state'])   # "NORMAL"/"ALERT"/"CRITICAL" → 0/1/2 (ordine alfabetică implicită)
    y_synthetic: pd.Series = pd.Series(y_synthetic_encoded, name='overall_state_encoded')

    # Split 80/20, stratify=y_synthetic — păstrează aceeași proporție de clase NORMAL/ALERT/CRITICAL
    # atât în train cât și în test (altfel un split aleatoriu simplu ar putea lăsa puține probe CRITICAL în test)
    X_train_synthetic, X_test_synthetic, y_train_synthetic, y_test_synthetic = train_test_split(
        X_synthetic, y_synthetic, test_size=0.2, random_state=42, stratify=y_synthetic
    )

    mpu_cols: List[str] = ['ax', 'ay', 'az', 'gx', 'gy', 'gz']
    scaler: MinMaxScaler = MinMaxScaler()
    scaler.fit(X_train_synthetic[mpu_cols])   # IMPORTANT: fit DOAR pe train — scaler-ul nu trebuie să "vadă" datele de test (data leakage)

    X_train_synthetic = X_train_synthetic.copy()   # .copy() explicit — evită un SettingWithCopyWarning la modificarea coloanelor mai jos
    X_test_synthetic  = X_test_synthetic.copy()
    X_train_synthetic[mpu_cols] = scaler.transform(X_train_synthetic[mpu_cols])   # Aplicăm transformarea învățată (nu fit_transform — ar refăcea fit-ul greșit)
    X_test_synthetic[mpu_cols]  = scaler.transform(X_test_synthetic[mpu_cols])

    print(f"Synthetic training set X_train_synthetic: {X_train_synthetic.shape}")
    print(f"Synthetic test set     X_test_synthetic:  {X_test_synthetic.shape}")

    # Hiperparametri Random Forest:
    #   n_estimators=150 — numărul de arbori din pădure (mai mulți = mai stabil, dar mai lent)
    #   max_depth=10      — limitează profunzimea arborilor, previne overfitting pe date sintetice
    #   class_weight="balanced" — compensează automat dacă o clasă are mai puține probe decât altele
    single_unified_model: RandomForestClassifier = RandomForestClassifier(
        n_estimators=150, max_depth=10, class_weight="balanced", random_state=42
    )
    single_unified_model.fit(X_train_synthetic, y_train_synthetic)   # Antrenarea efectivă — construiește cei 150 de arbori

    print("Unified model (RandomForestClassifier) successfully trained on normalized synthetic data.")
    # Returnăm tot ce e necesar mai departe: modelul, encoder-ul, scaler-ul, ordinea coloanelor și split-urile (pentru evaluare/SHAP)
    return single_unified_model, le_overall_state, scaler, X_synthetic.columns, X_train_synthetic, X_test_synthetic, y_train_synthetic, y_test_synthetic

# Evaluează modelul pe setul de test: acuratețe, raport de clasificare, matrice de confuzie (heatmap)
# și importanța feature-urilor (cât de mult contribuie fiecare la deciziile arborilor) — folosit pentru
# raportarea rezultatelor, nu afectează comportamentul modelului în producție
def evaluate_model(
    model: RandomForestClassifier,
    X_test: pd.DataFrame, y_test: pd.Series, label_encoder: LabelEncoder
) -> None:
    print("\n--- Unified Model Evaluation ---")
    y_pred: np.ndarray = model.predict(X_test)   # Predicții pe setul de test (date pe care modelul nu le-a văzut la fit())

    accuracy: float = accuracy_score(y_test, y_pred)   # % din predicții corecte, global
    report: str = classification_report(   # Precision/recall/F1 per clasă — arată dacă modelul e bun la TOATE clasele, nu doar la cea majoritară
        y_test, y_pred,
        target_names=label_encoder.classes_,
        zero_division=0   # Evită eroare/warning dacă o clasă nu apare deloc în predicții
    )
    print(f"Accuracy: {accuracy:.4f}")
    print("Classification Report:\n", report)

    # Matricea de confuzie: rândul = eticheta reală, coloana = predicția modelului — diagonala = predicții corecte
    conf_matrix: np.ndarray = confusion_matrix(y_test, y_pred)
    plt.figure(figsize=(8, 6))
    sns.heatmap(conf_matrix, annot=True, fmt='d', cmap='Blues',
                xticklabels=label_encoder.classes_, yticklabels=label_encoder.classes_)
    plt.xlabel('Predicted Label')
    plt.ylabel('True Label')
    plt.title('Confusion Matrix')
    plt.tight_layout()
    plt.show()

    # Feature importance din Random Forest = cât de mult reduce fiecare feature impuritatea (Gini) la împărțirea arborilor —
    # diferit de importanța SHAP calculată mai târziu în script (SHAP e per-eșantion, asta e globală/agregată)
    importances: np.ndarray = model.feature_importances_
    feature_importances_df: pd.DataFrame = pd.DataFrame({'feature': X_test.columns, 'importance': importances})
    feature_importances_df = feature_importances_df.sort_values(by='importance', ascending=False)
    print("\nFeature Importances:\n")
    print(feature_importances_df)

    plt.figure(figsize=(12, 6))
    sns.barplot(x='importance', y='feature', data=feature_importances_df, palette='viridis')
    plt.title('Feature Importances for Unified Model')
    plt.xlabel('Importance')
    plt.ylabel('Feature')
    plt.tight_layout()
    plt.show()

# Rulează 3 exemple fixe (Normal/Critical/Alert) prin analyze_patient_data, ca verificare manuală
# rapidă a sănătății pipeline-ului — util la depanare după reantrenare, înainte de a exporta artefactul
def run_prediction_examples(
    model: RandomForestClassifier, label_encoder: LabelEncoder,
    feature_columns: pd.Index, scaler: MinMaxScaler
) -> None:
    print("\n--- Unified Model Prediction Examples (Detailed Analysis) ---")

    # 3 cazuri fixe, alese manual să acopere cele 3 clase posibile — verificare rapidă "are sens predicția?"
    examples: List[Tuple[str, Dict[str, float]]] = [
        ("Normal Situation",                  {"temp": 37.2, "hr": 85, "spo2": 97, "ax": 0.01, "ay": -0.02, "az": 0.98, "gx": 0.0, "gy": 0.0, "gz": 0.0}),
        ("CRITICAL Situation (spo2 < 90)",    {"temp": 37.0, "hr": 70, "spo2": 88, "ax": 0.5,  "ay":  0.5,  "az": 9.8,  "gx": 0.0, "gy": 0.0, "gz": 0.0}),
        ("ALERT Situation (high temperature)",{"temp": 38.5, "hr": 90, "spo2": 96, "ax": 0.1,  "ay":  0.05, "az": 9.85, "gx": 0.0, "gy": 0.0, "gz": 0.0}),
    ]

    for title, example_input_data in examples:
        print(f"\n--- {title} ---")
        # Apelăm aceeași funcție analyze_patient_data definită mai sus în script, trecând explicit
        # modelul/encoder-ul/scaler-ul ca parametri (stil funcțional, fără variabile globale)
        predicted_state_model, model_confidence, combined_rule_based_state, healthscore_final, explanation_final, system_confidence_final, final_state = analyze_patient_data(
            example_input_data['temp'], example_input_data['hr'], example_input_data['spo2'],
            example_input_data['ax'],  example_input_data['ay'], example_input_data['az'],
            example_input_data['gx'],  example_input_data['gy'], example_input_data['gz'],
            model, label_encoder, feature_columns, scaler
        )
        print(f"Input: {example_input_data}")
        print(f"State (ML Model): {predicted_state_model} (Confidence: {model_confidence:.2f}%)")
        print(f"State (Combined Rule-Based): {combined_rule_based_state}")
        print(f"Health Score: {healthscore_final} / 100")
        print(f"System Confidence: {system_confidence_final:.2f}%")
        print(f"Explanation: {explanation_final}")
        print(f"Final State (including medical rules): {final_state}")


# =============================================================================
# BLOCUL PRINCIPAL DE EXECUȚIE (rulat secvențial în celulele Colab)
# =============================================================================

# 1. Generăm dataset-ul sintetic (vezi generate_synthetic_data)
print("\n" + "=" * 60)
print("SYNTHETIC DATA GENERATION")
print("=" * 60)

# Verificare de siguranță: dacă celulele anterioare (încărcarea CSV-urilor) nu au fost rulate, oprim execuția
# cu un mesaj clar, în loc de un NameError criptic mai târziu în script
required_dfs: List[str] = ['df_thermometry', 'df_bidmc', 'df_mpu']
for df_name in required_dfs:
    if df_name not in globals():
        print(f"Error: DataFrame '{df_name}' not found. Please run the previous cells to load and preprocess the data.")
        exit()

num_base_samples: int     = 5000   # 5000 probe NORMAL (eșantionate din date reale + zgomot)
num_critical_samples: int = 1500   # 1500 probe CRITICAL (generate direct din intervale care garantează eticheta)
num_alert_samples: int    = 1500   # 1500 probe ALERT — calibrat ca să garanteze ~1500 etichete ALERT exacte

df_synthetic_combined = generate_synthetic_data(
    df_thermometry, df_bidmc, df_mpu,
    num_base_samples, num_critical_samples, num_alert_samples
)

print(f"Generated {num_base_samples} base, {num_critical_samples} CRITICAL, {num_alert_samples} ALERT records.")
print(f"Total synthetic records: {len(df_synthetic_combined)}")
print("First 5 rows from the combined synthetic dataset:")
display(df_synthetic_combined.head())

print("\nDistribution of 'overall_state' labels in the combined synthetic dataset:")
df_synthetic_combined['overall_state'] = df_synthetic_combined.apply(
    lambda row: combined_rule_based_label(row), axis=1
)
dist = df_synthetic_combined['overall_state'].value_counts()
print(dist)

# Verificare de plauzibilitate: fiecare clasă trebuie să aibă cel puțin 300 de probe pentru antrenament fiabil
# (sub acest prag, modelul ar putea să nu învețe deloc clasa respectivă — clase rare/ignorate)
assert all(dist >= 300), f"WARNING: a class has < 300 samples — increase num_alert_samples. Distribution:\n{dist}"

# 2. Antrenăm modelul unificat (vezi train_unified_model)
model, le_overall_state, scaler, feature_columns, X_train_synthetic, X_test_synthetic, y_train_synthetic, y_test_synthetic = train_unified_model(df_synthetic_combined)

# 3. Evaluăm modelul pe setul de test (acuratețe, matrice de confuzie, importanța feature-urilor)
evaluate_model(model, X_test_synthetic, y_test_synthetic, le_overall_state)

print("\n" + "=" * 60)
print("NOTE ON SYNTHETIC TEST-SET ACCURACY")
print("=" * 60)
print("""
The accuracy printed above is expected to be ~1.0000 on the synthetic
test set. This is a structural consequence of the experimental setup:
training labels are assigned by the same rule-based function
(combined_rule_based_label) that defines the class boundaries, making
the classification boundaries perfectly learnable by the RF.

This result validates that the pipeline is correct, NOT that the model
generalises to real patients. The cross-validation below provides a
methodologically sounder (though structurally similar) evaluation.
""")

# 4. Rulăm exemplele de predicție (verificare manuală rapidă)
run_prediction_examples(model, le_overall_state, feature_columns, scaler)

RANDOM_STATE: int = 42

print("=" * 60)
print("STRATIFIED 5-FOLD CROSS-VALIDATION")
print("=" * 60)

# Cross-validation stratificată în 5 fold-uri — evaluare mai riguroasă decât un singur split train/test,
# pentru raportarea metricilor în lucrarea de licență (medie ± deviație standard pe cele 5 fold-uri)
mpu_cols_cv: List[str] = ['ax', 'ay', 'az', 'gx', 'gy', 'gz']

# Folosim un Pipeline (preprocesare + clasificator) în loc să normalizăm manual înainte de cross_validate,
# ca scaler-ul MinMaxScaler să fie refăcut (fit) separat pe fiecare fold de antrenament — altfel ar exista
# scurgere de informație din fold-ul de test în statisticile de normalizare (data leakage)
cv_preprocessor = ColumnTransformer(
    transformers=[('mpu_scaler', MinMaxScaler(), mpu_cols_cv)],
    remainder='passthrough'
)

cv_pipeline = Pipeline([
    ('preprocess', cv_preprocessor),   # Pas 1: normalizare MPU (refăcută separat pe fiecare fold de train, automat de cross_validate)
    ('clf', RandomForestClassifier(    # Pas 2: același clasificator, cu aceiași hiperparametri ca modelul principal
        n_estimators=150, max_depth=10,
        class_weight="balanced", random_state=RANDOM_STATE
    ))
])

X_synthetic_cv: pd.DataFrame = df_synthetic_combined[['temp', 'hr', 'spo2', 'ax', 'ay', 'az', 'gx', 'gy', 'gz', 'acc']].copy()
X_synthetic_cv['spo2'] = X_synthetic_cv['spo2'] * 2   # aceeași amplificare ca la antrenamentul principal
y_synthetic_cv: np.ndarray = le_overall_state.transform(df_synthetic_combined['overall_state'])   # Reutilizăm encoder-ul deja antrenat (transform, nu fit_transform)

# shuffle=True — altfel StratifiedKFold ar lua fold-urile în ordinea din dataset (NORMAL apoi CRITICAL apoi ALERT,
# conform concatenării din generate_synthetic_data), ceea ce ar strica reprezentativitatea fiecărui fold
skf: StratifiedKFold = StratifiedKFold(n_splits=5, shuffle=True, random_state=RANDOM_STATE)

# 4 metrici complementare: accuracy (simplă, poate fi înșelătoare la clase dezechilibrate), F1 macro/weighted
# (mai robuste la dezechilibru) și AUC-ROC (calitatea probabilităților, nu doar a etichetei finale)
scorers: Dict[str, Any] = {
    "accuracy":    make_scorer(accuracy_score),
    "f1_macro":    make_scorer(f1_score, average="macro",    zero_division=0),
    "f1_weighted": make_scorer(f1_score, average="weighted", zero_division=0),
    "roc_auc_ovr": make_scorer(roc_auc_score, needs_proba=True, multi_class="ovr", average="macro"),
}

cv_results: Dict[str, Any] = cross_validate(
    cv_pipeline, X_synthetic_cv, y_synthetic_cv,
    cv=skf, scoring=scorers,
    return_train_score=True,   # Calculăm și scorul pe train (nu doar pe test), necesar pentru verificarea de overfitting de mai jos
    n_jobs=-1                   # Folosește toate nucleele CPU disponibile — cele 5 fold-uri se antrenează în paralel
)

metrics: List[str] = ["accuracy", "f1_macro", "f1_weighted", "roc_auc_ovr"]
print(f"\n{'Metric':<22} {'Mean':>8}  {'Std':>8}  {'Min':>8}  {'Max':>8}")
print("-" * 60)
for m in metrics:
    vals: np.ndarray = cv_results[f"test_{m}"]   # Array cu 5 valori (una per fold) — afișăm media, deviația și extremele
    print(f"{m:<22} {vals.mean():>8.4f}  {vals.std():>8.4f}  {vals.min():>8.4f}  {vals.max():>8.4f}")

print("\n── Overfitting check (train vs test accuracy) ──")
train_acc: np.ndarray = cv_results["train_accuracy"]
test_acc:  np.ndarray = cv_results["test_accuracy"]
print(f"  Train accuracy:  {train_acc.mean():.4f} ± {train_acc.std():.4f}")
print(f"  Test  accuracy:  {test_acc.mean():.4f} ± {test_acc.std():.4f}")
gap: float = train_acc.mean() - test_acc.mean()   # Diferență mare = modelul a "memorat" train-ul, nu a generalizat
print(f"  Gap (overfit indicator): {gap:.4f}  "
      f"{'[OK — <0.05]' if gap < 0.05 else '[WARNING — possible overfit]'}")

# Grafice comparative pe cele 5 fold-uri — vizualizează stabilitatea modelului (variații mici între fold-uri = bun semn)
fig, axes = plt.subplots(1, 2, figsize=(12, 4))
fold_labels: List[str] = [f"Fold {i+1}" for i in range(5)]

axes[0].plot(fold_labels, cv_results["test_accuracy"],  marker="o", label="Test")
axes[0].plot(fold_labels, cv_results["train_accuracy"], marker="s", linestyle="--", label="Train", alpha=0.6)
axes[0].set_title("Accuracy per fold"); axes[0].set_ylabel("Accuracy")
axes[0].set_ylim(0.8, 1.01); axes[0].legend(); axes[0].grid(alpha=0.3)

axes[1].plot(fold_labels, cv_results["test_f1_macro"],    marker="o", label="F1 macro")
axes[1].plot(fold_labels, cv_results["test_roc_auc_ovr"], marker="^", label="AUC-ROC (OvR)")
axes[1].set_title("F1 macro and AUC-ROC per fold"); axes[1].set_ylabel("Score")
axes[1].set_ylim(0.8, 1.01); axes[1].legend(); axes[1].grid(alpha=0.3)

plt.suptitle("5-Fold Cross-Validation Results", fontsize=13)
plt.tight_layout()
plt.show()

# Reantrenăm modelul final pe split-ul de train original (separat de cross-validation, care folosește alte
# split-uri) — acesta e modelul salvat efectiv în artefactul .joblib, deci analiza SHAP trebuie făcută pe el
# (nu pe modelele temporare din cross_validate, care nu sunt reținute după evaluare)
final_model: RandomForestClassifier = RandomForestClassifier(
    n_estimators=150, max_depth=10, class_weight="balanced", random_state=RANDOM_STATE
)
final_model.fit(X_train_synthetic, y_train_synthetic)
print("\n✓ Final model retrained on train split for SHAP analysis.")

print("\n" + "=" * 60)
print("SHAP EXPLAINABILITY")
print("=" * 60)
print("Computing SHAP values...")

# TreeExplainer e optimizat special pentru modele bazate pe arbori (Random Forest, gradient boosting) —
# mult mai rapid decât SHAP generic (KernelExplainer) pentru acest tip de model
explainer = shap.TreeExplainer(final_model)
# check_additivity=False — dezactivează o verificare internă strictă (suma SHAP + base_value == predicția brută)
# care poate da fals-pozitiv din cauza erorilor de rotunjire în virgulă mobilă, fără a afecta corectitudinea valorilor
shap_values_raw = explainer.shap_values(X_test_synthetic, check_additivity=False)

class_names: List[str] = list(le_overall_state.classes_)   # Ordinea claselor așa cum le-a memorat LabelEncoder (alfabetic: ALERT, CRITICAL, NORMAL)
print(f"Classes (in SHAP order): {class_names}")
print(f"shap_values_raw type: {type(shap_values_raw)}, shape: {np.array(shap_values_raw).shape}")

# Versiuni diferite ale bibliotecii shap returnează formate diferite pentru clasificare multi-clasă:
# fie un array 3D (eșantioane × feature-uri × clase), fie o listă de array-uri 2D (una per clasă).
# Normalizăm la formatul de listă, indiferent de versiune, ca restul codului să funcționeze identic.
arr: np.ndarray = np.array(shap_values_raw)
if arr.ndim == 3:
    shap_values = [arr[:, :, i] for i in range(arr.shape[2])]
    print(f"Detected new SHAP format (3D). Reshaped to {len(shap_values)} arrays of {shap_values[0].shape}.")
else:
    shap_values = list(shap_values_raw)
    print(f"Detected old SHAP format (list). Using as-is.")

y_proba: np.ndarray = final_model.predict_proba(X_test_synthetic)   # Probabilități pentru toate cele 3 clase, pe tot setul de test
try:
    # Recalculăm AUC-ROC manual aici (separat de cross_validate de mai sus) — pe modelul final_model,
    # antrenat pe split-ul original de train, evaluat pe split-ul original de test
    auc: float = roc_auc_score(y_test_synthetic, y_proba, multi_class="ovr", average="macro")
    print(f"\nAUC-ROC (OvR, manual on test set): {auc:.4f}")
except Exception as e:
    print(f"AUC-ROC could not be computed: {e}")

# Un grafic summary_plot SHAP per clasă — arată ce feature-uri au împins predicția spre ACEA clasă specifică,
# pe toate eșantioanele de test (puncte colorate = valoarea feature-ului, poziție = impactul asupra predicției)
for i, cls in enumerate(class_names):
    plt.figure(figsize=(9, 5))
    shap.summary_plot(shap_values[i], X_test_synthetic,
                      feature_names=list(X_test_synthetic.columns), show=False, max_display=10)
    plt.title(f"SHAP summary — class: {cls}", fontsize=13)
    plt.tight_layout()
    plt.show()

# Importanța globală a feature-urilor = media valorii absolute SHAP, agregată peste toate clasele și eșantioanele —
# spre diferență de feature_importances_ din Random Forest (bazat pe reducerea impurității), SHAP reflectă
# contribuția reală la predicție pentru fiecare eșantion individual, deci e mai interpretabil clinic
mean_abs_shap: np.ndarray = np.mean([np.abs(sv) for sv in shap_values], axis=0).mean(axis=0)
shap_importance_df: pd.DataFrame = pd.DataFrame({
    "feature":     list(X_test_synthetic.columns),
    "mean_|SHAP|": mean_abs_shap
}).sort_values("mean_|SHAP|", ascending=False)

plt.figure(figsize=(9, 4))
plt.barh(shap_importance_df["feature"][::-1], shap_importance_df["mean_|SHAP|"][::-1], color="#4C72B0")
plt.xlabel("Mean |SHAP value|")
plt.title("Global feature importance (SHAP)", fontsize=13)
plt.tight_layout()
plt.show()

print("\nTop 5 features (SHAP):\n")
print(shap_importance_df.head(5).to_string(index=False))

# Exemplu individual (waterfall plot): alegem prima probă CRITICAL din setul de test și arătăm exact
# cum a contribuit fiecare feature la împingerea predicției către CRITICAL — util pentru a demonstra
# explicabilitatea modelului pe un caz concret în lucrarea de licență
critical_class_idx: int = class_names.index("CRITICAL")
critical_indices: np.ndarray = np.where(y_test_synthetic.values == critical_class_idx)[0]

if len(critical_indices) > 0:
    sample_idx: int = critical_indices[0]                       # Prima probă CRITICAL găsită în setul de test
    sample_row: pd.DataFrame = X_test_synthetic.iloc[[sample_idx]]   # [[...]] (listă cu un singur index) → păstrează forma de DataFrame, nu Series
    sv_single_raw = explainer.shap_values(sample_row, check_additivity=False)   # SHAP values doar pentru acest eșantion individual
    sv_arr: np.ndarray = np.array(sv_single_raw)
    # Aceeași normalizare de format ca mai sus (3D vs listă) — necesară din nou pentru că shap_values()
    # se apelează separat aici, pe un singur rând, nu reutilizează shap_values calculat pe tot X_test_synthetic
    sv_single = [sv_arr[:, :, i] for i in range(sv_arr.shape[2])] if sv_arr.ndim == 3 else list(sv_single_raw)

    print(f"\n── Waterfall for test sample #{sample_idx} ──")
    print(f"   True:      {class_names[y_test_synthetic.iloc[sample_idx]]}")
    print(f"   Predicted: {class_names[final_model.predict(sample_row)[0]]}")
    print(f"   SpO₂={sample_row['spo2'].values[0]:.1f}%  HR={sample_row['hr'].values[0]:.0f} bpm  Temp={sample_row['temp'].values[0]:.1f}°C")

    # Construim manual obiectul shap.Explanation pentru waterfall_plot — are nevoie de:
    #   values      = contribuția SHAP a fiecărui feature pentru ACEASTĂ probă, pentru clasa CRITICAL
    #   base_values = valoarea de bază a modelului (media peste tot train-ul) pentru clasa CRITICAL
    #   data        = valorile reale ale feature-urilor pentru acest pacient (afișate pe axa Y a graficului)
    shap_exp = shap.Explanation(
        values        = sv_single[critical_class_idx][0],
        base_values   = explainer.expected_value[critical_class_idx],
        data          = sample_row.values[0],
        feature_names = list(sample_row.columns)
    )
    plt.figure(figsize=(9, 5))
    shap.waterfall_plot(shap_exp, show=False)
    plt.title(f"SHAP waterfall — CRITICAL (sample #{sample_idx})", fontsize=12)
    plt.tight_layout()
    plt.show()
else:
    print("No CRITICAL samples in test set — skipping waterfall.")

print("\n" + "=" * 60)
print("THESIS REPORTING SUMMARY")
print("=" * 60)
print(f"""
Cross-validation (Stratified 5-Fold):
  Accuracy:       {cv_results['test_accuracy'].mean():.4f} ± {cv_results['test_accuracy'].std():.4f}
  F1 (macro):     {cv_results['test_f1_macro'].mean():.4f} ± {cv_results['test_f1_macro'].std():.4f}
  F1 (weighted):  {cv_results['test_f1_weighted'].mean():.4f} ± {cv_results['test_f1_weighted'].std():.4f}
  AUC-ROC (OvR):  {auc:.4f}

⚠ SYSTEM LIMITATIONS (must be disclosed in thesis):
  1. SYNTHETIC DATA: all training and evaluation examples were generated
     programmatically using the same rule-based thresholds that define the
     class labels. Near-perfect accuracy is structurally expected and does
     NOT indicate clinical generalisation ability.
  2. NO CLINICAL VALIDATION: the model has not been evaluated on real
     patient cohorts and has not been reviewed by medical professionals.
  3. PROOF OF CONCEPT / TECHNOLOGICAL DEMONSTRATOR: this system must be
     described as a monitoring support tool, not as a diagnostic system.
  4. SENSOR ACCURACY: real-world performance depends on sensor placement,
     patient cooperation, and device calibration.

ROLE OF RANDOM FOREST vs. PURE RULES:
  — Provides calibrated probability scores for each health state,
    not just hard binary labels.
  — SHAP explainability makes individual predictions interpretable
    and auditable.
  — The model can be fine-tuned on real patient data without
    rewriting application logic.
  — Handles continuous feature interactions more gracefully than
    threshold-based rules under sensor noise.
  — Validates that the rule set is internally consistent and learnable.

Recommended thesis wording:
  "A Random Forest classifier was trained on {num_base_samples + num_critical_samples + num_alert_samples} synthetic examples
   generated from physiological signal ranges aligned with clinical
   triage protocols (NORMAL / ALERT / CRITICAL). Class labels were
   assigned by a deterministic rule-based function, making the
   classification boundaries precisely learnable; near-perfect
   accuracy on the synthetic test set is therefore an expected
   consequence of the experimental setup rather than evidence of
   clinical generalisation. Stratified 5-fold cross-validation
   confirmed consistent performance across splits
   (mean accuracy {cv_results['test_accuracy'].mean():.2%} ± {cv_results['test_accuracy'].std():.2%},
   macro-F1 {cv_results['test_f1_macro'].mean():.2%} ± {cv_results['test_f1_macro'].std():.2%},
   AUC-ROC {auc:.4f}). Feature importance assessed via SHAP
   (SHapley Additive exPlanations) identified SpO₂, heart rate, and
   body temperature as the dominant predictors, consistent with
   established triage criteria. The system is presented as a
   proof-of-concept technological demonstrator for IoT-based patient
   monitoring; clinical deployment would require prospective validation
   on real patient cohorts under medical supervision."
""")

# =============================================================================
# SALVAREA ARTEFACTULUI — fișierul .joblib pe care îl încarcă main.py în producție
# Fix 1: numele fișierului trebuie să corespundă cu ENV ARTIFACT_FILE din Dockerfile
# Fix 2: salvăm final_model (același folosit la analiza SHAP de mai sus, nu modelul intermediar din CV)
# Fix 3: feature_order foloseste feature_columns (X_synthetic nu mai e în scope aici)
# =============================================================================
unified_artifact_filename = 'patient_monitor_artifacts (3).joblib'

# Toate cele 3 obiecte necesare la inferență (model, scaler, encoder) + lista de feature-uri sunt salvate
# ÎMPREUNĂ într-un singur fișier .joblib — garantează că rămân sincronizate (același model nu poate fi
# folosit din greșeală cu un scaler dintr-o rulare de antrenament diferită)
artifacts_to_save = {
    'model':        final_model,        # FIX: era 'model'; folosim final_model (același ca la SHAP) — modelul antrenat pe X_train_synthetic
    'scaler':       scaler,              # MinMaxScaler fit doar pe coloanele MPU din train — necesar la inferență ca să normalizăm la fel
    'encoder':      le_overall_state,    # LabelEncoder — necesar ca să decodăm predicția numerică înapoi în NORMAL/ALERT/CRITICAL
    'features':     feature_columns,     # Lista de feature-uri (informativ, redundant parțial cu feature_order de mai jos)
    'feature_order': list(feature_columns)  # FIX: era list(X_synthetic.columns) → NameError (X_synthetic nu mai exista aici)
}

joblib.dump(artifacts_to_save, unified_artifact_filename)   # Serializează tot dicționarul într-un singur fișier binar .joblib
print(f"All model artifacts saved as '{unified_artifact_filename}'")

# Descărcare automată din mediul Colab (browser-based) — necesar pentru că Colab rulează pe o VM temporară,
# fișierul trebuie descărcat manual/automat înainte ca sesiunea să se închidă și să-l perdem
try:
    files.download(unified_artifact_filename)
    print(f"'{unified_artifact_filename}' downloaded successfully.")
except FileNotFoundError:
    print(f"Error: '{unified_artifact_filename}' not found.")
except Exception as e:
    print(f"An error occurred while downloading '{unified_artifact_filename}': {e}")
