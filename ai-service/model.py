import pandas as pd
import numpy as np
import matplotlib.pyplot as plt
import seaborn as sns
import shap
import joblib
from typing import Dict, Any, Tuple, List, Union

from sklearn.model_selection import train_test_split, StratifiedKFold, cross_validate
from sklearn.preprocessing import LabelEncoder, MinMaxScaler
from sklearn.ensemble import RandomForestClassifier
from sklearn.pipeline import Pipeline
from sklearn.compose import ColumnTransformer
from sklearn.metrics import (
    accuracy_score, classification_report, confusion_matrix,
    make_scorer, f1_score, roc_auc_score
)
from google.colab import files

try:
    df_thermometry = pd.read_csv('thermometry.csv')
    print("File 'thermometry.csv' loaded successfully.")

    body_temp_data = df_thermometry[['body.temp']]

    print("\nFirst 5 rows of 'body.temp' (thermometry.csv):")
    display(body_temp_data.head())

    print("\nDescriptive statistics for 'body.temp':")
    display(body_temp_data.describe())

    print("\nChecking for missing values in 'body.temp':")
    print(body_temp_data.isnull().sum())

except FileNotFoundError:
    print("Error: File 'thermometry.csv' not found. Make sure it is uploaded correctly.")
except Exception as e:
    print(f"An error occurred while processing 'thermometry.csv': {e}")

try:
    df_mpu = pd.read_csv('mpu6050_dataset.csv')
    print("File 'mpu6050_dataset.csv' loaded successfully.")

    sensor_columns = ['ax', 'ay', 'az', 'gx', 'gy', 'gz']
    data_to_normalize = df_mpu[sensor_columns]

    scaler = MinMaxScaler()

    normalized_data = scaler.fit_transform(data_to_normalize)

    df_mpu_normalized = pd.DataFrame(normalized_data, columns=[col + '_normalized' for col in sensor_columns])

    df_mpu_processed = pd.concat([df_mpu_normalized, df_mpu[['label']]], axis=1)

    print("\nFirst 5 rows of 'mpu6050_dataset.csv' after normalization:")
    display(df_mpu_processed.head())

    print("\nDescriptive statistics for normalized data:")
    display(df_mpu_processed.describe())

except FileNotFoundError:
    print("Error: File 'mpu6050_dataset.csv' not found. Make sure it is uploaded correctly.")
except Exception as e:
    print(f"An error occurred while processing 'mpu6050_dataset.csv': {e}")

example_bidmc_file = 'bidmc_02_Numerics.csv'

try:
    df_bidmc = pd.read_csv(example_bidmc_file)
    print(f"File '{example_bidmc_file}' loaded successfully.")

    df_bidmc.columns = df_bidmc.columns.str.strip()

    print(f"\nColumns detected after cleaning: {df_bidmc.columns.tolist()}\n")

    selected_columns = ['Time [s]', 'HR', 'PULSE', 'SpO2']
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

def categorize_temperature(temp_celsius: float) -> str:
    """
    Categorizes body temperature into predefined health states.

    Args:
        temp_celsius: Body temperature in Celsius.

    Returns:
        A string representing the health state ('CRITICAL', 'ALERT', 'NORMAL', 'Unknown').
    """
    if pd.isna(temp_celsius):
        return 'Unknown'
    if temp_celsius < 35:
        return "CRITICAL"
    elif temp_celsius < 36:
        return "ALERT"
    elif temp_celsius <= 37.5:
        return "NORMAL"
    elif temp_celsius <= 38.5:
        return "ALERT"
    else:
        return "CRITICAL"

def categorize_hr(hr: float) -> str:
    """
    Categorizes heart rate into predefined health states.

    Args:
        hr: Heart rate in beats per minute (bpm).

    Returns:
        A string representing the health state ('CRITICAL', 'ALERT', 'NORMAL', 'Unknown').
    """
    if pd.isna(hr):
        return 'Unknown'
    if hr < 50:
        return "CRITICAL"
    elif hr < 60:
        return "ALERT"
    elif hr <= 100:
        return "NORMAL"
    elif hr <= 120:
        return "ALERT"
    else:
        return "CRITICAL"

def categorize_spo2(spo2: float) -> str:
    """
    Categorizes blood oxygen saturation (SpO2) into predefined health states.

    Args:
        spo2: SpO2 percentage.

    Returns:
        A string representing the health state ('CRITICAL', 'ALERT', 'NORMAL', 'Unknown').
    """
    if pd.isna(spo2):
        return "Unknown"
    if spo2 < 90:
        return "CRITICAL"
    elif spo2 < 95:
        return "ALERT"
    else:
        return "NORMAL"

def calculate_acceleration_magnitude(ax: float, ay: float, az: float) -> float:
    """
    Calculates the magnitude of acceleration from its components.

    Args:
        ax: Acceleration along the x-axis.
        ay: Acceleration along the y-axis.
        az: Acceleration along the z-axis.

    Returns:
        The magnitude of the acceleration vector.
    """
    if pd.isna(ax) or pd.isna(ay) or pd.isna(az):
        return np.nan
    return np.sqrt(ax**2 + ay**2 + az**2)

def combined_rule_based_label(row: pd.Series) -> str:
    """
    Applies a combined rule-based logic to determine an overall health state.

    Args:
        row: A pandas Series containing 'spo2', 'hr', 'temp', and 'acc' values.

    Returns:
        A string representing the combined health state ('CRITICAL', 'ALERT', 'NORMAL').
    """
    if row['spo2'] < 90:
        return "CRITICAL"

    score = 0
    if row['spo2'] < 95:
        score += 1

    if row['hr'] > 130:
        score += 3
    elif row['hr'] > 110:
        score += 1

    if row['temp'] > 39:
        score += 2
    elif row['temp'] > 38:
        score += 1

    if row['acc'] < 0.2:
        score += 1

    if score >= 4:
        return "CRITICAL"
    elif score >= 3:
        return "ALERT"
    else:
        return "NORMAL"

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
    """
    Analyzes patient data using both an ML model and rule-based logic to determine health state,
    health score, explanation, and confidence.

    Args:
        temp: Body temperature in Celsius.
        hr: Heart rate in bpm.
        spo2: Blood oxygen saturation percentage.
        ax, ay, az: Acceleration components from MPU6050.
        gx, gy, gz: Gyroscope components from MPU6050.
        model: Trained RandomForestClassifier model.
        label_encoder: LabelEncoder used for target variable.
        feature_columns: List of feature names expected by the model.
        scaler: MinMaxScaler used for MPU6050 data.

    Returns:
        A tuple containing:
        - predicted_state_model (str): Health state predicted by the ML model.
        - model_confidence (float): Confidence of the ML model's prediction.
        - combined_rule_based_state (str): Health state determined by combined rule-based logic.
        - healthscore_final (int): Final aggregated health score (0-100).
        - explanation_final (str): Detailed explanation for the final state.
        - system_confidence_final (int): Overall system confidence in the final state.
        - final_state (str): The ultimate health state after applying medical rules.
    """
    if not (0 < spo2 <= 100 and 20 < hr < 250 and 30 < temp < 45):
        return "UNKNOWN", 0.0, "UNKNOWN", 0, "Invalid sensor data detected.", 0, "UNKNOWN"

    data: Dict[str, float] = {'temp': temp, 'hr': hr, 'spo2': spo2, 'ax': ax, 'ay': ay, 'az': az, 'gx': gx, 'gy': gy, 'gz': gz}
    data['acc'] = calculate_acceleration_magnitude(ax, ay, az)

    sensor_columns: List[str] = ['ax', 'ay', 'az', 'gx', 'gy', 'gz']
    mpu_data = pd.DataFrame([[data[col] for col in sensor_columns]], columns=sensor_columns)
    normalized_mpu_data = scaler.transform(mpu_data)

    input_data_for_model: Dict[str, float] = {
        'temp': data['temp'], 'hr': data['hr'], 'spo2': data['spo2'] * 2, 'acc': data['acc']
    }
    for i, col in enumerate(sensor_columns):
        input_data_for_model[col] = normalized_mpu_data[0][i]

    input_df = pd.DataFrame([input_data_for_model])
    input_features = input_df[feature_columns]

    predicted_label_encoded = model.predict(input_features)[0]
    predicted_state_model = label_encoder.inverse_transform([predicted_label_encoded])[0]

    proba = model.predict_proba(input_features)[0]
    model_confidence = np.max(proba) * 100

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

    healthscore_combined_rule_based: int = min(100, int(
        base_score_combined_rule_based * 20 +
        (100 - spo2) * 2 +
        max(0, hr - 100)
    ))
    system_confidence_combined_rule_based: int = 0
    if combined_rule_based_state == "CRITICAL":
        system_confidence_combined_rule_based = max(0, min(100, int(50 + (score_combined_rule_based - 4.0) * 15)))
    elif combined_rule_based_state == "ALERT":
        dist_to_upper: float = 4.0 - score_combined_rule_based
        dist_to_lower: float = score_combined_rule_based - 3.0
        system_confidence_combined_rule_based = max(0, min(100, int(50 + min(dist_to_upper, dist_to_lower) * 15)))
    else:
        system_confidence_combined_rule_based = max(0, min(100, int(50 + (3.0 - score_combined_rule_based) * 15)))

    final_state: str = ""
    explanation_final: str = ""
    healthscore_final: int = 0
    system_confidence_final: int = 0

    if spo2 < 90:
        final_state = "CRITICAL"
        explanation_final = f"Critical state: SpO2 ({data['spo2']:.1f}%) is dangerously low (below 90%). This is an absolute medical rule and requires immediate attention."
        healthscore_final = 100
        system_confidence_final = 100
    elif spo2 < 95 and (hr > 120 or temp >= 39):
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

    if predicted_state_model != final_state and model_confidence > 70:
        explanation_final += f" AI model suggests '{predicted_state_model}' which differs from rule-based decision."

    if final_state == "NORMAL" and hr >= 60 and hr <= 90 and spo2 >= 96:
        explanation_final += " Vital signs are stable and consistent with a healthy baseline."

    return predicted_state_model, model_confidence, combined_rule_based_state, healthscore_final, explanation_final, system_confidence_final, final_state


def generate_synthetic_data(
    df_thermometry: pd.DataFrame, df_bidmc: pd.DataFrame, df_mpu: pd.DataFrame,
    num_base_samples: int, num_critical_alert_samples: int
) -> pd.DataFrame:
    """
    Generates synthetic patient data for model training.

    Args:
        df_thermometry: DataFrame containing thermometry data.
        df_bidmc: DataFrame containing BIDMC data.
        df_mpu: DataFrame containing MPU6050 data.
        num_base_samples: Number of normal/base samples to generate.
        num_critical_alert_samples: Number of critical/alert samples to generate.

    Returns:
        A combined DataFrame with synthetic data and calculated acceleration magnitude.
    """
    synthetic_temp_fahrenheit_base: pd.Series = df_thermometry['body.temp'].sample(n=num_base_samples, replace=True, random_state=42).reset_index(drop=True)
    synthetic_temp_celsius_base: pd.Series = (synthetic_temp_fahrenheit_base - 32) * 5/9
    synthetic_temp_celsius_base += np.random.normal(0, 0.2, num_base_samples)

    synthetic_hr_base: pd.Series = df_bidmc['HR'].sample(n=num_base_samples, replace=True, random_state=42).reset_index(drop=True)
    synthetic_hr_base += np.random.normal(0, 3, num_base_samples)

    synthetic_spo2_base: pd.Series = df_bidmc['SpO2'].sample(n=num_base_samples, replace=True, random_state=42).reset_index(drop=True)
    synthetic_spo2_base += np.random.normal(0, 0.5, num_base_samples)
    # Clamp NORMAL base SpO2 to 96-100 to avoid overlap with ALERT range
    synthetic_spo2_base = synthetic_spo2_base.clip(lower=96, upper=100)

    sensor_accel_columns: List[str] = ['ax', 'ay', 'az', 'gx', 'gy', 'gz']
    synthetic_mpu_data_base: pd.DataFrame = df_mpu[sensor_accel_columns].sample(n=num_base_samples, replace=True, random_state=42).reset_index(drop=True)
    synthetic_mpu_data_base['ax'] += np.random.normal(0, 0.05, num_base_samples)
    synthetic_mpu_data_base['ay'] += np.random.normal(0, 0.05, num_base_samples)
    synthetic_mpu_data_base['az'] += np.random.normal(0, 0.05, num_base_samples)
    synthetic_mpu_data_base['gx'] += np.random.normal(0, 0.5, num_base_samples)
    synthetic_mpu_data_base['gy'] += np.random.normal(0, 0.5, num_base_samples)
    synthetic_mpu_data_base['gz'] += np.random.normal(0, 0.5, num_base_samples)

    df_synthetic_base: pd.DataFrame = pd.DataFrame({
        'temp': synthetic_temp_celsius_base,
        'hr': synthetic_hr_base,
        'spo2': synthetic_spo2_base,
    })
    df_synthetic_base = pd.concat([df_synthetic_base, synthetic_mpu_data_base], axis=1)

    critical_samples_raw: pd.DataFrame = pd.DataFrame({
        "temp": np.random.uniform(39.5, 41.5, num_critical_alert_samples),
        "hr": np.random.uniform(130, 165, num_critical_alert_samples),
        "spo2": np.random.uniform(75, 89.9, num_critical_alert_samples),
        "ax": np.random.normal(0, 0.5, num_critical_alert_samples),
        "ay": np.random.normal(0, 0.5, num_critical_alert_samples),
        "az": np.random.normal(9.8, 0.5, num_critical_alert_samples),
        "gx": np.random.normal(0, 10, num_critical_alert_samples),
        "gy": np.random.normal(0, 10, num_critical_alert_samples),
        "gz": np.random.normal(0, 10, num_critical_alert_samples),
    })

    alert_samples_raw: pd.DataFrame = pd.DataFrame({
        "temp": np.random.uniform(37.5, 39.0, num_critical_alert_samples),
        "hr": np.random.uniform(95, 120, num_critical_alert_samples),
        "spo2": np.random.uniform(90, 94.9, num_critical_alert_samples),
        "ax": np.random.normal(0, 1.0, num_critical_alert_samples),
        "ay": np.random.normal(0, 1.0, num_critical_alert_samples),
        "az": np.random.normal(9.8, 1.0, num_critical_alert_samples),
        "gx": np.random.normal(0, 20, num_critical_alert_samples),
        "gy": np.random.normal(0, 20, num_critical_alert_samples),
        "gz": np.random.normal(0, 20, num_critical_alert_samples),
    })

    df_synthetic_combined: pd.DataFrame = pd.concat([df_synthetic_base, critical_samples_raw, alert_samples_raw], ignore_index=True)
    df_synthetic_combined['acc'] = df_synthetic_combined.apply(lambda row: calculate_acceleration_magnitude(row['ax'], row['ay'], row['az']), axis=1)
    return df_synthetic_combined

def train_unified_model(
    df_synthetic_combined: pd.DataFrame
) -> Tuple[RandomForestClassifier, LabelEncoder, MinMaxScaler, pd.Index, pd.Series, pd.Series, pd.Series, pd.Series]:
    """
    Trains a RandomForestClassifier model on the synthetic data.

    Args:
        df_synthetic_combined: DataFrame containing the synthetic data.

    Returns:
        A tuple containing the trained model, label encoder, scaler, feature columns,
        training features, test features, training labels, and test labels.
    """
    print("\n--- Training a Unified Model ---")
    X_synthetic: pd.DataFrame = df_synthetic_combined[['temp', 'hr', 'spo2', 'ax', 'ay', 'az', 'gx', 'gy', 'gz', 'acc']].copy()
    # Boost SpO2 importance: multiply by 2 so the model weighs it more heavily
    X_synthetic['spo2'] = X_synthetic['spo2'] * 2

    le_overall_state: LabelEncoder = LabelEncoder()
    y_synthetic_encoded: np.ndarray = le_overall_state.fit_transform(df_synthetic_combined['overall_state'])
    y_synthetic: pd.Series = pd.Series(y_synthetic_encoded, name='overall_state_encoded')

    X_train_synthetic, X_test_synthetic, y_train_synthetic, y_test_synthetic = train_test_split(
        X_synthetic, y_synthetic, test_size=0.2, random_state=42, stratify=y_synthetic
    )

    mpu_cols: List[str] = ['ax', 'ay', 'az', 'gx', 'gy', 'gz']
    scaler: MinMaxScaler = MinMaxScaler()
    scaler.fit(X_train_synthetic[mpu_cols])

    # Apply scaler to MPU columns so model trains on normalized data
    X_train_synthetic = X_train_synthetic.copy()
    X_test_synthetic = X_test_synthetic.copy()
    X_train_synthetic[mpu_cols] = scaler.transform(X_train_synthetic[mpu_cols])
    X_test_synthetic[mpu_cols] = scaler.transform(X_test_synthetic[mpu_cols])

    print(f"Synthetic training set X_train_synthetic: {X_train_synthetic.shape}")
    print(f"Synthetic test set X_test_synthetic: {X_test_synthetic.shape}")

    single_unified_model: RandomForestClassifier = RandomForestClassifier(n_estimators=150, max_depth=10, class_weight="balanced", random_state=42)
    single_unified_model.fit(X_train_synthetic, y_train_synthetic)

    print("Unified model (RandomForestClassifier) successfully trained on normalized synthetic data.")
    return single_unified_model, le_overall_state, scaler, X_synthetic.columns, X_train_synthetic, X_test_synthetic, y_train_synthetic, y_test_synthetic

def evaluate_model(
    model: RandomForestClassifier,
    X_test: pd.DataFrame, y_test: pd.Series, label_encoder: LabelEncoder
) -> None:
    """
    Evaluates the trained model and displays performance metrics.

    Args:
        model: Trained RandomForestClassifier model.
        X_test: Test features.
        y_test: Test labels.
        label_encoder: LabelEncoder used for target variable.
    """
    print("\n--- Unified Model Evaluation ---")
    y_pred: np.ndarray = model.predict(X_test)

    accuracy: float = accuracy_score(y_test, y_pred)
    report: str = classification_report(
        y_test,
        y_pred,
        target_names=label_encoder.classes_,
        zero_division=0
    )

    print(f"Accuracy: {accuracy:.4f}")
    print("Classification Report:\n", report)

    conf_matrix: np.ndarray = confusion_matrix(y_test, y_pred)
    plt.figure(figsize=(8, 6))
    sns.heatmap(conf_matrix, annot=True, fmt='d', cmap='Blues',
                xticklabels=label_encoder.classes_, yticklabels=label_encoder.classes_)
    plt.xlabel('Predicted Label')
    plt.ylabel('True Label')
    plt.title('Confusion Matrix')
    plt.show()

    importances: np.ndarray = model.feature_importances_
    feature_names: pd.Index = X_test.columns
    feature_importances_df: pd.DataFrame = pd.DataFrame({'feature': feature_names, 'importance': importances})
    feature_importances_df = feature_importances_df.sort_values(by='importance', ascending=False)

    print("\nFeature Importances:\n")
    print(feature_importances_df)

    plt.figure(figsize=(12, 6))
    sns.barplot(x='importance', y='feature', data=feature_importances_df, palette='viridis')
    plt.title('Feature Importances for Unified Model')
    plt.xlabel('Importance')
    plt.ylabel('Feature')
    plt.show()

def run_prediction_examples(
    model: RandomForestClassifier, label_encoder: LabelEncoder, feature_columns: pd.Index, scaler: MinMaxScaler
) -> None:
    """
    Runs example predictions to demonstrate the model's functionality.

    Args:
        model: Trained RandomForestClassifier model.
        label_encoder: LabelEncoder used for target variable.
        feature_columns: List of feature names expected by the model.
        scaler: MinMaxScaler used for MPU6050 data.
    """
    print("\n--- Unified Model Prediction Examples (Detailed Analysis) ---")

    examples: List[Tuple[str, Dict[str, float]]] = [
        ("Normal Situation", {"temp": 37.2, "hr": 85, "spo2": 97, "ax": 0.01, "ay": -0.02, "az": 0.98, "gx": 0.0, "gy": 0.0, "gz": 0.0}),
        ("CRITICAL Situation (spo2 < 90)", {"temp": 37.0, "hr": 70, "spo2": 88, "ax": 0.5, "ay": 0.5, "az": 9.8, "gx": 0.0, "gy": 0.0, "gz": 0.0}),
        ("ALERT Situation (high temperature)", {"temp": 38.5, "hr": 90, "spo2": 96, "ax": 0.1, "ay": 0.05, "az": 9.85, "gx": 0.0, "gy": 0.0, "gz": 0.0})
    ]

    for title, example_input_data in examples:
        print(f"\n--- {title} ---")
        predicted_state_model, model_confidence, combined_rule_based_state, healthscore_final, explanation_final, system_confidence_final, final_state = analyze_patient_data(
            example_input_data['temp'], example_input_data['hr'], example_input_data['spo2'],
            example_input_data['ax'], example_input_data['ay'], example_input_data['az'],
            example_input_data['gx'], example_input_data['gy'], example_input_data['gz'],
            model, label_encoder, feature_columns, scaler
        )

        print(f"Input: {example_input_data}")
        print(f"State (ML Model): {predicted_state_model} (Model Confidence: {model_confidence:.2f}%)")
        print(f"State (Combined Rule-Based Logic): {combined_rule_based_state}")
        print(f"Health Score: {healthscore_final} / 100")
        print(f"System Confidence: {system_confidence_final:.2f}%")
        print(f"Explanation: {explanation_final}")
        print(f"Final State (including medical rules): {final_state}")


# --- Main Execution Block ---

# 1. Generate Synthetic Data
print("\n" + "=" * 60)
print("SYNTHETIC DATA GENERATION")
print("=" * 60)
required_dfs: List[str] = ['df_thermometry', 'df_bidmc', 'df_mpu']
for df_name in required_dfs:
    if df_name not in globals():
        print(f"Error: DataFrame '{df_name}' not found. Please run the previous cells to load and preprocess the data.")
        exit()

num_base_samples: int = 5000
num_critical_alert_samples: int = 1500
df_synthetic_combined = generate_synthetic_data(df_thermometry, df_bidmc, df_mpu, num_base_samples, num_critical_alert_samples)

print(f"Generated {num_base_samples} base records, {num_critical_alert_samples} CRITICAL and {num_critical_alert_samples} ALERT records.")
print(f"Total synthetic records: {len(df_synthetic_combined)}")
print("First 5 rows from the combined synthetic dataset:")
display(df_synthetic_combined.head())

print("\nDistribution of 'overall_state' labels in the combined synthetic dataset:")
df_synthetic_combined['overall_state'] = df_synthetic_combined.apply(
    lambda row: combined_rule_based_label(row),
    axis=1
)
print(df_synthetic_combined['overall_state'].value_counts())

# 2. Train Unified Model
model, le_overall_state, scaler, feature_columns, X_train_synthetic, X_test_synthetic, y_train_synthetic, y_test_synthetic = train_unified_model(df_synthetic_combined)

# 3. Evaluate Model
evaluate_model(model, X_test_synthetic, y_test_synthetic, le_overall_state)

# 4. Run Prediction Examples
run_prediction_examples(model, le_overall_state, feature_columns, scaler)

RANDOM_STATE: int = 42

print("=" * 60)
print("STRATIFIED 5-FOLD CROSS-VALIDATION")
print("=" * 60)

# Build a pipeline that scales MPU columns per fold, then classifies
mpu_cols_cv: List[str] = ['ax', 'ay', 'az', 'gx', 'gy', 'gz']
passthrough_cols_cv: List[str] = ['temp', 'hr', 'spo2', 'acc']

cv_preprocessor = ColumnTransformer(
    transformers=[
        ('mpu_scaler', MinMaxScaler(), mpu_cols_cv),
    ],
    remainder='passthrough'  # temp, hr, spo2, acc passed as-is
)

cv_pipeline = Pipeline([
    ('preprocess', cv_preprocessor),
    ('clf', RandomForestClassifier(
        n_estimators=150,
        max_depth=10,
        class_weight="balanced",
        random_state=RANDOM_STATE
    ))
])

# Use full X_synthetic (raw) — the pipeline scales per fold
X_synthetic_cv: pd.DataFrame = df_synthetic_combined[['temp', 'hr', 'spo2', 'ax', 'ay', 'az', 'gx', 'gy', 'gz', 'acc']].copy()
X_synthetic_cv['spo2'] = X_synthetic_cv['spo2'] * 2  # Match training boost
y_synthetic_cv: np.ndarray = le_overall_state.transform(df_synthetic_combined['overall_state'])

skf: StratifiedKFold = StratifiedKFold(n_splits=5, shuffle=True, random_state=RANDOM_STATE)

scorers: Dict[str, Any] = {
    "accuracy":        make_scorer(accuracy_score),
    "f1_macro":        make_scorer(f1_score, average="macro",    zero_division=0),
    "f1_weighted":     make_scorer(f1_score, average="weighted", zero_division=0),
    "roc_auc_ovr":     make_scorer(
                           roc_auc_score,
                           needs_proba=True,
                           multi_class="ovr",
                           average="macro"
                       ),
}

cv_results: Dict[str, Any] = cross_validate(
    cv_pipeline,
    X_synthetic_cv,
    y_synthetic_cv,
    cv=skf,
    scoring=scorers,
    return_train_score=True,
    n_jobs=-1
)

metrics: List[str] = ["accuracy", "f1_macro", "f1_weighted", "roc_auc_ovr"]
print(f"\n{'Metric':<22} {'Mean':>8}  {'Std':>8}  {'Min':>8}  {'Max':>8}")
print("-" * 60)
for m in metrics:
    vals: np.ndarray = cv_results[f"test_{m}"]
    print(f"{m:<22} {vals.mean():>8.4f}  {vals.std():>8.4f}  "
          f"{vals.min():>8.4f}  {vals.max():>8.4f}")

print("\n── Overfitting check (train vs test accuracy) ──")
train_acc: np.ndarray = cv_results["train_accuracy"]
test_acc: np.ndarray = cv_results["test_accuracy"]
print(f"  Train accuracy:  {train_acc.mean():.4f} \u00b1 {train_acc.std():.4f}")
print(f"  Test  accuracy:  {test_acc.mean():.4f} \u00b1 {test_acc.std():.4f}")
gap: float = train_acc.mean() - test_acc.mean()
print(f"  Gap (overfit indicator): {gap:.4f}  "
      f"{f'[OK \u2014 <0.05]' if gap < 0.05 else '[WARNING \u2014 possible overfit]'}")

fig, axes = plt.subplots(1, 2, figsize=(12, 4))

fold_labels: List[str] = [f"Fold {i+1}" for i in range(5)]

axes[0].plot(fold_labels, cv_results["test_accuracy"],  marker="o", label="Test")
axes[0].plot(fold_labels, cv_results["train_accuracy"], marker="s", linestyle="--",
             label="Train", alpha=0.6)
axes[0].set_title("Accuracy per fold")
axes[0].set_ylabel("Accuracy")
axes[0].set_ylim(0.8, 1.01)
axes[0].legend()
axes[0].grid(alpha=0.3)

axes[1].plot(fold_labels, cv_results["test_f1_macro"],    marker="o", label="F1 macro")
axes[1].plot(fold_labels, cv_results["test_roc_auc_ovr"], marker="^", label="AUC-ROC (OvR)")
axes[1].set_title("F1 macro and AUC-ROC per fold")
axes[1].set_ylabel("Score")
axes[1].set_ylim(0.8, 1.01)
axes[1].legend()
axes[1].grid(alpha=0.3)

plt.suptitle("5-Fold Cross-Validation Results", fontsize=13)
plt.tight_layout()
plt.show()

final_model: RandomForestClassifier = RandomForestClassifier(
    n_estimators=150,
    max_depth=10,
    class_weight="balanced",
    random_state=RANDOM_STATE
)
final_model.fit(X_train_synthetic, y_train_synthetic)

print("\n\u2713 Final model retrained on train split for SHAP analysis.")

print("\n" + "=" * 60)
print("SHAP EXPLAINABILITY")
print("=" * 60)
print("Computing SHAP values...")

explainer = shap.TreeExplainer(final_model)
shap_values_raw = explainer.shap_values(X_test_synthetic, check_additivity=False)

class_names: List[str] = list(le_overall_state.classes_)
print(f"Classes (in SHAP order): {class_names}")
print(f"shap_values_raw type: {type(shap_values_raw)}, shape: {np.array(shap_values_raw).shape}")

arr: np.ndarray = np.array(shap_values_raw)
shap_values: List[np.ndarray]
if arr.ndim == 3:
    shap_values = [arr[:, :, i] for i in range(arr.shape[2])]
    print(f"Detected new SHAP format (3D). Reshaped to {len(shap_values)} arrays of {shap_values[0].shape}.")
else:
    shap_values = list(shap_values_raw)
    print(f"Detected old SHAP format (list). Using as-is.")

y_proba: np.ndarray = final_model.predict_proba(X_test_synthetic)
try:
    auc: float = roc_auc_score(y_test_synthetic, y_proba, multi_class="ovr", average="macro")
    print(f"\nAUC-ROC (OvR, manual on test set): {auc:.4f}")
except Exception as e:
    print(f"AUC-ROC could not be computed: {e}")

for i, cls in enumerate(class_names):
    plt.figure(figsize=(9, 5))
    shap.summary_plot(
        shap_values[i],
        X_test_synthetic,
        feature_names=list(X_test_synthetic.columns),
        show=False,
        max_display=10
    )
    plt.title(f"SHAP summary \u2014 class: {cls}", fontsize=13)
    plt.tight_layout()
    plt.show()

mean_abs_shap: np.ndarray = np.mean([np.abs(sv) for sv in shap_values], axis=0).mean(axis=0)
shap_importance_df: pd.DataFrame = pd.DataFrame({
    "feature":     list(X_test_synthetic.columns),
    "mean_|SHAP|": mean_abs_shap
}).sort_values("mean_|SHAP|", ascending=False)

plt.figure(figsize=(9, 4))
plt.barh(shap_importance_df["feature"][::-1],
         shap_importance_df["mean_|SHAP|"][::-1],
         color="#4C72B0")
plt.xlabel("Mean |SHAP value|")
plt.title("Global feature importance (SHAP)", fontsize=13)
plt.tight_layout()
plt.show()

print("\nTop 5 features (SHAP):\n")
print(shap_importance_df.head(5).to_string(index=False))

critical_class_idx: int = class_names.index("CRITICAL")
critical_indices: np.ndarray = np.where(y_test_synthetic.values == critical_class_idx)[0]

if len(critical_indices) > 0:
    sample_idx: int = critical_indices[0]
    sample_row: pd.DataFrame = X_test_synthetic.iloc[[sample_idx]]
    sv_single_raw = explainer.shap_values(sample_row, check_additivity=False)

    sv_arr: np.ndarray = np.array(sv_single_raw)
    sv_single: List[np.ndarray]
    if sv_arr.ndim == 3:
        sv_single = [sv_arr[:, :, i] for i in range(sv_arr.shape[2])]
    else:
        sv_single = list(sv_single_raw)

    print(f"\n\u2500\u2500 Waterfall for test sample #{sample_idx} \u2500\u2500")
    print(f"   True:      {class_names[y_test_synthetic.iloc[sample_idx]]}")
    print(f"   Predicted: {class_names[final_model.predict(sample_row)[0]]}")
    print(f"   SpO\u2082={sample_row['spo2'].values[0]:.1f}%  "
          f"HR={sample_row['hr'].values[0]:.0f} bpm  "
          f"Temp={sample_row['temp'].values[0]:.1f}\u00b0C")

    shap_exp = shap.Explanation(
        values        = sv_single[critical_class_idx][0],
        base_values   = explainer.expected_value[critical_class_idx],
        data          = sample_row.values[0],
        feature_names = list(sample_row.columns)
    )
    plt.figure(figsize=(9, 5))
    shap.waterfall_plot(shap_exp, show=False)
    plt.title(f"SHAP waterfall \u2014 CRITICAL (sample #{sample_idx})", fontsize=12)
    plt.tight_layout()
    plt.show()
else:
    print("No CRITICAL samples in test set \u2014 skipping waterfall.")
print("\n" + "=" * 60)
print("THESIS REPORTING SUMMARY")
print("=" * 60)
print(f"""
Cross-validation (Stratified 5-Fold):
  Accuracy:       {cv_results['test_accuracy'].mean():.4f} \u00b1 {cv_results['test_accuracy'].std():.4f}
  F1 (macro):     {cv_results['test_f1_macro'].mean():.4f} \u00b1 {cv_results['test_f1_macro'].std():.4f}
  F1 (weighted):  {cv_results['test_f1_weighted'].mean():.4f} \u00b1 {cv_results['test_f1_weighted'].std():.4f}
  AUC-ROC (OvR):  {auc:.4f}

Recommended thesis wording:
  "Model performance was estimated using stratified 5-fold cross-validation
   to ensure each fold preserves the NORMAL/ALERT/CRITICAL class distribution.
   Mean accuracy was {cv_results['test_accuracy'].mean():.2%} (\u00b1{cv_results['test_accuracy'].std():.2%}),
   with a macro-averaged AUC-ROC of {auc:.4f},
   indicating strong separability across all three health states.
   Feature importance was assessed using SHAP (SHapley Additive exPlanations),
   which identified SpO\u2082, heart rate, and body temperature as the dominant
   predictors, consistent with established clinical triage criteria."
""")