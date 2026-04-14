from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel
from typing import Optional, List, Dict, Tuple
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


def analyze_patient_data(
    temp: float, hr: float, spo2: float,
    ax: float, ay: float, az: float,
    gx: float, gy: float, gz: float,
) -> Dict:
    """
    Analyzes patient data using both ML model and rule-based logic.
    Returns a dict with all prediction fields.
    """
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

    # --- Penalize ML when it violates clear medical rules ---
    if spo2 < 90 and predicted_state_model != "CRITICAL":
        predicted_state_model = "CRITICAL"
        model_confidence = 100.0
    elif spo2 < 95 and predicted_state_model == "NORMAL":
        predicted_state_model = "ALERT"
        model_confidence *= 0.5

    probabilities: Dict[str, float] = {}
    for i, cls in enumerate(label_encoder.classes_):
        probabilities[cls] = round(float(proba[i]), 4)

    # Override probabilities when ML contradicts clear medical rules
    if spo2 < 90:
        probabilities = {"CRITICAL": 1.0, "ALERT": 0.0, "NORMAL": 0.0}
    elif spo2 < 95:
        # SpO2 < 95 is never NORMAL — redistribute probability
        normal_prob = probabilities.get("NORMAL", 0.0)
        probabilities["NORMAL"] = 0.0
        probabilities["ALERT"] = min(1.0, round(probabilities.get("ALERT", 0.0) + normal_prob * 0.7, 4))
        probabilities["CRITICAL"] = min(1.0, round(probabilities.get("CRITICAL", 0.0) + normal_prob * 0.3, 4))

    # --- Combined rule-based analysis ---
    reasons: List[str] = []
    base_score: int = 0

    if data["spo2"] < 95:
        base_score += 1
        reasons.append(f"SpO2 ({data['spo2']:.1f}%) is below optimal level (<95%).")
    if data["hr"] > 130:
        base_score += 3
        reasons.append(f"Heart rate ({data['hr']:.0f} bpm) is very high (>130 bpm), indicating severe tachycardia.")
    elif data["hr"] > 110:
        base_score += 1
        reasons.append(f"Heart rate ({data['hr']:.0f} bpm) is high (>110 bpm), indicating tachycardia.")
    if data["temp"] > 39:
        base_score += 2
        reasons.append(f"Temperature ({data['temp']:.1f}°C) is severely high (>39°C), indicating high fever.")
    elif data["temp"] > 38:
        base_score += 1
        reasons.append(f"Temperature ({data['temp']:.1f}°C) is high (>38°C), indicating fever.")
    if data["acc"] < 0.2 and data["hr"] < 50 and data["spo2"] < 95:
        base_score += 1
        reasons.append(f"Low acceleration ({data['acc']:.2f}g), low heart rate ({data['hr']:.0f} bpm), and low SpO2 ({data['spo2']:.1f}%) indicate potential prolonged immobility/fall with hypoxemia.")

    if base_score >= 4:
        combined_rule_state = "CRITICAL"
    elif base_score >= 3:
        combined_rule_state = "ALERT"
    else:
        combined_rule_state = "NORMAL"

    healthscore_combined = min(100, int(
        base_score * 20 + (100 - spo2) * 2 + max(0, hr - 100)
    ))

    system_confidence_combined: int = 0
    if combined_rule_state == "CRITICAL":
        system_confidence_combined = max(0, min(100, int(50 + (base_score - 4.0) * 15)))
    elif combined_rule_state == "ALERT":
        dist_up = 4.0 - base_score
        dist_down = base_score - 3.0
        system_confidence_combined = max(0, min(100, int(50 + min(dist_up, dist_down) * 15)))
    else:
        system_confidence_combined = max(0, min(100, int(50 + (3.0 - base_score) * 15)))

    # --- Final state: medical rules override ---
    final_state = ""
    explanation = ""
    health_score = 0
    system_confidence = 0

    if spo2 < 90:
        final_state = "CRITICAL"
        explanation = f"Critical state: SpO2 ({data['spo2']:.1f}%) is dangerously low (below 90%). This is an absolute medical rule and requires immediate attention."
        health_score = 100
        system_confidence = 100
    elif spo2 < 95 and (hr > 120 or temp >= 39):
        final_state = "CRITICAL"
        explanation = "Critical combination: low SpO2 with severe physiological stress (high heart rate or high fever)."
        health_score = 95
        system_confidence = 98
    elif temp >= 38.5:
        final_state = "ALERT"
        explanation = f"ALERT: High temperature ({data['temp']:.1f}°C) is detected (>= 38.5°C). This could indicate fever or infection and warrants attention."
        health_score = 60
        system_confidence = 85
    elif spo2 < 95:
        final_state = "ALERT"
        explanation = f"ALERT: SpO2 ({data['spo2']:.1f}%) is low (between 90% and 95%). This indicates a potential respiratory issue and requires monitoring."
        health_score = 70
        system_confidence = 90
    elif hr > 120:
        final_state = "ALERT"
        explanation = f"ALERT: Heart rate ({data['hr']:.0f} bpm) is high (>120 bpm). This may suggest tachycardia or stress and requires assessment."
        health_score = 50
        system_confidence = 80
    else:
        final_state = "NORMAL"
        explanation = "All vital signs are stable and within physiological norms. "
        if combined_rule_state != "NORMAL":
            explanation += f"However, the system's combined rule-based analysis suggests a '{combined_rule_state}' tendency due to factors like: " + "; ".join(reasons) + "."
            health_score = healthscore_combined
            system_confidence = system_confidence_combined
        else:
            explanation += "No significant risks identified from any analysis."
            health_score = 0
            system_confidence = 100

    if predicted_state_model != final_state and model_confidence > 70:
        explanation += f" AI model suggests '{predicted_state_model}' which differs from rule-based decision."

    if final_state == "NORMAL" and 60 <= hr <= 90 and spo2 >= 96:
        explanation += " Vital signs are stable and consistent with a healthy baseline."

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
        )

        return PredictionResponse(**result)

    except Exception as e:
        logger.error(f"Prediction error: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Prediction failed: {str(e)}")
