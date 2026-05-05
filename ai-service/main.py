from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel
from typing import Optional, List, Dict
import joblib
import numpy as np
import pandas as pd
import os
import logging

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

app = FastAPI(title="LifeAlertPlus AI Service", version="1.0.0")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

MODEL_DIR = os.environ.get("MODEL_DIR", os.path.join(os.path.dirname(__file__), "..", "LifeAlertPlus", "LifeAlertPlus.API", "AIFiles"))
ARTIFACT_FILE = os.environ.get("ARTIFACT_FILE", "patient_monitor_artifacts (3).joblib")

model = None
scaler = None
label_encoder = None
scaler_feature_names: List[str] = []
model_feature_names: List[str] = []
feature_order: List[str] = []


def load_models():
    global model, scaler, label_encoder, scaler_feature_names, model_feature_names, feature_order
    try:
        artifact_path = os.path.join(MODEL_DIR, ARTIFACT_FILE)
        artifacts = joblib.load(artifact_path)

        model = artifacts["model"]
        scaler = artifacts["scaler"]
        label_encoder = artifacts["encoder"]
        feature_order = artifacts.get("feature_order", list(artifacts.get("features", [])))

        logger.info("AI artifacts loaded from %s", ARTIFACT_FILE)
        logger.info(f"Label encoder classes: {list(label_encoder.classes_)}")
        logger.info(f"Feature order: {feature_order}")

        if hasattr(scaler, "feature_names_in_"):
            scaler_feature_names = list(scaler.feature_names_in_)
            logger.info(f"Scaler features ({len(scaler_feature_names)}): {scaler_feature_names}")

        if hasattr(model, "feature_names_in_"):
            model_feature_names = list(model.feature_names_in_)
            logger.info(f"Model features ({len(model_feature_names)}): {model_feature_names}")

    except Exception as e:
        logger.error(f"Error loading models: {e}")
        raise


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
    """Compute a 0..100 danger score where higher = worse.

    Uses patient-specific thresholds when available, falling back to
    evidence-based clinical defaults.
    """
    score = 0.0
    conds = conditions or []

    # --- Effective thresholds (personalized with clinical fallback) ---
    eff_max_hr   = max_hr   if max_hr   else 130
    eff_min_hr   = min_hr   if min_hr   else 50
    eff_max_temp = max_temp if max_temp else 39.0
    eff_min_temp = min_temp if min_temp else 35.0
    eff_min_spo2 = min_spo2 if min_spo2 else 95
    eff_crit_spo2 = max(70, eff_min_spo2 - 5)

    hr_warn_high   = int(eff_max_hr  * 0.85)
    hr_warn_low    = int(eff_min_hr  * 1.20)
    temp_warn_high = eff_max_temp - 1.0
    temp_warn_low  = eff_min_temp + 1.0

    # --- SpO2 (personalized thresholds) ---
    if spo2 < eff_crit_spo2:
        score += 60
    elif spo2 < eff_min_spo2:
        score += 30 + (eff_min_spo2 - spo2) * 3

    # --- Heart rate (relative to patient thresholds) ---
    if hr > eff_max_hr:
        score += 25
    elif hr > hr_warn_high:
        score += 12
    elif 0 < hr < eff_min_hr:
        score += 20
    elif 0 < hr < hr_warn_low:
        score += 10

    # --- Temperature (relative to patient thresholds) ---
    if temp > eff_max_temp:
        score += 20
    elif temp > temp_warn_high:
        score += 10
    elif 0 < temp < eff_min_temp:
        score += 20
    elif 0 < temp < temp_warn_low:
        score += 10

    # --- Immobility / fall suspicion ---
    if acc < 0.2:
        score += 8

    # --- Condition-specific penalty modifiers (non-SpO2; SpO2 handled via personalized thresholds) ---
    for cond in conds:
        if cond == "hypertension":
            if hr > 100:
                score += 8
        elif cond == "arrhythmia":
            if hr > 110 or (0 < hr < 52):
                score += 10
        elif cond == "mi_risk":
            if hr > 110 and temp > 37.8:
                score += 12
        elif cond == "diabetes":
            if temp > 38.0:
                score += 8
        elif cond in ("parkinson", "epilepsy"):
            if acc < 0.5:
                score += 5

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
    Analyzes patient data using both ML model and rule-based logic.
    Respects patient-specific thresholds and medical conditions.
    Returns a dict with all prediction fields.
    """
    conds = conditions or []

    # Validate sensor data
    if not (0 < spo2 <= 100 and 20 < hr < 250 and 30 < temp < 45):
        return {
            "prediction": "UNKNOWN",
            "confidence": 0.0,
            "risk_level": "UNKNOWN",
            "details": "Invalid sensor data detected.",
            "health_score": 0,
            "all_probabilities": {},
        }

    data: Dict[str, float] = {
        "temp": temp, "hr": hr, "spo2": spo2,
        "ax": ax, "ay": ay, "az": az,
        "gx": gx, "gy": gy, "gz": gz,
    }
    data["acc"] = calculate_acceleration_magnitude(ax, ay, az)

    # --- Effective patient thresholds ---
    eff_max_hr    = max_hr   if max_hr   else 130
    eff_min_hr    = min_hr   if min_hr   else 50
    eff_max_temp  = max_temp if max_temp else 39.0
    eff_min_temp  = min_temp if min_temp else 35.0
    eff_min_spo2  = min_spo2 if min_spo2 else 95
    eff_crit_spo2 = max(70, eff_min_spo2 - 5)

    hr_alert_high   = int(eff_max_hr * 0.85)
    hr_alert_low    = int(eff_min_hr * 1.20)
    temp_alert_high = eff_max_temp - 0.5

    # --- ML prediction ---
    sensor_columns = ["ax", "ay", "az", "gx", "gy", "gz"]
    mpu_data = pd.DataFrame([[data[col] for col in sensor_columns]], columns=sensor_columns)
    normalized_mpu_data = scaler.transform(mpu_data)

    input_data_for_model: Dict[str, float] = {
        "temp": data["temp"], "hr": data["hr"], "spo2": data["spo2"] * 2, "acc": data["acc"]
    }
    for i, col in enumerate(sensor_columns):
        input_data_for_model[col] = normalized_mpu_data[0][i]

    input_df = pd.DataFrame([input_data_for_model])
    cols = feature_order if feature_order else model_feature_names
    input_features = input_df[cols]

    predicted_label_encoded = model.predict(input_features)[0]
    predicted_state_model = label_encoder.inverse_transform([predicted_label_encoded])[0]

    proba = model.predict_proba(input_features)[0]
    model_confidence = float(np.max(proba)) * 100

    # Penalize ML when it violates clear medical rules
    if spo2 < eff_crit_spo2 and predicted_state_model != "CRITICAL":
        predicted_state_model = "CRITICAL"
        model_confidence = 100.0
    elif spo2 < eff_min_spo2 and predicted_state_model == "NORMAL":
        predicted_state_model = "ALERT"
        model_confidence *= 0.5

    probabilities: Dict[str, float] = {}
    for i, cls in enumerate(label_encoder.classes_):
        probabilities[cls] = round(float(proba[i]), 4)

    if spo2 < eff_crit_spo2:
        probabilities = {"CRITICAL": 1.0, "ALERT": 0.0, "NORMAL": 0.0}
    elif spo2 < eff_min_spo2:
        normal_prob = probabilities.get("NORMAL", 0.0)
        probabilities["NORMAL"] = 0.0
        probabilities["ALERT"] = min(1.0, round(probabilities.get("ALERT", 0.0) + normal_prob * 0.7, 4))
        probabilities["CRITICAL"] = min(1.0, round(probabilities.get("CRITICAL", 0.0) + normal_prob * 0.3, 4))

    # --- Combined rule-based analysis ---
    reasons: List[str] = []
    base_score: int = 0

    if data["spo2"] < eff_min_spo2:
        base_score += 1
        reasons.append(f"SpO2 ({data['spo2']:.1f}%) is below patient threshold (<{eff_min_spo2:.0f}%).")
    if data["hr"] > hr_alert_high + 20:
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
    if data["acc"] < 0.2 and data["hr"] < eff_min_hr and data["spo2"] < eff_min_spo2:
        base_score += 1
        reasons.append(f"Low acceleration, low heart rate and low SpO2 indicate potential immobility/fall.")

    if base_score >= 4:
        combined_rule_state = "CRITICAL"
    elif base_score >= 3:
        combined_rule_state = "ALERT"
    else:
        combined_rule_state = "NORMAL"

    health_score = compute_health_score(
        temp=temp, hr=hr, spo2=spo2, acc=data["acc"],
        conditions=conds,
        max_hr=max_hr, min_hr=min_hr,
        max_temp=max_temp, min_temp=min_temp,
        min_spo2=min_spo2,
    )

    system_confidence_combined: int = 0
    if combined_rule_state == "CRITICAL":
        system_confidence_combined = max(0, min(100, int(50 + (base_score - 4.0) * 15)))
    elif combined_rule_state == "ALERT":
        dist_up = 4.0 - base_score
        dist_down = base_score - 3.0
        system_confidence_combined = max(0, min(100, int(50 + min(dist_up, dist_down) * 15)))
    else:
        system_confidence_combined = max(0, min(100, int(50 + (3.0 - base_score) * 15)))

    # --- Final state: medical rules (patient-threshold-aware) ---
    final_state = ""
    explanation = ""
    system_confidence = 0

    if spo2 < eff_crit_spo2:
        final_state = "CRITICAL"
        explanation = f"Critical: SpO2 ({data['spo2']:.1f}%) is dangerously low (threshold: <{eff_crit_spo2:.0f}%). Requires immediate attention."
        system_confidence = 100
    elif spo2 < eff_min_spo2 and (hr > eff_max_hr or temp >= eff_max_temp):
        final_state = "CRITICAL"
        explanation = f"Critical combination: SpO2 ({data['spo2']:.1f}%) below patient threshold with severe physiological stress (high HR or high fever)."
        system_confidence = 98
    elif temp >= temp_alert_high:
        final_state = "ALERT"
        explanation = f"ALERT: Temperature ({data['temp']:.1f}°C) exceeds patient alert threshold ({temp_alert_high:.1f}°C)."
        system_confidence = 85
    elif spo2 < eff_min_spo2:
        final_state = "ALERT"
        explanation = f"ALERT: SpO2 ({data['spo2']:.1f}%) is below patient threshold ({eff_crit_spo2:.0f}–{eff_min_spo2:.0f}%). Potential respiratory issue."
        system_confidence = 90
    elif hr > hr_alert_high:
        final_state = "ALERT"
        explanation = f"ALERT: Heart rate ({data['hr']:.0f} bpm) exceeds patient alert threshold ({hr_alert_high} bpm)."
        system_confidence = 80
    elif 0 < hr < hr_alert_low:
        final_state = "ALERT"
        explanation = f"ALERT: Heart rate ({data['hr']:.0f} bpm) is below patient alert threshold ({hr_alert_low} bpm)."
        system_confidence = 80
    else:
        final_state = "NORMAL"
        explanation = "All vital signs are stable and within the patient's defined thresholds. "
        if combined_rule_state != "NORMAL":
            explanation += f"Combined rule analysis suggests '{combined_rule_state}' tendency: " + "; ".join(reasons) + "."
            system_confidence = system_confidence_combined
        else:
            explanation += "No significant risks identified."
            system_confidence = 100

    # --- Condition-specific final-state overrides (HR/temp; SpO2 handled by personalized thresholds) ---
    condition_notes: List[str] = []
    for cond in conds:
        if cond == "arrhythmia" and (hr > 110 or (0 < hr < 52)) and final_state == "NORMAL":
            final_state = "ALERT"
            system_confidence = max(system_confidence, 80)
            condition_notes.append(f"[arrhythmia] Heart rate ({hr:.0f} bpm) is abnormal for this patient.")
        elif cond == "mi_risk" and hr > 110 and temp > 37.8 and final_state != "CRITICAL":
            final_state = "ALERT"
            system_confidence = max(system_confidence, 85)
            condition_notes.append(f"[mi_risk] Cardiac stress markers detected (HR {hr:.0f} bpm, Temp {temp:.1f}°C).")

    if condition_notes:
        explanation += " " + " ".join(condition_notes)

    if predicted_state_model != final_state and model_confidence > 70:
        explanation += f" AI model suggests '{predicted_state_model}' (differs from rule-based decision)."

    if final_state == "NORMAL" and 60 <= hr <= 90 and spo2 >= 96:
        explanation += " Vital signs are consistent with a healthy baseline."

    return {
        "prediction": final_state,
        "confidence": round(system_confidence / 100.0, 4),
        "risk_level": final_state,
        "details": explanation,
        "health_score": health_score,
        "all_probabilities": probabilities,
    }


class PredictionRequest(BaseModel):
    pulse: float
    temperature: float
    spo2: Optional[float] = 97.0
    accel_x: Optional[float] = 0.0
    accel_y: Optional[float] = 0.0
    accel_z: Optional[float] = 0.0
    gyro_x: Optional[float] = 0.0
    gyro_y: Optional[float] = 0.0
    gyro_z: Optional[float] = 0.0
    conditions: Optional[List[str]] = []
    max_heart_rate: Optional[int] = None
    min_heart_rate: Optional[int] = None
    max_temperature: Optional[float] = None
    min_temperature: Optional[float] = None
    min_spo2: Optional[int] = None
    max_spo2: Optional[int] = None


class PredictionResponse(BaseModel):
    prediction: str
    confidence: float
    risk_level: str
    details: str
    health_score: int
    all_probabilities: dict


@app.on_event("startup")
async def startup_event():
    load_models()


@app.get("/health")
async def health():
    return {
        "status": "healthy",
        "model_loaded": model is not None,
        "scaler_loaded": scaler is not None,
        "encoder_loaded": label_encoder is not None,
    }


@app.get("/model-info")
async def model_info():
    info = {
        "classes": list(label_encoder.classes_) if label_encoder else [],
        "n_features": getattr(model, "n_features_in_", None),
        "feature_names": list(model.feature_names_in_) if hasattr(model, "feature_names_in_") else None,
        "model_type": type(model).__name__ if model else None,
        "scaler_features": scaler_feature_names,
        "scaler_n_features": getattr(scaler, "n_features_in_", None) if scaler else None,
        "feature_order": feature_order,
    }
    return info


@app.post("/predict", response_model=PredictionResponse)
async def predict(request: PredictionRequest):
    if model is None or scaler is None or label_encoder is None:
        raise HTTPException(status_code=503, detail="Models not loaded")

    try:
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

        return PredictionResponse(**result)

    except Exception as e:
        logger.error(f"Prediction error: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Prediction failed: {str(e)}")
